using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OsuXaml.Generator
{
    internal sealed partial class XamlCompiler
    {
        #region Templates

        private bool isTemplateType(ITypeSymbol type)
            => type != null && (inherits(type, compilation.GetTypeByMetadataName("OsuXaml.DataTemplate")) || inherits(type, compilation.GetTypeByMetadataName("OsuXaml.ControlTemplate")));

        private static bool isTemplateElement(XElement element)
            => !element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal)
               && (element.Name.LocalName == "DataTemplate" || element.Name.LocalName == "HierarchicalDataTemplate" || element.Name.LocalName == "ControlTemplate");

        /// <summary>
        /// The item type a template on <paramref name="memberName"/> should expect when it doesn't say x:DataType:
        /// ContentTemplate follows the type bound to Content (HeaderTemplate to Header, ...), ControlTemplate the element
        /// itself, and everything else the items of the nearest ItemsSource.
        /// </summary>
        private ITypeSymbol templateHint(INamedTypeSymbol elementType, XElement owner, string memberName, ITypeSymbol templateType, Scope scope)
        {
            if (inherits(templateType, compilation.GetTypeByMetadataName("OsuXaml.ControlTemplate")))
                return elementType;

            if (owner != null && memberName.EndsWith("Template", StringComparison.Ordinal) && memberName != item_template)
            {
                string contentName = memberName.Substring(0, memberName.Length - "Template".Length);
                var bound = boundType(owner, contentName, scope);
                if (bound != null)
                    return bound;
            }

            return scope.ItemsType;
        }

        /// <summary>The value type of <c>{Bind}</c> on an attribute, without reporting anything.</summary>
        private ITypeSymbol boundType(XElement owner, string attributeName, Scope scope)
        {
            var attribute = owner.Attribute(attributeName);
            if (attribute == null || !Markup.IsMarkup(attribute.Value))
                return null;

            var markup = Markup.Parse(attribute.Value, out _);
            if (markup == null || markup.Extension != Markup.Bind)
                return null;

            ITypeSymbol dcType;
            string elementName = markup.Get("ElementName");

            if (elementName != null)
                dcType = names.TryGetValue(elementName, out var named) ? (named == rootElement ? rootType : resolveType(named.Name.NamespaceName, named.Name.LocalName)) : null;
            else
                dcType = markup.Get("Source") != null ? scope.RootDataContextType : scope.DataContextType;

            var type = tryResolvePath(dcType, markup.Path);
            return type == null ? null : bindableValueType(type) ?? type;
        }

        /// <summary>The element type of an ItemsSource on <paramref name="element"/>, if it can be told at build time.</summary>
        private ITypeSymbol itemsTypeOf(XElement element, Scope scope)
        {
            var attribute = element.Attribute(items_source);
            if (attribute == null)
                return null;

            if (!Markup.IsMarkup(attribute.Value))
                return compilation.GetSpecialType(SpecialType.System_String);

            var markup = Markup.Parse(attribute.Value, out _);
            if (markup == null || markup.Extension != Markup.Bind)
                return null;

            ITypeSymbol dcType;
            string elementName = markup.Get("ElementName");

            if (elementName != null)
                dcType = names.TryGetValue(elementName, out var named) ? (named == rootElement ? rootType : resolveType(named.Name.NamespaceName, named.Name.LocalName)) : null;
            else
                dcType = markup.Get("Source") != null ? scope.RootDataContextType : scope.DataContextType;

            var type = tryResolvePath(dcType, markup.Path);
            return type == null ? null : enumerableItemType(type);
        }

        private ITypeSymbol enumerableItemType(ITypeSymbol type)
        {
            var listItem = bindableListItemType(type);
            if (listItem != null)
                return listItem;

            var value = bindableValueType(type);
            return enumerableElement(value ?? type);
        }

        private ITypeSymbol enumerableElement(ITypeSymbol type)
        {
            if (type == null || type.SpecialType == SpecialType.System_String)
                return null;

            if (type is IArrayTypeSymbol array)
                return array.ElementType;

            var element = genericInterfaceArgument(type, "System.Collections.Generic.IEnumerable<T>");
            if (element != null)
                return element;

            return isEnumerable(type) ? compilation.ObjectType : null;
        }

        private bool isEnumerable(ITypeSymbol type)
        {
            var enumerable = compilation.GetSpecialType(SpecialType.System_Collections_IEnumerable);
            return type.SpecialType != SpecialType.System_String && (SymbolEqualityComparer.Default.Equals(type, enumerable) || type.AllInterfaces.Contains(enumerable, SymbolEqualityComparer.Default));
        }

        /// <summary>
        /// Compiles a template into a local variable: <c>&lt;DataTemplate x:DataType="local:Person"&gt;</c>,
        /// <c>&lt;HierarchicalDataTemplate ItemsSource="{Bind Children}"&gt;</c>, <c>&lt;ControlTemplate TargetType="Button"&gt;</c>,
        /// or a bare element (the DataTemplate wrapper is optional). Returns the variable name.
        /// </summary>
        private string emitTemplate(XElement element, ITypeSymbol templateType, ITypeSymbol hint, Scope scope)
        {
            var controlTemplate = compilation.GetTypeByMetadataName("OsuXaml.ControlTemplate");
            bool control = inherits(templateType, controlTemplate);
            bool hierarchical = false;
            var content = element;
            var itemType = hint;
            XAttribute childrenSource = null;

            if (isTemplateElement(element))
            {
                string kind = element.Name.LocalName;

                if (control != (kind == "ControlTemplate"))
                {
                    report(Diagnostics.BadTemplate, element, control ? "This property takes a <ControlTemplate>" : $"This property takes a <DataTemplate>, not a <{kind}>");
                    return null;
                }

                hierarchical = kind == "HierarchicalDataTemplate";

                string dataType = directive(element, "DataType") ?? element.Attribute("DataType")?.Value ?? element.Attribute("TargetType")?.Value;

                if (dataType != null)
                {
                    itemType = resolveTypeName(stripTypeMarkup(dataType), element);

                    if (itemType == null)
                    {
                        report(Diagnostics.UnknownType, element, dataType, "");
                        return null;
                    }
                }

                if (hierarchical)
                    childrenSource = element.Attribute(items_source);

                var roots = element.Elements().Where(e => e.Name.LocalName.IndexOf('.') < 0).ToList();

                if (roots.Count != 1)
                {
                    report(Diagnostics.BadTemplate, element, $"A {kind} must contain exactly one element");
                    return null;
                }

                content = roots[0];
            }

            itemType ??= compilation.ObjectType;

            string t = "t" + counter;
            string item = "item" + counter;
            string itemScope = "scope" + counter++;

            string className = control ? "ControlTemplate" : hierarchical ? "HierarchicalDataTemplate" : "DataTemplate";

            var inner = control
                ? scope.With(s =>
                {
                    s.TemplatedParent = item;
                    s.TemplatedParentType = itemType;
                    s.Bindings = itemScope;
                    s.ItemsType = null;
                    s.InTemplate = true;
                })
                : scope.With(s =>
                {
                    s.DataContext = item;
                    s.DataContextType = itemType;
                    s.Item = item;
                    s.ItemType = itemType;
                    s.TemplatedParent = null;
                    s.Bindings = itemScope;
                    s.ItemsType = null;
                    s.InTemplate = true;
                });

            line($"var {t} = new global::OsuXaml.{className}<{typeName(itemType)}>(({item}, {itemScope}) =>");
            line("{");
            indent++;
            var (created, _) = emitValueElement(content, inner);
            line($"return {created ?? "null"};");
            indent--;

            if (!hierarchical)
            {
                line("});");
                return t;
            }

            string children = childrenExpression(childrenSource, element, itemType, inner);
            line($"}}, ({item}, {itemScope}) => {children ?? "null"});");
            return t;
        }

        /// <summary>The children of one tree item for HierarchicalDataTemplate's ItemsSource.</summary>
        private string childrenExpression(XAttribute source, XElement template, ITypeSymbol itemType, Scope inner)
        {
            if (source == null)
            {
                report(Diagnostics.BadTemplate, template, "A HierarchicalDataTemplate needs ItemsSource=\"{Bind Children}\"");
                return null;
            }

            var markup = Markup.IsMarkup(source.Value) ? Markup.Parse(source.Value, out _) : null;

            if (markup == null || markup.Extension != Markup.Bind)
            {
                report(Diagnostics.BadTemplate, source, "ItemsSource must be a binding to the item's children, e.g. ItemsSource=\"{Bind Children}\"");
                return null;
            }

            if (!resolvePath(inner.Item, itemType, markup.Path, source, out string expr, out _, out ITypeSymbol type))
                return null;

            if (bindableListItemType(type) != null)
                return $"global::OsuXaml.XamlBinding.ListCopy({inner.Bindings}, {expr})";

            var value = bindableValueType(type);

            if (value != null && isEnumerable(value))
                return expr + ".Value";

            if (isEnumerable(type))
                return expr;

            report(Diagnostics.TypeMismatch, source, describe(type), items_source, "IEnumerable", "");
            return null;
        }

        /// <summary>A template showing one member of each item as a TextBlock, for DisplayMemberPath and DisplayMemberBinding.</summary>
        private string emitTextTemplate(Markup bind, ITypeSymbol itemType, Scope scope, XObject source, string memberName)
        {
            if (itemType == null && !string.IsNullOrEmpty(bind.Path))
            {
                report(Diagnostics.BadTemplate, source, $"'{memberName}' can't tell the item type. Bind ItemsSource to a typed list (e.g. BindableList<Person>) or use an ItemTemplate with x:DataType");
                return null;
            }

            itemType ??= compilation.ObjectType;

            var textBlock = compilation.GetTypeByMetadataName("OsuXaml.Controls.TextBlock");
            var textMember = textBlock == null ? null : findMember(textBlock, "Text");

            if (textMember == null)
                return null;

            string t = "t" + counter;
            string item = "item" + counter;
            string itemScope = "scope" + counter;
            string text = "e" + counter++;

            var inner = scope.With(s =>
            {
                s.DataContext = item;
                s.DataContextType = itemType;
                s.Item = item;
                s.ItemType = itemType;
                s.TemplatedParent = null;
                s.Bindings = itemScope;
                s.ItemsType = null;
                s.InTemplate = true;
            });

            line($"var {t} = new global::OsuXaml.DataTemplate<{typeName(itemType)}>(({item}, {itemScope}) =>");
            line("{");
            indent++;
            line($"var {text} = new {typeName(textBlock)}();");

            if (bindingSource(bind, source, inner, out string dc, out ITypeSymbol dcType, out _))
                emitBindingCore(text, textMember, bind, source, inner, dc, dcType);

            line($"return {text};");
            indent--;
            line("});");
            return t;
        }

        /// <summary>
        /// An items control whose items have a known type and no ItemTemplate of its own uses the implicit
        /// <c>&lt;DataTemplate x:DataType="..."&gt;</c> resource (no x:Key) for that type, like WPF.
        /// </summary>
        private void emitImplicitItemTemplate(string v, INamedTypeSymbol type, XElement element, Scope scope, HashSet<string> local)
        {
            if (scope.ItemsType == null || element.Attribute(items_source) == null || local.Contains(item_template) || local.Contains("DisplayMemberPath"))
                return;

            if (!(findMember(type, item_template) is ISymbol member) || !isTemplateType(memberType(member)))
                return;

            var template = findResource(element, "{DataTemplate}" + scope.ItemsType.ToDisplayString(), null);
            if (template == null)
                return;

            string t = emitTemplate(template, memberType(member), scope.ItemsType, scope);
            if (t != null)
                assign(v, member, t);
        }

        /// <summary><c>ItemTemplate="{StaticResource Key}"</c> on a panel: the DataTemplate resource's element.</summary>
        private XElement resourceTemplate(XAttribute attribute)
        {
            var markup = Markup.IsMarkup(attribute.Value) ? Markup.Parse(attribute.Value, out _) : null;

            if (markup == null || markup.Extension != Markup.StaticResource)
            {
                report(Diagnostics.BadTemplate, attribute, "ItemTemplate here must be a property element or {StaticResource Key}");
                return null;
            }

            return findResource(attribute.Parent, markup.Path, attribute);
        }

        private static string stripTypeMarkup(string value)
        {
            value = value.Trim();

            if (value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal))
            {
                var markup = Markup.Parse(value, out _);
                if (markup != null && markup.Extension == Markup.Type)
                    return markup.Path;
            }

            return value;
        }

        private bool isItemBindingType(ITypeSymbol type)
            => type != null && inherits(type, compilation.GetTypeByMetadataName("OsuXaml.ItemBinding"));

        /// <summary>
        /// <c>Binding="{Bind Done}"</c> (or <c>Binding="Done"</c>) on an ItemBinding member: a typed accessor into each item,
        /// whose item type comes from the nearest ItemsSource.
        /// </summary>
        private void emitItemBinding(string v, ISymbol member, Markup bind, ITypeSymbol itemType, XObject source)
        {
            foreach (string option in bind.Options.Keys)
            {
                if (!option.Equals("Mode", StringComparison.OrdinalIgnoreCase) && !option.Equals("UpdateSourceTrigger", StringComparison.OrdinalIgnoreCase))
                {
                    report(Diagnostics.BadMarkup, source, $"'{member.Name}' takes a plain path; '{option}' isn't supported here");
                    return;
                }
            }

            if (itemType == null)
            {
                report(Diagnostics.BadTemplate, source, $"'{member.Name}' can't tell the item type. Bind ItemsSource to a typed list (e.g. BindableList<Person>)");
                return;
            }

            if (string.IsNullOrEmpty(bind.Path))
            {
                report(Diagnostics.BadMarkup, source, $"'{member.Name}' needs a path, e.g. {{Bind Name}}");
                return;
            }

            string x = "i" + counter++;

            if (!resolvePath(x, itemType, bind.Path, source, out string expr, out ISymbol last, out ITypeSymbol type) || last is IMethodSymbol)
            {
                if (last is IMethodSymbol)
                    report(Diagnostics.TypeMismatch, source, "method '" + last.Name + "'", member.Name, "a property path", "");
                return;
            }

            var bound = bindableValueType(type);
            var valueType = bound ?? type;
            string value = bound != null ? expr + ".Value" : expr;

            assign(v, member, $"new global::OsuXaml.ItemBinding<{typeName(itemType)}, {typeName(valueType)}>({SymbolDisplay.FormatLiteral(bind.Path, true)}, {x} => {expr}, {x} => {value})");
        }

        /// <summary>
        /// AutoGenerateColumns on a DataGrid: a column for each public member of the item type, compiled here from the
        /// symbols instead of reflected at run time. It is on when the attribute says so (or binds it), and, as in WPF,
        /// by default; a grid that declares its own columns starts with it off, so they aren't shown twice.
        /// </summary>
        private void emitAutoGeneratedColumns(string v, INamedTypeSymbol type, XElement element, Scope scope)
        {
            var dataGrid = compilation.GetTypeByMetadataName("OsuXaml.Controls.DataGrid");
            if (dataGrid == null || !inherits(type, dataGrid))
                return;

            var attribute = element.Attribute("AutoGenerateColumns");
            bool bound = attribute != null && Markup.IsMarkup(attribute.Value);

            if (attribute != null && !bound && !(bool.TryParse(attribute.Value.Trim(), out bool on) && on))
                return;

            if (attribute == null && element.Elements().Any(c => c.Name.LocalName.EndsWith(".Columns", StringComparison.Ordinal) && c.HasElements))
                return;

            var itemType = itemsTypeOf(element, scope);

            if (itemType == null || itemType.SpecialType == SpecialType.System_Object)
            {
                if (attribute != null)
                    report(Diagnostics.BadTemplate, attribute, "AutoGenerateColumns needs ItemsSource bound to a typed list (e.g. BindableList<Person>), because the columns are compiled from the item type");
                return;
            }

            if (attribute == null)
                line($"{v}.AutoGenerateColumns = true;");

            const string controls = "global::OsuXaml.Controls.";

            line($"{v}.SetAutoGeneratedColumns(new {controls}DataGridAutoColumn[]");
            line("{");
            indent++;

            foreach (var column in autoColumns(itemType))
            {
                string x = "i" + counter++;
                string access = $"{x}.{column.Member.Name}";
                string value = column.Bindable ? access + ".Value" : access;
                string binding = $"new global::OsuXaml.ItemBinding<{typeName(itemType)}, {typeName(column.ValueType)}>({SymbolDisplay.FormatLiteral(column.Member.Name, true)}, {x} => {access}, {x} => {value})";

                bool nullableBool = column.ValueType is INamedTypeSymbol { OriginalDefinition: { SpecialType: SpecialType.System_Nullable_T } } n
                                    && n.TypeArguments[0].SpecialType == SpecialType.System_Boolean;
                bool checkBox = nullableBool || column.ValueType.SpecialType == SpecialType.System_Boolean;

                var settings = new List<string> { $"Header = {SymbolDisplay.FormatLiteral(column.Header, true)}", $"Binding = {binding}" };

                if (nullableBool)
                    settings.Add("IsThreeState = true");
                if (column.ReadOnly)
                    settings.Add("IsReadOnly = true");

                string kind = checkBox ? "DataGridCheckBoxColumn" : "DataGridTextColumn";
                line($"new {controls}DataGridAutoColumn({SymbolDisplay.FormatLiteral(column.Member.Name, true)}, typeof({typeName(column.ValueType)}), () => new {controls}{kind} {{ {string.Join(", ", settings)} }}),");
            }

            indent--;
            line("});");
        }

        private sealed class AutoColumn
        {
            public ISymbol Member;
            public ITypeSymbol ValueType;
            public bool Bindable;
            public string Header;
            public int Order;
            public bool ReadOnly;
        }

        /// <summary>
        /// The public instance properties and fields of <paramref name="itemType"/> that make columns, base class members first and
        /// in declaration order, then by <c>[Display(Order = n)]</c>. Collections and delegates are left out, as are members
        /// marked <c>[Browsable(false)]</c> or <c>[Display(AutoGenerateField = false)]</c>.
        /// </summary>
        private List<AutoColumn> autoColumns(ITypeSymbol itemType)
        {
            var types = new List<ITypeSymbol>();

            for (var t = itemType; t != null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
                types.Insert(0, t);

            if (itemType.TypeKind == TypeKind.Interface)
                types.InsertRange(0, itemType.AllInterfaces.Reverse());

            var columns = new List<AutoColumn>();

            foreach (var member in types.SelectMany(t => t.GetMembers()))
            {
                ITypeSymbol memberType;

                if (member is IPropertySymbol { IsStatic: false, IsIndexer: false, GetMethod: { DeclaredAccessibility: Accessibility.Public } } property && property.DeclaredAccessibility == Accessibility.Public)
                    memberType = property.Type;
                else if (member is IFieldSymbol { IsStatic: false, IsConst: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } field)
                    memberType = field.Type;
                else
                    continue;

                if (!compilation.IsSymbolAccessibleWithin(memberType, rootType) || memberType.IsRefLikeType || memberType.TypeKind == TypeKind.Pointer
                    || memberType.TypeKind == TypeKind.Delegate || isEnumerable(memberType))
                    continue;

                var attributes = member.GetAttributes();

                if (attributeArgument(attributes, "System.ComponentModel.BrowsableAttribute", 0) is false
                    || namedArgument(attributes, "System.ComponentModel.DataAnnotations.DisplayAttribute", "AutoGenerateField") is false)
                    continue;

                var bindable = bindableValueType(memberType);

                var column = new AutoColumn
                {
                    Member = member,
                    ValueType = bindable ?? memberType,
                    Bindable = bindable != null,
                    Header = namedArgument(attributes, "System.ComponentModel.DataAnnotations.DisplayAttribute", "Name") as string
                             ?? attributeArgument(attributes, "System.ComponentModel.DisplayNameAttribute", 0) as string
                             ?? member.Name,
                    Order = namedArgument(attributes, "System.ComponentModel.DataAnnotations.DisplayAttribute", "Order") as int? ?? 10000,
                    // Only bindable members can be written back to.
                    ReadOnly = bindable == null
                               || attributeArgument(attributes, "System.ComponentModel.ReadOnlyAttribute", 0) is true
                               || attributeArgument(attributes, "System.ComponentModel.DataAnnotations.EditableAttribute", 0) is false,
                };

                // A member hiding one of a base class takes its place.
                int hidden = columns.FindIndex(c => c.Member.Name == member.Name);

                if (hidden >= 0)
                    columns[hidden] = column;
                else
                    columns.Add(column);
            }

            // Stable, so members without an Order keep declaration order.
            return columns.Select((c, i) => (c, i)).OrderBy(p => p.c.Order).ThenBy(p => p.i).Select(p => p.c).ToList();
        }

        private static object attributeArgument(ImmutableArray<AttributeData> attributes, string attributeType, int index)
        {
            var data = attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeType);
            return data != null && data.ConstructorArguments.Length > index ? data.ConstructorArguments[index].Value : null;
        }

        private static object namedArgument(ImmutableArray<AttributeData> attributes, string attributeType, string name)
        {
            var data = attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeType);
            return data?.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value;
        }

        #endregion

        #region Resources

        /// <summary>Resource entries visible from <paramref name="context"/>, nearest first, ending with shared dictionaries.</summary>
        private IEnumerable<XElement> resourceEntries(XElement context)
        {
            for (var e = context; e != null; e = e.Parent)
            {
                foreach (var child in e.Elements())
                {
                    string local = child.Name.LocalName;

                    if (local == e.Name.LocalName + ".Resources")
                    {
                        foreach (var entry in dictionaryEntries(child))
                            yield return entry;
                    }
                }

                if (e.Parent == null && e.Name.LocalName == "ResourceDictionary")
                {
                    foreach (var entry in dictionaryEntries(e))
                        yield return entry;
                }
            }

            foreach (var dictionary in globalDictionaries)
            {
                foreach (var entry in dictionaryEntries(dictionary))
                    yield return entry;
            }
        }

        private static IEnumerable<XElement> dictionaryEntries(XElement dictionary)
        {
            foreach (var child in dictionary.Elements())
            {
                if (child.Name.LocalName == "ResourceDictionary")
                {
                    foreach (var nested in dictionaryEntries(child))
                        yield return nested;
                }
                else if (child.Name.LocalName.IndexOf('.') < 0)
                    yield return child;
            }
        }

        /// <summary>x:Key, or for keyless entries the implicit key: a Style's TargetType or a DataTemplate's DataType.</summary>
        private string resourceKey(XElement entry)
        {
            string key = directive(entry, "Key");
            if (key != null)
                return key;

            switch (entry.Name.LocalName)
            {
                case "Style":
                {
                    string target = entry.Attribute("TargetType")?.Value;
                    var type = target == null ? null : resolveTypeName(stripTypeMarkup(target), entry);
                    return type == null ? null : "{Style}" + type.ToDisplayString();
                }

                case "DataTemplate":
                case "HierarchicalDataTemplate":
                {
                    string dataType = directive(entry, "DataType") ?? entry.Attribute("DataType")?.Value;
                    var type = dataType == null ? null : resolveTypeName(stripTypeMarkup(dataType), entry);
                    return type == null ? null : "{DataTemplate}" + type.ToDisplayString();
                }
            }

            return null;
        }

        /// <summary>Finds a resource by key. Reports a missing one when <paramref name="source"/> is given.</summary>
        private XElement findResource(XElement context, string key, XObject source)
        {
            var found = resourceEntries(context).FirstOrDefault(e => resourceKey(e) == key);

            if (found == null && source != null)
                report(Diagnostics.UnknownResource, source, key);

            return found;
        }

        /// <summary>A resource written as text, like <c>&lt;Colour4 x:Key="Accent"&gt;#89b4fa&lt;/Colour4&gt;</c>.</summary>
        private static bool isLiteralResource(XElement resource)
            => !resource.HasElements && !resource.Attributes().Any(a => !a.IsNamespaceDeclaration && !directive_namespaces.Contains(a.Name.NamespaceName) && a.Name.Namespace != XNamespace.Xml);

        private readonly HashSet<XElement> expanding = new HashSet<XElement>();

        /// <summary>
        /// <c>{StaticResource Key}</c> on a property. Resources are resolved at build time: text resources become literals of
        /// the property's type, templates are compiled in place and other elements are created fresh for each use.
        /// </summary>
        private void emitResourceValue(string v, INamedTypeSymbol type, ISymbol member, string key, XObject source, XElement context, XElement owner, Scope scope)
        {
            var resource = findResource(context, key, source);
            if (resource == null)
                return;

            if (!expanding.Add(resource))
            {
                report(Diagnostics.BadMarkup, source, $"Resource '{key}' refers to itself");
                return;
            }

            try
            {
                var target = memberType(member);

                if (member is IEventSymbol || target == null)
                {
                    report(Diagnostics.UnknownMember, source, type.Name, member.Name + " (events take a method name)");
                    return;
                }

                if (isTemplateType(target))
                {
                    string t = emitTemplate(resource, target, templateHint(type, owner, member.Name, target, scope), scope);
                    if (t != null)
                        assign(v, member, t);
                    return;
                }

                if (resource.Name.LocalName == "Style" || isTemplateElement(resource))
                {
                    report(Diagnostics.BadMarkup, source, $"Resource '{key}' is a {resource.Name.LocalName} and can't be used for '{member.Name}'");
                    return;
                }

                if (isLiteralResource(resource))
                {
                    var valueTarget = valueType(member);
                    string literal = convertLiteral(resource.Value, valueTarget, out string hint);

                    if (literal == null)
                        report(Diagnostics.BadLiteral, source, resource.Value, valueTarget.ToDisplayString(), hint ?? $" (from resource '{key}')");
                    else
                        assign(v, member, literal);

                    return;
                }

                var (c, childType) = emitValueElement(resource, scope);
                if (c != null)
                    assignChecked(v, member, c, childType, source);
            }
            finally
            {
                expanding.Remove(resource);
            }
        }

        /// <summary><c>{x:Static local:Palette.Accent}</c>: a static field, property or constant.</summary>
        private (string Expr, ITypeSymbol Type) staticMember(string value, XElement context, XObject source)
        {
            int dot = value.LastIndexOf('.');

            if (dot <= 0)
            {
                report(Diagnostics.BadMarkup, source, $"{{x:Static}} needs Type.Member, not '{value}'");
                return (null, null);
            }

            var owner = resolveTypeName(value.Substring(0, dot), context);

            if (owner == null)
            {
                report(Diagnostics.UnknownType, source, value.Substring(0, dot), "");
                return (null, null);
            }

            string name = value.Substring(dot + 1);
            var member = owner.GetMembers(name).FirstOrDefault(m => m.IsStatic && (m is IFieldSymbol || m is IPropertySymbol) && canAccess(m));

            if (member == null)
            {
                report(Diagnostics.UnknownPath, source, owner.ToDisplayString(), name + " (as a static field or property)");
                return (null, null);
            }

            return ($"{typeName(owner)}.{name}", memberType(member));
        }

        #endregion

        #region Styles

        /// <summary>
        /// The setters that apply to an element: from <c>Style="{StaticResource Key}"</c>, or else from an implicit
        /// <c>&lt;Style TargetType="Button"&gt;</c> in scope. BasedOn styles come first and later setters win.
        /// </summary>
        private List<PropertyValue> styleSetters(XElement element, INamedTypeSymbol type)
        {
            var setters = new List<PropertyValue>();
            XElement style;

            var attribute = element.Attribute("Style");

            if (attribute != null && findMember(type, "Style") == null)
            {
                var markup = Markup.IsMarkup(attribute.Value) ? Markup.Parse(attribute.Value, out _) : null;

                if (markup == null || markup.Extension != Markup.StaticResource)
                {
                    report(Diagnostics.BadMarkup, attribute, "Style must be {StaticResource Key}");
                    return setters;
                }

                style = findResource(element, markup.Path, attribute);
                if (style == null)
                    return setters;

                if (style.Name.LocalName != "Style")
                {
                    report(Diagnostics.BadMarkup, attribute, $"Resource '{markup.Path}' is not a Style");
                    return setters;
                }
            }
            else
                style = findResource(element, "{Style}" + type.ToDisplayString(), null);

            if (style != null)
                collectSetters(style, type, element, setters, new HashSet<XElement>());

            return setters;
        }

        private void collectSetters(XElement style, INamedTypeSymbol type, XElement element, List<PropertyValue> setters, HashSet<XElement> visited)
        {
            if (!visited.Add(style))
            {
                report(Diagnostics.BadMarkup, style, "Style is BasedOn itself");
                return;
            }

            string targetName = style.Attribute("TargetType")?.Value;

            if (targetName != null)
            {
                var target = resolveTypeName(stripTypeMarkup(targetName), style);

                if (target == null)
                {
                    report(Diagnostics.UnknownType, style, targetName, "");
                    return;
                }

                if (!inherits(type, target))
                {
                    report(Diagnostics.BadMarkup, element, $"A Style for '{target.Name}' can't be applied to '{type.Name}'");
                    return;
                }
            }

            var basedOn = style.Attribute("BasedOn");

            if (basedOn != null)
            {
                var markup = Markup.IsMarkup(basedOn.Value) ? Markup.Parse(basedOn.Value, out _) : null;
                var baseStyle = markup?.Extension == Markup.StaticResource ? findResource(style, markup.Path, basedOn) : null;

                if (markup?.Extension != Markup.StaticResource)
                    report(Diagnostics.BadMarkup, basedOn, "BasedOn must be {StaticResource Key}");
                else if (baseStyle != null)
                    collectSetters(baseStyle, type, element, setters, visited);
            }

            foreach (var setter in style.Elements())
            {
                string kind = setter.Name.LocalName;

                if (kind != "Setter" && kind != "EventSetter")
                {
                    if (kind.IndexOf('.') < 0)
                        report(Diagnostics.BadMarkup, setter, $"Styles hold Setter and EventSetter elements, not '{kind}'");
                    continue;
                }

                var nameAttribute = setter.Attribute(kind == "Setter" ? "Property" : "Event");
                var valueAttribute = setter.Attribute(kind == "Setter" ? "Value" : "Handler");
                var valueElement = setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value");

                if (nameAttribute == null || (valueAttribute == null && valueElement == null))
                {
                    report(Diagnostics.BadMarkup, setter, kind == "Setter" ? "A Setter needs Property and Value" : "An EventSetter needs Event and Handler");
                    continue;
                }

                setters.RemoveAll(s => s.Name == nameAttribute.Value);
                setters.Add(new PropertyValue
                {
                    Name = nameAttribute.Value,
                    Text = valueAttribute?.Value,
                    Element = valueAttribute == null ? valueElement : null,
                    Source = (XObject)valueAttribute ?? setter,
                    Context = setter,
                    Owner = element,
                });
            }
        }

        #endregion
    }
}
