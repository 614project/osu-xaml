using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace OsuXaml.Generator
{
    internal sealed partial class XamlCompiler
    {
        #region Type resolution

        /// <summary>The element's type, honouring <c>x:TypeArguments</c>. Reports why when it can't be resolved.</summary>
        private INamedTypeSymbol resolveType(XElement element)
        {
            var type = elementType(element, out var error, out var errorArgs);

            if (type == null)
                report(error ?? Diagnostics.UnknownType, element, errorArgs ?? new object[] { element.Name.LocalName, namespaceHint(element.Name.NamespaceName) });

            return type;
        }

        /// <summary>The element's type, honouring <c>x:TypeArguments</c>, without reporting anything.</summary>
        private INamedTypeSymbol elementType(XElement element) => elementType(element, out _, out _);

        private INamedTypeSymbol elementType(XElement element, out DiagnosticDescriptor error, out object[] errorArgs)
        {
            error = null;
            errorArgs = null;

            string typeArguments = directive(element, "TypeArguments");
            List<ITypeSymbol> arguments = null;

            if (typeArguments != null)
            {
                arguments = new List<ITypeSymbol>();
                int i = 0;

                do
                {
                    var argument = parseTypeName(typeArguments, ref i, element, out error, out errorArgs);
                    if (argument == null)
                        return null;

                    arguments.Add(argument);
                    skipWhitespace(typeArguments, ref i);
                }
                while (i < typeArguments.Length && typeArguments[i] == ',' && ++i > 0);

                if (i != typeArguments.Length || arguments.Count == 0)
                {
                    error = Diagnostics.UnknownType;
                    errorArgs = new object[] { typeArguments, " (x:TypeArguments takes a comma-separated list of types, e.g. \"x:String\" or \"local:Pair(x:String, x:Int32)\")" };
                    return null;
                }
            }

            return constructType(element.Name.NamespaceName, element.Name.LocalName, arguments, element.Name.LocalName, out error, out errorArgs);
        }

        /// <summary>
        /// Resolves <c>prefix:Type</c> or <c>Type</c> using the xmlns declarations in scope at <paramref name="context"/>.
        /// Generic types name their type arguments in parentheses, as XAML 2009 does: <c>local:Pair(x:String, x:Int32)</c>.
        /// </summary>
        private INamedTypeSymbol resolveTypeName(string name, XElement context)
        {
            int i = 0;
            var type = parseTypeName(name, ref i, context, out _, out _);
            skipWhitespace(name, ref i);

            return i == name.Length ? type : null;
        }

        private INamedTypeSymbol parseTypeName(string text, ref int i, XElement context, out DiagnosticDescriptor error, out object[] errorArgs)
        {
            skipWhitespace(text, ref i);

            int start = i;
            while (i < text.Length && text[i] != '(' && text[i] != ')' && text[i] != ',' && !char.IsWhiteSpace(text[i]))
                i++;

            string name = text.Substring(start, i - start);
            List<ITypeSymbol> arguments = null;

            skipWhitespace(text, ref i);

            if (i < text.Length && text[i] == '(')
            {
                arguments = new List<ITypeSymbol>();
                i++;

                do
                {
                    var argument = parseTypeName(text, ref i, context, out error, out errorArgs);
                    if (argument == null)
                        return null;

                    arguments.Add(argument);
                    skipWhitespace(text, ref i);
                }
                while (i < text.Length && text[i] == ',' && ++i > 0);

                if (i >= text.Length || text[i] != ')')
                {
                    error = Diagnostics.UnknownType;
                    errorArgs = new object[] { text, " (a generic type's arguments go in parentheses, e.g. local:Pair(x:String, x:Int32))" };
                    return null;
                }

                i++;
            }

            int colon = name.IndexOf(':');
            string ns = colon < 0 ? context.GetDefaultNamespace().NamespaceName : context.GetNamespaceOfPrefix(name.Substring(0, colon))?.NamespaceName;
            string local = colon < 0 ? name : name.Substring(colon + 1);

            if (ns == null || local.Length == 0)
            {
                error = Diagnostics.UnknownType;
                errorArgs = new object[] { name, ns == null ? $" (the prefix '{name.Substring(0, Math.Max(0, colon))}' isn't declared with xmlns)" : "" };
                return null;
            }

            return constructType(ns, local, arguments, name, out error, out errorArgs);
        }

        /// <summary>
        /// The type <paramref name="localName"/> in an XML namespace, constructed with <paramref name="arguments"/> when it is generic.
        /// The <c>x:</c> namespace names the built-in types (<c>x:String</c>, <c>x:Int32</c>, ...).
        /// </summary>
        private INamedTypeSymbol constructType(string xmlNamespace, string localName, List<ITypeSymbol> arguments, string displayName,
                                               out DiagnosticDescriptor error, out object[] errorArgs)
        {
            error = null;
            errorArgs = null;

            if (directive_namespaces.Contains(xmlNamespace))
            {
                if (primitiveType(localName) is INamedTypeSymbol primitive && arguments == null)
                    return primitive;

                error = Diagnostics.UnknownType;
                errorArgs = new object[] { displayName, " (the x: namespace has String, Object, Boolean, Char, Byte, Int16, Int32, Int64, Single, Double, Decimal and TimeSpan)" };
                return null;
            }

            if (arguments == null || arguments.Count == 0)
            {
                var type = resolveType(xmlNamespace, localName);

                if (type == null)
                {
                    error = Diagnostics.UnknownType;
                    errorArgs = new object[] { displayName, genericDefinition(xmlNamespace, localName) is INamedTypeSymbol generic
                        ? $" as a non-generic type. '{generic.Name}' takes {generic.Arity} type argument{(generic.Arity == 1 ? "" : "s")}: add x:TypeArguments=\"...\" (or write {displayName}(...) in a type name)"
                        : namespaceHint(xmlNamespace) };
                }

                return type;
            }

            var definition = resolveType(xmlNamespace, localName + "`" + arguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (definition == null)
            {
                var other = genericDefinition(xmlNamespace, localName) ?? resolveType(xmlNamespace, localName);

                error = Diagnostics.UnknownType;
                errorArgs = new object[] { displayName, other != null
                    ? $" with {arguments.Count} type argument{(arguments.Count == 1 ? "" : "s")} ('{other.Name}' takes {other.Arity})"
                    : namespaceHint(xmlNamespace) };
                return null;
            }

            string violation = constraintViolation(definition, arguments);

            if (violation != null)
            {
                error = Diagnostics.BadTypeArgument;
                errorArgs = new object[] { violation };
                return null;
            }

            return definition.Construct(arguments.ToArray());
        }

        /// <summary>A generic type with this name (any arity), for hints when the type arguments are missing or wrong.</summary>
        private INamedTypeSymbol genericDefinition(string xmlNamespace, string localName)
        {
            for (int arity = 1; arity <= 8; arity++)
            {
                var type = resolveType(xmlNamespace, localName + "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>Why <paramref name="arguments"/> can't be used for <paramref name="definition"/>'s type parameters, or null when they can.</summary>
        private string constraintViolation(INamedTypeSymbol definition, List<ITypeSymbol> arguments)
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                var parameter = definition.TypeParameters[i];
                var argument = arguments[i];
                string where = $"'{argument.ToDisplayString()}' can't be {parameter.Name} of {definition.Name}<{string.Join(", ", definition.TypeParameters.Select(p => p.Name))}>";

                bool nullableValue = argument.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

                if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
                    return where + " (it must be a reference type)";

                if (parameter.HasValueTypeConstraint && (!argument.IsValueType || nullableValue))
                    return where + " (it must be a non-nullable value type)";

                if (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
                    return where + " (it must be an unmanaged type)";

                if (parameter.HasConstructorConstraint && !argument.IsValueType
                    && !(argument is INamedTypeSymbol named && !named.IsAbstract && named.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public)))
                    return where + " (it needs a public parameterless constructor)";

                foreach (var constraint in parameter.ConstraintTypes)
                {
                    // Constraints that mention other type parameters (where T : IComparable<T>) are left to the C# compiler.
                    if (mentionsTypeParameter(constraint))
                        continue;

                    var conversion = ((CSharpCompilation)compilation).ClassifyConversion(argument, constraint);

                    if (!conversion.IsIdentity && !(conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing)))
                        return where + $" (it must be a {constraint.ToDisplayString()})";
                }
            }

            return null;
        }

        private static bool mentionsTypeParameter(ITypeSymbol type) => type switch
        {
            ITypeParameterSymbol _ => true,
            IArrayTypeSymbol array => mentionsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(mentionsTypeParameter),
            _ => false,
        };

        private static void skipWhitespace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
        }

        /// <summary>Types named in the x: namespace, as in <c>x:TypeArguments="x:String"</c>.</summary>
        private ITypeSymbol primitiveType(string name)
        {
            switch (name)
            {
                case "Object": return compilation.GetSpecialType(SpecialType.System_Object);
                case "Char": return compilation.GetSpecialType(SpecialType.System_Char);
                case "Byte": return compilation.GetSpecialType(SpecialType.System_Byte);
                case "Int16": return compilation.GetSpecialType(SpecialType.System_Int16);
                case "Int64": return compilation.GetSpecialType(SpecialType.System_Int64);
                case "Decimal": return compilation.GetSpecialType(SpecialType.System_Decimal);
                case "TimeSpan": return compilation.GetTypeByMetadataName("System.TimeSpan");
                default: return directiveType(name);
            }
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
                        // A control's own list (Calendar.SelectedDates) takes a two-way list binding.
                        case IPropertySymbol pl when pl.GetMethod != null && canAccess(pl.GetMethod) && isBindableListClass(pl.Type):
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
