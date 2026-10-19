using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OsuXaml.Generator
{
    /// <summary>
    /// Writes XML schemas (XSD) describing the elements, attributes and enum values XAML can use, so XML editors
    /// (VS Code's XML extension, Visual Studio, Rider) can offer completion. The schemas are deliberately permissive:
    /// anything they don't know is allowed, because the build, not the editor, reports real mistakes.
    /// </summary>
    internal sealed class XamlSchema
    {
        public const string MainFile = "osu-xaml.xsd";
        public const string DirectivesFile = "osu-xaml-x.xsd";
        public const string CatalogFile = "catalog.xml";

        private const string main_namespace = "osu-xaml";
        private const string directives_namespace = "osu-xaml:x";
        private const string clr_prefix = "clr-namespace:";

        private static readonly XNamespace xs = "http://www.w3.org/2001/XMLSchema";

        /// <summary>Element names the generator understands without a type behind them, with their attributes.</summary>
        private static readonly (string Name, string[] Attributes)[] keywords =
        {
            ("ResourceDictionary", new string[0]),
            ("Style", new[] { "TargetType", "BasedOn" }),
            ("Setter", new[] { "Property", "Value" }),
            ("EventSetter", new[] { "Event", "Handler" }),
            ("DataTemplate", new[] { "DataType" }),
            ("HierarchicalDataTemplate", new[] { "DataType", "ItemsSource" }),
            ("ControlTemplate", new[] { "TargetType" }),
            ("ItemsPanelTemplate", new string[0]),
        };

        private static readonly string[] directive_attributes = { "Class", "Name", "Key", "DataType", "TypeArguments" };
        private static readonly string[] directive_elements = { "String", "Double", "Single", "Int32", "Int64", "Boolean" };

        private readonly Compilation compilation;
        private readonly IAssemblySymbol project;
        private readonly IReadOnlyList<string> defaultNamespaces = XamlCompiler.DefaultNamespaces;
        private readonly INamedTypeSymbol drawable;
        private readonly INamedTypeSymbol container;
        private readonly INamedTypeSymbol panel;
        private readonly INamedTypeSymbol attributeClass;

        private readonly Dictionary<INamedTypeSymbol, List<Member>> declared = new Dictionary<INamedTypeSymbol, List<Member>>(SymbolEqualityComparer.Default);

        /// <summary>Enum types the schema being written defines, and the ones the main schema defined (which a clr-namespace schema imports).</summary>
        private Dictionary<ITypeSymbol, string> enumTypes = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

        private Dictionary<ITypeSymbol, string> mainEnumTypes = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

        /// <summary>The prefix of the main schema's shared types (<c>bool</c>, <c>markup</c>) in the schema being written.</summary>
        private string mainPrefix = string.Empty;

        private XamlSchema(Compilation compilation, IAssemblySymbol project)
        {
            this.compilation = compilation;
            this.project = project;
            drawable = compilation.GetTypeByMetadataName("osu.Framework.Graphics.Drawable");
            container = compilation.GetTypeByMetadataName("osu.Framework.Graphics.Containers.Container`1");
            panel = compilation.GetTypeByMetadataName("OsuXaml.Controls.Panel");
            attributeClass = compilation.GetTypeByMetadataName("System.Attribute");
        }

        /// <summary>
        /// The schema files (name and content) for XAML compiled against <paramref name="compilation"/>: one for the default
        /// namespace, one for x: directives, one per <c>clr-namespace:</c> URI the project's XAML uses, and an XML catalog
        /// mapping each namespace URI to its file.
        /// </summary>
        /// <param name="project">The project's own assembly, whose internal types its XAML may also use.</param>
        public static List<(string FileName, string Content)> Generate(Compilation compilation, IAssemblySymbol project, IEnumerable<string> clrNamespaceUris)
        {
            var schema = new XamlSchema(compilation, project);
            var uris = clrNamespaceUris.Where(u => u.StartsWith(clr_prefix, StringComparison.Ordinal)).Distinct().OrderBy(u => u, StringComparer.Ordinal).ToList();

            var files = new List<(string, string)>
            {
                (MainFile, schema.mainSchema(out var mainGroups)),
                (DirectivesFile, directivesSchema()),
            };

            foreach (string uri in uris)
                files.Add((ClrFileName(uri), schema.clrSchema(uri, uris, mainGroups)));

            files.Add((CatalogFile, catalog(uris)));
            return files;
        }

        /// <summary>The <c>clr-namespace:</c> URIs a XAML document declares, which each get a schema.</summary>
        public static IEnumerable<string> ClrNamespaceUris(string xaml)
        {
            XDocument document;

            try
            {
                document = XDocument.Parse(xaml);
            }
            catch (XmlException)
            {
                return Enumerable.Empty<string>();
            }

            return document.Descendants().Attributes().Where(a => a.IsNamespaceDeclaration && a.Value.StartsWith(clr_prefix, StringComparison.Ordinal)).Select(a => a.Value);
        }

        /// <summary>The schema file for a <c>clr-namespace:</c> URI.</summary>
        public static string ClrFileName(string uri)
        {
            var name = new StringBuilder();

            foreach (char c in uri.Substring(clr_prefix.Length))
                name.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '-');

            return "clr-namespace." + name + ".xsd";
        }

        #region Schemas

        private string mainSchema(out HashSet<string> groupsDefined)
        {
            var root = schemaElement(main_namespace);
            root.Add(new XAttribute(XNamespace.Xmlns + "x", directives_namespace));
            root.Add(new XElement(xs + "import", new XAttribute("namespace", directives_namespace), new XAttribute("schemaLocation", DirectivesFile)));

            var elements = elementTypes(defaultNamespaces, includeInternal: false);
            var attached = attachedProperties();

            // Style, DataTemplate and the other keywords mean what the generator makes of them, not a type of that name.
            foreach (var keyword in keywords)
                elements.Remove(keyword.Name);

            var body = new List<XElement>();
            var groups = new Dictionary<string, XElement>();

            // Shared pieces: markup values, booleans, x: directives, the element group and property element content.
            body.Add(simpleType("markup", new XElement(xs + "restriction", new XAttribute("base", "xs:string"),
                new XElement(xs + "pattern", new XAttribute("value", @"\s*\{[\s\S]*\}\s*")))));
            body.Add(choiceType("bool", new[] { "True", "False" }, @"\s*([Tt][Rr][Uu][Ee]|[Ff][Aa][Ll][Ss][Ee])\s*"));

            body.Add(new XElement(xs + "attributeGroup", new XAttribute("name", "directives"),
                directive_attributes.Select(a => new XElement(xs + "attribute", new XAttribute("ref", "x:" + a)))));

            body.Add(new XElement(xs + "attributeGroup", new XAttribute("name", "attached"),
                attached.Select(a => attribute(a.Name, a.Type, a.Description))));

            body.Add(new XElement(xs + "attributeGroup", new XAttribute("name", "style"),
                attribute("Style", null, "A style resource for this element, e.g. {StaticResource Danger}")));

            // Panels and osu!framework containers fill themselves from a bound list without having these properties.
            body.Add(new XElement(xs + "attributeGroup", new XAttribute("name", "items"),
                attribute("ItemsSource", null, "A list to create one child per item from, e.g. {Bind Items}"),
                attribute("ItemTemplate", null, "The DataTemplate (or {StaticResource Key}) each item's child is made from")));

            body.Add(new XElement(xs + "complexType", new XAttribute("name", "property"), new XAttribute("mixed", "true"),
                new XElement(xs + "choice", new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                    new XElement(xs + "group", new XAttribute("ref", "elements")),
                    new XElement(xs + "any", new XAttribute("namespace", "##other"), new XAttribute("processContents", "lax")))));

            var names = elements.Keys.Concat(keywords.Select(k => k.Name)).OrderBy(n => n, StringComparer.Ordinal).ToList();

            body.Add(new XElement(xs + "group", new XAttribute("name", "elements"),
                new XElement(xs + "choice", names.Select(n => new XElement(xs + "element", new XAttribute("ref", n))))));

            foreach (string name in names)
            {
                if (elements.TryGetValue(name, out var type))
                    body.Add(typeElement(name, type, attached, groups, null, "##other", "elements"));
                else
                    body.Add(keywordElement(keywords.First(k => k.Name == name)));
            }

            root.Add(enumTypes.OrderBy(e => e.Value, StringComparer.Ordinal).Select(e => enumType(e.Value, (INamedTypeSymbol)e.Key)));
            root.Add(groups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Value));
            root.Add(body);

            groupsDefined = new HashSet<string>(groups.Keys);
            mainEnumTypes = enumTypes;
            return serialize(root);
        }

        private string clrSchema(string uri, List<string> allUris, HashSet<string> mainGroups)
        {
            var root = schemaElement(uri);
            root.Add(new XAttribute(XNamespace.Xmlns + "x", directives_namespace));
            root.Add(new XAttribute(XNamespace.Xmlns + "o", main_namespace));
            root.Add(new XElement(xs + "import", new XAttribute("namespace", main_namespace), new XAttribute("schemaLocation", MainFile)));
            root.Add(new XElement(xs + "import", new XAttribute("namespace", directives_namespace), new XAttribute("schemaLocation", DirectivesFile)));

            string ns = uri.Substring(clr_prefix.Length);
            int semicolon = ns.IndexOf(';');
            if (semicolon >= 0)
                ns = ns.Substring(0, semicolon);

            var elements = elementTypes(new[] { ns }, includeInternal: true);
            var attached = attachedProperties();

            enumTypes = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);
            mainPrefix = "o:";

            // Elements from the x: namespace and other clr namespaces; osu-xaml elements come through o:elements.
            string others = string.Join(" ", new[] { directives_namespace }.Concat(allUris.Where(u => u != uri)));

            var body = new List<XElement>();
            var groups = new Dictionary<string, XElement>();

            body.Add(new XElement(xs + "group", new XAttribute("name", "elements"),
                new XElement(xs + "choice", elements.Keys.OrderBy(n => n, StringComparer.Ordinal).Select(n => new XElement(xs + "element", new XAttribute("ref", n))))));

            foreach (var pair in elements.OrderBy(e => e.Key, StringComparer.Ordinal))
                body.Add(typeElement(pair.Key, pair.Value, attached, groups, mainGroups, others, "elements"));

            if (elements.Count == 0)
                body.Clear();

            root.Add(enumTypes.OrderBy(e => e.Value, StringComparer.Ordinal).Select(e => enumType(e.Value, (INamedTypeSymbol)e.Key)));
            root.Add(groups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Value));
            root.Add(body);

            return serialize(root);
        }

        private static string directivesSchema()
        {
            var root = new XElement(xs + "schema",
                new XAttribute(XNamespace.Xmlns + "xs", xs.NamespaceName),
                new XAttribute("targetNamespace", directives_namespace),
                new XAttribute("elementFormDefault", "qualified"),
                new XElement(xs + "annotation", new XElement(xs + "documentation", "XAML directives (x:Class, x:Name, ...) for osu!xaml. Generated for editor completion.")));

            var docs = new Dictionary<string, string>
            {
                ["Class"] = "The partial C# class this XAML file builds, e.g. App.MainView",
                ["Name"] = "Creates a field for this element in the x:Class",
                ["Key"] = "The key of a resource, used with {StaticResource Key}",
                ["DataType"] = "The view model (or template item) type that {Bind} paths are checked against",
                ["TypeArguments"] = "Type arguments of a generic element, e.g. x:String or local:Pair(x:String, x:Int32)",
            };

            foreach (string name in directive_attributes)
                root.Add(new XElement(xs + "attribute", new XAttribute("name", name), documentation(docs[name])));

            foreach (string name in directive_elements)
            {
                root.Add(new XElement(xs + "element", new XAttribute("name", name),
                    new XElement(xs + "complexType",
                        new XElement(xs + "simpleContent",
                            new XElement(xs + "extension", new XAttribute("base", "xs:string"),
                                new XElement(xs + "anyAttribute", new XAttribute("namespace", "##any"), new XAttribute("processContents", "lax")))))));
            }

            return serialize(root);
        }

        private static string catalog(List<string> uris)
        {
            XNamespace cat = "urn:oasis:names:tc:entity:xmlns:xml:catalog";

            var root = new XElement(cat + "catalog",
                new XComment(" Maps the XAML namespaces to their completion schemas. VS Code: add this file to the xml.catalogs setting. "),
                new XElement(cat + "uri", new XAttribute("name", main_namespace), new XAttribute("uri", MainFile)),
                new XElement(cat + "uri", new XAttribute("name", directives_namespace), new XAttribute("uri", DirectivesFile)),
                uris.Select(u => new XElement(cat + "uri", new XAttribute("name", u), new XAttribute("uri", ClrFileName(u)))));

            return serialize(root);
        }

        private static XElement schemaElement(string targetNamespace) => new XElement(xs + "schema",
            new XAttribute(XNamespace.Xmlns + "xs", xs.NamespaceName),
            new XAttribute("xmlns", targetNamespace),
            new XAttribute("targetNamespace", targetNamespace),
            new XAttribute("elementFormDefault", "qualified"),
            new XElement(xs + "annotation", new XElement(xs + "documentation",
                "Completion schema for osu!xaml, generated from the project's types. It accepts anything it doesn't know; build errors are the real check.")));

        private static string serialize(XElement root)
        {
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
            var builder = new StringBuilder();

            using (var writer = XmlWriter.Create(new System.IO.StringWriter(builder, CultureInfo.InvariantCulture), settings))
                new XDocument(root).Save(writer);

            return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"") + "\n";
        }

        #endregion

        #region Elements

        /// <summary>
        /// One XSD element for a type: its attributes (one group per class in its hierarchy, so they are written once),
        /// <c>Type.Member</c> property elements for object-valued members, and any element as content.
        /// </summary>
        private XElement typeElement(string name, INamedTypeSymbol type, List<Member> attached, Dictionary<string, XElement> groups,
                                     HashSet<string> externalGroups, string otherNamespaces, string elementGroup)
        {
            bool inMain = externalGroups == null;
            string prefix = inMain ? string.Empty : "o:";

            var choice = new XElement(xs + "choice", new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"));

            if (!inMain)
                choice.Add(new XElement(xs + "group", new XAttribute("ref", "o:elements")));

            choice.Add(new XElement(xs + "group", new XAttribute("ref", elementGroup)));

            var members = chain(type).SelectMany(c => declaredMembers(c)).ToList();
            var propertyNames = new HashSet<string> { "Resources" };
            var memberNames = new HashSet<string>(members.Select(m => m.Name));

            foreach (var member in members.Where(m => m.PropertyElement))
                propertyNames.Add(member.Name);

            bool isDrawable = drawable != null && inherits(type, drawable);
            bool takesItems = (container != null && inherits(type, container) || panel != null && inherits(type, panel))
                              && !memberNames.Contains("ItemsSource") && !memberNames.Contains("ItemTemplate");

            if (isDrawable)
            {
                foreach (var global in attached.Where(a => a.PropertyElement && a.Name.IndexOf('.') < 0 && !memberNames.Contains(a.Name)))
                    propertyNames.Add(global.Name);
            }

            if (takesItems)
                propertyNames.Add("ItemTemplate");

            foreach (string property in propertyNames.OrderBy(p => p, StringComparer.Ordinal))
                choice.Add(new XElement(xs + "element", new XAttribute("name", name + "." + property), new XAttribute("type", prefix + "property")));

            choice.Add(new XElement(xs + "any", new XAttribute("namespace", otherNamespaces), new XAttribute("processContents", "lax")));

            var complex = new XElement(xs + "complexType", new XAttribute("mixed", "true"), choice);

            foreach (var c in chain(type))
            {
                string group = groupName(c);

                if (externalGroups != null && externalGroups.Contains(group))
                {
                    complex.Add(new XElement(xs + "attributeGroup", new XAttribute("ref", "o:" + group)));
                    continue;
                }

                if (!groups.ContainsKey(group))
                    groups[group] = new XElement(xs + "attributeGroup", new XAttribute("name", group), declaredMembers(c).Where(m => m.Attribute).Select(m => attribute(m.Name, m.Type, m.Description)));

                complex.Add(new XElement(xs + "attributeGroup", new XAttribute("ref", group)));
            }

            complex.Add(new XElement(xs + "attributeGroup", new XAttribute("ref", prefix + "directives")));

            if (!memberNames.Contains("Style"))
                complex.Add(new XElement(xs + "attributeGroup", new XAttribute("ref", prefix + "style")));

            if (takesItems)
                complex.Add(new XElement(xs + "attributeGroup", new XAttribute("ref", prefix + "items")));

            if (isDrawable)
            {
                // Global properties (HorizontalAlignment, ToolTip) give way to a member of the same name.
                if (attached.Any(a => memberNames.Contains(a.Name)))
                    complex.Add(attached.Where(a => !memberNames.Contains(a.Name)).Select(a => attribute(a.Name, a.Type, a.Description)));
                else
                    complex.Add(new XElement(xs + "attributeGroup", new XAttribute("ref", prefix + "attached")));
            }

            complex.Add(new XElement(xs + "anyAttribute", new XAttribute("namespace", "##any"), new XAttribute("processContents", "lax")));

            return new XElement(xs + "element", new XAttribute("name", name), documentation(type.OriginalDefinition.ToDisplayString()), complex);
        }

        private XElement keywordElement((string Name, string[] Attributes) keyword)
        {
            var choice = new XElement(xs + "choice", new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                new XElement(xs + "group", new XAttribute("ref", "elements")));

            if (keyword.Name == "Setter")
                choice.Add(new XElement(xs + "element", new XAttribute("name", "Setter.Value"), new XAttribute("type", "property")));

            choice.Add(new XElement(xs + "any", new XAttribute("namespace", "##other"), new XAttribute("processContents", "lax")));

            return new XElement(xs + "element", new XAttribute("name", keyword.Name),
                new XElement(xs + "complexType", new XAttribute("mixed", "true"), choice,
                    keyword.Attributes.Select(a => new XElement(xs + "attribute", new XAttribute("name", a))),
                    new XElement(xs + "attributeGroup", new XAttribute("ref", "directives")),
                    new XElement(xs + "anyAttribute", new XAttribute("namespace", "##any"), new XAttribute("processContents", "lax"))));
        }

        /// <summary>
        /// Types XAML can create in the given CLR namespaces, by element name. An earlier namespace wins a name, as in the
        /// generator; a non-generic type wins over a generic one of the same name.
        /// </summary>
        private Dictionary<string, INamedTypeSymbol> elementTypes(IEnumerable<string> namespaces, bool includeInternal)
        {
            var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

            foreach (string ns in namespaces)
            {
                var symbol = findNamespace(ns);
                if (symbol == null)
                    continue;

                foreach (var type in symbol.GetTypeMembers())
                {
                    if (!isElement(type, includeInternal))
                        continue;

                    if (!result.TryGetValue(type.Name, out var existing) || existing.Arity > 0 && type.Arity == 0)
                        result[type.Name] = type;
                }
            }

            return result;
        }

        /// <summary>Types XAML can create, and drawables a view can derive from (the root element names the base class).</summary>
        private bool isElement(INamedTypeSymbol type, bool includeInternal)
        {
            if (!accessible(type, includeInternal) || type.IsStatic || !type.CanBeReferencedByName)
                return false;

            if (type.TypeKind == TypeKind.Struct)
                return true;

            if (type.TypeKind != TypeKind.Class || attributeClass != null && inherits(type, attributeClass) || obsolete(type))
                return false;

            if (drawable != null && inherits(type, drawable) && !type.IsSealed)
                return true;

            // A type parsed from text, such as Geometry, can be a resource: <Geometry x:Key="Heart">M 0,0 ...</Geometry>.
            return !type.IsAbstract && (type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && accessible(c, includeInternal)) || isLiteral(type));
        }

        private bool accessible(ISymbol symbol, bool includeInternal)
            => symbol.DeclaredAccessibility == Accessibility.Public
               || includeInternal && SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, project)
                                  && (symbol.DeclaredAccessibility == Accessibility.Internal || symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal);

        private INamespaceSymbol findNamespace(string name)
        {
            var ns = compilation.GlobalNamespace;

            foreach (string part in name.Split('.'))
            {
                ns = ns.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
                if (ns == null)
                    return null;
            }

            return ns;
        }

        private static IEnumerable<INamedTypeSymbol> chain(INamedTypeSymbol type)
        {
            for (var t = type; t != null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
                yield return t.OriginalDefinition;
        }

        private static bool obsolete(ISymbol symbol) => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute");

        private static string groupName(INamedTypeSymbol type) => "T." + typeId(type);

        /// <summary>The full metadata name of a type as an XML name: <c>osu.Framework.Graphics.Containers.Container`1</c> becomes <c>...Container-1</c>.</summary>
        private static string typeId(INamedTypeSymbol type)
        {
            var parts = new List<string>();

            for (var t = type; t != null; t = t.ContainingType)
                parts.Insert(0, t.MetadataName);

            if (!type.ContainingNamespace.IsGlobalNamespace)
                parts.Insert(0, type.ContainingNamespace.ToDisplayString());

            var name = new StringBuilder();

            foreach (char c in string.Join(".", parts))
                name.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '-');

            return name.ToString();
        }

        #endregion

        #region Members

        private sealed class Member
        {
            public string Name;
            public ITypeSymbol Type;
            public string Description;

            /// <summary>Settable as an attribute.</summary>
            public bool Attribute;

            /// <summary>Worth offering as a <c>Type.Member</c> property element: it holds objects, templates or a collection.</summary>
            public bool PropertyElement;
        }

        /// <summary>
        /// Public members of <paramref name="type"/> (not its bases) that XAML can set, leaving out names a base class
        /// already offers, so the attribute groups of a hierarchy never repeat a name.
        /// </summary>
        private List<Member> declaredMembers(INamedTypeSymbol type)
        {
            if (declared.TryGetValue(type, out var cached))
                return cached;

            var inherited = new HashSet<string>(type.BaseType == null ? Enumerable.Empty<string>() : chain(type.BaseType).SelectMany(declaredMembers).Select(m => m.Name));
            var result = new List<Member>();

            foreach (var symbol in type.GetMembers())
            {
                if (symbol.IsStatic || symbol.IsImplicitlyDeclared || !symbol.CanBeReferencedByName || symbol.DeclaredAccessibility != Accessibility.Public
                    || inherited.Contains(symbol.Name) || result.Any(m => m.Name == symbol.Name) || obsolete(symbol))
                    continue;

                var member = describe(symbol);
                if (member != null)
                    result.Add(member);
            }

            declared[type] = result;
            return result;
        }

        private Member describe(ISymbol symbol)
        {
            switch (symbol)
            {
                case IPropertySymbol p when !p.IsIndexer:
                {
                    bool settable = p.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                    bool readable = p.GetMethod?.DeclaredAccessibility == Accessibility.Public;

                    if (settable || readable && bindableValue(p.Type) != null)
                        return member(p.Name, p.Type, attribute: true);

                    if (readable && hasAdd(p.Type))
                        return new Member { Name = p.Name, Type = p.Type, Description = p.Type.ToDisplayString(), PropertyElement = true };

                    return null;
                }

                case IFieldSymbol f when !f.IsReadOnly && !f.IsConst:
                    return member(f.Name, f.Type, attribute: true);

                case IEventSymbol e:
                    return new Member { Name = e.Name, Description = "event " + e.Type.ToDisplayString() + " (a code-behind method name or {Bind Method})", Attribute = true };

                default:
                    return null;
            }
        }

        private Member member(string name, ITypeSymbol type, bool attribute)
        {
            var value = bindableValue(type) ?? type;

            return new Member
            {
                Name = name,
                Type = value,
                Description = type.ToDisplayString(),
                Attribute = attribute,
                PropertyElement = !isLiteral(value) || hasAdd(value) && value.SpecialType != SpecialType.System_String,
            };
        }

        /// <summary>
        /// Global properties every drawable takes (<c>HorizontalAlignment</c>) and attached properties (<c>Grid.Row</c>):
        /// public static <c>SetName(Drawable, value)</c> methods.
        /// </summary>
        private List<Member> attachedProperties()
        {
            var result = new List<Member>();
            if (drawable == null)
                return result;

            var globalProviders = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
            {
                foreach (var attribute in assembly.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() == "OsuXaml.XamlGlobalPropertiesAttribute"
                        && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol provider)
                        globalProviders.Add(provider);
                }
            }

            foreach (var provider in globalProviders.OrderBy(p => p.ToDisplayString() == "OsuXaml.Controls.Layout" ? 0 : 1).ThenBy(p => p.ToDisplayString()))
                addSetters(provider, null, result);

            foreach (string ns in defaultNamespaces)
            {
                var symbol = findNamespace(ns);
                if (symbol == null)
                    continue;

                foreach (var owner in symbol.GetTypeMembers().Where(t => t.DeclaredAccessibility == Accessibility.Public && !globalProviders.Contains(t)).OrderBy(t => t.Name, StringComparer.Ordinal))
                    addSetters(owner, owner.Name, result);
            }

            return result;
        }

        private void addSetters(INamedTypeSymbol owner, string qualifier, List<Member> result)
        {
            foreach (var method in owner.GetMembers().OfType<IMethodSymbol>())
            {
                if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.Parameters.Length != 2 || !method.Name.StartsWith("Set", StringComparison.Ordinal)
                    || method.Name.Length == 3 || !takesDrawable(method.Parameters[0].Type))
                    continue;

                string name = (qualifier == null ? string.Empty : qualifier + ".") + method.Name.Substring(3);

                if (result.Any(m => m.Name == name))
                    continue;

                var m = member(name, method.Parameters[1].Type, attribute: true);
                m.Description = $"{owner.ToDisplayString()}.{method.Name}(element, {method.Parameters[1].Type.ToDisplayString()})";
                result.Add(m);
            }
        }

        private bool takesDrawable(ITypeSymbol parameter)
            => parameter.SpecialType != SpecialType.System_Object && (SymbolEqualityComparer.Default.Equals(parameter, drawable) || drawable.AllInterfaces.Contains(parameter, SymbolEqualityComparer.Default)
                                                                     || inherits(drawable, parameter));

        #endregion

        #region Types

        private XElement attribute(string name, ITypeSymbol type, string description)
        {
            var element = new XElement(xs + "attribute", new XAttribute("name", name));
            string typeName = attributeType(type);

            if (typeName != null)
                element.Add(new XAttribute("type", typeName));

            if (description != null)
                element.Add(documentation(description));

            return element;
        }

        /// <summary>The XSD type checked for a member: booleans and enums (whose names editors offer); anything else is free text.</summary>
        private string attributeType(ITypeSymbol type)
        {
            if (type == null)
                return null;

            if (type is INamedTypeSymbol { OriginalDefinition: { SpecialType: SpecialType.System_Nullable_T } } nullable)
                type = nullable.TypeArguments[0];

            if (type.SpecialType == SpecialType.System_Boolean)
                return mainPrefix + "bool";

            if (type.TypeKind != TypeKind.Enum || type.DeclaredAccessibility != Accessibility.Public)
                return null;

            if (mainEnumTypes != enumTypes && mainEnumTypes.TryGetValue(type, out string imported))
                return mainPrefix + imported;

            if (!enumTypes.TryGetValue(type, out string name))
                enumTypes[type] = name = "enum." + typeId((INamedTypeSymbol)type);

            return name;
        }

        private XElement enumType(string name, INamedTypeSymbol type)
        {
            var values = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst && f.DeclaredAccessibility == Accessibility.Public).Select(f => f.Name).ToList();

            // The generator accepts flag combinations such as "X|Y" (or "X, Y") for any enum.
            string one = "(" + string.Join("|", values) + ")";
            string pattern = $@"\s*{one}(\s*[|,]\s*{one})*\s*";

            return choiceType(name, values, values.Count > 0 ? pattern : null, mainPrefix);
        }

        /// <summary>A simple type taking one of <paramref name="values"/> (which editors offer), text matching <paramref name="pattern"/>, or markup.</summary>
        private static XElement choiceType(string name, IEnumerable<string> values, string pattern, string markupPrefix = "")
        {
            var union = new XElement(xs + "union", new XAttribute("memberTypes", markupPrefix + "markup"),
                new XElement(xs + "simpleType", new XElement(xs + "restriction", new XAttribute("base", "xs:string"),
                    values.Select(v => new XElement(xs + "enumeration", new XAttribute("value", v))))));

            if (pattern != null)
            {
                union.Add(new XElement(xs + "simpleType", new XElement(xs + "restriction", new XAttribute("base", "xs:string"),
                    new XElement(xs + "pattern", new XAttribute("value", pattern)))));
            }

            return simpleType(name, union);
        }

        private static XElement simpleType(string name, XElement content) => new XElement(xs + "simpleType", new XAttribute("name", name), content);

        private static XElement documentation(string text) => new XElement(xs + "annotation", new XElement(xs + "documentation", text));

        /// <summary>Types written as text in XAML; members of other types get <c>Type.Member</c> property elements.</summary>
        private bool isLiteral(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition: { SpecialType: SpecialType.System_Nullable_T } } nullable)
                type = nullable.TypeArguments[0];

            if (type.TypeKind == TypeKind.Enum || type.TypeKind == TypeKind.Delegate || type.IsValueType)
                return true;

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                    return true;
            }

            switch (type.ToDisplayString())
            {
                case "osu.Framework.Localisation.LocalisableString":
                case "osu.Framework.Graphics.Colour.ColourInfo":
                    return true;
            }

            // Types with a static Parse(string), such as GridLengthCollection, are written as text too.
            return type is INamedTypeSymbol named && named.GetMembers("Parse").OfType<IMethodSymbol>().Any(m => m.IsStatic && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String);
        }

        private static ITypeSymbol bindableValue(ITypeSymbol type)
        {
            for (var t = type as INamedTypeSymbol; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.OriginalDefinition.ToDisplayString() == "osu.Framework.Bindables.Bindable<T>")
                    return t.TypeArguments[0];
            }

            return null;
        }

        private static bool hasAdd(ITypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.GetMembers("Add").OfType<IMethodSymbol>().Any(m => !m.IsStatic && m.Parameters.Length == 1 && m.DeclaredAccessibility == Accessibility.Public))
                    return true;
            }

            return type.AllInterfaces.Any(i => i.GetMembers("Add").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 1));
        }

        private static bool inherits(ITypeSymbol type, ITypeSymbol baseType)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, baseType.OriginalDefinition))
                    return true;
            }

            return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, baseType));
        }

        #endregion
    }
}
