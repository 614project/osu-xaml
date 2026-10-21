using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Pages behind a strip of tabs, like WPF's TabControl. List <c>&lt;TabItem Header="General"&gt;...&lt;/TabItem&gt;</c> children,
    /// or bind <c>ItemsSource</c> and give <c>ItemTemplate</c> (or <c>DisplayMemberPath</c>) for the tab headers and
    /// <see cref="ContentTemplate"/> for the pages. <c>SelectedIndex</c> and <c>SelectedItem</c> bind two-way, and the first tab
    /// is selected whenever nothing else is. Only the selected page is shown; a page is built the first time its tab is
    /// selected and kept while the tab exists. With focus, Left/Right (Up/Down for side strips), Home and End move between tabs,
    /// and Ctrl+Tab / Ctrl+Shift+Tab cycle through them from anywhere inside the control.
    /// </summary>
    public partial class TabControl : Selector
    {
        private readonly Dictionary<TabItem, ContentPresenter> pages = new Dictionary<TabItem, ContentPresenter>();
        private readonly DockPanel layout;
        private readonly TabStrip strip;
        private readonly PageArea pageArea;

        private Dock tabStripPlacement = Dock.Top;
        private DataTemplate contentTemplate;
        private StackPanel defaultPanel;
        private TabItem shownTab;
        private object pendingSelection;

        public TabControl()
        {
            layout = new DockPanel();
            strip = new TabStrip();
            pageArea = new PageArea();
        }

        /// <summary>Which side the tabs are on.</summary>
        public Dock TabStripPlacement
        {
            get => tabStripPlacement;
            set
            {
                tabStripPlacement = value;
                applyPlacement();
            }
        }

        /// <summary>Builds the page for data items and for TabItems whose content isn't a drawable and that have no ContentTemplate of their own.</summary>
        public DataTemplate ContentTemplate
        {
            get => contentTemplate;
            set
            {
                contentTemplate = value;

                foreach (var (tab, page) in pages)
                    applyPage(tab, page);
            }
        }

        /// <summary>The content of the selected tab.</summary>
        public object SelectedContent => shownTab?.Content;

        /// <summary>The background of the page area.</summary>
        public Colour4 Background
        {
            get => pageArea.Fill.Colour;
            set
            {
                pageArea.Fill.Colour = value;
                pageArea.Fill.Alpha = 1;
            }
        }

        /// <summary>The colour of the line between the tabs and the page.</summary>
        public Colour4 BorderBrush
        {
            get => strip.Divider.Colour;
            set => strip.Divider.Colour = value;
        }

        /// <summary>The colour of the selected-tab marker.</summary>
        public Colour4 Accent
        {
            get => strip.Indicator.Colour;
            set => strip.Indicator.Colour = value;
        }

        public new float CornerRadius
        {
            get => pageArea.CornerRadius;
            set => pageArea.CornerRadius = value;
        }

        /// <summary>Space around each page.</summary>
        public new MarginPadding Padding
        {
            get => pageArea.Pages.Padding;
            set => pageArea.Pages.Padding = value;
        }

        public override bool AcceptsFocus => true;

        /// <summary>Selects the next tab (or the previous one for a negative step), wrapping around and skipping disabled tabs.</summary>
        public void SelectNext(int step = 1)
        {
            if (step != 0)
                selectFrom(SelectedIndex.Value + step, Math.Sign(step));
        }

        protected override Panel CreateDefaultItemsPanel() => defaultPanel = new StackPanel();

        protected override Drawable CreateItemsHost(Panel panel)
        {
            // The panel fills the strip, and the strip takes the size the panel measures.
            panel.RelativeSizeAxes = Axes.Both;
            strip.SetPanel(panel);

            layout.Add(strip);
            layout.Add(pageArea);
            applyPlacement();

            return layout;
        }

        protected override bool IsItemItsOwnContainer(object item) => item is TabItem;

        protected override Drawable CreateContainer() => new TabItem();

        protected override void PrepareContainer(Drawable container, object item)
        {
            var tab = (TabItem)container;

            tab.Owner = this;

            if (!ReferenceEquals(tab, item))
            {
                tab.HeaderTemplate = ItemTemplate;
                tab.Header = item is Drawable ? null : item;
                tab.Content = item;
            }

            if (tab.TakeSelectionRequest())
                pendingSelection = item;

            base.PrepareContainer(container, item);
        }

        protected override void ClearContainer(Drawable container, object item)
        {
            base.ClearContainer(container, item);

            var tab = (TabItem)container;

            if (pages.Remove(tab, out var page))
            {
                if (tab == shownTab)
                {
                    shownTab = null;
                    pageArea.Shown = null;
                }

                pageArea.Pages.Remove(page, true);
            }

            tab.Owner = null;
        }

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();

            if (pendingSelection != null)
            {
                object requested = pendingSelection;
                pendingSelection = null;

                if (IndexOf(requested) >= 0)
                {
                    Select(requested);
                    return;
                }
            }

            // Like WPF, a tab control with tabs always shows one.
            if (SelectedIndex.Value < 0)
                selectFrom(0, 1);
        }

        protected override void OnSelectionChanged()
        {
            base.OnSelectionChanged();

            var tab = ContainerFromIndex(SelectedIndex.Value) as TabItem;
            strip.Select(tab);
            showPage(tab);
        }

        public override void SelectContainer(Drawable container, UIEvent e = null)
        {
            // A selection bound to a disabled bindable is locked, as for the keyboard in selectFrom.
            if (container is TabItem { IsEnabled: false } || SelectedIndex.Disabled)
                return;

            base.SelectContainer(container, e);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (Items.Count == 0)
                return base.OnKeyDown(e);

            if (e.Key == Key.Tab && e.ControlPressed && focusIsWithin())
            {
                SelectNext(e.ShiftPressed ? -1 : 1);
                return true;
            }

            if (!HasFocus)
                return base.OnKeyDown(e);

            bool horizontal = tabStripPlacement == Dock.Top || tabStripPlacement == Dock.Bottom;

            switch (e.Key)
            {
                case Key.Left when horizontal:
                case Key.Up when !horizontal:
                    SelectNext(-1);
                    return true;

                case Key.Right when horizontal:
                case Key.Down when !horizontal:
                    SelectNext(1);
                    return true;

                case Key.Home:
                    selectFrom(0, 1);
                    return true;

                case Key.End:
                    selectFrom(Items.Count - 1, -1);
                    return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>Called by a tab when its content or content template changes.</summary>
        internal void TabContentChanged(TabItem tab)
        {
            if (pages.TryGetValue(tab, out var page))
                applyPage(tab, page);
        }

        /// <summary>Selects the first enabled tab at or after <paramref name="start"/> going in <paramref name="direction"/>, wrapping around.</summary>
        private void selectFrom(int start, int direction)
        {
            int count = Items.Count;

            if (SelectedIndex.Disabled)
                return;

            for (int i = 0; i < count; i++)
            {
                int index = ((start + i * direction) % count + count) % count;

                if (ContainerFromIndex(index) is TabItem { IsEnabled: true })
                {
                    SelectedIndex.Value = index;
                    return;
                }
            }
        }

        private void applyPage(TabItem tab, ContentPresenter page)
        {
            var template = tab.ContentTemplate ?? contentTemplate;

            if (page.ContentTemplate != template)
                page.ContentTemplate = template;

            page.Content = tab.Content;
        }

        private void showPage(TabItem tab)
        {
            if (tab == shownTab)
                return;

            if (shownTab != null && pages.TryGetValue(shownTab, out var old))
            {
                // Focus must not stay in a page nobody can see.
                if (isWithin(GetContainingInputManager()?.FocusedDrawable, old))
                    GetContainingFocusManager()?.ChangeFocus(this);

                old.ClearTransforms();
                old.Alpha = 0;
            }

            shownTab = tab;

            if (tab == null)
            {
                pageArea.Shown = null;
                return;
            }

            if (!pages.TryGetValue(tab, out var page))
            {
                pages[tab] = page = new ContentPresenter { RelativeSizeAxes = Axes.Both };
                applyPage(tab, page);
                pageArea.Pages.Add(page);
            }

            pageArea.Shown = page;

            if (page.LoadState >= LoadState.Ready && IsLoaded)
                page.FadeInFromZero(150, Easing.OutQuint);
            else
                page.Alpha = 1;
        }

        private void applyPlacement()
        {
            bool horizontal = tabStripPlacement == Dock.Top || tabStripPlacement == Dock.Bottom;

            DockPanel.SetDock(strip, tabStripPlacement);
            strip.Placement = tabStripPlacement;

            if (defaultPanel != null)
                defaultPanel.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        }

        private bool focusIsWithin() => isWithin(GetContainingInputManager()?.FocusedDrawable, this);

        private static bool isWithin(Drawable drawable, Drawable ancestor)
        {
            for (var d = drawable; d != null; d = d.Parent)
            {
                if (d == ancestor)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The tab headers, a line between them and the page, and the animated marker under the selected tab. Tabs that don't
        /// fit scroll: the mouse wheel moves them, arrow buttons appear at both ends, and the selected tab is kept in view.
        /// </summary>
        internal partial class TabStrip : CompositeDrawable, ILayoutElement
        {
            public readonly Box Divider;
            public readonly Circle Indicator;

            private const float indicator_thickness = 3;
            private const float button_size = 24;
            private const float wheel_step = 80;

            private readonly Container viewport;
            private readonly Container content;
            private readonly TabStripScrollButton back;
            private readonly TabStripScrollButton forward;

            private Panel panel;
            private Dock placement;
            private TabItem selected;
            private bool animateNext;
            private RectangleF? lastTarget;
            private bool revealSelected;
            private float viewportLength;

            public TabStrip()
            {
                InternalChildren = new Drawable[]
                {
                    Divider = new Box { Colour = Theme.Border },
                    viewport = new Container
                    {
                        Child = content = new Container
                        {
                            Child = Indicator = new Circle { Colour = Theme.Accent, Alpha = 0, Depth = -1 },
                        },
                    },
                    back = new TabStripScrollButton(this, -1) { Alpha = 0 },
                    forward = new TabStripScrollButton(this, 1) { Alpha = 0 },
                };
            }

            private bool horizontal => placement == Dock.Top || placement == Dock.Bottom;

            /// <summary>Whether the tabs are longer than the strip, so they scroll.</summary>
            public bool Overflows { get; private set; }

            /// <summary>How far the tabs are scrolled, in pixels along the strip.</summary>
            public float ScrollOffset { get; private set; }

            /// <summary>Where <see cref="ScrollOffset"/> is heading.</summary>
            public float ScrollTarget { get; private set; }

            /// <summary>The furthest the tabs can scroll.</summary>
            public float MaxScroll { get; private set; }

            public Dock Placement
            {
                get => placement;
                set
                {
                    placement = value;

                    // One pixel thick, full length.
                    Divider.RelativeSizeAxes = horizontal ? Axes.X : Axes.Y;
                    Divider.Size = Vector2.One;
                    Divider.Anchor = Divider.Origin = value switch
                    {
                        Dock.Top => Anchor.BottomLeft,
                        Dock.Bottom => Anchor.TopLeft,
                        Dock.Left => Anchor.TopRight,
                        _ => Anchor.TopLeft,
                    };

                    back.Icon = horizontal ? FontAwesome.Solid.ChevronLeft : FontAwesome.Solid.ChevronUp;
                    forward.Icon = horizontal ? FontAwesome.Solid.ChevronRight : FontAwesome.Solid.ChevronDown;

                    lastTarget = null;
                    revealSelected = true;

                    // Which way the strip caps its length to what it is offered.
                    Layout.InvalidateMeasure(this);
                }
            }

            public void SetPanel(Panel itemsPanel)
            {
                panel = itemsPanel;
                content.Add(panel);
                Layout.InvalidateMeasure(this);
            }

            public void Select(TabItem tab)
            {
                if (tab == selected)
                    return;

                selected = tab;
                animateNext = true;
                revealSelected = true;
            }

            /// <summary>Scrolls the tabs by <paramref name="delta"/> pixels (positive towards the end).</summary>
            public void ScrollBy(float delta) => ScrollTarget = Math.Clamp(ScrollTarget + delta, 0, MaxScroll);

            public bool ReportsMeasureChanges => true;

            public Vector2 Measure(Vector2 availableSize)
            {
                if (panel == null)
                    return Vector2.Zero;

                // Tabs that don't fit scroll, so the strip never asks for more length than it is offered.
                var desired = panel.Measure(availableSize);
                return horizontal ? new Vector2(Math.Min(desired.X, availableSize.X), desired.Y) : new Vector2(desired.X, Math.Min(desired.Y, availableSize.Y));
            }

            protected override bool OnScroll(ScrollEvent e)
            {
                if (!Overflows)
                    return false;

                // A vertical wheel scrolls a horizontal strip too; a sideways wheel scrolls it directly.
                float delta = horizontal && e.ScrollDelta.X != 0 ? e.ScrollDelta.X : e.ScrollDelta.Y;
                ScrollBy(-delta * wheel_step);
                return true;
            }

            protected override void Update()
            {
                base.Update();

                if (panel == null)
                    return;

                float length = horizontal ? DrawWidth : DrawHeight;
                float cross = horizontal ? DrawHeight : DrawWidth;

                // Measured with the strip's own length, so a wrapping items panel wraps instead of scrolling.
                var desired = panel.Measure(along(length, cross));
                float extent = horizontal ? desired.X : desired.Y;
                bool overflows = length > 0 && extent > length + 0.5f;

                if (overflows != Overflows)
                {
                    Overflows = overflows;
                    revealSelected = true;
                    back.FadeTo(overflows ? 1 : 0, 150, Easing.OutQuint);
                    forward.FadeTo(overflows ? 1 : 0, 150, Easing.OutQuint);
                }

                float buttons = overflows ? button_size : 0;
                float newLength = Math.Max(0, length - buttons * 2);

                if (newLength != viewportLength)
                {
                    viewportLength = newLength;
                    revealSelected = true;
                }

                viewport.Position = along(buttons, 0);
                viewport.Size = along(viewportLength, cross);
                viewport.Masking = overflows;

                // The panel gets at least the viewport, so tabs that fit lay out as before.
                content.Size = along(Math.Max(extent, viewportLength), cross);

                back.Size = forward.Size = along(button_size, cross);
                forward.Position = along(length - button_size, 0);

                MaxScroll = overflows ? Math.Max(0, extent - viewportLength) : 0;
                ScrollTarget = Math.Clamp(ScrollTarget, 0, MaxScroll);
                ScrollOffset = Math.Clamp((float)Interpolation.DampContinuously(ScrollOffset, ScrollTarget, 40, Time.Elapsed), 0, MaxScroll);

                if (Precision.AlmostEquals(ScrollOffset, ScrollTarget, 0.5f))
                    ScrollOffset = ScrollTarget;

                content.Position = along(-ScrollOffset, 0);

                back.IsEnabled = ScrollTarget > 0;
                forward.IsEnabled = ScrollTarget < MaxScroll;
            }

            protected override void UpdateAfterChildren()
            {
                base.UpdateAfterChildren();

                bool hasSelected = selected != null && selected.Parent != null && selected.DrawWidth > 0 && selected.DrawHeight > 0;

                // The panel has arranged the tabs by now, so the selected one's place is known.
                if (revealSelected && hasSelected)
                {
                    revealSelected = false;
                    reveal(selected);
                }

                if (!hasSelected)
                {
                    Indicator.FadeOut(100);
                    lastTarget = null;
                    return;
                }

                // The marker lives in the scrolled content, so it moves with its tab.
                var topLeft = selected.ToSpaceOfOtherDrawable(Vector2.Zero, content);
                var size = selected.DrawSize;
                float contentWidth = content.DrawWidth, contentHeight = content.DrawHeight;
                const float inset = 10;

                var target = placement switch
                {
                    Dock.Top => new RectangleF(topLeft.X + inset, contentHeight - indicator_thickness, Math.Max(0, size.X - inset * 2), indicator_thickness),
                    Dock.Bottom => new RectangleF(topLeft.X + inset, 0, Math.Max(0, size.X - inset * 2), indicator_thickness),
                    Dock.Left => new RectangleF(contentWidth - indicator_thickness, topLeft.Y + 6, indicator_thickness, Math.Max(0, size.Y - 12)),
                    _ => new RectangleF(0, topLeft.Y + 6, indicator_thickness, Math.Max(0, size.Y - 12)),
                };

                if (lastTarget == target)
                    return;

                bool running = Indicator.LatestTransformEndTime > Time.Current;

                if (lastTarget == null || Indicator.Alpha == 0 || (!animateNext && !running))
                {
                    // First placement, or the layout moved: no animation.
                    Indicator.ClearTransforms();
                    Indicator.Position = target.Location;
                    Indicator.Size = target.Size;
                }
                else
                {
                    double duration = animateNext ? 250 : Math.Max(0, Indicator.LatestTransformEndTime - Time.Current);
                    Indicator.MoveTo(target.Location, duration, Easing.OutQuint);
                    Indicator.ResizeTo(target.Size, duration, Easing.OutQuint);
                }

                Indicator.FadeIn(100);
                animateNext = false;
                lastTarget = target;
            }

            /// <summary>Scrolls just enough to show all of <paramref name="tab"/>.</summary>
            private void reveal(TabItem tab)
            {
                if (!Overflows)
                    return;

                var position = tab.ToSpaceOfOtherDrawable(Vector2.Zero, content);
                float start = horizontal ? position.X : position.Y;
                float end = start + (horizontal ? tab.DrawWidth : tab.DrawHeight);

                if (start < ScrollTarget)
                    ScrollTarget = start;
                else if (end > ScrollTarget + viewportLength)
                    ScrollTarget = end - viewportLength;

                ScrollTarget = Math.Clamp(ScrollTarget, 0, MaxScroll);
            }

            private Vector2 along(float length, float cross) => horizontal ? new Vector2(length, cross) : new Vector2(cross, length);
        }

        /// <summary>
        /// An arrow at one end of an overflowing tab strip: a click scrolls the tabs by about half the strip, and holding it
        /// keeps them moving.
        /// </summary>
        internal partial class TabStripScrollButton : CompositeDrawable
        {
            private const double hold_delay = 300;
            private const float hold_speed = 0.6f;

            private readonly TabStrip strip;
            private readonly int direction;
            private readonly Box highlight;
            private readonly SpriteIcon icon;
            private bool isEnabled = true;
            private bool held;
            private double heldSince;

            public TabStripScrollButton(TabStrip strip, int direction)
            {
                this.strip = strip;
                this.direction = direction;

                Masking = true;
                CornerRadius = 4;

                InternalChildren = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(10),
                        Colour = Theme.SubtleText,
                    },
                };
            }

            /// <summary>-1 for the button that scrolls back towards the first tab, 1 for the other.</summary>
            public int Direction => direction;

            public IconUsage Icon
            {
                set => icon.Icon = value;
            }

            /// <summary>False at the end the button scrolls towards.</summary>
            public bool IsEnabled
            {
                get => isEnabled;
                set
                {
                    if (isEnabled == value)
                        return;

                    isEnabled = value;
                    icon.FadeTo(value ? 1 : 0.3f, 150, Easing.OutQuint);
                }
            }

            protected override bool OnHover(HoverEvent e)
            {
                highlight.FadeTo(0.08f, 150, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => highlight.FadeOut(150, Easing.OutQuint);

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button != MouseButton.Left || Alpha == 0)
                    return false;

                strip.ScrollBy(direction * Math.Max(40, pageLength()));
                held = true;
                heldSince = Time.Current;
                highlight.FadeTo(0.2f, 60, Easing.OutQuint);
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                if (e.Button == MouseButton.Left)
                {
                    held = false;
                    highlight.FadeTo(IsHovered ? 0.08f : 0, 150, Easing.OutQuint);
                }

                base.OnMouseUp(e);
            }

            // Clicks stop here rather than reaching a tab underneath.
            protected override bool OnClick(ClickEvent e) => true;

            protected override bool OnDragStart(DragStartEvent e) => true;

            protected override void Update()
            {
                base.Update();

                if (held && Time.Current - heldSince > hold_delay)
                    strip.ScrollBy(direction * hold_speed * (float)Time.Elapsed);
            }

            private float pageLength() => (strip.Placement == Dock.Top || strip.Placement == Dock.Bottom ? strip.DrawWidth : strip.DrawHeight) / 2;
        }

        /// <summary>Holds the pages; only the shown one is visible and counts for measuring.</summary>
        private partial class PageArea : Container, ILayoutElement
        {
            public readonly Box Fill;
            public readonly Container Pages;

            private ContentPresenter shown;

            public PageArea()
            {
                Masking = true;
                Children = new Drawable[]
                {
                    Fill = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Mantle, Alpha = 0 },
                    Pages = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(12) },
                };
            }

            public ContentPresenter Shown
            {
                get => shown;
                set => Layout.SetAffectsMeasure(this, ref shown, value);
            }

            public bool ReportsMeasureChanges => true;

            public Vector2 Measure(Vector2 availableSize)
            {
                var padding = new Vector2(Pages.Padding.TotalHorizontal, Pages.Padding.TotalVertical);
                return (Shown?.Measure(Vector2.ComponentMax(Vector2.Zero, availableSize - padding)) ?? Vector2.Zero) + padding;
            }
        }
    }

    /// <summary>
    /// One tab of a <see cref="TabControl"/>. <see cref="Header"/> is shown in the tab strip; <see cref="Content"/> (or the
    /// child element) is the page shown while the tab is selected. Both accept text, a drawable or an object with a template.
    /// </summary>
    public partial class TabItem : CompositeDrawable, ISelectableItem, ILayoutElement
    {
        private readonly Container highlight;
        private readonly HeaderPresenter header;

        private object content;
        private DataTemplate contentTemplate;
        private MarginPadding padding = new MarginPadding { Horizontal = 14, Top = 9, Bottom = 10 };
        private Colour4 foreground = Theme.SubtleText;
        private Colour4 selectedForeground = Theme.Text;
        private bool isSelected;
        private bool selectionRequested;
        private bool hovered;
        private bool isEnabled = true;

        public TabItem()
        {
            InternalChildren = new Drawable[]
            {
                highlight = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(3),
                    Alpha = 0,
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 6,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text },
                    },
                },
                header = new HeaderPresenter(this) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
            };

            applyPadding();
        }

        /// <summary>The tab control this tab is in; set by the control.</summary>
        public TabControl Owner { get; internal set; }

        /// <summary>What the tab shows in the strip.</summary>
        public object Header
        {
            get => header.Content;
            set => header.Content = value;
        }

        public DataTemplate HeaderTemplate
        {
            get => header.ContentTemplate;
            set => header.ContentTemplate = value;
        }

        /// <summary>The page shown while this tab is selected.</summary>
        public object Content
        {
            get => content;
            set
            {
                if (ReferenceEquals(content, value))
                    return;

                content = value;
                Owner?.TabContentChanged(this);
            }
        }

        /// <summary>Builds the page for a non-drawable <see cref="Content"/>; falls back to the control's ContentTemplate.</summary>
        public DataTemplate ContentTemplate
        {
            get => contentTemplate;
            set
            {
                contentTemplate = value;
                Owner?.TabContentChanged(this);
            }
        }

        /// <summary>The page as a drawable; a child element in XAML lands here.</summary>
        public Drawable Child
        {
            get => content as Drawable;
            set => Content = value;
        }

        /// <summary>Space around the header inside the tab.</summary>
        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                if (!Layout.SetAffectsMeasure(this, ref padding, value))
                    return;

                applyPadding();
            }
        }

        /// <summary>The header text colour of a tab that isn't selected.</summary>
        public Colour4 Foreground
        {
            get => foreground;
            set
            {
                foreground = value;
                updateState(false);
            }
        }

        /// <summary>The header text colour of the selected tab.</summary>
        public Colour4 SelectedForeground
        {
            get => selectedForeground;
            set
            {
                selectedForeground = value;
                updateState(false);
            }
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                updateState(true);
            }
        }

        /// <summary>
        /// Whether this is the selected tab. Setting it to true selects the tab; in XAML, <c>IsSelected="True"</c> picks the tab
        /// shown first.
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (Owner != null)
                {
                    if (value)
                        Owner.SelectContainer(this);
                }
                else
                    selectionRequested = value;
            }
        }

        bool ISelectableItem.IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;

                isSelected = value;
                updateState(true);
            }
        }

        /// <summary>Returns and clears a selection asked for before the tab joined a control.</summary>
        internal bool TakeSelectionRequest()
        {
            bool requested = selectionRequested;
            selectionRequested = false;
            return requested;
        }

        public bool ReportsMeasureChanges => true;

        public Vector2 Measure(Vector2 availableSize)
        {
            var pad = new Vector2(padding.TotalHorizontal, padding.TotalVertical);
            return header.Measure(Vector2.ComponentMax(Vector2.Zero, availableSize - pad)) + pad;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateState(false);
        }

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateState(true);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState(true);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return false;

            Owner?.SelectContainer(this, e);
            return true;
        }

        private void applyPadding()
        {
            header.X = padding.Left;
            header.Y = (padding.Top - padding.Bottom) / 2;
        }

        private Colour4 textColour => !isEnabled ? foreground : isSelected ? selectedForeground : hovered ? selectedForeground : foreground;

        private void updateState(bool animate)
        {
            if (!IsLoaded)
                return;

            double duration = animate ? 150 : 0;

            highlight.FadeTo(hovered && isEnabled && !isSelected ? 0.06f : 0, duration, Easing.OutQuint);
            this.FadeTo(isEnabled ? 1 : 0.4f, duration, Easing.OutQuint);

            if (header.OwnText != null && ReferenceEquals(header.PresentedContent, header.OwnText))
                header.OwnText.FadeColour(textColour, duration, Easing.OutQuint);
        }

        /// <summary>Presents the header; text headers follow the tab's selected and hover colours.</summary>
        private partial class HeaderPresenter : ContentPresenter
        {
            private readonly TabItem owner;

            public HeaderPresenter(TabItem owner)
            {
                this.owner = owner;
            }

            public TextBlock OwnText { get; private set; }

            protected override Drawable CreateTextContent(LocalisableString text) => OwnText = new TextBlock { Text = text, Colour = owner.textColour };
        }
    }
}
