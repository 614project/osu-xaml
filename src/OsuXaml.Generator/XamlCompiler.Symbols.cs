using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace OsuXaml.Generator
{
    internal sealed partial class XamlCompiler
    {
        #region Type resolution

        private INamedTypeSymbol resolveType(XElement element)
        {
            var type = resolveType(element.Name.NamespaceName, element.Name.LocalName);

            if (type == null)
                report(Diagnostics.UnknownType, element, element.Name.LocalName, namespaceHint(element.Name.NamespaceName));

            return type;
        }

        /// <summary>Resolves <c>prefix:Type</c> or <c>Type</c> using the xmlns declarations in scope at <paramref name="context"/>.</summary>
        private INamedTypeSymbol resolveTypeName(string name, XElement context)
        {
            int colon = name.IndexOf(':');
            string ns = colon < 0 ? context.GetDefaultNamespace().NamespaceName : context.GetNamespaceOfPrefix(name.Substring(0, colon))?.NamespaceName;

            return ns == null ? null : resolveType(ns, colon < 0 ? name : name.Substring(colon + 1));
        }

        private INamedTypeSymbol resolveType(string xmlNamespace, string localName)
        {
            const string clr = "clr-namespace:";

            if (xmlNamespace.StartsWith(clr, StringComparison.Ordinal))
            {
                string ns = xmlNamespace.Substring(clr.Length);
                int semicolon = ns.IndexOf(';');
                if (semicolon >= 0)
                    ns = ns.Substring(0, semicolon);

                return compilation.GetTypeByMetadataName(ns + "." + localName);
            }

            foreach (string ns in default_namespaces)
            {
                var type = compilation.GetTypeByMetadataName(ns + "." + localName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static string namespaceHint(string xmlNamespace)
            => xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)
                ? $" in {xmlNamespace.Substring("clr-namespace:".Length)}"
                : " in the osu!framework namespaces (use xmlns:local=\"clr-namespace:Your.Namespace\" for your own types)";

        private static string directive(XElement element, string name)
            => element.Attributes().FirstOrDefault(a => directive_namespaces.Contains(a.Name.NamespaceName) && a.Name.LocalName == name)?.Value;

        #endregion

        #region Members

        /// <summary>Finds a property or field with an accessible setter, or an event, walking base types.</summary>
        private ISymbol findMember(ITypeSymbol type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var m in t.GetMembers(name))
                {
                    switch (m)
                    {
                        case IPropertySymbol p when p.SetMethod != null && canAccess(p.SetMethod):
                        case IPropertySymbol pb when pb.GetMethod != null && canAccess(pb.GetMethod) && bindableClassValueType(pb.Type) != null:
                        case IFieldSymbol f when !f.IsReadOnly && !f.IsConst && canAccess(f):
                        case IEventSymbol e when canAccess(e):
                            return m;
                    }
                }
            }

            return null;
        }

        /// <summary>Finds a readable property, field or method for a binding path segment.</summary>
        private ISymbol findReadable(ITypeSymbol type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var m in t.GetMembers(name))
                {
                    switch (m)
                    {
                        case IPropertySymbol p when p.GetMethod != null && canAccess(p.GetMethod):
                        case IFieldSymbol f when canAccess(f):
                        case IMethodSymbol { MethodKind: MethodKind.Ordinary } method when canAccess(method):
                            return m;
                    }
                }
            }

            if (type.TypeKind == TypeKind.Interface)
            {
                foreach (var i in type.AllInterfaces)
                {
                    var m = i.GetMembers(name).FirstOrDefault();
                    if (m != null)
                        return m;
                }
            }

            return null;
        }

        /// <summary>A get-only collection property that XAML fills with Add, e.g. <c>&lt;GridView.Columns&gt;</c>.</summary>
        private IPropertySymbol findCollection(ITypeSymbol type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var p in t.GetMembers(name).OfType<IPropertySymbol>())
                {
                    if (p.GetMethod != null && canAccess(p.GetMethod) && findMethods(p.Type, "Add").Any(m => !m.IsStatic && m.Parameters.Length == 1))
                        return p;
                }
            }

            return null;
        }

        /// <summary>Generated code lives inside the x:Class, so it may use public, internal and protected members.</summary>
        private bool canAccess(ISymbol symbol) => compilation.IsSymbolAccessibleWithin(symbol, rootType);

        private static ITypeSymbol memberType(ISymbol member) => member switch
        {
            IPropertySymbol p => p.Type,
            IFieldSymbol f => f.Type,
            IEventSymbol e => e.Type,
            _ => null,
        };

        #endregion

        #region Type helpers

        private static bool inherits(ITypeSymbol type, ITypeSymbol baseType)
        {
            if (baseType == null)
                return false;

            for (var t = type; t != null; t = t.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(t, baseType))
                    return true;
            }

            return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, baseType));
        }

        /// <summary>The T of the nearest <c>Container&lt;T&gt;</c> base, or null when the type cannot hold children.</summary>
        private static ITypeSymbol containerChildType(ITypeSymbol type)
        {
            for (var t = type as INamedTypeSymbol; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.OriginalDefinition.ToDisplayString() == "osu.Framework.Graphics.Containers.Container<T>")
                    return t.TypeArguments[0];
            }

            return null;
        }

        /// <summary>The T of <c>IBindable&lt;T&gt;</c>, for one-way bindings from any bindable.</summary>
        private static ITypeSymbol bindableValueType(ITypeSymbol type) => genericInterfaceArgument(type, "osu.Framework.Bindables.IBindable<T>");

        /// <summary>The T of <c>IBindableList&lt;T&gt;</c>, for ItemsSource.</summary>
        private static ITypeSymbol bindableListItemType(ITypeSymbol type) => genericInterfaceArgument(type, "osu.Framework.Bindables.IBindableList<T>");

        /// <summary>The T when the type is or derives from the <c>Bindable&lt;T&gt;</c> class, for two-way bindings.</summary>
        private static ITypeSymbol bindableClassValueType(ITypeSymbol type)
        {
            for (var t = type as INamedTypeSymbol; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.OriginalDefinition.ToDisplayString() == "osu.Framework.Bindables.Bindable<T>")
                    return t.TypeArguments[0];
            }

            return null;
        }

        /// <summary>Whether the type is or derives from the <c>BindableList&lt;T&gt;</c> class, which two-way list bindings link with BindTo.</summary>
        private static bool isBindableListClass(ITypeSymbol type)
        {
            for (var t = type as INamedTypeSymbol; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.OriginalDefinition.ToDisplayString() == "osu.Framework.Bindables.BindableList<T>")
                    return true;
            }

            return false;
        }

        private static ITypeSymbol genericInterfaceArgument(ITypeSymbol type, string definition)
        {
            if (type is INamedTypeSymbol named && named.IsGenericType && named.OriginalDefinition.ToDisplayString() == definition)
                return named.TypeArguments[0];

            return type?.AllInterfaces.FirstOrDefault(i => i.IsGenericType && i.OriginalDefinition.ToDisplayString() == definition)?.TypeArguments[0];
        }

        private static bool isText(ITypeSymbol type)
            => type.SpecialType == SpecialType.System_String || type.ToDisplayString() == "osu.Framework.Localisation.LocalisableString";

        private static string typeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string describe(ITypeSymbol type) => type?.ToDisplayString() ?? "value";

        #endregion

        #region Output and diagnostics

        private void line(string code) => body.Append(' ', indent * 4).AppendLine(code);

        private void report(DiagnosticDescriptor descriptor, XObject at, params object[] args)
        {
            var info = (IXmlLineInfo)at;
            var position = info != null && info.HasLineInfo() ? new LinePosition(info.LineNumber - 1, info.LinePosition - 1) : default;

            // Mistakes inside a shared ResourceDictionary point at that file.
            string file = at?.Document != null && documentPaths.TryGetValue(at.Document, out string p) ? p : path;
            report(descriptor, file, position, args);
        }

        private void report(DiagnosticDescriptor descriptor, LinePosition position, params object[] args) => report(descriptor, path, position, args);

        private void report(DiagnosticDescriptor descriptor, string file, LinePosition position, object[] args)
        {
            var location = Location.Create(file, default, new LinePositionSpan(position, position));
            diagnostics.Add(Diagnostic.Create(descriptor, location, args));
        }

        #endregion
    }
}
