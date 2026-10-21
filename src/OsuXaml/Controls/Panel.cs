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
    /// Base for WPF-style panels. After children have updated (so auto-sized children know their size), a panel measures its
    /// children and arranges their slots, but only when something changed: a child was added, removed or resized, an attached
    /// or panel property changed, an element inside reported a new desired size (<see cref="Layout.InvalidateMeasure"/>), or the
    /// panel's own size changed. Otherwise it keeps last frame's layout. A panel that isn't inside another panel and has no
    /// size of its own sizes itself to its content.
    /// </summary>
    public abstract partial class Panel : CompositeDrawable, ILayoutElement, IItemsHost
    {
        private readonly List<Slot> slots = new List<Slot>();
        private readonly Box background;

        private bool sizeToContentX;
        private bool sizeToContentY;

        // Bumped by every invalidation; a measurement or arrangement made at an older version is out of date.
        private int layoutVersion;
        private bool arrangeRequested = true;

        private int measuredVersion = -1;
        private Vector2 measuredFor;
        private Vector2 measured;
        private double measuredAt = double.NaN;

        private int arrangedVersion = -1;
        private Vector2 arrangedSize;

        // Whether the last measure or arrange measured an element that doesn't report its changes; until that's no longer
        // so, this panel measures and arranges every frame (keeping a measurement only for the frame it was made in).
        private bool measuredUnreported;
        private bool arrangedUnreported;

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
            set
            {
                if (base.Padding.Equals(value))
                    return;

                base.Padding = value;
                InvalidateMeasure();
            }
        }

        public bool ReportsMeasureChanges => true;

        /// <summary>How many times this panel has measured its children (not counting measurements it kept).</summary>
        internal int MeasureCount { get; private set; }

        /// <summary>How many times this panel has arranged its children.</summary>
        internal int ArrangeCount { get; private set; }

        /// <summary>Whether this panel measures and arranges every frame, because it holds an element that doesn't report its changes.</summary>
        internal bool LaysOutEveryFrame => measuredUnreported || arrangedUnreported;

        private bool unreported => LaysOutEveryFrame;

        public void Add(Drawable drawable) => Insert(slots.Count, drawable);

        public void Insert(int index, Drawable drawable)
        {
            var slot = new Slot(drawable);
            slots.Insert(index, slot);
            AddInternal(slot);
            InvalidateMeasure();
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
            InvalidateMeasure();
            return true;
        }

        public void Clear()
        {
            foreach (var slot in slots)
                RemoveInternal(slot, true);
            slots.Clear();
            InvalidateMeasure();
        }

        /// <summary>Puts the slots in the order of <paramref name="contents"/>, which must be exactly this panel's children.</summary>
        internal void ReorderChildren(IEnumerable<Drawable> contents)
        {
            var byContent = slots.ToDictionary(s => s.Content);
            slots.Clear();

            foreach (var content in contents)
                slots.Add(byContent[content]);

            InvalidateMeasure();
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
            if (measuredVersion == layoutVersion && measuredFor == availableSize && (!unreported || measuredAt == Time.Current))
            {
                // Whoever measures this panel can't keep the result past this frame either.
                if (unreported)
                    Layout.MeasuredUnreported();

                return measured;
            }

            int version = layoutVersion;
            bool outer = Layout.BeginWatch();

            var padding = new Vector2(Padding.TotalHorizontal, Padding.TotalVertical);
            var desired = MeasureOverride(Vector2.ComponentMax(Vector2.Zero, availableSize - padding)) + padding;

            measuredUnreported = Layout.EndWatch(outer);
            measuredVersion = version;
            measuredAt = Time.Current;
            measuredFor = availableSize;
            MeasureCount++;
            return measured = desired;
        }

        /// <summary>
        /// Makes this panel, and every panel it is in, measure and arrange again on the next frame, for a change to something
        /// its measuring reads that it doesn't notice by itself.
        /// </summary>
        public void InvalidateMeasure() => Layout.InvalidateMeasure(this);

        /// <summary>Makes this panel arrange its children again on the next frame, for a change that moves them without changing the panel's desired size.</summary>
        public void InvalidateArrange() => arrangeRequested = true;

        /// <summary>Marks this panel's own measurement and arrangement out of date; <see cref="Layout.InvalidateMeasure"/> calls it on every panel on the way up.</summary>
        internal void InvalidateLayout() => layoutVersion++;

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

            var size = ChildSize;

            if (arrangedVersion == layoutVersion && !arrangeRequested && arrangedSize == size && !unreported)
                return;

            // Invalidations made while arranging (a virtualizing panel adding the rows it now shows) count for the next frame.
            int version = layoutVersion;
            arrangeRequested = false;
            bool outer = Layout.BeginWatch();

            ArrangeOverride(size);

            arrangedUnreported = Layout.EndWatch(outer);
            arrangedVersion = version;
            arrangedSize = size;
            ArrangeCount++;
        }

        protected static float Axis(Vector2 v, Orientation o) => o == Orientation.Horizontal ? v.X : v.Y;

        protected static float Cross(Vector2 v, Orientation o) => o == Orientation.Horizontal ? v.Y : v.X;

        protected static Vector2 Make(float along, float cross, Orientation o) => o == Orientation.Horizontal ? new Vector2(along, cross) : new Vector2(cross, along);

        protected static float Finite(float value) => float.IsInfinity(value) ? 0 : value;
    }

    /// <summary>Lays children out in a line, like WPF's StackPanel. <c>Spacing</c> adds a gap between children.</summary>
    public partial class StackPanel : Panel
    {
        private Orientation orientation;
        private float spacing;

        public Orientation Orientation
        {
            get => orientation;
            set => Layout.SetAffectsMeasure(this, ref orientation, value);
        }

        public float Spacing
        {
            get => spacing;
            set => Layout.SetAffectsMeasure(this, ref spacing, value);
        }

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
        private Orientation orientation = Orientation.Horizontal;
        private float spacing;

        public Orientation Orientation
        {
            get => orientation;
            set => Layout.SetAffectsMeasure(this, ref orientation, value);
        }

        public float Spacing
        {
            get => spacing;
            set => Layout.SetAffectsMeasure(this, ref spacing, value);
        }

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
        private bool lastChildFill = true;

        public bool LastChildFill
        {
            get => lastChildFill;
            set => Layout.SetAffectsMeasure(this, ref lastChildFill, value);
        }

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
        private int rows;
        private int columns;

        public int Rows
        {
            get => rows;
            set => Layout.SetAffectsMeasure(this, ref rows, value);
        }

        public int Columns
        {
            get => columns;
            set => Layout.SetAffectsMeasure(this, ref columns, value);
        }

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
