using System;
using System.Linq;
using System.Text;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>What a <see cref="MenuItem"/> is, from where it sits and whether it has children (WPF's MenuItemRole).</summary>
    public enum MenuItemRole
    {
        /// <summary>An item in a menu bar that opens a submenu.</summary>
        TopLevelHeader,

        /// <summary>An item in a menu bar that is clicked directly.</summary>
        TopLevelItem,

        /// <summary>An item in a submenu or context menu that opens a further submenu.</summary>
        SubmenuHeader,

        /// <summary>An item in a submenu or context menu that is clicked directly.</summary>
        SubmenuItem,
    }

    /// <summary>
    /// What <see cref="Menu"/>, <see cref="ContextMenu"/> and <see cref="MenuItem"/> share: their children become
    /// <see cref="MenuItem"/>s (data items through <c>ItemTemplate</c>, nested through a HierarchicalDataTemplate),
    /// one child at a time has its submenu open, and the keyboard moves through the open levels.
    /// </summary>
    public abstract partial class MenuBase : ItemsControl
    {
        /// <summary>How long the mouse rests on a submenu item before its submenu opens, in milliseconds.</summary>
        public static double ShowDelay = 250;

        private ScheduledDelegate pendingOpen;

        /// <summary>The child whose submenu is open, if any.</summary>
        public MenuItem OpenItem { get; private set; }

        /// <summary>The child the mouse or keyboard points at, if any.</summary>
        public MenuItem HighlightedItem { get; private set; }

        /// <summary>
        /// Raised by the outermost menu when an item without a submenu is clicked anywhere in it. The argument is the
        /// data item for items from an ItemsSource, otherwise the <see cref="MenuItem"/> itself.
        /// </summary>
        public event Action<object> ItemClick;

        /// <summary>The menu level this one opened from; null for a Menu or ContextMenu.</summary>
        internal abstract MenuBase ParentLevel { get; }

        /// <summary>Whether children are submenu rows (a dropped-down list) rather than menu bar headers.</summary>
        internal virtual bool HostsSubmenuRows => true;

        /// <summary>The outermost menu this level belongs to.</summary>
        public MenuBase RootMenu
        {
            get
            {
                var menu = this;
                while (menu.ParentLevel != null)
                    menu = menu.ParentLevel;
                return menu;
            }
        }

        /// <summary>Closes the whole menu this level belongs to, as clicking an item does.</summary>
        public void CloseMenu() => RootMenu.CloseCore();

        /// <summary>Closes this menu and everything open below it.</summary>
        protected abstract void CloseCore();

        protected override bool IsItemItsOwnContainer(object item) => item is Drawable;

        protected override Drawable CreateContainer() => new MenuItem();

        protected override void PrepareContainer(Drawable container, object item)
        {
            switch (container)
            {
                case MenuItem menuItem:
                    menuItem.ParentMenu = this;

                    if (!ReferenceEquals(menuItem, item))
                        menuItem.PrepareFor(item, ItemTemplate);

                    break;

                // Separators between menu items are inset from the edges.
                case Separator separator when separator.Parent == null:
                    separator.Margin = new MarginPadding { Vertical = 4, Horizontal = 6 };
                    break;
            }
        }

        protected override void ClearContainer(Drawable container, object item)
        {
            if (!(container is MenuItem menuItem))
                return;

            if (OpenItem == menuItem)
                SetOpenItem(null);
            if (HighlightedItem == menuItem)
                SetHighlighted(null);

            menuItem.ClearGenerated();
        }

        /// <summary>Opens <paramref name="item"/>'s submenu (closing any other), or closes the open one for null.</summary>
        internal void SetOpenItem(MenuItem item, bool highlightFirst = false)
        {
            pendingOpen?.Cancel();

            if (OpenItem != item)
            {
                var previous = OpenItem;
                OpenItem = item;

                if (previous != null)
                    previous.IsSubmenuOpen.Value = false;

                if (item != null)
                {
                    SetHighlighted(item);
                    item.IsSubmenuOpen.Value = true;
                }
            }

            if (item != null && highlightFirst)
                item.MoveHighlight(1, fromStart: true);
        }

        internal void SetHighlighted(MenuItem item)
        {
            if (HighlightedItem == item)
                return;

            if (HighlightedItem != null)
                HighlightedItem.IsHighlighted = false;

            HighlightedItem = item;

            if (item != null)
                item.IsHighlighted = true;
        }

        /// <summary>A child's submenu opened, from code or from this level.</summary>
        internal void ChildOpened(MenuItem item)
        {
            if (OpenItem == item)
                return;

            var previous = OpenItem;
            OpenItem = item;
            SetHighlighted(item);

            if (previous != null)
                previous.IsSubmenuOpen.Value = false;
        }

        internal void ChildClosed(MenuItem item)
        {
            if (OpenItem == item)
                OpenItem = null;
        }

        internal void ItemHovered(MenuItem item)
        {
            pendingOpen?.Cancel();

            // Reaching a submenu keeps it open even if the mouse crossed a sibling of its header on the way.
            if (this is MenuItem header && header.ParentMenu != null)
                header.ParentMenu.submenuEntered(header);

            if (!item.IsEnabled)
            {
                SetHighlighted(null);
                return;
            }

            SetHighlighted(item);

            if (!HostsSubmenuRows)
            {
                // In a menu bar, hovering another header switches menus while one is open.
                if (OpenItem != null && OpenItem != item && item.HasItems)
                    SetOpenItem(item);
                return;
            }

            if (OpenItem == item)
                return;

            pendingOpen = Scheduler.AddDelayed(() =>
            {
                if (HighlightedItem == item)
                    SetOpenItem(item.HasItems ? item : null);
            }, ShowDelay);
        }

        internal void ItemUnhovered(MenuItem item)
        {
            if (HighlightedItem == item && OpenItem != item)
                SetHighlighted(OpenItem);
        }

        private void submenuEntered(MenuItem header)
        {
            pendingOpen?.Cancel();
            SetHighlighted(header);
        }

        internal void RaiseItemClick(object item) => ItemClick?.Invoke(item);

        /// <summary>Moves the highlight among enabled items, wrapping around like desktop menus.</summary>
        internal void MoveHighlight(int direction, bool fromStart = false)
        {
            var candidates = Containers.OfType<MenuItem>().Where(m => m.IsEnabled).ToList();
            if (candidates.Count == 0)
                return;

            int index = fromStart ? -1 : candidates.IndexOf(HighlightedItem);
            int next = index < 0 ? (direction > 0 ? 0 : candidates.Count - 1) : (index + direction + candidates.Count) % candidates.Count;
            SetHighlighted(candidates[next]);
        }

        /// <summary>
        /// Keyboard navigation for an open menu: Up/Down move, Right/Enter open submenus, Left closes one,
        /// Enter/Space click, and a letter clicks the item with that access key. Escape is left to the popup layer.
        /// </summary>
        protected bool HandleMenuKey(KeyDownEvent e)
        {
            MenuBase level = this;
            while (level.OpenItem != null && level.OpenItem.IsSubmenuOpen.Value)
                level = level.OpenItem;

            if (!level.HostsSubmenuRows)
                return false;

            var highlighted = level.HighlightedItem;

            switch (e.Key)
            {
                case Key.Down:
                    level.MoveHighlight(1);
                    return true;

                case Key.Up:
                    level.MoveHighlight(-1);
                    return true;

                case Key.Right:
                    if (highlighted != null && highlighted.HasItems && highlighted.IsEnabled)
                        level.SetOpenItem(highlighted, highlightFirst: true);
                    else if (RootMenu is Menu menu)
                        menu.MoveTopLevel(1);
                    return true;

                case Key.Left:
                    if (level is MenuItem item && item.ParentMenu != null && item.ParentMenu.HostsSubmenuRows)
                        item.IsSubmenuOpen.Value = false;
                    else if (RootMenu is Menu bar)
                        bar.MoveTopLevel(-1);
                    return true;

                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space:
                    highlighted?.ActivateFromKeyboard();
                    return true;
            }

            char letter = MenuItem.LetterOf(e.Key);

            if (letter != '\0' && !e.ControlPressed && !e.AltPressed)
            {
                var match = level.Containers.OfType<MenuItem>().FirstOrDefault(m => m.IsEnabled && m.AccessKey == letter);

                if (match != null)
                {
                    level.SetHighlighted(match);
                    match.ActivateFromKeyboard();
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A horizontal menu bar, like WPF's Menu: <c>&lt;Menu&gt;&lt;MenuItem Header="_File"&gt;&lt;MenuItem Header="_Open" Click="{Bind Open}" /&gt;...</c>.
    /// Pressing a header opens its submenu, and while one is open hovering another header switches to it.
    /// Alt plus a header's access key (the letter after <c>_</c>) opens it from the keyboard.
    /// </summary>
    public partial class Menu : MenuBase
    {
        private readonly Border bar;

        public Menu()
        {
            bar = new Border
            {
                Background = Theme.Mantle,
                Padding = new MarginPadding { Horizontal = 4, Vertical = 3 },
            };
        }

        public Colour4 Background
        {
            get => bar.Background;
            set => bar.Background = value;
        }

        public Colour4 BorderBrush
        {
            get => bar.BorderBrush;
            set => bar.BorderBrush = value;
        }

        public new float BorderThickness
        {
            get => bar.BorderThickness;
            set => bar.BorderThickness = value;
        }

        public new MarginPadding Padding
        {
            get => bar.Padding;
            set => bar.Padding = value;
        }

        internal override MenuBase ParentLevel => null;

        internal override bool HostsSubmenuRows => false;

        protected override Panel CreateDefaultItemsPanel() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        protected override Drawable CreateItemsHost(Panel panel)
        {
            bar.Child = panel;
            return bar;
        }

        protected override void CloseCore()
        {
            SetOpenItem(null);
            SetHighlighted(null);
        }

        /// <summary>Opens the next or previous header's submenu, for Left/Right in an open menu.</summary>
        internal void MoveTopLevel(int direction)
        {
            var headers = Containers.OfType<MenuItem>().Where(m => m.IsEnabled && m.HasItems).ToList();
            if (headers.Count == 0)
                return;

            int index = headers.IndexOf(OpenItem);
            int next = index < 0 ? 0 : (index + direction + headers.Count) % headers.Count;
            SetOpenItem(headers[next], highlightFirst: true);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (OpenItem != null && OpenItem.IsSubmenuOpen.Value)
                return HandleMenuKey(e) || base.OnKeyDown(e);

            char letter = MenuItem.LetterOf(e.Key);

            if (e.AltPressed && letter != '\0')
            {
                var item = Containers.OfType<MenuItem>().FirstOrDefault(m => m.IsEnabled && m.AccessKey == letter);

                if (item != null)
                {
                    SetHighlighted(item);
                    item.ActivateFromKeyboard();
                    return true;
                }
            }

            return base.OnKeyDown(e);
        }
    }

    /// <summary>
    /// One entry of a <see cref="Menu"/>, <see cref="ContextMenu"/> or submenu, like WPF's MenuItem. <c>Header</c> is text
    /// (a leading <c>_</c> marks the access key and isn't shown), an element, or data shown through <c>HeaderTemplate</c>.
    /// Child MenuItems and Separators form its submenu, which opens below a menu bar header and to the right otherwise.
    /// Clicking an item without children raises <see cref="Click"/>, runs <see cref="Command"/>, toggles
    /// <see cref="IsChecked"/> when <see cref="IsCheckable"/>, and closes the whole menu.
    /// </summary>
    public partial class MenuItem : MenuBase
    {
        private const float icon_column = 34;
        private const float arrow_column = 28;

        private readonly Container row;
        private readonly Box highlight;
        private readonly Container iconHost;
        private readonly ContentPresenter iconPresenter;
        private readonly SpriteIcon checkMark;
        private readonly ContentPresenter headerPresenter;
        private readonly TextBlock gestureText;
        private readonly SpriteIcon arrow;
        private readonly Popup popup;
        private readonly PopupMenuBorder submenu;

        private object header;
        private DataTemplate headerTemplate;
        private object icon;
        private bool isEnabled = true;
        private bool isHighlighted;
        private MenuBase parentMenu;

        // Bindings for children of a generated item (HierarchicalDataTemplate); a field, so disposal unbinds them.
        private BindingScope generatedBindings;
        private object dataItem;
        private bool isGenerated;

        public MenuItem()
        {
            AddInternal(row = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4,
                Children = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                    iconHost = new Container
                    {
                        Size = new Vector2(16),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 10,
                        Children = new Drawable[]
                        {
                            iconPresenter = new ContentPresenter { Anchor = Anchor.Centre, Origin = Anchor.Centre },
                            checkMark = new SpriteIcon
                            {
                                Icon = FontAwesome.Solid.Check,
                                Size = new Vector2(12),
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Colour = Theme.Accent,
                                Alpha = 0,
                            },
                        },
                    },
                    headerPresenter = new ContentPresenter { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, X = icon_column },
                    gestureText = new TextBlock
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -arrow_column,
                        FontSize = 14,
                        Colour = Theme.SubtleText,
                        Alpha = 0,
                    },
                    arrow = new SpriteIcon
                    {
                        Icon = FontAwesome.Solid.ChevronRight,
                        Size = new Vector2(10),
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.Centre,
                        X = -arrow_column / 2,
                        Colour = Theme.SubtleText,
                        Alpha = 0,
                    },
                },
            });

            popup = new Popup
            {
                PlacementTarget = this,
                Child = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Padding = new MarginPadding(2),
                    Child = submenu = new PopupMenuBorder(),
                },
            };

            IsSubmenuOpen.ValueChanged += submenuOpenChanged;
            IsChecked.ValueChanged += checkedChanged;
        }

        /// <summary>
        /// What the item shows: text (<c>"_Open"</c>; the underscore marks the access key), an element, or data shown
        /// through <see cref="HeaderTemplate"/>.
        /// </summary>
        public object Header
        {
            get => header;
            set
            {
                header = value;
                presentHeader();
            }
        }

        /// <summary>Builds the drawable for a non-drawable <see cref="Header"/>.</summary>
        public DataTemplate HeaderTemplate
        {
            get => headerTemplate;
            set
            {
                headerTemplate = value;
                presentHeader();
            }
        }

        /// <summary>The shortcut shown at the right edge, e.g. <c>Ctrl+O</c>. Display only, as in WPF.</summary>
        public string InputGestureText
        {
            get => gestureText.Text.ToString();
            set
            {
                gestureText.Text = value ?? string.Empty;
                updateLayout();
            }
        }

        /// <summary>An element (or text) shown in the icon column of submenu items.</summary>
        public object Icon
        {
            get => icon;
            set
            {
                icon = value;
                iconPresenter.Content = value;
                updateLayout();
            }
        }

        /// <summary>Clicking toggles <see cref="IsChecked"/>.</summary>
        public bool IsCheckable { get; set; }

        /// <summary>Shows a check mark. Binds two-way.</summary>
        public Bindable<bool> IsChecked { get; } = new Bindable<bool>();

        /// <summary>Clicking doesn't close the menu.</summary>
        public bool StaysOpenOnClick { get; set; }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                row.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);

                if (!value)
                {
                    IsSubmenuOpen.Value = false;
                    IsHighlighted = false;
                }
            }
        }

        /// <summary>Whether this item's submenu is showing. Binds two-way.</summary>
        public Bindable<bool> IsSubmenuOpen => popup.IsOpen;

        /// <summary>Whether the mouse or keyboard points at this item.</summary>
        public bool IsHighlighted
        {
            get => isHighlighted;
            internal set
            {
                if (isHighlighted == value)
                    return;

                isHighlighted = value;
                updateState();
            }
        }

        public bool HasItems => Items.Count > 0;

        /// <summary>The letter after the underscore in a text <see cref="Header"/>, upper case, or '\0'.</summary>
        public char AccessKey { get; private set; }

        public MenuItemRole Role
        {
            get
            {
                bool topLevel = parentMenu != null && !parentMenu.HostsSubmenuRows;
                return topLevel
                    ? HasItems ? MenuItemRole.TopLevelHeader : MenuItemRole.TopLevelItem
                    : HasItems ? MenuItemRole.SubmenuHeader : MenuItemRole.SubmenuItem;
            }
        }

        /// <summary>The menu, context menu or item this item is a child of; set by that parent.</summary>
        public MenuBase ParentMenu
        {
            get => parentMenu;
            internal set
            {
                parentMenu = value;
                updateLayout();
            }
        }

        /// <summary>Raised when the item is clicked (not for items that open a submenu).</summary>
        public event Action Click;

        /// <summary>Runs on click, like WPF's Command: <c>Command="{Bind Save}"</c>.</summary>
        public Action Command { get; set; }

        /// <summary>The same as <see cref="Command"/>, named like Button.Action.</summary>
        public Action Action { get; set; }

        public event Action Checked;

        public event Action Unchecked;

        public event Action SubmenuOpened;

        public event Action SubmenuClosed;

        internal override MenuBase ParentLevel => parentMenu;

        private bool isTopLevel => parentMenu != null && !parentMenu.HostsSubmenuRows;

        /// <summary>Clicks the item as the mouse would: opens its submenu, or runs it and closes the menu.</summary>
        public void PerformClick()
        {
            if (!isEnabled)
                return;

            if (HasItems)
            {
                if (parentMenu != null)
                    parentMenu.SetOpenItem(this);
                else
                    IsSubmenuOpen.Value = true;

                return;
            }

            activate();
        }

        internal void ActivateFromKeyboard()
        {
            if (!isEnabled)
                return;

            if (HasItems)
            {
                if (parentMenu != null)
                    parentMenu.SetOpenItem(this, highlightFirst: true);
                else
                    IsSubmenuOpen.Value = true;
            }
            else
                activate();
        }

        /// <summary>Points a generated container at its data item.</summary>
        internal void PrepareFor(object item, DataTemplate template)
        {
            generatedBindings?.UnbindAll();
            generatedBindings = null;

            isGenerated = true;
            dataItem = item;
            headerTemplate = template;
            Header = item;

            if (template is IHierarchicalTemplate hierarchical)
            {
                generatedBindings = new BindingScope();
                ItemTemplate = template;
                ItemsSource = hierarchical.GetChildren(item, generatedBindings);
            }
        }

        internal void ClearGenerated()
        {
            generatedBindings?.UnbindAll();
            generatedBindings = null;
        }

        /// <summary>The letter an access key compares against for <paramref name="key"/>, or '\0'.</summary>
        internal static char LetterOf(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
                return (char)('A' + (key - Key.A));
            if (key >= Key.Number0 && key <= Key.Number9)
                return (char)('0' + (key - Key.Number0));
            return '\0';
        }

        /// <summary>Removes WPF's access key marker: the first single <c>_</c> marks the next letter, <c>__</c> is a literal underscore.</summary>
        internal static string StripAccessKey(string text, out char accessKey)
        {
            accessKey = '\0';
            var result = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '_' && i + 1 < text.Length)
                {
                    if (text[i + 1] == '_')
                    {
                        result.Append('_');
                        i++;
                        continue;
                    }

                    if (accessKey == '\0')
                    {
                        accessKey = char.ToUpperInvariant(text[i + 1]);
                        continue;
                    }
                }

                result.Append(text[i]);
            }

            return result.ToString();
        }

        protected override Drawable CreateItemsHost(Panel panel)
        {
            submenu.Child = panel;
            return popup;
        }

        protected override void CloseCore() => IsSubmenuOpen.Value = false;

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();
            updateLayout();
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var headerSize = headerPresenter.DrawSize;
            float height = Math.Max(30, headerSize.Y + 10);

            if (isTopLevel)
                return new Vector2(headerSize.X + 20, height);

            float width = icon_column + headerSize.X + arrow_column;
            if (gestureText.Alpha > 0)
                width += 32 + gestureText.DrawWidth;

            return new Vector2(Math.Max(140, width), height);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateLayout();
            updateState();
        }

        protected override bool OnHover(HoverEvent e)
        {
            parentMenu?.ItemHovered(this);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            parentMenu?.ItemUnhovered(this);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            // Menu bar headers open on press like desktop menus. While open, the popup layer takes the press and closes them.
            if (isEnabled && isTopLevel && HasItems)
                parentMenu.SetOpenItem(IsSubmenuOpen.Value ? null : this);

            return true;
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return true;

            if (HasItems)
            {
                if (!isTopLevel && parentMenu != null)
                    parentMenu.SetOpenItem(this);
                return true;
            }

            activate();
            return true;
        }

        private void activate()
        {
            var root = RootMenu;

            if (!StaysOpenOnClick)
                root.CloseMenu();

            if (IsCheckable)
                IsChecked.Value = !IsChecked.Value;

            Click?.Invoke();
            Command?.Invoke();
            Action?.Invoke();
            root.RaiseItemClick(isGenerated ? dataItem : this);
        }

        private void presentHeader()
        {
            object shown = header;
            char key = '\0';

            if (header is string text)
                shown = StripAccessKey(text, out key);

            AccessKey = key;
            headerPresenter.Content = null;
            headerPresenter.ContentTemplate = headerTemplate;
            headerPresenter.Content = shown;
        }

        private void submenuOpenChanged(ValueChangedEvent<bool> e)
        {
            if (e.NewValue)
            {
                parentMenu?.ChildOpened(this);
                SubmenuOpened?.Invoke();
            }
            else
            {
                // Closing a submenu closes everything opened from it.
                SetOpenItem(null);
                SetHighlighted(null);
                parentMenu?.ChildClosed(this);
                SubmenuClosed?.Invoke();
            }

            updateState();
        }

        private void checkedChanged(ValueChangedEvent<bool> e)
        {
            updateLayout();

            if (e.NewValue)
                Checked?.Invoke();
            else
                Unchecked?.Invoke();
        }

        private void updateLayout()
        {
            bool topLevel = isTopLevel;

            iconHost.Alpha = topLevel ? 0 : 1;
            checkMark.Alpha = IsChecked.Value ? 1 : 0;
            iconPresenter.Alpha = IsChecked.Value ? 0 : 1;
            headerPresenter.X = topLevel ? 10 : icon_column;
            gestureText.Alpha = !topLevel && !string.IsNullOrEmpty(gestureText.Text.ToString()) ? 1 : 0;
            arrow.Alpha = !topLevel && HasItems ? 1 : 0;

            popup.Placement = topLevel ? PlacementMode.Bottom : PlacementMode.Right;

            // Line a nested submenu's first item up with this item (popup padding, border and inner padding).
            popup.VerticalOffset = topLevel ? 0 : -7;
            popup.HorizontalOffset = topLevel ? -2 : 0;
        }

        private void updateState()
        {
            if (!IsLoaded)
                return;

            float alpha = !isEnabled ? 0 : IsSubmenuOpen.Value ? 0.12f : isHighlighted ? 0.08f : 0;
            highlight.FadeTo(alpha, 100, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            generatedBindings?.UnbindAll();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>The panel drop-downs and menus open in: dark background, border, rounded corners and a shadow.</summary>
    internal partial class PopupMenuBorder : Border
    {
        /// <summary>The tallest the panel grows; content beyond it should scroll.</summary>
        public float MaxHeight = float.PositiveInfinity;

        public PopupMenuBorder()
        {
            Background = Theme.Mantle;
            BorderBrush = Theme.Border;
            BorderThickness = 1;
            CornerRadius = 8;
            Padding = new MarginPadding(4);

            Chrome.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Colour4.Black.Opacity(0.35f),
                Radius = 10,
                Offset = new Vector2(0, 3),
            };
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var desired = base.Measure(availableSize);
            desired.Y = Math.Min(desired.Y, MaxHeight);
            return desired;
        }
    }
}
