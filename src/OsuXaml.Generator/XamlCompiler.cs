using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace OsuXaml.Generator
{
    internal sealed class CompileResult
    {
        public string ClassName;
        public string Source;
        public ImmutableArray<Diagnostic> Diagnostics;
    }

    /// <summary>
    /// Turns one XAML document into C#. Element names are osu!framework type names, attributes are
    /// their properties, and every name and binding path is resolved against the compilation so
    /// mistakes surface as build errors pointing at the XAML line.
    /// </summary>
    internal sealed partial class XamlCompiler
    {
        /// <summary>URIs accepted for the <c>x:</c> directives namespace.</summary>
        private static readonly HashSet<string> directive_namespaces = new HashSet<string>
        {
            "http://schemas.microsoft.com/winfx/2006/xaml",
            "osu-xaml:x",
        };

        /// <summary>Namespaces searched for elements in the default (non clr-namespace) XML namespace.</summary>
        private static readonly string[] default_namespaces =
        {
            "osu.Framework.Graphics",
            "osu.Framework.Graphics.Containers",
            "osu.Framework.Graphics.Sprites",
            "osu.Framework.Graphics.Shapes",
            "osu.Framework.Graphics.UserInterface",
            "osu.Framework.Graphics.Cursor",
            "osu.Framework.Screens",
            "OsuXaml.Controls",
        };

        private const string items_source = "ItemsSource";
        private const string item_template = "ItemTemplate";

        private readonly Compilation compilation;
        private readonly string path;
        private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();
        private readonly List<string> fields = new List<string>();
        private readonly StringBuilder body = new StringBuilder();

        private INamedTypeSymbol rootType;
        private int indent;
        private int counter;

        public XamlCompiler(Compilation compilation, string path)
        {
            this.compilation = compilation;
            this.path = path;
        }

        /// <summary>What <c>{Bind}</c> paths resolve against at a point in the tree.</summary>
        private sealed class Scope
        {
            /// <summary>Expression for the data context, e.g. <c>dataContext</c> or <c>item3</c>.</summary>
            public string DataContext;

            public ITypeSymbol DataContextType;

            /// <summary>The page-level data context, reachable from templates with <c>Source=Root</c>.</summary>
            public string RootDataContext;

            public ITypeSymbol RootDataContextType;

            /// <summary>The current template item, passed to one-parameter methods bound from inside a template.</summary>
            public string Item;

            public ITypeSymbol ItemType;

            /// <summary>Expression for the <c>BindingScope</c> that owns bound copies created here.</summary>
            public string Bindings;

            public bool InTemplate => Item != null;
        }

        public CompileResult Compile(string text)
        {
            XDocument doc;

            try
            {
                doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                report(Diagnostics.InvalidXml, new LinePosition(Math.Max(0, e.LineNumber - 1), Math.Max(0, e.LinePosition - 1)), e.Message);
                return result(null);
            }

            var root = doc.Root;
            string className = directive(root, "Class");

            if (className == null)
            {
                report(Diagnostics.MissingClass, root, "The root element needs x:Class=\"Namespace.TypeName\" naming a partial class");
                return result(null);
            }

            rootType = compilation.GetTypeByMetadataName(className);

            if (rootType == null)
            {
                report(Diagnostics.MissingClass, root, $"x:Class '{className}' was not found. Declare it as a partial class in C#");
                return result(className);
            }

            var elementType = resolveType(root);

            if (elementType != null && !inherits(rootType, elementType))
                report(Diagnostics.MissingClass, root, $"'{className}' must derive from '{elementType.Name}' to match the root element");

            ITypeSymbol dataType = null;
            string dataTypeName = directive(root, "DataType");

            if (dataTypeName != null)
            {
                dataType = resolveTypeName(dataTypeName, root);
                if (dataType == null)
                    report(Diagnostics.UnknownType, root, dataTypeName, "");
            }

            var scope = new Scope
            {
                DataContext = dataType != null ? "dataContext" : null,
                DataContextType = dataType,
                RootDataContext = dataType != null ? "dataContext" : null,
                RootDataContextType = dataType,
                Bindings = "this.xamlBindings",
            };

            indent = 3;
            emitElement(root, rootType, scope, isRoot: true);

            return result(className, generate(className, dataType));
        }

        private string generate(string className, ITypeSymbol dataType)
        {
            var sb = new StringBuilder();
            string ns = rootType.ContainingNamespace.IsGlobalNamespace ? null : rootType.ContainingNamespace.ToDisplayString();

            sb.AppendLine("// <auto-generated>Generated by OsuXaml from " + System.IO.Path.GetFileName(path) + "</auto-generated>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine();

            if (ns != null)
                sb.AppendLine("namespace " + ns).AppendLine("{");

            sb.AppendLine("    partial class " + rootType.Name);
            sb.AppendLine("    {");

            foreach (string f in fields)
                sb.AppendLine("        " + f);

            // A field, so osu!framework's disposal (which unbinds IUnbindable fields) disconnects every binding.
            sb.AppendLine("        private readonly global::OsuXaml.BindingScope xamlBindings = new global::OsuXaml.BindingScope();");
            sb.AppendLine();

            string parameter = dataType != null ? typeName(dataType) + " dataContext" : string.Empty;

            sb.AppendLine("        /// <summary>Builds the drawable tree declared in " + System.IO.Path.GetFileName(path) + ".</summary>");
            sb.AppendLine("        private void InitializeComponent(" + parameter + ")");
            sb.AppendLine("        {");
            sb.Append(body);
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            if (ns != null)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private CompileResult result(string className, string source = null) => new CompileResult
        {
            ClassName = className,
            Source = diagnostics.Count == 0 ? source : null,
            Diagnostics = diagnostics.ToImmutableArray(),
        };

        #region Elements

        private string emitElement(XElement element, INamedTypeSymbol type, Scope scope, bool isRoot)
        {
            string v;

            if (isRoot)
                v = "this";
            else
            {
                v = "e" + counter++;

                bool hasCtor = type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

                if (type.IsAbstract || !hasCtor)
                {
                    report(Diagnostics.UnknownType, element, type.Name, " as a creatable type (it must be non-abstract with a public parameterless constructor)");
                    return null;
                }

                line($"var {v} = new {typeName(type)}();");
            }

            string name = directive(element, "Name");

            if (name != null)
            {
                if (scope.InTemplate)
                    report(Diagnostics.BadTemplate, element, "x:Name cannot be used inside an ItemTemplate, because the template creates one drawable per item");
                else if (!isRoot)
                {
                    fields.Add($"private {typeName(type)} {name};");
                    line($"this.{name} = {v};");
                }
            }

            XAttribute itemsSource = null;

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || directive_namespaces.Contains(attribute.Name.NamespaceName))
                    continue;

                string member = attribute.Name.LocalName;

                if (member == items_source && findMember(type, member) == null)
                {
                    itemsSource = attribute;
                    continue;
                }

                emitAttribute(v, type, attribute, scope);
            }

            XElement template = null;

            foreach (var child in element.Elements())
            {
                string local = child.Name.LocalName;
                int dot = local.IndexOf('.');

                if (dot >= 0)
                {
                    if (local.Substring(dot + 1) == item_template)
                        template = child;
                    else
                        report(Diagnostics.UnknownMember, child, type.Name, local.Substring(dot + 1));

                    continue;
                }

                var childType = resolveType(child);
                if (childType == null)
                    continue;

                string c = emitElement(child, childType, scope, isRoot: false);
                if (c != null)
                    emitAddChild(v, type, childType, c, child, isRoot);
            }

            string content = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();

            if (content.Length > 0)
                emitContent(v, type, content, element, scope);

            if (itemsSource != null || template != null)
                emitItems(v, type, element, itemsSource, template, scope);

            return v;
        }

        private void emitAddChild(string v, INamedTypeSymbol parent, INamedTypeSymbol child, string c, XElement childElement, bool isRoot)
        {
            var containerChild = containerChildType(parent);

            if (containerChild != null)
            {
                if (!inherits(child, containerChild))
                {
                    report(Diagnostics.TypeMismatch, childElement, child.Name, parent.Name + " children", containerChild.Name, "");
                    return;
                }

                line($"{v}.Add({c});");
                return;
            }

            if (isRoot && inherits(parent, compilation.GetTypeByMetadataName("osu.Framework.Graphics.Containers.CompositeDrawable")))
            {
                line($"AddInternal({c});");
                return;
            }

            report(Diagnostics.NoChildren, childElement, parent.Name);
        }

        /// <summary>Text inside an element, e.g. <c>&lt;SpriteText&gt;Hello&lt;/SpriteText&gt;</c>, sets its Text property.</summary>
        private void emitContent(string v, INamedTypeSymbol type, string content, XElement element, Scope scope)
        {
            if (!(findMember(type, "Text") is ISymbol textMember) || memberType(textMember) == null)
            {
                report(Diagnostics.NoChildren, element, type.Name + " (text content needs a Text property)");
                return;
            }

            emitAssignment(v, type, textMember, content, element, scope);
        }

        #endregion

        #region Attributes

        private void emitAttribute(string v, INamedTypeSymbol type, XAttribute attribute, Scope scope)
        {
            string name = attribute.Name.LocalName;
            var member = findMember(type, name);

            if (member == null)
            {
                report(Diagnostics.UnknownMember, attribute, type.Name, name);
                return;
            }

            emitAssignment(v, type, member, attribute.Value, attribute, scope);
        }

        private void emitAssignment(string v, INamedTypeSymbol type, ISymbol member, string value, XObject source, Scope scope)
        {
            if (BindMarkup.IsMarkup(value))
            {
                var bind = BindMarkup.Parse(value, out string error);

                if (bind == null)
                    report(Diagnostics.BadMarkup, source, error);
                else
                    emitBinding(v, member, bind, source, scope);

                return;
            }

            value = BindMarkup.Unescape(value);

            if (member is IEventSymbol ev)
            {
                string handler = methodHandler((INamedTypeSymbol)ev.Type, value, source);
                if (handler != null)
                    line($"{v}.{member.Name} += {handler};");
                return;
            }

            var target = memberType(member);

            if (target is INamedTypeSymbol { TypeKind: TypeKind.Delegate } del)
            {
                string handler = methodHandler(del, value, source);
                if (handler != null)
                    line($"{v}.{member.Name} = {handler};");
                return;
            }

            string literal = convertLiteral(value, target, out string hint);

            if (literal == null)
            {
                report(Diagnostics.BadLiteral, source, value, target.ToDisplayString(), hint ?? "");
                return;
            }

            line($"{v}.{member.Name} = {literal};");
        }

        /// <summary>A plain attribute value on a delegate or event names a method on the x:Class (code-behind).</summary>
        private string methodHandler(INamedTypeSymbol delegateType, string methodName, XObject source)
        {
            var method = rootType.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();

            if (method == null)
            {
                report(Diagnostics.UnknownMethod, source, $"'{rootType.Name}' has no method named '{methodName}'");
                return null;
            }

            return lambda(delegateType, "this." + method.Name, method, null, null);
        }

        /// <summary>
        /// Builds a lambda matching <paramref name="delegateType"/> that calls <paramref name="method"/>.
        /// Delegate arguments are passed through when the method takes the same number of parameters;
        /// otherwise the method is called with no arguments, or with the template item when it takes exactly one.
        /// </summary>
        private string lambda(INamedTypeSymbol delegateType, string target, IMethodSymbol method, string item, ITypeSymbol itemType)
        {
            var invoke = delegateType.DelegateInvokeMethod;
            var names = invoke.Parameters.Select((_, i) => "a" + i).ToArray();
            string parameters = "(" + string.Join(", ", names) + ")";

            string args;

            if (method.Parameters.Length == invoke.Parameters.Length)
                args = string.Join(", ", names);
            else if (method.Parameters.Length == 1 && item != null && ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)compilation).ClassifyConversion(itemType, method.Parameters[0].Type).IsImplicit)
                args = item;
            else if (method.Parameters.Length == 0)
                args = string.Empty;
            else
                return null;

            return $"{parameters} => {target}({args})";
        }

        #endregion

        #region Bindings

        private void emitBinding(string v, ISymbol member, BindMarkup bind, XObject source, Scope scope)
        {
            bool fromRoot = string.Equals(bind.Get("Source"), "Root", StringComparison.OrdinalIgnoreCase);
            string dc = fromRoot ? scope.RootDataContext : scope.DataContext;
            var dcType = fromRoot ? scope.RootDataContextType : scope.DataContextType;

            if (dc == null)
            {
                report(Diagnostics.BadMarkup, source, "{Bind} needs a data context. Add x:DataType=\"prefix:ViewModelType\" to the root element");
                return;
            }

            if (!resolvePath(dc, dcType, bind.Path, source, out string expr, out ISymbol last, out ITypeSymbol sourceType))
                return;

            // Methods bind to events and delegate properties: Action="{Bind AddItem}".
            if (last is IMethodSymbol method)
            {
                var del = member is IEventSymbol ev ? ev.Type as INamedTypeSymbol : memberType(member) as INamedTypeSymbol;

                if (del?.TypeKind != TypeKind.Delegate)
                {
                    report(Diagnostics.TypeMismatch, source, "method '" + method.Name + "'", member.Name, memberType(member)?.ToDisplayString() ?? "event", " (methods bind only to events and delegates)");
                    return;
                }

                string handler = lambda(del, expr, method, scope.Item, scope.ItemType);

                if (handler == null)
                {
                    report(Diagnostics.UnknownMethod, source, $"'{method.Name}' must take no parameters, the template item, or the same parameters as {del.Name}");
                    return;
                }

                line(member is IEventSymbol ? $"{v}.{member.Name} += {handler};" : $"{v}.{member.Name} = {handler};");
                return;
            }

            var target = memberType(member);

            if (target == null)
            {
                report(Diagnostics.UnknownMember, source, member.ContainingType.Name, member.Name);
                return;
            }

            string mode = bind.Get("Mode") ?? "Default";

            // A control's own bindable (Checkbox.Current, TextBox.Current) links to a view model bindable in both directions.
            var targetBindableValue = bindableClassValueType(target);

            if (targetBindableValue != null && !mode.Equals("OneWay", StringComparison.OrdinalIgnoreCase))
            {
                var sourceBindableValue = bindableClassValueType(sourceType);

                if (sourceBindableValue == null || !SymbolEqualityComparer.Default.Equals(sourceBindableValue, targetBindableValue))
                {
                    report(Diagnostics.TypeMismatch, source, describe(sourceType), member.Name, target.ToDisplayString(),
                        $" (two-way bindings need a Bindable<{targetBindableValue.ToDisplayString()}> source)");
                    return;
                }

                line($"global::OsuXaml.XamlBinding.TwoWay({v}.{member.Name}, {expr});");
                return;
            }

            if (mode.Equals("TwoWay", StringComparison.OrdinalIgnoreCase))
            {
                report(Diagnostics.TypeMismatch, source, describe(sourceType), member.Name, target.ToDisplayString(), " (Mode=TwoWay needs a Bindable target such as Current)");
                return;
            }

            var assignTo = targetBindableValue ?? target;
            string assign = targetBindableValue != null ? $"{v}.{member.Name}.Value" : $"{v}.{member.Name}";

            var valueType = bindableValueType(sourceType);

            if (valueType != null)
            {
                string converted = convertValue("x", valueType, assignTo, bind, source);
                if (converted == null)
                    return;

                string animate = bind.Get("Animate");

                if (animate != null && targetBindableValue == null)
                {
                    if (!double.TryParse(animate, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                    {
                        report(Diagnostics.BadMarkup, source, $"Animate must be a duration in milliseconds, not '{animate}'");
                        return;
                    }

                    string easing = bind.Get("Easing") ?? "OutQuint";
                    var easingType = compilation.GetTypeByMetadataName("osu.Framework.Graphics.Easing");

                    if (easingType?.GetMembers(easing).OfType<IFieldSymbol>().Any() != true)
                    {
                        report(Diagnostics.BadMarkup, source, $"Unknown Easing '{easing}'");
                        return;
                    }

                    // The first value is applied directly; later changes run as an osu!framework transform.
                    line($"global::OsuXaml.XamlBinding.OneWay({scope.Bindings}, {expr}, (x, initial) =>");
                    line("{");
                    indent++;
                    line($"var value = {converted};");
                    line($"if (initial) {assign} = value;");
                    line($"else global::osu.Framework.Graphics.TransformableExtensions.TransformTo({v}, nameof({v}.{member.Name}), value, {literalDouble(duration)}, global::osu.Framework.Graphics.Easing.{easing});");
                    indent--;
                    line("});");
                    return;
                }

                line($"global::OsuXaml.XamlBinding.OneWay({scope.Bindings}, {expr}, (x, _) => {assign} = {converted});");
                return;
            }

            // A plain property or field is read once.
            string once = convertValue(expr, sourceType, assignTo, bind, source);
            if (once != null)
                line($"{assign} = {once};");
        }

        private void emitItems(string v, INamedTypeSymbol type, XElement element, XAttribute itemsSource, XElement template, Scope scope)
        {
            if (itemsSource == null || template == null)
            {
                report(Diagnostics.BadTemplate, element, "ItemsSource and an ItemTemplate property element must be used together");
                return;
            }

            if (containerChildType(type)?.ToDisplayString() != "osu.Framework.Graphics.Drawable")
            {
                report(Diagnostics.BadTemplate, element, $"ItemsSource needs a Container<Drawable> such as FillFlowContainer, but '{type.Name}' is not one");
                return;
            }

            var bind = BindMarkup.IsMarkup(itemsSource.Value) ? BindMarkup.Parse(itemsSource.Value, out _) : null;

            if (bind == null)
            {
                report(Diagnostics.BadTemplate, itemsSource, "ItemsSource must be a binding, e.g. ItemsSource=\"{Bind Items}\"");
                return;
            }

            bool fromRoot = string.Equals(bind.Get("Source"), "Root", StringComparison.OrdinalIgnoreCase);
            string dc = fromRoot ? scope.RootDataContext : scope.DataContext;
            var dcType = fromRoot ? scope.RootDataContextType : scope.DataContextType;

            if (dc == null)
            {
                report(Diagnostics.BadMarkup, itemsSource, "{Bind} needs a data context. Add x:DataType to the root element");
                return;
            }

            if (!resolvePath(dc, dcType, bind.Path, itemsSource, out string expr, out _, out ITypeSymbol sourceType))
                return;

            var itemType = bindableListItemType(sourceType);

            if (itemType == null)
            {
                report(Diagnostics.TypeMismatch, itemsSource, describe(sourceType), items_source, "IBindableList<T>", "");
                return;
            }

            var templateRoots = template.Elements().ToList();

            if (templateRoots.Count != 1)
            {
                report(Diagnostics.BadTemplate, template, "ItemTemplate must contain exactly one element");
                return;
            }

            var rootElement = templateRoots[0];
            var rootElementType = resolveType(rootElement);
            if (rootElementType == null)
                return;

            string item = "item" + counter;
            string itemScope = "scope" + counter++;

            var inner = new Scope
            {
                DataContext = item,
                DataContextType = itemType,
                RootDataContext = scope.RootDataContext,
                RootDataContextType = scope.RootDataContextType,
                Item = item,
                ItemType = itemType,
                Bindings = itemScope,
            };

            line($"global::OsuXaml.XamlBinding.Items<{typeName(itemType)}>({scope.Bindings}, {v}, {expr}, ({item}, {itemScope}) =>");
            line("{");
            indent++;
            string created = emitElement(rootElement, rootElementType, inner, isRoot: false);
            if (created != null)
                line($"return {created};");
            else
                line("return null;");
            indent--;
            line("});");
        }

        /// <summary>Resolves a dotted path such as <c>Player.Volume</c> against the data context type.</summary>
        private bool resolvePath(string dc, ITypeSymbol dcType, string bindingPath, XObject source, out string expr, out ISymbol last, out ITypeSymbol type)
        {
            expr = dc;
            last = null;
            type = dcType;

            foreach (string segment in bindingPath.Split('.'))
            {
                if (last is IMethodSymbol)
                {
                    report(Diagnostics.UnknownPath, source, last.Name, segment);
                    return false;
                }

                var member = findReadable(type, segment);

                if (member == null)
                {
                    report(Diagnostics.UnknownPath, source, type.ToDisplayString(), segment);
                    return false;
                }

                expr += "." + segment;
                last = member;
                type = member is IMethodSymbol m ? m.ReturnType : memberType(member);
            }

            return true;
        }

        /// <summary>
        /// Converts a bound value to the target type: Converter first, then StringFormat, then ToString for text targets.
        /// Anything that still has no implicit conversion is a build error rather than a runtime surprise.
        /// </summary>
        private string convertValue(string expr, ITypeSymbol from, ITypeSymbol to, BindMarkup bind, XObject source)
        {
            string converter = bind.Get("Converter");

            if (converter != null)
            {
                var method = findConverter(converter, from, source);
                if (method == null)
                    return null;

                expr = $"{method.Value.Call}({expr})";
                from = method.Value.Method.ReturnType;
            }

            string format = bind.Get("StringFormat");

            if (format != null)
            {
                expr = $"string.Format(global::System.Globalization.CultureInfo.CurrentCulture, {SymbolDisplay.FormatLiteral(format, true)}, {expr})";
                from = compilation.GetSpecialType(SpecialType.System_String);
            }

            if (isText(to) && !isText(from))
            {
                expr = from.IsValueType ? $"{expr}.ToString()" : $"{expr}?.ToString()";
                from = compilation.GetSpecialType(SpecialType.System_String);
            }

            var conversion = ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)compilation).ClassifyConversion(from, to);

            if (!conversion.IsImplicit)
            {
                report(Diagnostics.TypeMismatch, source, from.ToDisplayString(), source is XAttribute a ? a.Name.LocalName : "value", to.ToDisplayString(), " (add a Converter)");
                return null;
            }

            return expr;
        }

        /// <summary>
        /// A converter is a one-parameter method: <c>Converter=DoneToAlpha</c> looks on the x:Class,
        /// <c>Converter=local:Converters.DoneToAlpha</c> on a static class.
        /// </summary>
        private (string Call, IMethodSymbol Method)? findConverter(string name, ITypeSymbol input, XObject source)
        {
            INamedTypeSymbol owner = rootType;
            string methodName = name;
            string call = name;

            int dot = name.LastIndexOf('.');

            if (dot >= 0)
            {
                owner = resolveTypeName(name.Substring(0, dot), source.Parent ?? (XElement)source);
                methodName = name.Substring(dot + 1);

                if (owner == null)
                {
                    report(Diagnostics.UnknownType, source, name.Substring(0, dot), "");
                    return null;
                }

                call = typeName(owner) + "." + methodName;
            }

            var method = owner.GetMembers(methodName).OfType<IMethodSymbol>()
                              .FirstOrDefault(m => m.Parameters.Length == 1 && ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)compilation).ClassifyConversion(input, m.Parameters[0].Type).IsImplicit);

            if (method == null)
            {
                report(Diagnostics.UnknownMethod, source, $"Converter '{name}' must be a method on '{owner.Name}' taking one {input.ToDisplayString()} parameter");
                return null;
            }

            return (call, method);
        }

        #endregion

        #region Literals

        private string convertLiteral(string value, ITypeSymbol type, out string hint)
        {
            hint = null;
            string s = value.Trim();
            string full = type.ToDisplayString();

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                    return SymbolDisplay.FormatLiteral(value, true);

                case SpecialType.System_Boolean:
                    return bool.TryParse(s, out bool b) ? (b ? "true" : "false") : null;

                case SpecialType.System_Int32:
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i.ToString(CultureInfo.InvariantCulture) : null;

                case SpecialType.System_Single:
                    return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? literalFloat(f) : null;

                case SpecialType.System_Double:
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? literalDouble(d) : null;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                var parts = s.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                var names = type.GetMembers().OfType<IFieldSymbol>().Select(m => m.Name).ToList();

                if (parts.Length == 0 || parts.Any(p => !names.Contains(p)))
                {
                    hint = ". Valid values: " + string.Join(", ", names.Take(12)) + (names.Count > 12 ? ", ..." : "");
                    return null;
                }

                return string.Join(" | ", parts.Select(p => typeName(type) + "." + p));
            }

            switch (full)
            {
                case "osu.Framework.Localisation.LocalisableString":
                    return SymbolDisplay.FormatLiteral(value, true);

                case "osuTK.Vector2":
                {
                    var n = numbers(s);
                    if (n == null || n.Length < 1 || n.Length > 2)
                        break;

                    return n.Length == 1 ? $"new global::osuTK.Vector2({literalFloat(n[0])})" : $"new global::osuTK.Vector2({literalFloat(n[0])}, {literalFloat(n[1])})";
                }

                case "osu.Framework.Graphics.MarginPadding":
                {
                    var n = numbers(s);
                    const string mp = "global::osu.Framework.Graphics.MarginPadding";

                    if (n?.Length == 1)
                        return $"new {mp}({literalFloat(n[0])})";
                    if (n?.Length == 2)
                        return $"new {mp} {{ Horizontal = {literalFloat(n[0])}, Vertical = {literalFloat(n[1])} }}";
                    if (n?.Length == 4)
                        return $"new {mp} {{ Left = {literalFloat(n[0])}, Top = {literalFloat(n[1])}, Right = {literalFloat(n[2])}, Bottom = {literalFloat(n[3])} }}";

                    hint = " (use \"all\", \"horizontal,vertical\" or \"left,top,right,bottom\")";
                    return null;
                }

                case "osu.Framework.Graphics.Colour4":
                case "osu.Framework.Graphics.Colour.ColourInfo":
                case "osuTK.Graphics.Color4":
                    return colour(s, out hint);

                case "osu.Framework.Graphics.Sprites.FontUsage":
                    return font(s);
            }

            hint = " (no XAML conversion exists for this type yet)";
            return null;
        }

        private string colour(string s, out string hint)
        {
            hint = null;
            const string c4 = "global::osu.Framework.Graphics.Colour4";

            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                string hex = s.Substring(1);

                if ((hex.Length == 3 || hex.Length == 4 || hex.Length == 6 || hex.Length == 8) && hex.All(Uri.IsHexDigit))
                    return $"{c4}.FromHex({SymbolDisplay.FormatLiteral(hex, true)})";

                hint = " (expected #rgb, #rgba, #rrggbb or #rrggbbaa)";
                return null;
            }

            var colour4 = compilation.GetTypeByMetadataName("osu.Framework.Graphics.Colour4");

            if (colour4?.GetMembers(s).Any(m => m.IsStatic && (m is IPropertySymbol || m is IFieldSymbol)) == true)
                return $"{c4}.{s}";

            hint = " (use #rrggbb or a Colour4 name such as White)";
            return null;
        }

        /// <summary>
        /// <c>Font="24"</c>, <c>Font="Bold,24"</c>, <c>Font="Roboto,18,Italic"</c>: numbers set the size,
        /// Italic sets italics, known weights set the weight and anything else is the family.
        /// </summary>
        private string font(string s)
        {
            string family = null, size = null, weight = null, italics = null;
            var weights = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Thin", "Light", "Regular", "Medium", "SemiBold", "Bold", "Black" };

            foreach (string raw in s.Split(','))
            {
                string p = raw.Trim();

                if (p.Length == 0)
                    continue;

                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    size = literalFloat(f);
                else if (p.Equals("Italic", StringComparison.OrdinalIgnoreCase))
                    italics = "true";
                else if (weights.Contains(p))
                    weight = SymbolDisplay.FormatLiteral(p, true);
                else
                    family = SymbolDisplay.FormatLiteral(p, true);
            }

            var args = new List<string>();
            if (family != null) args.Add("family: " + family);
            if (size != null) args.Add("size: " + size);
            if (weight != null) args.Add("weight: " + weight);
            if (italics != null) args.Add("italics: " + italics);

            return "global::osu.Framework.Graphics.FrameworkFont.Regular.With(" + string.Join(", ", args) + ")";
        }

        private static float[] numbers(string s)
        {
            var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new float[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    return null;
            }

            return result;
        }

        private static string literalFloat(float f) => f.ToString("R", CultureInfo.InvariantCulture) + "f";

        private static string literalDouble(double d)
        {
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            return s.Contains(".") || s.Contains("E") ? s : s + ".0";
        }

        #endregion
    }
}
