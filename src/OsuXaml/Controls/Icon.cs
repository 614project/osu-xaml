using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A FontAwesome icon from the fonts osu!framework ships: <c>&lt;Icon Kind="Solid.Check" Size="20" Foreground="#a6e3a1" /&gt;</c>.
    /// <see cref="Kind"/> names the icon as osu!framework's <c>FontAwesome</c> class does (<c>Solid.Check</c>,
    /// <c>Regular.Bell</c>, <c>Brands.Github</c>) or as FontAwesome's CSS does (<c>check-circle</c>); a name without a style
    /// is looked up in Solid, then Regular, then Brands. An unknown name shows a question mark. 16 pixels by default.
    /// It also works as a menu item's icon: <c>&lt;MenuItem.Icon&gt;&lt;Icon Kind="Solid.Save" /&gt;&lt;/MenuItem.Icon&gt;</c>.
    /// </summary>
    /// <remarks>C# files that import both osuTK and OsuXaml.Controls need an alias, as osuTK has an Icon class too.</remarks>
    public partial class Icon : SpriteIcon
    {
        private static readonly Type[] styles = { typeof(FontAwesome.Solid), typeof(FontAwesome.Regular), typeof(FontAwesome.Brands) };
        private static readonly Dictionary<string, IconUsage?> cache = new Dictionary<string, IconUsage?>(StringComparer.OrdinalIgnoreCase);

        private string kind;

        public Icon()
        {
            Size = new Vector2(16);
            Colour = Theme.Text;
        }

        /// <summary>Which icon to show, e.g. <c>Solid.Check</c>.</summary>
        public string Kind
        {
            get => kind;
            set
            {
                kind = value;
                base.Icon = Find(value) ?? FontAwesome.Solid.Question;
            }
        }

        public Colour4 Foreground
        {
            get => Colour;
            set => Colour = value;
        }

        /// <summary>Whether <see cref="Kind"/> named an icon that exists.</summary>
        public bool IsKnownKind => Find(kind) != null;

        /// <summary>The FontAwesome icon for a name such as <c>Solid.Check</c>, <c>Check</c> or <c>check-circle</c>, or null.</summary>
        public static IconUsage? Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            lock (cache)
            {
                if (cache.TryGetValue(name, out var cached))
                    return cached;

                return cache[name] = lookup(name.Trim());
            }
        }

        private static IconUsage? lookup(string name)
        {
            const string prefix = "FontAwesome.";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(prefix.Length);

            string style = null;
            int dot = name.LastIndexOf('.');

            if (dot >= 0)
            {
                style = name.Substring(0, dot);
                name = name.Substring(dot + 1);
            }

            string member = pascalCase(name);

            // The icons are static properties on nested classes, so a name from XAML finds its icon by reflection.
            foreach (var type in styles)
            {
                if (style != null && !type.Name.Equals(style, StringComparison.OrdinalIgnoreCase))
                    continue;

                var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                if (property?.PropertyType == typeof(IconUsage))
                    return (IconUsage)property.GetValue(null);
            }

            return null;
        }

        /// <summary>FontAwesome's CSS names (<c>fa-check-circle</c>) to the property names osu!framework uses (<c>CheckCircle</c>).</summary>
        private static string pascalCase(string name)
        {
            if (name.StartsWith("fa-", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(3);

            if (name.IndexOf('-') < 0)
                return name;

            var result = new StringBuilder(name.Length);

            foreach (string part in name.Split('-', StringSplitOptions.RemoveEmptyEntries))
                result.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);

            return result.ToString();
        }
    }
}
