using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Utils;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>When a <see cref="ScrollViewer"/> scrolls along an axis and shows its scroll bar, like WPF's ScrollBarVisibility.</summary>
    public enum ScrollBarVisibility
    {
        /// <summary>No scrolling: the content gets the viewport's size on this axis.</summary>
        Disabled,

        /// <summary>Scrolls, with a scroll bar while the content doesn't fit.</summary>
        Auto,

        /// <summary>Scrolls without a scroll bar.</summary>
        Hidden,

        /// <summary>Scrolls with a scroll bar. osu!framework only draws a bar while there is something to scroll, so this is the same as Auto.</summary>
        Visible,
    }

    /// <summary>
    /// Scrolls one child, like WPF's ScrollViewer: vertically by default, and horizontally or both ways through
    /// <see cref="HorizontalScrollBarVisibility"/> and <see cref="VerticalScrollBarVisibility"/>. The child is measured with
    /// unlimited space on each scrolling axis and arranged at least as large as the viewport, so a panel child fills the
    /// viewport and grows with its content. The mouse wheel scrolls vertically (Shift+wheel or a sideways wheel scrolls
    /// horizontally) and is passed on to an outer ScrollViewer when there is nothing to scroll here.
    /// </summary>
    public partial class ScrollViewer : CompositeDrawable, ILayoutElement
    {
        // The vertical scroller fills the viewport; the horizontal one sits inside it, as wide as the viewport and as tall
        // as the content, so each axis keeps osu!framework's own scrolling, dragging and scroll bar.
        private readonly AxisScroller vertical;
        private readonly AxisScroller horizontal;

        private ScrollBarVisibility horizontalVisibility = ScrollBarVisibility.Disabled;
        private ScrollBarVisibility verticalVisibility = ScrollBarVisibility.Visible;

        private Drawable child;
        private Slot slot;

        private Vector2 lastOffset;
        private double laidOutAt = double.NaN;

        public ScrollViewer()
        {
            horizontal = new AxisScroller(Direction.Horizontal) { RelativeSizeAxes = Axes.X };
            vertical = new AxisScroller(Direction.Vertical) { RelativeSizeAxes = Axes.Both };
            vertical.Add(horizontal);

            InternalChild = vertical;
            applyVisibility();
        }

        /// <summary>The scrolled content. Replacing it disposes the previous child.</summary>
        public Drawable Child
        {
            get => child;
            set
            {
                if (ReferenceEquals(child, value))
                    return;

                if (slot != null)
                {
                    horizontal.Remove(slot, true);
                    slot = null;
                }

                child = value;

                if (child != null)
                    horizontal.Add(slot = new ContentSlot(child, this));
            }
        }

        public ScrollBarVisibility HorizontalScrollBarVisibility
        {
            get => horizontalVisibility;
            set
            {
                horizontalVisibility = value;
                applyVisibility();
            }
        }

        public ScrollBarVisibility VerticalScrollBarVisibility
        {
            get => verticalVisibility;
            set
            {
                verticalVisibility = value;
                applyVisibility();
            }
        }

        /// <summary>Raised when <see cref="HorizontalOffset"/> or <see cref="VerticalOffset"/> changes.</summary>
        public event Action ScrollChanged;

        /// <summary>The current scroll offset from the top.</summary>
        public double VerticalOffset => vertical.Current;

        /// <summary>The current scroll offset from the left.</summary>
        public double HorizontalOffset => horizontal.Current;

        /// <summary>The size of the visible area.</summary>
        public float ViewportWidth => vertical.ChildSize.X;

        public float ViewportHeight => vertical.ChildSize.Y;

        /// <summary>The size of the content as arranged (at least the viewport size).</summary>
        public float ExtentWidth => slot?.DrawWidth ?? 0;

        public float ExtentHeight => slot?.DrawHeight ?? 0;

        /// <summary>How far the content can scroll.</summary>
        public double ScrollableWidth => horizontal.ScrollableExtent;

        public double ScrollableHeight => vertical.ScrollableExtent;

        private bool canScrollX => horizontalVisibility != ScrollBarVisibility.Disabled;

        private bool canScrollY => verticalVisibility != ScrollBarVisibility.Disabled;

        /// <summary>Scrolls until <paramref name="descendant"/> is visible.</summary>
        public void ScrollIntoView(Drawable descendant, bool animated = true)
        {
            if (canScrollY)
                vertical.ScrollIntoView(descendant, animated);
            if (canScrollX)
                horizontal.ScrollIntoView(descendant, animated);
        }

        /// <summary>Scrolls to the top.</summary>
        public void ScrollToStart(bool animated = true) => vertical.ScrollToStart(animated);

        /// <summary>Scrolls to the bottom.</summary>
        public void ScrollToEnd(bool animated = true) => vertical.ScrollToEnd(animated);

        public void ScrollToTop(bool animated = true) => vertical.ScrollToStart(animated);

        public void ScrollToBottom(bool animated = true) => vertical.ScrollToEnd(animated);

        public void ScrollToLeftEnd(bool animated = true) => horizontal.ScrollToStart(animated);

        public void ScrollToRightEnd(bool animated = true) => horizontal.ScrollToEnd(animated);

        /// <summary>Scrolls to the top-left corner.</summary>
        public void ScrollToHome(bool animated = true)
        {
            ScrollToTop(animated);
            ScrollToLeftEnd(animated);
        }

        public void ScrollToVerticalOffset(double offset, bool animated = true) => vertical.ScrollTo(offset, animated);

        /// <summary>
        /// Shifts the scroll position (and any scroll under way) at once, without animating: a virtualizing panel uses it
        /// to keep the rows in view in place when the estimated sizes above them are corrected.
        /// </summary>
        internal void OffsetScroll(Vector2 delta)
        {
            if (canScrollY && delta.Y != 0)
            {
                vertical.OffsetScrollPosition(delta.Y);
                offsetTotal.Y += delta.Y;
            }

            if (canScrollX && delta.X != 0)
            {
                horizontal.OffsetScrollPosition(delta.X);
                offsetTotal.X += delta.X;
            }
        }

        private Vector2d offsetTotal;

        /// <summary>
        /// Where the scroll is heading, less every <see cref="OffsetScroll"/> so far: it changes only when something scrolls on
        /// purpose (the user, or code), so a pending bring-into-view can tell that it should stop.
        /// </summary>
        internal Vector2d IntendedTarget => new Vector2d(horizontal.Target - offsetTotal.X, vertical.Target - offsetTotal.Y);

        /// <summary>Whether a scroll is still easing towards its target.</summary>
        internal bool IsScrollAnimating => !Precision.AlmostEquals(vertical.Current, vertical.Target) || !Precision.AlmostEquals(horizontal.Current, horizontal.Target);

        public void ScrollToHorizontalOffset(double offset, bool animated = true) => horizontal.ScrollTo(offset, animated);

        public Vector2 Measure(Vector2 availableSize)
        {
            if (slot == null)
                return Vector2.Zero;

            var desired = slot.Measure(new Vector2(canScrollX ? float.PositiveInfinity : availableSize.X, canScrollY ? float.PositiveInfinity : availableSize.Y));

            // A scroll viewer wants its content's size but takes no more than it is offered; the rest scrolls.
            return Vector2.ComponentMin(desired, availableSize);
        }

        private void applyVisibility()
        {
            vertical.CanScroll = canScrollY;
            horizontal.CanScroll = canScrollX;
            horizontal.YieldsVerticalInput = canScrollY;

            vertical.ScrollbarVisible = verticalVisibility == ScrollBarVisibility.Auto || verticalVisibility == ScrollBarVisibility.Visible;
            horizontal.ScrollbarVisible = horizontalVisibility == ScrollBarVisibility.Auto || horizontalVisibility == ScrollBarVisibility.Visible;

            // Leaving a scrolled axis behind snaps it back to the start.
            if (!canScrollY) vertical.ScrollToStart(false);
            if (!canScrollX) horizontal.ScrollToStart(false);
        }

        /// <summary>Measures the content and sizes the scrolled area to it (at least the viewport on each scrolling axis).</summary>
        private void layoutContent()
        {
            laidOutAt = Time.Current;

            var viewport = vertical.ChildSize;
            var extent = viewport;

            if (slot != null)
            {
                var desired = slot.Measure(new Vector2(canScrollX ? float.PositiveInfinity : viewport.X, canScrollY ? float.PositiveInfinity : viewport.Y));

                if (canScrollX) extent.X = Math.Max(desired.X, viewport.X);
                if (canScrollY) extent.Y = Math.Max(desired.Y, viewport.Y);

                slot.Arrange(Vector2.Zero, extent);
            }

            horizontal.Height = extent.Y;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            // The content slot normally lays out before the scrollers move; this covers a frame where it didn't update.
            if (laidOutAt != Time.Current)
                layoutContent();

            // The horizontal bar belongs to the content-tall inner scroller; keep it at the bottom of the viewport instead.
            horizontal.ScrollbarOffset = (float)Math.Min(0, vertical.Current + vertical.ChildSize.Y - horizontal.Height);

            var offset = new Vector2((float)horizontal.Current, (float)vertical.Current);

            if (offset != lastOffset)
            {
                lastOffset = offset;
                ScrollChanged?.Invoke();
            }
        }

        /// <summary>
        /// Holds the content. It lays the content out right after the content updates, before either scroller updates its
        /// position, so a scroll position set this frame (such as a virtualizing panel keeping its rows in place) is
        /// checked against this frame's extent rather than last frame's.
        /// </summary>
        private partial class ContentSlot : Slot
        {
            private readonly ScrollViewer viewer;

            public ContentSlot(Drawable content, ScrollViewer viewer)
                : base(content)
            {
                this.viewer = viewer;
            }

            protected override void UpdateAfterChildren()
            {
                base.UpdateAfterChildren();
                viewer.layoutContent();
            }
        }

        /// <summary>Scrolls one axis. Input it shouldn't take (a disabled axis, nothing to scroll) goes on to the other axis or an outer scroller.</summary>
        private partial class AxisScroller : ScrollContainer<Drawable>, IKeyBindingHandler<PlatformAction>
        {
            public bool CanScroll = true;

            /// <summary>Set on the horizontal scroller while the vertical one scrolls: plain wheel and paging keys belong to the vertical axis.</summary>
            public bool YieldsVerticalInput;

            public AxisScroller(Direction direction)
                : base(direction)
            {
            }

            public float ScrollbarOffset
            {
                set => Scrollbar.Y = value;
            }

            private bool hasRoom => CanScroll && ScrollableExtent > 0;

            protected override ScrollbarContainer CreateScrollbar(Direction direction) => new ThemedScrollbar(direction);

            protected override bool OnScroll(ScrollEvent e)
            {
                if (!hasRoom)
                    return false;

                if (YieldsVerticalInput && !e.ShiftPressed && Math.Abs(e.ScrollDelta.X) <= Math.Abs(e.ScrollDelta.Y))
                    return false;

                return base.OnScroll(e);
            }

            protected override bool OnDragStart(DragStartEvent e) => hasRoom && base.OnDragStart(e);

            protected override bool OnKeyDown(KeyDownEvent e)
            {
                if (!CanScroll || YieldsVerticalInput)
                    return false;

                return base.OnKeyDown(e);
            }

            bool IKeyBindingHandler<PlatformAction>.OnPressed(KeyBindingPressEvent<PlatformAction> e)
                => CanScroll && !YieldsVerticalInput && OnPressed(e);

            void IKeyBindingHandler<PlatformAction>.OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
            {
            }

            /// <summary>A thin rounded bar in the theme's colours that brightens on hover.</summary>
            private partial class ThemedScrollbar : ScrollbarContainer
            {
                private const float thickness = 6;

                private readonly Box bar;

                public ThemedScrollbar(Direction direction)
                    : base(direction)
                {
                    CornerRadius = thickness / 2;
                    Masking = true;
                    Margin = direction == Direction.Vertical ? new MarginPadding { Right = 2 } : new MarginPadding { Bottom = 2 };
                    Child = bar = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Overlay, Alpha = 0.6f };
                }

                protected override float MinimumDimSize => 24;

                public override void ResizeTo(float val, int duration = 0, Easing easing = Easing.None)
                {
                    var size = new Vector2(thickness);
                    size[(int)ScrollDirection] = val;
                    this.ResizeTo(size, duration, easing);
                }

                protected override bool OnHover(HoverEvent e)
                {
                    bar.FadeTo(1, 100, Easing.OutQuint);
                    return base.OnHover(e);
                }

                protected override void OnHoverLost(HoverLostEvent e)
                {
                    bar.FadeTo(0.6f, 250, Easing.OutQuint);
                    base.OnHoverLost(e);
                }
            }
        }
    }
}
