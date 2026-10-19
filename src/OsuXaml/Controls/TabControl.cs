using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
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

        /// <summary>The tab headers, a line between them and the page, and the animated marker under the selected tab.</summary>
        private partial class TabStrip : CompositeDrawable, ILayoutElement
        {
            public readonly Box Divider;
            public readonly Circle Indicator;

            private const float indicator_thickness = 3;

            private Panel panel;
            private Dock placement;
            private TabItem selected;
            private bool animateNext;
            private RectangleF? lastTarget;

            public TabStrip()
            {
                InternalChildren = new Drawable[]
                {
                    Divider = new Box { Colour = Theme.Border },
                    Indicator = new Circle { Colour = Theme.Accent, Alpha = 0, Depth = -1 },
                };
            }

            public Dock Placement
            {
                get => placement;
                set
                {
                    placement = value;

                    bool horizontal = value == Dock.Top || value == Dock.Bottom;

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

                    lastTarget = null;
                }
            }

            public void SetPanel(Panel itemsPanel)
            {
                panel = itemsPanel;
                AddInternal(panel);
                Layout.InvalidateMeasure(this);
            }

            public void Select(TabItem tab)
            {
                if (tab == selected)
                    return;

                selected = tab;
                animateNext = true;
            }

            public bool ReportsMeasureChanges => true;

            public Vector2 Measure(Vector2 availableSize) => panel?.Measure(availableSize) ?? Vector2.Zero;

            protected override void UpdateAfterChildren()
            {
                base.UpdateAfterChildren();

                if (selected == null || selected.Parent == null || selected.DrawWidth <= 0 || selected.DrawHeight <= 0)
                {
                    Indicator.FadeOut(100);
                    lastTarget = null;
                    return;
                }

                var topLeft = selected.ToSpaceOfOtherDrawable(Vector2.Zero, this);
                var size = selected.DrawSize;
                const float inset = 10;

                var target = placement switch
                {
                    Dock.Top => new RectangleF(topLeft.X + inset, DrawHeight - indicator_thickness, Math.Max(0, size.X - inset * 2), indicator_thickness),
                    Dock.Bottom => new RectangleF(topLeft.X + inset, 0, Math.Max(0, size.X - inset * 2), indicator_thickness),
                    Dock.Left => new RectangleF(DrawWidth - indicator_thickness, topLeft.Y + 6, indicator_thickness, Math.Max(0, size.Y - 12)),
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
