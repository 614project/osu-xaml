using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OsuXaml.Generator
{
    internal sealed partial class XamlCompiler
    {
        #region Literals

        private string convertLiteral(string value, ITypeSymbol type, out string hint)
        {
            hint = null;
            string s = value.Trim();

            // int? and friends take the same literals as their underlying type.
            if (type is INamedTypeSymbol { OriginalDefinition: { SpecialType: SpecialType.System_Nullable_T } } nullable)
                type = nullable.TypeArguments[0];

            string full = type.ToDisplayString();

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                case SpecialType.System_Object:
                    return SymbolDisplay.FormatLiteral(value, true);

                case SpecialType.System_DateTime:
                    return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        ? $"new global::System.DateTime({date.Year}, {date.Month}, {date.Day}, {date.Hour}, {date.Minute}, {date.Second})"
                        : null;

                case SpecialType.System_Collections_IEnumerable:
                {
                    // ItemsSource="Red,Green,Blue": a quick list of strings.
                    var items = value.Split(',').Select(p => SymbolDisplay.FormatLiteral(p.Trim(), true));
                    return "new string[] { " + string.Join(", ", items) + " }";
                }

                case SpecialType.System_Boolean:
                    return bool.TryParse(s, out bool b) ? (b ? "true" : "false") : null;

                case SpecialType.System_Int32:
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i.ToString(CultureInfo.InvariantCulture) : null;

                case SpecialType.System_Single:
                    return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? literalFloat(f) : null;

                case SpecialType.System_Double:
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? literalDouble(d) : null;

                case SpecialType.System_Int64:
                    return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l.ToString(CultureInfo.InvariantCulture) + "L" : null;
            }

            if (full == "System.TimeSpan")
            {
                return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var span)
                    ? $"global::System.TimeSpan.FromTicks({span.Ticks}L)"
                    : null;
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

            if (full == "OsuXaml.Controls.GridLengthCollection" && !validGridLengths(s))
            {
                hint = " (use a comma-separated list of Auto, pixels, * or 2*)";
                return null;
            }

            // Cursor="Hand" names one of the shapes in Cursors, as in WPF, so a misspelt name fails the build.
            if (full == "OsuXaml.Controls.Cursor")
            {
                var shapes = compilation.GetTypeByMetadataName("OsuXaml.Controls.Cursors")?.GetMembers().OfType<IPropertySymbol>()
                                        .Where(m => m.IsStatic).Select(m => m.Name).ToList() ?? new List<string>();

                if (shapes.Contains(s))
                    return $"global::OsuXaml.Controls.Cursors.{s}";

                hint = ". Valid values: " + string.Join(", ", shapes);
                return null;
            }

            // Any type with a static Parse(string) accepts a string, e.g. RowDefinitions="Auto,*,40".
            if (type is INamedTypeSymbol named && named.GetMembers("Parse").OfType<IMethodSymbol>()
                                                    .Any(m => m.IsStatic && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String
                                                              && SymbolEqualityComparer.Default.Equals(m.ReturnType, type)))
                return $"{typeName(type)}.Parse({SymbolDisplay.FormatLiteral(value, true)})";

            hint = " (no XAML conversion exists for this type yet)";
            return null;
        }

        private static bool validGridLengths(string s)
        {
            foreach (string raw in s.Split(','))
            {
                string p = raw.Trim();

                if (p.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                    continue;

                string number = p.EndsWith("*", StringComparison.Ordinal) ? p.Substring(0, p.Length - 1) : p;

                if (number.Length == 0 && p == "*")
                    continue;

                if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || f < 0)
                    return false;
            }

            return true;
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
