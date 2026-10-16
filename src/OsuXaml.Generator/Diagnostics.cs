using Microsoft.CodeAnalysis;

namespace OsuXaml.Generator
{
    internal static class Diagnostics
    {
        private const string category = "OsuXaml";

        private static DiagnosticDescriptor error(string id, string title, string format)
            => new DiagnosticDescriptor(id, title, format, category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidXml = error("OX0001", "Invalid XAML", "XAML could not be parsed: {0}");
        public static readonly DiagnosticDescriptor MissingClass = error("OX0002", "Missing x:Class", "{0}");
        public static readonly DiagnosticDescriptor UnknownType = error("OX0003", "Unknown type", "Type '{0}' was not found{1}");
        public static readonly DiagnosticDescriptor UnknownMember = error("OX0004", "Unknown member", "'{0}' has no settable property, field or event named '{1}'");
        public static readonly DiagnosticDescriptor BadLiteral = error("OX0005", "Invalid value", "Cannot convert \"{0}\" to {1}{2}");
        public static readonly DiagnosticDescriptor UnknownPath = error("OX0006", "Unknown binding path", "'{0}' has no member named '{1}'");
        public static readonly DiagnosticDescriptor TypeMismatch = error("OX0007", "Binding type mismatch", "Cannot bind {0} to '{1}' of type {2}{3}");
        public static readonly DiagnosticDescriptor NoChildren = error("OX0008", "Children not supported", "'{0}' cannot contain child elements");
        public static readonly DiagnosticDescriptor BadTemplate = error("OX0009", "Invalid template", "{0}");
        public static readonly DiagnosticDescriptor BadMarkup = error("OX0010", "Invalid markup extension", "{0}");
        public static readonly DiagnosticDescriptor UnknownMethod = error("OX0011", "Unknown method", "{0}");
        public static readonly DiagnosticDescriptor UnknownResource = error("OX0012", "Unknown resource", "No resource with key '{0}' is in scope (look in X.Resources of this element or its parents, or a ResourceDictionary file)");
    }
}
