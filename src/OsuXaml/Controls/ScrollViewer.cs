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
    /// Draws the scroll bars of <see cref="ScrollViewer"/>s in place of their built-in thin bars, for a theme library. A viewer
    /// asks the nearest element around it that implements this when it loads, then <see cref="ScrollViewer.DefaultScrollBarProvider"/>.
    /// </summary>
    public interface IScrollBarProvider
    {
        /// <summary>
        /// The bars of <paramref name="viewer"/>: a drawable laid over the whole viewer, taking input only where it draws, that
        /// reads the viewer's offsets and scroll bar visibilities and scrolls it. Null keeps the built-in bars.
        /// </summary>
        Drawable CreateScrollBars(ScrollViewer viewer);
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

        // How long a bring-into-view request keeps correcting the scroll position while the layout settles.
        private const double max_bring_into_view_time = 3000;

        private Func<(Drawable drawable, bool final)> bringIntoView;
        private int bringIntoViewRequest;
        private double bringIntoViewStarted;
        private bool bringIntoViewStarting;
        private int bringIntoViewStill;
        private int bringIntoViewScrolls;

        private Drawable scrollBars;

        /// <summary>Gives every ScrollViewer without a provider around it these scroll bars (null: the built-in ones).</summary>
        public static IScrollBarProvider DefaultScrollBarProvider { get; set; }

        /// <summary>The bars an <see cref="IScrollBarProvider"/> made for this viewer, once it has loaded; null with the built-in bars.</summary>
        public Drawable ScrollBars => scrollBars;

        public ScrollViewer()
        {
            horizontal = new AxisScroller(Direction.Horizontal, this) { RelativeSizeAxes = Axes.X };
            vertical = new AxisScroller(Direction.Vertical, this) { RelativeSizeAxes = Axes.Both };
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

                Layout.InvalidateMeasure(this);
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
            ScrollRequests++;

            if (canScrollY)
                vertical.ScrollIntoView(descendant, animated);
            if (canScrollX)
                horizontal.ScrollIntoView(descendant, animated);
        }

        /// <summary>Scrolls to the top.</summary>
        public void ScrollToStart(bool animated = true) => ScrollToTop(animated);

        /// <summary>Scrolls to the bottom.</summary>
        public void ScrollToEnd(bool animated = true) => ScrollToBottom(animated);

        public void ScrollToTop(bool animated = true)
        {
            ScrollRequests++;
            vertical.ScrollToStart(animated);
        }

        public void ScrollToBottom(bool animated = true)
        {
            ScrollRequests++;
            vertical.ScrollToEnd(animated);
        }

        public void ScrollToLeftEnd(bool animated = true)
        {
            ScrollRequests++;
            horizontal.ScrollToStart(animated);
        }

        public void ScrollToRightEnd(bool animated = true)
        {
            ScrollRequests++;
            horizontal.ScrollToEnd(animated);
        }

        /// <summary>Scrolls to the top-left corner.</summary>
        public void ScrollToHome(bool animated = true)
        {
            ScrollToTop(animated);
            ScrollToLeftEnd(animated);
        }

        public void ScrollToVerticalOffset(double offset, bool animated = true)
        {
            ScrollRequests++;
            vertical.ScrollTo(offset, animated);
        }

        /// <summary>
        /// Shifts the scroll position (and any scroll under way) at once, without animating: a virtualizing panel uses it
        /// to keep the rows in view in place when the estimated sizes above them are corrected.
        /// </summary>
        internal void OffsetScroll(Vector2 delta)
        {
            if (canScrollY && delta.Y != 0)
                vertical.OffsetScrollPosition(delta.Y);

            if (canScrollX && delta.X != 0)
                horizontal.OffsetScrollPosition(delta.X);
        }

        /// <summary>
        /// Counts the scrolls asked for, by the user (wheel, drag, scroll bar, keys) or by code through this viewer; not
        /// <see cref="OffsetScroll"/>, nor the scrollers keeping within the content. A pending bring-into-view stops when it changes.
        /// </summary>
        internal int ScrollRequests { get; private set; }

        /// <summary>Where the scroll is heading.</summary>
        internal Vector2d ScrollTarget => new Vector2d(horizontal.Target, vertical.Target);

        /// <summary>
        /// Scrolls until the drawable <paramref name="target"/> returns is in view, again each frame once the content has laid out,
        /// until the scroll has stayed put for a couple of frames: a row placed by estimate, or a tree node that only lays out
        /// once it comes into view, can move as the scroll reaches it. While <paramref name="target"/> returns a stand-in that
        /// isn't <c>final</c> (an ancestor of a node that has no position yet), the scroll doesn't settle. Stops when anything
        /// else scrolls, when another request replaces this one, or when <paramref name="target"/> returns null.
        /// Returns the request, for <see cref="IsBringingIntoView"/>.
        /// </summary>
        internal int BringIntoView(Func<(Drawable drawable, bool final)> target)
        {
            bringIntoView = target;
            bringIntoViewStarting = true;
            bringIntoViewStarted = Time.Current;
            bringIntoViewStill = 0;
            return ++bringIntoViewRequest;
        }

        /// <summary>Whether <paramref name="request"/>, returned by <see cref="BringIntoView"/>, is still settling.</summary>
        internal bool IsBringingIntoView(int request) => bringIntoView != null && request == bringIntoViewRequest;

        /// <summary>Whether a scroll is still easing towards its target.</summary>
        internal bool IsScrollAnimating => !Precision.AlmostEquals(vertical.Current, vertical.Target) || !Precision.AlmostEquals(horizontal.Current, horizontal.Target);

        public void ScrollToHorizontalOffset(double offset, bool animated = true)
        {
            ScrollRequests++;
            horizontal.ScrollTo(offset, animated);
        }

        /// <summary>True: the content's slot passes on its changes, and changing the content or scrolling axes reports itself.</summary>
        public bool ReportsMeasureChanges => true;

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
            Layout.InvalidateMeasure(this);

            vertical.CanScroll = canScrollY;
            horizontal.CanScroll = canScrollX;
            horizontal.YieldsVerticalInput = canScrollY;

            vertical.ScrollbarVisible = scrollBars == null && (verticalVisibility == ScrollBarVisibility.Auto || verticalVisibility == ScrollBarVisibility.Visible);
            horizontal.ScrollbarVisible = scrollBars == null && (horizontalVisibility == ScrollBarVisibility.Auto || horizontalVisibility == ScrollBarVisibility.Visible);

            // Leaving a scrolled axis behind snaps it back to the start.
            if (!canScrollY) vertical.ScrollToStart(false);
            if (!canScrollX) horizontal.ScrollToStart(false);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            IScrollBarProvider provider = null;

            for (var d = Parent; d != null && provider == null; d = d.Parent)
                provider = d as IScrollBarProvider;

            scrollBars = (provider ?? DefaultScrollBarProvider)?.CreateScrollBars(this);

            if (scrollBars != null)
            {
                AddInternal(scrollBars);
                applyVisibility();
            }
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

            continueBringIntoView();

            // The horizontal bar belongs to the content-tall inner scroller; keep it at the bottom of the viewport instead.
            horizontal.ScrollbarOffset = (float)Math.Min(0, vertical.Current + vertical.ChildSize.Y - horizontal.Height);

            var offset = new Vector2((float)horizontal.Current, (float)vertical.Current);

            if (offset != lastOffset)
            {
                lastOffset = offset;
                ScrollChanged?.Invoke();
            }
        }

        private void continueBringIntoView()
        {
            if (bringIntoView == null)
                return;

            // Someone else scrolled since last frame: they win.
            if (!bringIntoViewStarting && ScrollRequests != bringIntoViewScrolls)
            {
                bringIntoView = null;
                return;
            }

            var (target, final) = bringIntoView();

            if (target == null)
            {
                bringIntoView = null;
                return;
            }

            var before = ScrollTarget;
            ScrollIntoView(target);
            bringIntoViewScrolls = ScrollRequests;
            bringIntoViewStarting = false;

            bool still = final && (ScrollTarget - before).Length < 0.5 && !IsScrollAnimating;
            bringIntoViewStill = still ? bringIntoViewStill + 1 : 0;

            if (bringIntoViewStill >= 2 || Time.Current - bringIntoViewStarted > max_bring_into_view_time)
                bringIntoView = null;
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

            private readonly ScrollViewer viewer;

            public AxisScroller(Direction direction, ScrollViewer viewer)
                : base(direction)
            {
                this.viewer = viewer;
            }

            // Wheel, drag and scroll bar.
            protected override void OnUserScroll(double value, bool animated = true, double? distanceDecay = null)
            {
                viewer.ScrollRequests++;
                base.OnUserScroll(value, animated, distanceDecay);
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
                if (!CanScroll || YieldsVerticalInput || !base.OnKeyDown(e))
                    return false;

                viewer.ScrollRequests++;
                return true;
            }

            bool IKeyBindingHandler<PlatformAction>.OnPressed(KeyBindingPressEvent<PlatformAction> e)
            {
                if (!CanScroll || YieldsVerticalInput || !OnPressed(e))
                    return false;

                viewer.ScrollRequests++;
                return true;
            }

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
