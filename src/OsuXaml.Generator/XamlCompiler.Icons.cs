using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace OsuXaml.Generator
{
    internal sealed partial class XamlCompiler
    {
        #region Icon names

        /// <summary>The nested classes of osu!framework's <c>FontAwesome</c>, in the order <c>Icon.Find</c> searches them.</summary>
        private static readonly string[] icon_styles = { "Solid", "Regular", "Brands" };

        private List<(string Style, string Name)> fontAwesomeIcons;

        /// <summary>
        /// <c>Kind="Solid.Chek"</c>: a literal for a member marked <c>[IconName]</c> must name an icon osu!framework's
        /// FontAwesome has, read the way <c>Icon.Find</c> reads it at runtime. Returns false after reporting when it doesn't.
        /// </summary>
        private bool checkIconName(ISymbol member, string value, XObject source)
        {
            if (!isIconName(member))
                return true;

            var icons = fontAwesome();

            // Without osu!framework's FontAwesome there is nothing to check against; the runtime lookup decides.
            if (icons.Count == 0)
                return true;

            var (style, name) = splitIconName(value);

            if (icons.Any(i => (style == null || i.Style.Equals(style, StringComparison.OrdinalIgnoreCase)) && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return true;

            string suggestion = closestIcon(icons, style, name);
            string hint;

            if (suggestion != null)
                hint = $" (did you mean {suggestion}?)";
            else if (style != null && !icon_styles.Contains(style, StringComparer.OrdinalIgnoreCase))
                hint = " (the styles are Solid, Regular and Brands)";
            else
                hint = " (osu!framework's FontAwesome has no icon by that name)";

            report(Diagnostics.BadLiteral, source, value, "a FontAwesome icon", hint);
            return false;
        }

        private static bool isIconName(ISymbol member)
        {
            for (var p = member as IPropertySymbol; p != null; p = p.OverriddenProperty)
            {
                if (p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "OsuXaml.IconNameAttribute"))
                    return true;
            }

            return false;
        }

        /// <summary>Every <c>IconUsage</c> property on FontAwesome's style classes.</summary>
        private List<(string Style, string Name)> fontAwesome()
        {
            if (fontAwesomeIcons != null)
                return fontAwesomeIcons;

            fontAwesomeIcons = new List<(string, string)>();
            var usage = compilation.GetTypeByMetadataName("osu.Framework.Graphics.Sprites.IconUsage");

            foreach (string style in icon_styles)
            {
                var type = compilation.GetTypeByMetadataName("osu.Framework.Graphics.Sprites.FontAwesome+" + style);
                if (type == null || usage == null)
                    continue;

                foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsStatic && property.DeclaredAccessibility == Accessibility.Public && SymbolEqualityComparer.Default.Equals(property.Type, usage))
                        fontAwesomeIcons.Add((style, property.Name));
                }
            }

            return fontAwesomeIcons;
        }

        /// <summary>
        /// <c>Solid.Check</c>, <c>FontAwesome.Solid.Check</c>, <c>check</c> or <c>fa-check-circle</c> to a style (or null for any)
        /// and a property name, as <c>Icon.lookup</c> does.
        /// </summary>
        private static (string Style, string Name) splitIconName(string value)
        {
            const string prefix = "FontAwesome.";
            string name = value.Trim();

            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(prefix.Length);

            string style = null;
            int dot = name.LastIndexOf('.');

            if (dot >= 0)
            {
                style = name.Substring(0, dot);
                name = name.Substring(dot + 1);
            }

            if (name.StartsWith("fa-", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(3);

            if (name.IndexOf('-') < 0)
                return (style, name);

            var pascal = new StringBuilder(name.Length);

            foreach (string part in name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries))
                pascal.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);

            return (style, pascal.ToString());
        }

        /// <summary>The nearest icon by edit distance, preferring the style that was asked for, or null when nothing is close.</summary>
        private static string closestIcon(List<(string Style, string Name)> icons, string style, string name)
        {
            if (name.Length == 0)
                return null;

            string lower = name.ToLowerInvariant();
            int limit = Math.Max(1, name.Length / 3);
            (string Style, string Name)? best = null;
            int bestScore = int.MaxValue;

            foreach (var icon in icons)
            {
                int distance = editDistance(lower, icon.Name.ToLowerInvariant());
                if (distance > limit)
                    continue;

                // Ties go to the style that was written, then to Solid, Regular, Brands.
                int score = distance * 2 + (style != null && !icon.Style.Equals(style, StringComparison.OrdinalIgnoreCase) ? 1 : 0);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = icon;
                }
            }

            return best == null ? null : best.Value.Style + "." + best.Value.Name;
        }

        private static int editDistance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;

                for (int j = 1; j <= b.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        #endregion
    }
}
