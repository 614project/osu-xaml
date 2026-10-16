using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        /// <summary>A fixed size in pixels, like WPF's <c>new GridLength(120)</c>.</summary>
        public GridLength(float pixels)
            : this(pixels, GridUnitType.Pixel)
        {
        }

        /// <summary>Sized to the content of the row or column.</summary>
        public static GridLength Auto => new GridLength(0, GridUnitType.Auto);

        /// <summary>One share of the space left over (<c>*</c>).</summary>
        public static GridLength Star => new GridLength(1, GridUnitType.Star);

        public bool IsAuto => Type == GridUnitType.Auto;

        public bool IsStar => Type == GridUnitType.Star;

        public bool IsAbsolute => Type == GridUnitType.Pixel;

        /// <summary>Reads <c>Auto</c>, <c>120</c>, <c>*</c> or <c>2*</c>.</summary>
        /// <exception cref="FormatException">The text is none of those.</exception>
        public static GridLength Parse(string text)
        {
            string s = text.Trim();

            if (s.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                return Auto;

            bool star = s.EndsWith("*", StringComparison.Ordinal);
            string number = star ? s.Substring(0, s.Length - 1) : s;

            if (star && number.Length == 0)
                return Star;

            if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0 || !float.IsFinite(value))
                throw new FormatException($"'{text}' is not a grid length: use Auto, a number of pixels, * or a weighted star such as 2*.");

            return new GridLength(value, star ? GridUnitType.Star : GridUnitType.Pixel);
        }

        public override string ToString() => Type switch
        {
            GridUnitType.Auto => "Auto",
            GridUnitType.Star => Value == 1 ? "*" : Value.ToString(CultureInfo.InvariantCulture) + "*",
            _ => Value.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// One row or column of a <see cref="Grid"/>: its requested length, optional limits, and the size the grid gave it.
    /// XAML usually writes the axis-specific <see cref="RowDefinition"/> or <see cref="ColumnDefinition"/>.
    /// Changing <see cref="Length"/> at runtime (as <see cref="GridSplitter"/> does) takes effect on the next layout pass.
    /// </summary>
    public class DefinitionBase
    {
        public DefinitionBase()
        {
        }

        public DefinitionBase(GridLength length)
        {
            Length = length;
        }

        public GridLength Length { get; set; } = GridLength.Star;

        public float MinLength { get; set; }

        public float MaxLength { get; set; } = float.PositiveInfinity;

        /// <summary>The size from the most recent layout pass.</summary>
        public float ActualLength { get; internal set; }

        /// <summary>Applies the limits. Like WPF, the minimum wins when the limits contradict each other.</summary>
        internal float Constrain(float size) => Math.Max(MinLength, Math.Min(size, MaxLength));

        public override string ToString() => Length.ToString();
    }

    /// <summary>A grid row: <c>&lt;RowDefinition Height="Auto" MinHeight="40" /&gt;</c>. Height defaults to <c>*</c>.</summary>
    public sealed class RowDefinition : DefinitionBase
    {
        public GridLength Height
        {
            get => Length;
            set => Length = value;
        }

        public float MinHeight
        {
            get => MinLength;
            set => MinLength = value;
        }

        public float MaxHeight
        {
            get => MaxLength;
            set => MaxLength = value;
        }

        public float ActualHeight => ActualLength;
    }

    /// <summary>A grid column: <c>&lt;ColumnDefinition Width="2*" MinWidth="40" /&gt;</c>. Width defaults to <c>*</c>.</summary>
    public sealed class ColumnDefinition : DefinitionBase
    {
        public GridLength Width
        {
            get => Length;
            set => Length = value;
        }

        public float MinWidth
        {
            get => MinLength;
            set => MinLength = value;
        }

        public float MaxWidth
        {
            get => MaxLength;
            set => MaxLength = value;
        }

        public float ActualWidth => ActualLength;
    }

    /// <summary>
    /// The rows or columns of a <see cref="Grid"/>. XAML fills it either from one string,
    /// <c>RowDefinitions="Auto,*,2*,40"</c> (validated at build time, then <see cref="Parse"/>), or WPF-style from
    /// <c>&lt;Grid.RowDefinitions&gt;&lt;RowDefinition Height="Auto" /&gt;...&lt;/Grid.RowDefinitions&gt;</c> through Add.
    /// </summary>
    public sealed class GridLengthCollection : Collection<DefinitionBase>
    {
        public void Add(GridLength length) => Add(new DefinitionBase(length));

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
    /// Definitions are written compactly (<c>ColumnDefinitions="Auto,*,2*"</c>) or as WPF elements with limits
    /// (<c>&lt;ColumnDefinition Width="2*" MinWidth="40" /&gt;</c>). The grid lays out every frame, so lengths changed
    /// at runtime (by code or a <see cref="GridSplitter"/>) apply on the next frame.
    /// </summary>
    public partial class Grid : Panel
    {
        /// <summary>The rows, top to bottom. Without any, the grid has one star row.</summary>
        public GridLengthCollection RowDefinitions { get; set; } = new GridLengthCollection();

        /// <summary>The columns, left to right. Without any, the grid has one star column.</summary>
        public GridLengthCollection ColumnDefinitions { get; set; } = new GridLengthCollection();

        public float RowSpacing { get; set; }

        public float ColumnSpacing { get; set; }

        public static void SetRow(Drawable drawable, int value) => Layout.Get(drawable).Row = value;

        public static void SetColumn(Drawable drawable, int value) => Layout.Get(drawable).Column = value;

        public static void SetRowSpan(Drawable drawable, int value) => Layout.Get(drawable).RowSpan = Math.Max(1, value);

        public static void SetColumnSpan(Drawable drawable, int value) => Layout.Get(drawable).ColumnSpan = Math.Max(1, value);

        public static int GetRow(Drawable drawable) => Layout.Get(drawable).Row;

        public static int GetColumn(Drawable drawable) => Layout.Get(drawable).Column;

        public static int GetRowSpan(Drawable drawable) => Layout.Get(drawable).RowSpan;

        public static int GetColumnSpan(Drawable drawable) => Layout.Get(drawable).ColumnSpan;

        // A grid without definitions has one star row and column; they are per grid because layout writes their actual size.
        private readonly DefinitionBase[] implicitRow = { new RowDefinition() };
        private readonly DefinitionBase[] implicitColumn = { new ColumnDefinition() };

        /// <summary>The rows used for layout: <see cref="RowDefinitions"/>, or a single star row when there are none.</summary>
        internal IReadOnlyList<DefinitionBase> EffectiveRows => RowDefinitions?.Count > 0 ? RowDefinitions : implicitRow;

        /// <summary>The columns used for layout: <see cref="ColumnDefinitions"/>, or a single star column when there are none.</summary>
        internal IReadOnlyList<DefinitionBase> EffectiveColumns => ColumnDefinitions?.Count > 0 ? ColumnDefinitions : implicitColumn;

        protected override Vector2 MeasureOverride(Vector2 available)
        {
            var (columnSizes, rowSizes) = solveTracks(available);

            return new Vector2(
                columnSizes.Sum() + ColumnSpacing * (columnSizes.Length - 1),
                rowSizes.Sum() + RowSpacing * (rowSizes.Length - 1));
        }

        protected override void ArrangeOverride(Vector2 size)
        {
            var columns = EffectiveColumns;
            var rows = EffectiveRows;
            var (columnSizes, rowSizes) = solveTracks(size);

            for (int i = 0; i < columns.Count; i++)
                columns[i].ActualLength = columnSizes[i];
            for (int i = 0; i < rows.Count; i++)
                rows[i].ActualLength = rowSizes[i];

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
        /// Sizes the columns, then the rows. Like WPF, children are measured for the columns with the width their cells
        /// already have (pixel columns), or unlimited width (Auto columns, and star columns while the grid sizes to its
        /// content); they are then measured again at their final cell width for the rows, so text that wraps in a star
        /// column gets the height it needs at that width.
        /// </summary>
        private (float[] columns, float[] rows) solveTracks(Vector2 available)
        {
            var columns = EffectiveColumns;
            var rows = EffectiveRows;

            var forColumns = Slots.Select(s =>
            {
                var p = Layout.Get(s.Content);
                return s.Measure(new Vector2(cellExtent(columns, p.Column, p.ColumnSpan, ColumnSpacing, null) ?? float.PositiveInfinity, available.Y));
            }).ToList();

            var columnSizes = solve(columns, available.X, ColumnSpacing, forColumns, d => d.X, s => (Layout.Get(s.Content).Column, Layout.Get(s.Content).ColumnSpan));

            var forRows = Slots.Select(s =>
            {
                var p = Layout.Get(s.Content);
                float width = cellExtent(columns, p.Column, p.ColumnSpan, ColumnSpacing, columnSizes) ?? available.X;
                float height = cellExtent(rows, p.Row, p.RowSpan, RowSpacing, null) ?? available.Y;
                return s.Measure(new Vector2(width, height));
            }).ToList();

            var rowSizes = solve(rows, available.Y, RowSpacing, forRows, d => d.Y, s => (Layout.Get(s.Content).Row, Layout.Get(s.Content).RowSpan));

            return (columnSizes, rowSizes);
        }

        /// <summary>
        /// The extent of the cells a child spans, from <paramref name="sizes"/> once the tracks are solved. Before that,
        /// only spans made of pixel tracks have a known extent; others return null.
        /// </summary>
        private static float? cellExtent(IReadOnlyList<DefinitionBase> defs, int index, int spanCount, float spacing, float[] sizes)
        {
            var (first, last) = span(index, spanCount, defs.Count);
            float extent = spacing * (last - first);

            for (int i = first; i <= last; i++)
            {
                if (sizes != null)
                    extent += sizes[i];
                else if (defs[i].Length.Type == GridUnitType.Pixel)
                    extent += defs[i].Constrain(defs[i].Length.Value);
                else
                    return null;
            }

            return extent;
        }

        /// <summary>
        /// Sizes one axis: pixels first, then Auto from the largest single-span child, then stars share what is left.
        /// Every track is kept within its Min/Max limits. When the grid is sizing to its content (infinite space),
        /// a star track takes the size its content needs.
        /// </summary>
        private float[] solve(IReadOnlyList<DefinitionBase> defs, float available, float spacing, List<Vector2> desired, Func<Vector2, float> axis,
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

                float size = axis(desired[i]);
                if (float.IsFinite(size))
                    content[index] = Math.Max(content[index], size);
            }

            float used = spacing * (defs.Count - 1);
            var stars = new List<int>();

            for (int i = 0; i < defs.Count; i++)
            {
                switch (defs[i].Length.Type)
                {
                    case GridUnitType.Pixel:
                        sizes[i] = defs[i].Constrain(defs[i].Length.Value);
                        used += sizes[i];
                        break;

                    case GridUnitType.Auto:
                        sizes[i] = defs[i].Constrain(content[i]);
                        used += sizes[i];
                        break;

                    case GridUnitType.Star:
                        stars.Add(i);
                        break;
                }
            }

            if (stars.Count == 0)
                return sizes;

            if (float.IsInfinity(available))
            {
                float unit = 0;

                foreach (int i in stars)
                {
                    if (defs[i].Length.Value > 0)
                        unit = Math.Max(unit, content[i] / defs[i].Length.Value);
                }

                foreach (int i in stars)
                    sizes[i] = defs[i].Constrain(unit * defs[i].Length.Value);
            }
            else
                shareStars(defs, stars, sizes, Math.Max(0, available - used));

            return sizes;
        }

        /// <summary>
        /// Shares <paramref name="space"/> between star tracks by weight, keeping each within its limits. Each round
        /// clamps every share; if the clamps add space overall the tracks held at their minimum are fixed there, if they
        /// remove space the ones held at their maximum are, and the rest is shared again among the others. So a MinWidth
        /// column never squeezes below its minimum and a capped column hands what it can't take to its neighbours.
        /// </summary>
        private static void shareStars(IReadOnlyList<DefinitionBase> defs, List<int> stars, float[] sizes, float space)
        {
            var pending = new List<int>(stars);
            var fixedNow = new List<int>();

            while (pending.Count > 0)
            {
                float totalWeight = 0;
                foreach (int i in pending)
                    totalWeight += defs[i].Length.Value;

                float unit = totalWeight > 0 ? space / totalWeight : 0;
                float violation = 0;

                foreach (int i in pending)
                {
                    float share = unit * defs[i].Length.Value;
                    sizes[i] = defs[i].Constrain(share);
                    violation += sizes[i] - share;
                }

                if (Math.Abs(violation) < 0.001f)
                    return;

                fixedNow.Clear();
                fixedNow.AddRange(pending.Where(i => violation > 0
                    ? sizes[i] > unit * defs[i].Length.Value
                    : sizes[i] < unit * defs[i].Length.Value));

                foreach (int i in fixedNow)
                {
                    space = Math.Max(0, space - sizes[i]);
                    pending.Remove(i);
                }
            }
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
