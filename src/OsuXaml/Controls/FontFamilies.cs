using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuXaml.Controls
{
    /// <summary>
    /// What the text controls know about a font family the game added: which weights it has, how its glyphs relate to
    /// <c>FontSize</c>, and other names it goes by. A library that brings its own fonts registers them once, before building UI:
    /// <code>
    /// FontFamilies.Register("Selawik", FontFamilies.LineBoxPerEm(2457, 2048), FontWeight.Light, FontWeight.Regular, FontWeight.SemiBold, FontWeight.Bold);
    /// FontFamilies.AddAlias("Segoe UI", "Selawik");
    /// </code>
    /// Families that aren't registered keep osu!framework's rules: the weight name is looked up as written and <c>FontSize</c> is the
    /// height of the font's line box.
    /// </summary>
    public static class FontFamilies
    {
        private sealed class Family
        {
            public float SizeScale = 1;
            public FontWeight[] Weights;
        }

        private static readonly Dictionary<string, Family> families = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase)
        {
            // The fonts osu!framework ships.
            ["Roboto"] = new Family { Weights = new[] { FontWeight.Regular, FontWeight.Bold } },
            ["RobotoCondensed"] = new Family { Weights = new[] { FontWeight.Regular, FontWeight.Bold } },
        };

        private static readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a family. <paramref name="sizeScale"/> is the font's line box (ascender to descender, which osu!framework sizes
        /// fonts by) over its em: with it, <c>FontSize="14"</c> means a 14 pixel em as in WPF and CSS. <paramref name="weights"/> are
        /// the weights it has; another weight asks for the nearest one, as CSS does. Registering a family again replaces it.
        /// </summary>
        public static void Register(string family, float sizeScale, params FontWeight[] weights)
        {
            if (string.IsNullOrEmpty(family))
                throw new ArgumentException("A family needs a name.", nameof(family));

            lock (families)
                families[family] = new Family { SizeScale = sizeScale > 0 ? sizeScale : 1, Weights = weights?.Length > 0 ? weights.Distinct().ToArray() : null };
        }

        /// <summary>The line box per em of a font from its metrics: (hhea ascender - descender) / units per em.</summary>
        public static float LineBoxPerEm(float lineBoxUnits, float unitsPerEm) => lineBoxUnits / unitsPerEm;

        /// <summary>Makes <paramref name="alias"/> (in XAML, <c>FontFamily="Segoe UI"</c>) mean <paramref name="family"/>.</summary>
        public static void AddAlias(string alias, string family)
        {
            lock (families)
                aliases[alias] = family;
        }

        /// <summary>The family a name stands for: itself, or what it is an alias of.</summary>
        public static string Resolve(string family)
        {
            if (family == null)
                return null;

            lock (families)
                return aliases.TryGetValue(family, out string target) ? target : family;
        }

        /// <summary>How many line box pixels one em pixel of the family takes (1 for families that aren't registered).</summary>
        public static float SizeScale(string family)
        {
            if (family == null)
                return 1;

            lock (families)
                return families.TryGetValue(family, out var f) ? f.SizeScale : 1;
        }

        /// <summary>The weight name osu!framework looks a family's font up by, for the nearest weight the family has.</summary>
        public static string WeightName(string family, FontWeight weight)
        {
            FontWeight[] available = null;

            if (family != null)
            {
                lock (families)
                    available = families.TryGetValue(family, out var f) ? f.Weights : null;
            }

            return Name(available == null ? weight : Nearest(weight, available));
        }

        /// <summary>The weight a font weight name stands for (<c>SemiBold</c>, <c>Bold</c>); Regular for an unknown or empty name.</summary>
        public static FontWeight Parse(string weightName)
        {
            if (string.IsNullOrEmpty(weightName))
                return FontWeight.Regular;

            foreach (var w in all)
            {
                if (string.Equals(Name(w), weightName, StringComparison.OrdinalIgnoreCase))
                    return w;
            }

            return FontWeight.Regular;
        }

        /// <summary>The name osu!framework font files use for a weight: Regular, Bold, SemiBold...</summary>
        public static string Name(FontWeight weight) => weight switch
        {
            FontWeight.Thin => "Thin",
            FontWeight.ExtraLight => "ExtraLight",
            FontWeight.Light => "Light",
            FontWeight.SemiLight => "SemiLight",
            FontWeight.Medium => "Medium",
            FontWeight.SemiBold => "SemiBold",
            FontWeight.Bold => "Bold",
            FontWeight.ExtraBold => "ExtraBold",
            FontWeight.Black => "Black",
            _ => "Regular",
        };

        /// <summary>The weight's number, as CSS and OpenType give it (Regular is 400, Bold 700).</summary>
        public static int Number(FontWeight weight) => weight switch
        {
            FontWeight.Thin => 100,
            FontWeight.ExtraLight => 200,
            FontWeight.Light => 300,
            FontWeight.SemiLight => 350,
            FontWeight.Medium => 500,
            FontWeight.SemiBold => 600,
            FontWeight.Bold => 700,
            FontWeight.ExtraBold => 800,
            FontWeight.Black => 900,
            _ => 400,
        };

        private static readonly FontWeight[] all =
        {
            FontWeight.Thin, FontWeight.ExtraLight, FontWeight.Light, FontWeight.SemiLight, FontWeight.Regular, FontWeight.Medium,
            FontWeight.SemiBold, FontWeight.Bold, FontWeight.ExtraBold, FontWeight.Black,
        };

        /// <summary>
        /// CSS's font matching: for 400 to 500 the weights up to 500 first, then lighter, then heavier; lighter wanted weights
        /// look lighter first, heavier ones heavier first.
        /// </summary>
        public static FontWeight Nearest(FontWeight wanted, IReadOnlyCollection<FontWeight> available)
        {
            if (available.Count == 0 || available.Contains(wanted))
                return wanted;

            int w = Number(wanted);
            var byNumber = available.OrderBy(Number).ToList();

            IEnumerable<FontWeight> order;

            if (w >= 400 && w <= 500)
            {
                order = byNumber.Where(a => Number(a) >= w && Number(a) <= 500)
                                .Concat(byNumber.Where(a => Number(a) < w).Reverse())
                                .Concat(byNumber.Where(a => Number(a) > 500));
            }
            else if (w < 400)
                order = byNumber.Where(a => Number(a) < w).Reverse().Concat(byNumber.Where(a => Number(a) > w));
            else
                order = byNumber.Where(a => Number(a) > w).Concat(byNumber.Where(a => Number(a) < w).Reverse());

            return order.First();
        }
    }
}
