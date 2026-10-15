using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Graphics;
using osuTK;

namespace OsuXaml.Controls
{
    public enum GridUnitType
    {
        Auto,
        Pixel,
        Star,
    }

    /// <summary>One row height or column width: <c>Auto</c>, <c>120</c>, <c>*</c> or <c>2*</c>.</summary>
    public readonly struct GridLength
    {
        public readonly float Value;
        public readonly GridUnitType Type;

        public GridLength(float value, GridUnitType type)
        {
            Value = value;
            Type = type;
        }

        public static GridLength Parse(string text)
        {
            string s = text.Trim();

            if (s.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                return new GridLength(0, GridUnitType.Auto);

            if (s.EndsWith("*", StringComparison.Ordinal))
            {
                string weight = s.Substring(0, s.Length - 1);
                return new GridLength(weight.Length == 0 ? 1 : float.Parse(weight, CultureInfo.InvariantCulture), GridUnitType.Star);
            }

            return new GridLength(float.Parse(s, CultureInfo.InvariantCulture), GridUnitType.Pixel);
        }

        public override string ToString() => Type switch
        {
            GridUnitType.Auto => "Auto",
            GridUnitType.Star => Value == 1 ? "*" : Value.ToString(CultureInfo.InvariantCulture) + "*",
            _ => Value.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Row or column definitions written as one string: <c>RowDefinitions="Auto,*,2*,40"</c>.
    /// The XAML compiler validates the syntax at build time and calls <see cref="Parse"/>.
    /// </summary>
    public sealed class GridLengthCollection : List<GridLength>
    {
        public static GridLengthCollection Parse(string text)
        {
            var result = new GridLengthCollection();

            foreach (string part in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(GridLength.Parse(part));

            return result;
        }

        public override string ToString() => string.Join(",", this);
    }

    /// <summary>
    /// WPF-style Grid: rows and columns sized by pixels, by content (Auto) or by weight (*),
    /// with children placed by the Grid.Row / Grid.Column / Grid.RowSpan / Grid.ColumnSpan attached properties.
    /// </summary>
    public partial class Grid : Panel
    {
        public GridLengthCollection RowDefinitions { get; set; } = new GridLengthCollection();

        public GridLengthCollection ColumnDefinitions { get; set; } = new GridLengthCollection();

        public float RowSpacing { get; set; }

        public float ColumnSpacing { get; set; }

        public static void SetRow(Drawable drawable, int value) => Layout.Get(drawable).Row = value;

        public static void SetColumn(Drawable drawable, int value) => Layout.Get(drawable).Column = value;

        public static void SetRowSpan(Drawable drawable, int value) => Layout.Get(drawable).RowSpan = Math.Max(1, value);

        public static void SetColumnSpan(Drawable drawable, int value) => Layout.Get(drawable).ColumnSpan = Math.Max(1, value);

        public static int GetRow(Drawable drawable) => Layout.Get(drawable).Row;

        public static int GetColumn(Drawable drawable) => Layout.Get(drawable).Column;

        private static readonly GridLength[] single_star = { new GridLength(1, GridUnitType.Star) };

        private IReadOnlyList<GridLength> rows => RowDefinitions.Count > 0 ? RowDefinitions : single_star;

        private IReadOnlyList<GridLength> columns => ColumnDefinitions.Count > 0 ? ColumnDefinitions : single_star;

        protected override Vector2 MeasureOverride(Vector2 available)
        {
            var desired = Slots.Select(s => s.Measure(available)).ToList();

            var columnSizes = solve(columns, available.X, ColumnSpacing, desired, d => d.X, cellOf: s => (Layout.Get(s.Content).Column, Layout.Get(s.Content).ColumnSpan));
            var rowSizes = solve(rows, available.Y, RowSpacing, desired, d => d.Y, cellOf: s => (Layout.Get(s.Content).Row, Layout.Get(s.Content).RowSpan));

            return new Vector2(
                columnSizes.Sum() + ColumnSpacing * (columnSizes.Length - 1),
                rowSizes.Sum() + RowSpacing * (rowSizes.Length - 1));
        }

        protected override void ArrangeOverride(Vector2 size)
        {
            var desired = Slots.Select(s => s.Measure(size)).ToList();

            var columnSizes = solve(columns, size.X, ColumnSpacing, desired, d => d.X, s => (Layout.Get(s.Content).Column, Layout.Get(s.Content).ColumnSpan));
            var rowSizes = solve(rows, size.Y, RowSpacing, desired, d => d.Y, s => (Layout.Get(s.Content).Row, Layout.Get(s.Content).RowSpan));

            var columnStarts = starts(columnSizes, ColumnSpacing);
            var rowStarts = starts(rowSizes, RowSpacing);

            foreach (var slot in Slots)
            {
                var p = Layout.Get(slot.Content);
                var (c0, c1) = span(p.Column, p.ColumnSpan, columnSizes.Length);
                var (r0, r1) = span(p.Row, p.RowSpan, rowSizes.Length);

                float x = columnStarts[c0], y = rowStarts[r0];
                float w = columnStarts[c1] + columnSizes[c1] - x;
                float h = rowStarts[r1] + rowSizes[r1] - y;

                slot.Arrange(new Vector2(x, y), new Vector2(w, h));
            }
        }

        /// <summary>
        /// Sizes one axis: pixels first, then Auto from the largest single-span child, then stars share what is left.
        /// When the grid is sizing to its content (infinite space), a star track takes the size its content needs.
        /// </summary>
        private float[] solve(IReadOnlyList<GridLength> defs, float available, float spacing, List<Vector2> desired, Func<Vector2, float> axis,
                              Func<Slot, (int index, int span)> cellOf)
        {
            var sizes = new float[defs.Count];
            var content = new float[defs.Count];

            for (int i = 0; i < Slots.Count; i++)
            {
                var (index, spanCount) = cellOf(Slots[i]);
                if (spanCount != 1)
                    continue;

                index = Math.Clamp(index, 0, defs.Count - 1);
                content[index] = Math.Max(content[index], axis(desired[i]));
            }

            float used = spacing * (defs.Count - 1);
            float totalWeight = 0;

            for (int i = 0; i < defs.Count; i++)
            {
                switch (defs[i].Type)
                {
                    case GridUnitType.Pixel:
                        sizes[i] = defs[i].Value;
                        used += sizes[i];
                        break;

                    case GridUnitType.Auto:
                        sizes[i] = content[i];
                        used += sizes[i];
                        break;

                    case GridUnitType.Star:
                        totalWeight += defs[i].Value;
                        break;
                }
            }

            if (totalWeight > 0)
            {
                float unit;

                if (float.IsInfinity(available))
                {
                    unit = 0;
                    for (int i = 0; i < defs.Count; i++)
                    {
                        if (defs[i].Type == GridUnitType.Star && defs[i].Value > 0)
                            unit = Math.Max(unit, content[i] / defs[i].Value);
                    }
                }
                else
                    unit = Math.Max(0, available - used) / totalWeight;

                for (int i = 0; i < defs.Count; i++)
                {
                    if (defs[i].Type == GridUnitType.Star)
                        sizes[i] = unit * defs[i].Value;
                }
            }

            return sizes;
        }

        private static float[] starts(float[] sizes, float spacing)
        {
            var result = new float[sizes.Length];
            for (int i = 1; i < sizes.Length; i++)
                result[i] = result[i - 1] + sizes[i - 1] + spacing;
            return result;
        }

        private static (int first, int last) span(int index, int spanCount, int count)
        {
            int first = Math.Clamp(index, 0, count - 1);
            int last = Math.Clamp(index + spanCount - 1, first, count - 1);
            return (first, last);
        }
    }
}
