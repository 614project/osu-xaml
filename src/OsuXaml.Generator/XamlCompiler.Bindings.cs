using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OsuXaml.Generator
{
    internal sealed partial class XamlCompiler
    {
        #region Bindings

        private void emitBinding(string v, INamedTypeSymbol type, ISymbol member, Markup bind, XObject source, XElement owner, Scope scope)
        {
            var target = member is IEventSymbol ? null : memberType(member);

            if (target != null && isItemBindingType(target) && bind.Extension == Markup.Bind)
            {
                emitItemBinding(v, member, bind, templateHint(type, owner, member.Name, target, scope), source);
                return;
            }

            // DisplayMemberBinding="{Bind Name}": a binding on a template property shows that member as text for each item.
            if (target != null && isTemplateType(target) && bind.Extension == Markup.Bind)
            {
                string t = emitTextTemplate(bind, templateHint(type, owner, member.Name, target, scope), scope, source, member.Name);
                if (t != null)
                    assign(v, member, t);
                return;
            }

            if (!bindingSource(bind, source, scope, out string dc, out ITypeSymbol dcType, out bool toNamedElement))
                return;

            if (!toNamedElement)
            {
                emitBindingCore(v, member, bind, source, scope, dc, dcType);
                return;
            }

            // Named elements may be declared later in the document, so these bindings run at the end of InitializeComponent.
            var saved = body;
            int savedIndent = indent;
            body = deferred;
            indent = 3;

            emitBindingCore(v, member, bind, source, scope, dc, dcType);

            body = saved;
            indent = savedIndent;
        }

        /// <summary>Resolves what a binding reads from: the data context, Source=Root, ElementName or the templated parent.</summary>
        private bool bindingSource(Markup bind, XObject source, Scope scope, out string dc, out ITypeSymbol dcType, out bool toNamedElement)
        {
            dc = null;
            dcType = null;
            toNamedElement = false;

            string elementName = bind.Get("ElementName");
            string sourceOption = bind.Get("Source");

            if (bind.Extension == Markup.TemplateBinding)
            {
                if (scope.TemplatedParent == null)
                {
                    report(Diagnostics.BadMarkup, source, "{TemplateBinding} can only be used inside a ControlTemplate");
                    return false;
                }

                dc = scope.TemplatedParent;
                dcType = scope.TemplatedParentType;
                return true;
            }

            if (elementName != null)
            {
                if (scope.InTemplate)
                {
                    report(Diagnostics.BadMarkup, source, "ElementName can't be used inside a template. Use Source=Root to reach the page's data context");
                    return false;
                }

                if (!names.TryGetValue(elementName, out var named))
                {
                    report(Diagnostics.BadMarkup, source, $"No element is named '{elementName}' (x:Name)");
                    return false;
                }

                dc = named == rootElement ? "this" : "this." + elementName;
                dcType = named == rootElement ? rootType : elementType(named);
                toNamedElement = true;
                return dcType != null;
            }

            if (sourceOption != null && !sourceOption.Equals("Root", StringComparison.OrdinalIgnoreCase))
            {
                report(Diagnostics.BadMarkup, source, $"Unknown binding Source '{sourceOption}'. Use Source=Root, or ElementName=name");
                return false;
            }

            bool fromRoot = sourceOption != null;
            dc = fromRoot ? scope.RootDataContext : scope.DataContext;
            dcType = fromRoot ? scope.RootDataContextType : scope.DataContextType;

            if (dc == null)
            {
                report(Diagnostics.BadMarkup, source, "{Bind} needs a data context. Add x:DataType=\"prefix:ViewModelType\" to the root element");
                return false;
            }

            return true;
        }

        private void emitBindingCore(string v, ISymbol member, Markup bind, XObject source, Scope scope, string dc, ITypeSymbol dcType)
        {
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

                line(member is IEventSymbol ? $"{firstBuildOnly(v)}{v}.{member.Name} += {handler};" : $"{v}.{member.Name} = {handler};");
                return;
            }

            var target = memberType(member);

            if (target == null || member is IEventSymbol)
            {
                report(Diagnostics.UnknownMember, source, member.ContainingType.Name, member.Name + " (events bind to methods)");
                return;
            }

            string mode = bind.Get("Mode") ?? "Default";
            bool twoWay = mode.Equals("TwoWay", StringComparison.OrdinalIgnoreCase);
            bool oneWay = mode.Equals("OneWay", StringComparison.OrdinalIgnoreCase) || mode.Equals("OneTime", StringComparison.OrdinalIgnoreCase);

            // A control's own bindable (Checkbox.Current, ListBox.SelectedItem) links to a view model bindable in both directions.
            var targetBindableValue = bindableClassValueType(target);

            if (targetBindableValue != null && !oneWay)
            {
                var sourceBindableValue = bindableClassValueType(sourceType);

                if (sourceBindableValue != null && SymbolEqualityComparer.Default.Equals(sourceBindableValue, targetBindableValue))
                {
                    line($"{firstBuildOnly(v)}global::OsuXaml.XamlBinding.TwoWay({v}.{member.Name}, {expr});");
                    return;
                }

                if (sourceBindableValue != null && targetBindableValue.SpecialType == SpecialType.System_Object)
                {
                    line($"global::OsuXaml.XamlBinding.TwoWayObject({scope.Bindings}, {v}.{member.Name}, {expr});");
                    return;
                }

                // CheckBox.IsChecked is a bool? as in WPF, and still binds two-way to a view model's Bindable<bool>.
                if (sourceBindableValue != null && targetBindableValue is INamedTypeSymbol { OriginalDefinition: { SpecialType: SpecialType.System_Nullable_T } } nullable
                    && SymbolEqualityComparer.Default.Equals(nullable.TypeArguments[0], sourceBindableValue))
                {
                    line($"global::OsuXaml.XamlBinding.TwoWayNullable({scope.Bindings}, {v}.{member.Name}, {expr});");
                    return;
                }

                if (twoWay || (sourceBindableValue != null && !implicitly(sourceBindableValue, targetBindableValue)))
                {
                    report(Diagnostics.TypeMismatch, source, describe(sourceType), member.Name, target.ToDisplayString(),
                        $" (two-way bindings need a Bindable<{targetBindableValue.ToDisplayString()}> source)");
                    return;
                }
            }

            if (twoWay && targetBindableValue == null)
            {
                report(Diagnostics.TypeMismatch, source, describe(sourceType), member.Name, target.ToDisplayString(), " (Mode=TwoWay needs a Bindable target such as Current)");
                return;
            }

            var assignTo = targetBindableValue ?? target;
            string assignTarget = targetBindableValue != null ? $"{v}.{member.Name}.Value" : $"{v}.{member.Name}";

            // ItemsSource="{Bind People}": the control gets a bound copy of the list, owned by the view.
            var listItem = bindableListItemType(sourceType);

            // A control's own list (Calendar.SelectedDates) links to a view model list of the same type in both directions.
            if (listItem != null && !oneWay && isBindableListClass(target) && isBindableListClass(sourceType)
                && SymbolEqualityComparer.Default.Equals(bindableListItemType(target), listItem))
            {
                line($"global::OsuXaml.XamlBinding.TwoWayList({v}.{member.Name}, {expr});");
                return;
            }

            if (listItem != null && isEnumerable(assignTo) && bind.Get("Converter") == null)
            {
                var copyType = compilation.GetTypeByMetadataName("osu.Framework.Bindables.IBindableList`1")?.Construct(listItem);

                if (copyType != null && implicitly(copyType, assignTo))
                {
                    line($"{assignTarget} = global::OsuXaml.XamlBinding.ListCopy({scope.Bindings}, {expr});");
                    return;
                }
            }

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
                    // Typed as the property, so a Colour4 animating a Brush property transforms as a Brush.
                    line($"{typeName(assignTo)} value = {converted};");
                    line($"if (initial) {assignTarget} = value;");
                    line($"else global::osu.Framework.Graphics.TransformableExtensions.TransformTo({v}, nameof({v}.{member.Name}), value, {literalDouble(duration)}, global::osu.Framework.Graphics.Easing.{easing});");
                    indent--;
                    line("});");
                    return;
                }

                line($"global::OsuXaml.XamlBinding.OneWay({scope.Bindings}, {expr}, (x, _) => {assignTarget} = {converted});");
                return;
            }

            // A plain property or field is read once.
            string once = convertValue(expr, sourceType, assignTo, bind, source);
            if (once != null)
                line($"{assignTarget} = {once};");
        }

        /// <summary>ItemsSource + ItemTemplate on a panel or osu!framework container: one child per list item.</summary>
        private void emitItems(string v, INamedTypeSymbol type, XElement element, XAttribute itemsSource, XElement template, Scope scope)
        {
            if (itemsSource == null || template == null)
            {
                report(Diagnostics.BadTemplate, element, "ItemsSource and an ItemTemplate property element must be used together");
                return;
            }

            var itemsHost = compilation.GetTypeByMetadataName("OsuXaml.Controls.IItemsHost");

            if (containerChildType(type)?.ToDisplayString() != "osu.Framework.Graphics.Drawable" && !inherits(type, itemsHost))
            {
                report(Diagnostics.BadTemplate, element, $"ItemsSource needs a panel, an items control or a Container<Drawable> such as FillFlowContainer, but '{type.Name}' is none of them");
                return;
            }

            var bind = Markup.IsMarkup(itemsSource.Value) ? Markup.Parse(itemsSource.Value, out _) : null;

            if (bind == null || bind.Extension != Markup.Bind)
            {
                report(Diagnostics.BadTemplate, itemsSource, "ItemsSource must be a binding, e.g. ItemsSource=\"{Bind Items}\"");
                return;
            }

            if (!bindingSource(bind, itemsSource, scope, out string dc, out ITypeSymbol dcType, out bool toNamedElement))
                return;

            if (toNamedElement)
            {
                report(Diagnostics.BadTemplate, itemsSource, "ItemsSource on a panel can't use ElementName");
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

            // <DataTemplate> around the template body is optional here.
            var templateRoot = template;

            if (isTemplateElement(template))
            {
                var roots = template.Elements().Where(e => e.Name.LocalName.IndexOf('.') < 0).ToList();

                if (roots.Count != 1)
                {
                    report(Diagnostics.BadTemplate, template, "A DataTemplate must contain exactly one element");
                    return;
                }

                templateRoot = roots[0];
            }

            string item = "item" + counter;
            string itemScope = "scope" + counter++;

            var inner = scope.With(s =>
            {
                s.DataContext = item;
                s.DataContextType = itemType;
                s.Item = item;
                s.ItemType = itemType;
                s.Bindings = itemScope;
                s.ItemsType = null;
                s.TemplatedParent = null;
                s.InTemplate = true;
            });

            line($"global::OsuXaml.XamlBinding.Items<{typeName(itemType)}>({scope.Bindings}, {v}, {expr}, ({item}, {itemScope}) =>");
            line("{");
            indent++;
            var (created, _) = emitValueElement(templateRoot, inner);
            line($"return {created ?? "null"};");
            indent--;
            line("});");
        }

        /// <summary>Resolves a dotted path such as <c>Player.Volume</c> against the data context type.</summary>
        private bool resolvePath(string dc, ITypeSymbol dcType, string bindingPath, XObject source, out string expr, out ISymbol last, out ITypeSymbol type)
        {
            expr = dc;
            last = null;
            type = dcType;

            // {Bind} with no path binds the data context itself, e.g. each string of an ItemsSource.
            if (string.IsNullOrEmpty(bindingPath))
                return true;

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
                    report(Diagnostics.UnknownPath, source, type.ToDisplayString(), segment + (type.SpecialType == SpecialType.System_Object ? " (the item type is unknown here; add x:DataType)" : ""));
                    return false;
                }

                expr += "." + segment;
                last = member;
                type = member is IMethodSymbol m ? m.ReturnType : memberType(member);
            }

            return true;
        }

        /// <summary>The type a path ends at, without reporting anything; used to infer template item types.</summary>
        private ITypeSymbol tryResolvePath(ITypeSymbol type, string bindingPath)
        {
            if (type == null)
                return null;

            if (string.IsNullOrEmpty(bindingPath))
                return type;

            foreach (string segment in bindingPath.Split('.'))
            {
                var member = findReadable(type, segment);

                if (member == null || member is IMethodSymbol)
                    return null;

                type = memberType(member);
            }

            return type;
        }

        /// <summary>
        /// Converts a bound value to the target type: Converter first, then StringFormat, then ToString for text targets.
        /// Anything that still has no implicit conversion is a build error rather than a runtime surprise.
        /// </summary>
        private string convertValue(string expr, ITypeSymbol from, ITypeSymbol to, Markup bind, XObject source)
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

            if (!implicitly(from, to))
            {
                report(Diagnostics.TypeMismatch, source, from.ToDisplayString(), source is XAttribute a ? a.Name.LocalName : "value", to.ToDisplayString(), " (add a Converter)");
                return null;
            }

            return expr;
        }

        /// <summary>
        /// A converter is a one-parameter method: <c>Converter=DoneToAlpha</c> looks on the x:Class and then
        /// on OsuXaml.Converters, <c>Converter=local:Converters.DoneToAlpha</c> on a static class.
        /// </summary>
        private (string Call, IMethodSymbol Method)? findConverter(string name, ITypeSymbol input, XObject source)
        {
            int dot = name.LastIndexOf('.');

            if (dot >= 0)
            {
                var owner = resolveTypeName(name.Substring(0, dot), source as XElement ?? source.Parent);

                if (owner == null)
                {
                    report(Diagnostics.UnknownType, source, name.Substring(0, dot), "");
                    return null;
                }

                var m = converterMethod(owner, name.Substring(dot + 1), input);

                if (m == null)
                {
                    report(Diagnostics.UnknownMethod, source, $"Converter '{name}' must be a method on '{owner.Name}' taking one {input.ToDisplayString()} parameter");
                    return null;
                }

                return (typeName(owner) + "." + m.Name, m);
            }

            var own = converterMethod(rootType, name, input);
            if (own != null)
                return (name, own);

            var builtIn = compilation.GetTypeByMetadataName("OsuXaml.Converters");
            var shared = builtIn == null ? null : converterMethod(builtIn, name, input);
            if (shared != null)
                return (typeName(builtIn) + "." + name, shared);

            report(Diagnostics.UnknownMethod, source, $"Converter '{name}' must be a method on '{rootType.Name}' (or OsuXaml.Converters) taking one {input.ToDisplayString()} parameter");
            return null;
        }

        private IMethodSymbol converterMethod(INamedTypeSymbol owner, string name, ITypeSymbol input)
            => owner.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == 1 && implicitly(input, m.Parameters[0].Type) && canAccess(m));

        #endregion

        #region Attached and global properties

        /// <summary>Emits <c>Owner.SetName(element, value)</c> for a literal, a binding, a resource or an element.</summary>
        private void emitAttachedValue(string v, ITypeSymbol elementType, INamedTypeSymbol owner, string name, PropertyValue value, Scope scope)
        {
            var setters = attachedSetters(owner, name, elementType).ToList();

            if (setters.Count == 0)
            {
                report(Diagnostics.UnknownMember, value.Source, owner.Name, name + " (attached properties need a static Set" + name + "(Drawable, value) method)");
                return;
            }

            string call(IMethodSymbol setter) => typeName(setter.ContainingType) + "." + setter.Name;

            string text = value.Text;

            if (value.Element != null)
            {
                var children = value.Element.Elements().ToList();

                if (children.Count == 1)
                {
                    var (c, childType) = emitValueElement(children[0], scope);
                    if (c == null)
                        return;

                    var setter = setters.FirstOrDefault(s => implicitly(childType, s.Parameters[1].Type));

                    if (setter == null)
                        report(Diagnostics.TypeMismatch, children[0], childType.ToDisplayString(), name, setters[0].Parameters[1].Type.ToDisplayString(), "");
                    else
                        line($"{call(setter)}({v}, {c});");

                    return;
                }

                text = textContent(value.Element);
            }

            if (text == null)
                return;

            if (!Markup.IsMarkup(text))
            {
                string literalText = Markup.Unescape(text);
                string hint = null;

                foreach (var setter in setters)
                {
                    string literal = convertLiteral(literalText, setter.Parameters[1].Type, out hint);

                    if (literal != null)
                    {
                        line($"{call(setter)}({v}, {literal});");
                        return;
                    }
                }

                report(Diagnostics.BadLiteral, value.Source, text, setters[0].Parameters[1].Type.ToDisplayString(), hint ?? "");
                return;
            }

            var markup = Markup.Parse(text, out string error);

            if (markup == null)
            {
                report(Diagnostics.BadMarkup, value.Source, error);
                return;
            }

            switch (markup.Extension)
            {
                case Markup.StaticResource:
                {
                    var resource = findResource(value.Context, markup.Path, value.Source);
                    if (resource == null)
                        return;

                    if (isLiteralResource(resource))
                    {
                        foreach (var setter in setters)
                        {
                            string literal = convertLiteral(resource.Value, setter.Parameters[1].Type, out _);

                            if (literal != null)
                            {
                                line($"{call(setter)}({v}, {literal});");
                                return;
                            }
                        }

                        report(Diagnostics.BadLiteral, value.Source, resource.Value, setters[0].Parameters[1].Type.ToDisplayString(), "");
                        return;
                    }

                    var (c, childType) = emitValueElement(resource, scope);
                    if (c == null)
                        return;

                    var match = setters.FirstOrDefault(s => implicitly(childType, s.Parameters[1].Type));

                    if (match == null)
                        report(Diagnostics.TypeMismatch, value.Source, childType.ToDisplayString(), name, setters[0].Parameters[1].Type.ToDisplayString(), "");
                    else
                        line($"{call(match)}({v}, {c});");

                    return;
                }

                case Markup.Static:
                {
                    var (expr, exprType) = staticMember(markup.Path, value.Context, value.Source);
                    if (expr == null)
                        return;

                    var match = setters.FirstOrDefault(s => implicitly(exprType, s.Parameters[1].Type)) ?? setters[0];
                    string converted = convertValue(expr, exprType, match.Parameters[1].Type, new Markup(), value.Source);
                    if (converted != null)
                        line($"{call(match)}({v}, {converted});");
                    return;
                }

                case Markup.Bind:
                case Markup.TemplateBinding:
                {
                    if (!bindingSource(markup, value.Source, scope, out string dc, out ITypeSymbol dcType, out bool toNamedElement))
                        return;

                    var saved = body;
                    int savedIndent = indent;

                    if (toNamedElement)
                    {
                        body = deferred;
                        indent = 3;
                    }

                    if (resolvePath(dc, dcType, markup.Path, value.Source, out string expr, out _, out ITypeSymbol sourceType))
                    {
                        var bound = bindableValueType(sourceType);
                        var from = bound ?? sourceType;
                        var match = setters.FirstOrDefault(s => implicitly(from, s.Parameters[1].Type)) ?? setters[0];
                        string converted = convertValue(bound != null ? "x" : expr, from, match.Parameters[1].Type, markup, value.Source);

                        if (converted != null)
                        {
                            line(bound != null
                                ? $"global::OsuXaml.XamlBinding.OneWay({scope.Bindings}, {expr}, (x, _) => {call(match)}({v}, {converted}));"
                                : $"{call(match)}({v}, {converted});");
                        }
                    }

                    body = saved;
                    indent = savedIndent;
                    return;
                }

                default:
                    report(Diagnostics.BadMarkup, value.Source, $"{{x:{markup.Extension}}} can't be used for '{name}'");
                    return;
            }
        }

        /// <summary>Static <c>SetName(element, value)</c> methods on <paramref name="owner"/> or a base type, as C# would find them:
        /// <c>VirtualizingStackPanel.IsVirtualizing</c> uses the setter declared on VirtualizingPanel.</summary>
        private IEnumerable<IMethodSymbol> attachedSetters(INamedTypeSymbol owner, string name, ITypeSymbol elementType)
        {
            for (var type = owner; type != null; type = type.BaseType)
            {
                var setters = type.GetMembers("Set" + name).OfType<IMethodSymbol>()
                                  .Where(m => m.IsStatic && m.Parameters.Length == 2 && implicitly(elementType, m.Parameters[0].Type) && canAccess(m))
                                  .ToList();

                if (setters.Count > 0)
                    return setters;
            }

            return Enumerable.Empty<IMethodSymbol>();
        }

        private List<INamedTypeSymbol> providers;

        /// <summary>Static classes registered with [assembly: XamlGlobalProperties(typeof(...))], whose setters every element accepts.</summary>
        private List<INamedTypeSymbol> globalProviders()
        {
            if (providers != null)
                return providers;

            providers = new List<INamedTypeSymbol>();

            foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
            {
                foreach (var attribute in assembly.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() == "OsuXaml.XamlGlobalPropertiesAttribute"
                        && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol type)
                        providers.Add(type);
                }
            }

            // Layout (alignment, visibility) goes first, then anything else in a stable order.
            providers = providers.OrderBy(p => p.ToDisplayString() == "OsuXaml.Controls.Layout" ? 0 : 1).ThenBy(p => p.ToDisplayString()).ToList();
            return providers;
        }

        private bool implicitly(ITypeSymbol from, ITypeSymbol to) => from != null && to != null && ((CSharpCompilation)compilation).ClassifyConversion(from, to).IsImplicit;

        private IEnumerable<IMethodSymbol> findMethods(ITypeSymbol type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var m in t.GetMembers(name).OfType<IMethodSymbol>())
                {
                    if (canAccess(m))
                        yield return m;
                }
            }

            if (type.TypeKind == TypeKind.Interface)
            {
                foreach (var i in type.AllInterfaces)
                {
                    foreach (var m in i.GetMembers(name).OfType<IMethodSymbol>())
                        yield return m;
                }
            }
        }

        #endregion
    }
}
