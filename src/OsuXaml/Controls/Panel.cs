using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Base for WPF-style panels. Each frame, after children have updated (so auto-sized children know their size),
    /// the panel measures its children and arranges their slots. A panel that isn't inside another panel and
    /// has no size of its own sizes itself to its content.
    /// </summary>
    public abstract partial class Panel : CompositeDrawable, ILayoutElement, IItemsHost
    {
        private readonly List<Slot> slots = new List<Slot>();
        private readonly Box background;

        private bool sizeToContentX;
        private bool sizeToContentY;

        private double measuredAt = double.NaN;
        private Vector2 measuredFor;
        private Vector2 measured;

        protected Panel()
        {
            AddInternal(background = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, Depth = float.MaxValue });
        }

        protected IReadOnlyList<Slot> Slots => slots;

        public IEnumerable<Drawable> Children => slots.Select(s => s.Content);

        public Colour4 Background
        {
            get => background.Colour;
            set
            {
                background.Colour = value;
                background.Alpha = 1;
            }
        }

        public new MarginPadding Padding
        {
            get => base.Padding;
            set => base.Padding = value;
        }

        public void Add(Drawable drawable) => Insert(slots.Count, drawable);

        public void Insert(int index, Drawable drawable)
        {
            var slot = new Slot(drawable);
            slots.Insert(index, slot);
            AddInternal(slot);
        }

        public bool Remove(Drawable drawable, bool disposeImmediately)
        {
            int index = slots.FindIndex(s => s.Content == drawable);
            if (index < 0)
                return false;

            var slot = slots[index];
            slots.RemoveAt(index);

            // A child that isn't disposed may be added somewhere else, so it leaves its slot.
            if (!disposeImmediately)
                slot.Detach();

            RemoveInternal(slot, true);
            return true;
        }

        public void Clear()
        {
            foreach (var slot in slots)
                RemoveInternal(slot, true);
            slots.Clear();
        }

        /// <summary>Puts the slots in the order of <paramref name="contents"/>, which must be exactly this panel's children.</summary>
        internal void ReorderChildren(IEnumerable<Drawable> contents)
        {
            var byContent = slots.ToDictionary(s => s.Content);
            slots.Clear();

            foreach (var content in contents)
                slots.Add(byContent[content]);
        }

        /// <summary>The slot holding <paramref name="content"/>, or null.</summary>
        internal Slot SlotOf(Drawable content)
        {
            // A loaded slot is its content's parent; before loading, parents aren't set yet.
            if (content?.Parent is Slot slot && slot.Parent == this)
                return slot;

            return content == null ? null : slots.Find(s => s.Content == content);
        }

        public Vector2 Measure(Vector2 availableSize)
        {
            // Nested panels measure their subtree once per frame per constraint.
            if (Time.Current == measuredAt && measuredFor == availableSize)
                return measured;

            var padding = new Vector2(Padding.TotalHorizontal, Padding.TotalVertical);
            var desired = MeasureOverride(Vector2.ComponentMax(Vector2.Zero, availableSize - padding)) + padding;

            measuredAt = Time.Current;
            measuredFor = availableSize;
            return measured = desired;
        }

        /// <summary>Makes the next <see cref="Measure"/> run again this frame, for panels whose desired size just changed.</summary>
        protected void InvalidateMeasure() => measuredAt = double.NaN;

        protected abstract Vector2 MeasureOverride(Vector2 available);

        protected abstract void ArrangeOverride(Vector2 size);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && Height == 0;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (sizeToContentX || sizeToContentY)
            {
                var desired = Measure(new Vector2(sizeToContentX ? float.PositiveInfinity : DrawWidth, sizeToContentY ? float.PositiveInfinity : DrawHeight));
                if (sizeToContentX) Width = desired.X;
                if (sizeToContentY) Height = desired.Y;
            }

            ArrangeOverride(ChildSize);
        }

        protected static float Axis(Vector2 v, Orientation o) => o == Orientation.Horizontal ? v.X : v.Y;

        protected static float Cross(Vector2 v, Orientation o) => o == Orientation.Horizontal ? v.Y : v.X;

        protected static Vector2 Make(float along, float cross, Orientation o) => o == Orientation.Horizontal ? new Vector2(along, cross) : new Vector2(cross, along);

        protected static float Finite(float value) => float.IsInfinity(value) ? 0 : value;
    }

    /// <summary>Lays children out in a line, like WPF's StackPanel. <c>Spacing</c> adds a gap between children.</summary>
    public partial class StackPanel : Panel
    {
        public Orientation Orientation { get; set; }

        public float Spacing { get; set; }

        protected override Vector2 MeasureOverride(Vector2 available) => MeasureStack(Slots, available, Orientation, Spacing);

        protected override void ArrangeOverride(Vector2 size) => ArrangeStack(Slots, size, Orientation, Spacing);

        /// <summary>The size of <paramref name="slots"/> in a line; shared with <see cref="VirtualizingStackPanel"/> when it isn't virtualizing.</summary>
        internal static Vector2 MeasureStack(IReadOnlyList<Slot> slots, Vector2 available, Orientation orientation, float spacing)
        {
            float along = 0, cross = 0;
            var constraint = Make(float.PositiveInfinity, Cross(available, orientation), orientation);

            int visible = 0;

            foreach (var slot in slots)
            {
                if (Layout.IsCollapsed(slot.Content))
                    continue;

                var d = slot.Measure(constraint);
                along += Axis(d, orientation);
                cross = Math.Max(cross, Cross(d, orientation));
                visible++;
            }

            along += Math.Max(0, visible - 1) * spacing;
            return Make(along, cross, orientation);
        }

        internal static void ArrangeStack(IReadOnlyList<Slot> slots, Vector2 size, Orientation orientation, float spacing)
        {
            float position = 0;
            float cross = Cross(size, orientation);
            var constraint = Make(float.PositiveInfinity, cross, orientation);

            foreach (var slot in slots)
            {
                if (Layout.IsCollapsed(slot.Content))
                {
                    slot.Arrange(Make(position, 0, orientation), Vector2.Zero);
                    continue;
                }

                float length = Axis(slot.Measure(constraint), orientation);
                slot.Arrange(Make(position, 0, orientation), Make(length, cross, orientation));
                position += length + spacing;
            }
        }
    }

    /// <summary>Flows children in lines that wrap at the panel edge.</summary>
    public partial class WrapPanel : Panel
    {
        public Orientation Orientation { get; set; } = Orientation.Horizontal;

        public float Spacing { get; set; }

        protected override Vector2 MeasureOverride(Vector2 available) => layout(available, arrange: false);

        protected override void ArrangeOverride(Vector2 size) => layout(size, arrange: true);

        private Vector2 layout(Vector2 size, bool arrange)
        {
            float limit = Axis(size, Orientation);
            float along = 0, lineCross = 0, crossOffset = 0, maxAlong = 0;
            var lineStart = 0;
            var sizes = Slots.Select(s => s.Measure(new Vector2(float.PositiveInfinity))).ToList();

            for (int i = 0; i < Slots.Count; i++)
            {
                if (Layout.IsCollapsed(Slots[i].Content))
                {
                    if (arrange)
                        Slots[i].Arrange(Make(along, crossOffset, Orientation), Vector2.Zero);
                    continue;
                }

                float a = Axis(sizes[i], Orientation);

                if (i > lineStart && along + a > limit)
                {
                    crossOffset += lineCross + Spacing;
                    along = 0;
                    lineCross = 0;
                    lineStart = i;
                }

                if (arrange)
                    Slots[i].Arrange(Make(along, crossOffset, Orientation), sizes[i]);

                along += a + Spacing;
                lineCross = Math.Max(lineCross, Cross(sizes[i], Orientation));
                maxAlong = Math.Max(maxAlong, along - Spacing);
            }

            return Make(maxAlong, crossOffset + lineCross, Orientation);
        }
    }

    /// <summary>Docks children to the edges in order; the last child fills what is left.</summary>
    public partial class DockPanel : Panel
    {
        public bool LastChildFill { get; set; } = true;

        public static void SetDock(Drawable drawable, Dock dock) => Layout.Get(drawable).Dock = dock;

        public static Dock GetDock(Drawable drawable) => Layout.Get(drawable).Dock;

        protected override Vector2 MeasureOverride(Vector2 available)
        {
            float usedX = 0, usedY = 0, maxX = 0, maxY = 0;

            foreach (var slot in Slots)
            {
                var d = slot.Measure(new Vector2(Math.Max(0, available.X - usedX), Math.Max(0, available.Y - usedY)));

                switch (GetDock(slot.Content))
                {
                    case Dock.Left:
                    case Dock.Right:
                        maxY = Math.Max(maxY, usedY + d.Y);
                        usedX += d.X;
                        break;

                    default:
                        maxX = Math.Max(maxX, usedX + d.X);
                        usedY += d.Y;
                        break;
                }
            }

            return new Vector2(Math.Max(maxX, usedX), Math.Max(maxY, usedY));
        }

        protected override void ArrangeOverride(Vector2 size)
        {
            float left = 0, top = 0, right = size.X, bottom = size.Y;

            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                var remaining = new Vector2(Math.Max(0, right - left), Math.Max(0, bottom - top));

                if (LastChildFill && i == Slots.Count - 1)
                {
                    slot.Arrange(new Vector2(left, top), remaining);
                    break;
                }

                var d = slot.Measure(remaining);

                switch (GetDock(slot.Content))
                {
                    case Dock.Left:
                        slot.Arrange(new Vector2(left, top), new Vector2(d.X, remaining.Y));
                        left += d.X;
                        break;

                    case Dock.Right:
                        slot.Arrange(new Vector2(right - d.X, top), new Vector2(d.X, remaining.Y));
                        right -= d.X;
                        break;

                    case Dock.Top:
                        slot.Arrange(new Vector2(left, top), new Vector2(remaining.X, d.Y));
                        top += d.Y;
                        break;

                    case Dock.Bottom:
                        slot.Arrange(new Vector2(left, bottom - d.Y), new Vector2(remaining.X, d.Y));
                        bottom -= d.Y;
                        break;
                }
            }
        }
    }

    /// <summary>Places children at Canvas.Left / Canvas.Top (or Right / Bottom) with their own size.</summary>
    public partial class Canvas : Panel
    {
        public static void SetLeft(Drawable drawable, float value) => Layout.Get(drawable).Left = value;

        public static void SetTop(Drawable drawable, float value) => Layout.Get(drawable).Top = value;

        public static void SetRight(Drawable drawable, float value) => Layout.Get(drawable).Right = value;

        public static void SetBottom(Drawable drawable, float value) => Layout.Get(drawable).Bottom = value;

        protected override Vector2 MeasureOverride(Vector2 available) => Vector2.Zero;

        protected override void ArrangeOverride(Vector2 size)
        {
            foreach (var slot in Slots)
            {
                var p = Layout.Get(slot.Content);
                var d = slot.Measure(new Vector2(float.PositiveInfinity));

                float x = !float.IsNaN(p.Left) ? p.Left : !float.IsNaN(p.Right) ? size.X - p.Right - d.X : 0;
                float y = !float.IsNaN(p.Top) ? p.Top : !float.IsNaN(p.Bottom) ? size.Y - p.Bottom - d.Y : 0;

                slot.Arrange(new Vector2(x, y), d);
            }
        }
    }

    /// <summary>Equal-sized cells. With neither Rows nor Columns set it picks a near-square grid.</summary>
    public partial class UniformGrid : Panel
    {
        public int Rows { get; set; }

        public int Columns { get; set; }

        private (int rows, int columns) dimensions()
        {
            int n = Math.Max(1, Slots.Count);
            int columns = Columns > 0 ? Columns : Rows > 0 ? (int)Math.Ceiling(n / (double)Rows) : (int)Math.Ceiling(Math.Sqrt(n));
            int rows = Rows > 0 ? Rows : (int)Math.Ceiling(n / (double)columns);
            return (rows, columns);
        }

        protected override Vector2 MeasureOverride(Vector2 available)
        {
            var (rows, columns) = dimensions();
            var cell = new Vector2(available.X / columns, available.Y / rows);
            var max = Vector2.Zero;

            foreach (var slot in Slots)
                max = Vector2.ComponentMax(max, slot.Measure(cell));

            return new Vector2(max.X * columns, max.Y * rows);
        }

        protected override void ArrangeOverride(Vector2 size)
        {
            var (rows, columns) = dimensions();
            var cell = new Vector2(size.X / columns, size.Y / rows);

            for (int i = 0; i < Slots.Count; i++)
                Slots[i].Arrange(new Vector2(i % columns * cell.X, i / columns * cell.Y), cell);
        }
    }
}
