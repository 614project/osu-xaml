using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
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
            // WPF-style controls come first so Button and TextBox mean the ready-to-use ones.
            "OsuXaml.Controls",
            "OsuXaml",
            "osu.Framework.Graphics",
            "osu.Framework.Graphics.Containers",
            "osu.Framework.Graphics.Sprites",
            "osu.Framework.Graphics.Shapes",
            "osu.Framework.Graphics.UserInterface",
            "osu.Framework.Graphics.Cursor",
            "osu.Framework.Screens",
        };

        private const string items_source = "ItemsSource";
        private const string item_template = "ItemTemplate";

        private readonly Compilation compilation;
        private readonly string path;
        private readonly ImmutableArray<(string Path, string Text)> dictionarySources;
        private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();
        private readonly List<string> fields = new List<string>();
        private readonly StringBuilder main = new StringBuilder();
        private readonly StringBuilder deferred = new StringBuilder();
        private readonly Dictionary<XDocument, string> documentPaths = new Dictionary<XDocument, string>();
        private readonly Dictionary<string, XElement> names = new Dictionary<string, XElement>();
        private readonly List<XElement> globalDictionaries = new List<XElement>();

        private StringBuilder body;
        private INamedTypeSymbol rootType;
        private XElement rootElement;
        private int indent;
        private int counter;

        public XamlCompiler(Compilation compilation, string path, ImmutableArray<(string Path, string Text)> dictionaries = default)
        {
            this.compilation = compilation;
            this.path = path;
            dictionarySources = dictionaries.IsDefault ? ImmutableArray<(string, string)>.Empty : dictionaries;
            body = main;
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

            /// <summary>The control a ControlTemplate is building, for <c>{TemplateBinding}</c>.</summary>
            public string TemplatedParent;

            public ITypeSymbol TemplatedParentType;

            /// <summary>Expression for the <c>BindingScope</c> that owns bound copies created here.</summary>
            public string Bindings;

            /// <summary>Item type of the nearest ItemsSource, used by templates that don't name x:DataType.</summary>
            public ITypeSymbol ItemsType;

            /// <summary>Inside a template body, which runs once per item rather than once per page.</summary>
            public bool InTemplate;

            public Scope With(Action<Scope> change)
            {
                var copy = (Scope)MemberwiseClone();
                change(copy);
                return copy;
            }
        }

        /// <summary>A property value from an attribute, a property element or a style setter.</summary>
        private sealed class PropertyValue
        {
            /// <summary>Member name, possibly qualified: <c>Background</c>, <c>Grid.Row</c>.</summary>
            public string Name;

            /// <summary>XML namespace of a qualified name's owner prefix, or empty for the default namespace.</summary>
            public string Namespace = string.Empty;

            /// <summary>Attribute-style text value; null when <see cref="Element"/> holds the value.</summary>
            public string Text;

            /// <summary>A property element (or Setter.Value) whose content is the value.</summary>
            public XElement Element;

            /// <summary>Where diagnostics point.</summary>
            public XObject Source;

            /// <summary>Where type prefixes and resources are looked up from.</summary>
            public XElement Context;

            /// <summary>The element the property is applied to.</summary>
            public XElement Owner;
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

            documentPaths[doc] = path;

            var root = rootElement = doc.Root;
            string className = directive(root, "Class");

            // Shared resource dictionaries (a ResourceDictionary root without x:Class) generate no code of their own.
            if (className == null && root.Name.LocalName == "ResourceDictionary")
                return result(null);

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

            loadDictionaries();
            collectNames(root);

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

            return result(className, generate(dataType));
        }

        private void loadDictionaries()
        {
            foreach (var (dictionaryPath, dictionaryText) in dictionarySources)
            {
                if (string.Equals(dictionaryPath, path, StringComparison.OrdinalIgnoreCase) || dictionaryText == null)
                    continue;

                try
                {
                    var doc = XDocument.Parse(dictionaryText, LoadOptions.SetLineInfo);

                    if (doc.Root.Name.LocalName != "ResourceDictionary" || directive(doc.Root, "Class") != null)
                        continue;

                    documentPaths[doc] = dictionaryPath;
                    globalDictionaries.Add(doc.Root);
                }
                catch (XmlException)
                {
                    // Reported when that file itself is compiled.
                }
            }
        }

        /// <summary>Indexes x:Name elements outside templates and resources, for <c>{Bind ..., ElementName=x}</c>.</summary>
        private void collectNames(XElement element)
        {
            string name = directive(element, "Name");
            if (name != null && !names.ContainsKey(name))
                names[name] = element;

            foreach (var child in element.Elements())
            {
                string local = child.Name.LocalName;

                if (local.EndsWith(".Resources", StringComparison.Ordinal) || local.EndsWith("Template", StringComparison.Ordinal)
                    || local.EndsWith(".DisplayMemberBinding", StringComparison.Ordinal) || local == "Style")
                    continue;

                collectNames(child);
            }
        }

        private string generate(ITypeSymbol dataType)
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
            sb.Append(main);

            if (deferred.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("            // Bindings to named elements run once every element exists.");
                sb.Append(deferred);
            }

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
                    report(Diagnostics.BadTemplate, element, "x:Name cannot be used inside a template, because the template creates one drawable per item");
                else if (!isRoot)
                {
                    fields.Add($"private {typeName(type)} {name};");
                    line($"this.{name} = {v};");
                }
            }

            // Templates below this element that don't name x:DataType take the type of its ItemsSource items.
            var itemsType = itemsTypeOf(element, scope);
            if (itemsType != null)
                scope = scope.With(s => s.ItemsType = itemsType);

            var local = localPropertyNames(element);

            // Style setters first, so the element's own attributes win.
            var styled = new HashSet<string>();

            if (!isRoot)
            {
                foreach (var setter in styleSetters(element, type))
                {
                    styled.Add(setter.Name);

                    if (!local.Contains(setter.Name))
                        emitProperty(v, type, setter, scope);
                }
            }

            XAttribute legacyItemsSource = null;
            XElement legacyTemplate = null;
            bool hasContent = false;

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || directive_namespaces.Contains(attribute.Name.NamespaceName))
                    continue;

                string member = attribute.Name.LocalName;

                if (member == "Style" && findMember(type, member) == null)
                    continue;

                // Panels and osu!framework containers take ItemsSource + ItemTemplate without having such properties.
                if (member == items_source && findMember(type, member) == null)
                {
                    legacyItemsSource = attribute;
                    continue;
                }

                if (member == item_template && findMember(type, member) == null)
                {
                    legacyTemplate = resourceTemplate(attribute);
                    continue;
                }

                if (member == "Content")
                    hasContent = true;

                emitProperty(v, type, new PropertyValue
                {
                    Name = member,
                    Namespace = attribute.Name.NamespaceName,
                    Text = attribute.Value,
                    Source = attribute,
                    Context = element,
                    Owner = element,
                }, scope);
            }

            emitImplicitItemTemplate(v, type, element, scope, new HashSet<string>(local.Concat(styled)));

            foreach (var child in element.Elements())
            {
                string childName = child.Name.LocalName;
                int dot = childName.IndexOf('.');

                if (dot >= 0)
                {
                    string member = childName.Substring(dot + 1);

                    if (member == "Resources")
                        continue;

                    if (member == item_template && findMember(type, member) == null)
                    {
                        var roots = child.Elements().ToList();

                        if (roots.Count != 1)
                            report(Diagnostics.BadTemplate, child, "ItemTemplate must contain exactly one element");
                        else
                            legacyTemplate = roots[0];

                        continue;
                    }

                    if (member == "Content")
                        hasContent = true;

                    emitPropertyElement(v, type, child, scope);
                    continue;
                }

                hasContent = true;

                var (c, childType) = emitValueElement(child, scope);
                if (c != null)
                    emitAddChild(v, type, childType, c, child, isRoot);
            }

            string content = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();

            if (content.Length > 0)
            {
                hasContent = true;
                emitContent(v, type, content, element, scope);
            }

            // <ContentPresenter /> in a ControlTemplate shows the templated control's content.
            if (!hasContent && scope.TemplatedParent != null && inherits(type, compilation.GetTypeByMetadataName("OsuXaml.Controls.ContentPresenter"))
                && inherits(scope.TemplatedParentType, compilation.GetTypeByMetadataName("OsuXaml.Controls.ContentControl")))
                line($"{scope.TemplatedParent}.AttachPresenter({v});");

            if (legacyItemsSource != null || legacyTemplate != null)
                emitItems(v, type, element, legacyItemsSource, legacyTemplate, scope);

            return v;
        }

        /// <summary>
        /// Creates the value an element stands for: a drawable or object, or a literal for
        /// <c>&lt;x:String&gt;</c>, <c>&lt;x:Double&gt;</c> and friends.
        /// </summary>
        private (string Expr, ITypeSymbol Type) emitValueElement(XElement element, Scope scope)
        {
            if (directive_namespaces.Contains(element.Name.NamespaceName))
            {
                var primitive = directiveType(element.Name.LocalName);

                if (primitive == null)
                {
                    report(Diagnostics.UnknownType, element, "x:" + element.Name.LocalName, " (use x:String, x:Double, x:Single, x:Int32 or x:Boolean)");
                    return (null, null);
                }

                string literal = convertLiteral(element.Value, primitive, out string hint);

                if (literal == null)
                {
                    report(Diagnostics.BadLiteral, element, element.Value, primitive.ToDisplayString(), hint ?? "");
                    return (null, null);
                }

                string v = "e" + counter++;
                line($"var {v} = {literal};");
                return (v, primitive);
            }

            var type = resolveType(element);
            if (type == null)
                return (null, null);

            return (emitElement(element, type, scope, isRoot: false), type);
        }

        private ITypeSymbol directiveType(string name)
        {
            switch (name)
            {
                case "String": return compilation.GetSpecialType(SpecialType.System_String);
                case "Double": return compilation.GetSpecialType(SpecialType.System_Double);
                case "Single": return compilation.GetSpecialType(SpecialType.System_Single);
                case "Int32": return compilation.GetSpecialType(SpecialType.System_Int32);
                case "Boolean": return compilation.GetSpecialType(SpecialType.System_Boolean);
                default: return null;
            }
        }

        /// <summary>Attribute and property element names set directly on an element; these beat style setters.</summary>
        private static HashSet<string> localPropertyNames(XElement element)
        {
            var set = new HashSet<string>();

            foreach (var a in element.Attributes())
            {
                if (!a.IsNamespaceDeclaration && !directive_namespaces.Contains(a.Name.NamespaceName))
                    set.Add(a.Name.LocalName);
            }

            foreach (var c in element.Elements())
            {
                int dot = c.Name.LocalName.IndexOf('.');
                if (dot >= 0)
                    set.Add(c.Name.LocalName.Substring(dot + 1));
            }

            return set;
        }

        private void emitAddChild(string v, INamedTypeSymbol parent, ITypeSymbol child, string c, XElement childElement, bool isRoot)
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

            // Panels expose Add(Drawable) and items controls Add(object) without being osu!framework containers.
            var add = findMethods(parent, "Add").FirstOrDefault(m => !m.IsStatic && m.Parameters.Length == 1 && implicitly(child, m.Parameters[0].Type));

            if (add != null)
            {
                line($"{v}.Add({c});");
                return;
            }

            // Single-child controls (Border, Button, ScrollViewer) take their content through Child.
            if (findMember(parent, "Child") is IPropertySymbol childProperty && implicitly(child, childProperty.Type))
            {
                line($"{v}.Child = {c};");
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
            var textMember = findMember(type, "Text") ?? findMember(type, "Content");

            if (textMember == null || memberType(textMember) == null)
            {
                report(Diagnostics.NoChildren, element, type.Name + " (text content needs a Text or Content property)");
                return;
            }

            emitAssignment(v, type, textMember, content, element, element, element, scope);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Sets one property: a member of the element, an attached property (<c>Grid.Row</c>),
        /// or a global property every element accepts (<c>HorizontalAlignment</c>, <c>ToolTip</c>).
        /// </summary>
        private void emitProperty(string v, INamedTypeSymbol type, PropertyValue value, Scope scope)
        {
            string name = value.Name;
            int dot = name.IndexOf('.');

            if (dot > 0)
            {
                string ownerName = name.Substring(0, dot);
                string memberName = name.Substring(dot + 1);
                var owner = resolveType(value.Namespace.Length > 0 ? value.Namespace : value.Context.GetDefaultNamespace().NamespaceName, ownerName);

                if (owner == null)
                {
                    report(Diagnostics.UnknownType, value.Source, ownerName, "");
                    return;
                }

                // Button.Background on a Button is just Background (and <Bag.Tags> fills the Tags collection).
                if (inherits(type, owner))
                {
                    if (findMember(type, memberName) is ISymbol own)
                    {
                        emitMemberValue(v, type, own, value, scope);
                        return;
                    }

                    if (value.Element != null && findCollection(type, memberName) is IPropertySymbol ownCollection)
                    {
                        emitCollection(v, type, ownCollection, value, scope);
                        return;
                    }

                    // <Border.ContextMenu> is WPF's spelling of a property every element accepts.
                    if (!attachedSetters(owner, memberName, type).Any()
                        && globalProviders().FirstOrDefault(p => attachedSetters(p, memberName, type).Any()) is INamedTypeSymbol globalOwner)
                    {
                        emitAttachedValue(v, type, globalOwner, memberName, value, scope);
                        return;
                    }
                }

                emitAttachedValue(v, type, owner, memberName, value, scope);
                return;
            }

            var member = findMember(type, name);

            if (member != null)
            {
                emitMemberValue(v, type, member, value, scope);
                return;
            }

            if (value.Element != null && findCollection(type, name) is IPropertySymbol collection)
            {
                emitCollection(v, type, collection, value, scope);
                return;
            }

            var provider = globalProviders().FirstOrDefault(p => attachedSetters(p, name, type).Any());

            if (provider != null)
            {
                emitAttachedValue(v, type, provider, name, value, scope);
                return;
            }

            report(Diagnostics.UnknownMember, value.Source, type.Name, name);
        }

        private void emitPropertyElement(string v, INamedTypeSymbol type, XElement property, Scope scope)
        {
            emitProperty(v, type, new PropertyValue
            {
                Name = property.Name.LocalName,
                Namespace = property.Name.NamespaceName,
                Element = property,
                Source = property,
                Context = property,
                Owner = property.Parent,
            }, scope);
        }

        private void emitMemberValue(string v, INamedTypeSymbol type, ISymbol member, PropertyValue value, Scope scope)
        {
            if (value.Element == null)
            {
                emitAssignment(v, type, member, value.Text, value.Source, value.Context, value.Owner, scope);
                return;
            }

            var children = value.Element.Elements().ToList();
            string text = string.Concat(value.Element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
            var target = memberType(member);

            // <ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate></ListBox.ItemsPanel>: the wrapper is optional.
            if (children.Count == 1 && children[0].Name.LocalName == "ItemsPanelTemplate" && !children[0].Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
                children = children[0].Elements().ToList();

            if (children.Count == 0)
            {
                if (text.Length > 0)
                    emitAssignment(v, type, member, text, value.Element, value.Context, value.Owner, scope);
                return;
            }

            // Several children, or a child the property can't hold, add to the collection the property already has,
            // as WPF does for <Grid.RowDefinitions><RowDefinition /></Grid.RowDefinitions>.
            if (member is IPropertySymbol { GetMethod: { } } && target != null && findMethods(target, "Add").Any(m => !m.IsStatic && m.Parameters.Length == 1)
                && (children.Count > 1 || !childAssignable(children[0], target)))
            {
                foreach (var child in children)
                {
                    var (item, itemType) = emitValueElement(child, scope);
                    if (item == null)
                        continue;

                    if (findMethods(target, "Add").Any(m => !m.IsStatic && m.Parameters.Length == 1 && implicitly(itemType, m.Parameters[0].Type)))
                        line($"{v}.{member.Name}.Add({item});");
                    else
                        report(Diagnostics.TypeMismatch, child, itemType.ToDisplayString(), member.Name, target.ToDisplayString(), "");
                }

                return;
            }

            if (children.Count > 1)
            {
                report(Diagnostics.BadTemplate, value.Element, $"'{member.Name}' takes a single element");
                return;
            }

            if (member is IEventSymbol || target == null)
            {
                report(Diagnostics.UnknownMember, value.Element, type.Name, member.Name + " (events take a method name)");
                return;
            }

            if (isTemplateType(target))
            {
                string t = emitTemplate(children[0], target, templateHint(type, value.Owner, member.Name, target, scope), scope);
                if (t != null)
                    assign(v, member, t);
                return;
            }

            var (c, childType) = emitValueElement(children[0], scope);
            if (c != null)
                assignChecked(v, member, c, childType, children[0]);
        }

        /// <summary>Whether the element's type (resolved quietly) can be assigned to <paramref name="target"/>.</summary>
        private bool childAssignable(XElement child, ITypeSymbol target)
        {
            var type = directive_namespaces.Contains(child.Name.NamespaceName)
                ? directiveType(child.Name.LocalName)
                : resolveType(child.Name.NamespaceName, child.Name.LocalName);

            return type == null || implicitly(type, bindableClassValueType(target) ?? target) || isTemplateType(target);
        }

        /// <summary>A read-only collection property filled from a property element: <c>&lt;GridView.Columns&gt;</c>.</summary>
        private void emitCollection(string v, INamedTypeSymbol type, IPropertySymbol collection, PropertyValue value, Scope scope)
        {
            foreach (var child in value.Element.Elements())
            {
                var (c, childType) = emitValueElement(child, scope);
                if (c == null)
                    continue;

                bool canAdd = findMethods(collection.Type, "Add").Any(m => !m.IsStatic && m.Parameters.Length == 1 && implicitly(childType, m.Parameters[0].Type));

                if (!canAdd)
                    report(Diagnostics.TypeMismatch, child, childType.Name, collection.Name, collection.Type.ToDisplayString(), "");
                else
                    line($"{v}.{collection.Name}.Add({c});");
            }
        }

        private void emitAssignment(string v, INamedTypeSymbol type, ISymbol member, string value, XObject source, XElement context, XElement owner, Scope scope)
        {
            if (Markup.IsMarkup(value))
            {
                var markup = Markup.Parse(value, out string error);

                if (markup == null)
                {
                    report(Diagnostics.BadMarkup, source, error);
                    return;
                }

                switch (markup.Extension)
                {
                    case Markup.Bind:
                    case Markup.TemplateBinding:
                        emitBinding(v, type, member, markup, source, owner, scope);
                        return;

                    case Markup.StaticResource:
                        emitResourceValue(v, type, member, markup.Path, source, context, owner, scope);
                        return;

                    case Markup.Static:
                    {
                        var (expr, exprType) = staticMember(markup.Path, context, source);
                        if (expr == null)
                            return;

                        string converted = convertValue(expr, exprType, valueType(member), new Markup(), source);
                        if (converted != null)
                            assign(v, member, converted);
                        return;
                    }

                    case Markup.Null:
                    {
                        var target = valueType(member);
                        if (target.IsValueType && target.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
                            report(Diagnostics.BadLiteral, source, "{x:Null}", target.ToDisplayString(), " (value types can't be null)");
                        else
                            assign(v, member, "null");
                        return;
                    }

                    default:
                        report(Diagnostics.BadMarkup, source, $"{{x:{markup.Extension}}} can't be used as a property value");
                        return;
                }
            }

            value = Markup.Unescape(value);

            if (member is IEventSymbol ev)
            {
                string handler = methodHandler((INamedTypeSymbol)ev.Type, value, source);
                if (handler != null)
                    line($"{v}.{member.Name} += {handler};");
                return;
            }

            var memberTarget = memberType(member);

            if (memberTarget is INamedTypeSymbol { TypeKind: TypeKind.Delegate } del)
            {
                string handler = methodHandler(del, value, source);
                if (handler != null)
                    line($"{v}.{member.Name} = {handler};");
                return;
            }

            if (isItemBindingType(memberTarget))
            {
                emitItemBinding(v, member, Markup.ForPath(value), templateHint(type, owner, member.Name, memberTarget, scope), source);
                return;
            }

            // DisplayMemberPath="Name": a path on a template property means "show this member as text".
            if (isTemplateType(memberTarget))
            {
                string t = emitTextTemplate(Markup.ForPath(value), templateHint(type, owner, member.Name, memberTarget, scope), scope, source, member.Name);
                if (t != null)
                    assign(v, member, t);
                return;
            }

            // A literal on a bindable member (TextBox.Text, CheckBox.IsChecked) sets its value.
            var target2 = valueType(member);
            string literal = convertLiteral(value, target2, out string hint);

            if (literal == null)
            {
                report(Diagnostics.BadLiteral, source, value, target2.ToDisplayString(), hint ?? "");
                return;
            }

            assign(v, member, literal);
        }

        /// <summary>The type a value is assigned as: the T of a Bindable&lt;T&gt; member, else the member type.</summary>
        private ITypeSymbol valueType(ISymbol member)
        {
            var target = memberType(member);
            return bindableClassValueType(target) ?? target;
        }

        /// <summary><c>v.Member = expr</c>, or <c>v.Member.Value = expr</c> for a bindable member.</summary>
        private void assign(string v, ISymbol member, string expr)
            => line(bindableClassValueType(memberType(member)) != null ? $"{v}.{member.Name}.Value = {expr};" : $"{v}.{member.Name} = {expr};");

        private void assignChecked(string v, ISymbol member, string expr, ITypeSymbol exprType, XObject source)
        {
            var target = valueType(member);

            if (exprType != null && !implicitly(exprType, target))
            {
                report(Diagnostics.TypeMismatch, source, exprType.ToDisplayString(), member.Name, target.ToDisplayString(), "");
                return;
            }

            assign(v, member, expr);
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

            string handler = lambda(delegateType, "this." + method.Name, method, null, null);

            if (handler == null)
                report(Diagnostics.UnknownMethod, source, $"'{method.Name}' must take no parameters or the same parameters as {delegateType.Name}");

            return handler;
        }

        /// <summary>
        /// Builds a lambda matching <paramref name="delegateType"/> that calls <paramref name="method"/>.
        /// Delegate arguments are passed through when the method takes the same number of parameters;
        /// otherwise the method is called with no arguments, or with the template item when it takes exactly one.
        /// </summary>
        private string lambda(INamedTypeSymbol delegateType, string target, IMethodSymbol method, string item, ITypeSymbol itemType)
        {
            var invoke = delegateType.DelegateInvokeMethod;
            var parameterNames = invoke.Parameters.Select((_, i) => "a" + i).ToArray();
            string parameters = "(" + string.Join(", ", parameterNames) + ")";

            string args;

            if (method.Parameters.Length == invoke.Parameters.Length)
                args = string.Join(", ", parameterNames);
            else if (method.Parameters.Length == 1 && item != null && implicitly(itemType, method.Parameters[0].Type))
                args = item;
            else if (method.Parameters.Length == 0)
                args = string.Empty;
            else
                return null;

            return $"{parameters} => {target}({args})";
        }

        #endregion
    }
}
