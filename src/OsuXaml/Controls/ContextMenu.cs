using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osuTK;

[assembly: OsuXaml.XamlGlobalProperties(typeof(OsuXaml.Controls.ContextMenuService))]

namespace OsuXaml.Controls
{
    /// <summary>
    /// A menu that pops up at the mouse when its element is right-clicked, like WPF's ContextMenu. Attach it to any element:
    /// <c>&lt;ContextMenuService.ContextMenu&gt;&lt;ContextMenu&gt;&lt;MenuItem Header="Delete" Click="{Bind Delete}" /&gt;&lt;/ContextMenu&gt;&lt;/ContextMenuService.ContextMenu&gt;</c>.
    /// Items work as in a <see cref="Menu"/>, including nested submenus. Picking an item, clicking outside or Escape closes it.
    /// It can also be opened from code by setting <see cref="PlacementTarget"/> and <see cref="IsOpen"/>.
    /// </summary>
    public partial class ContextMenu : MenuBase
    {
        private readonly Popup popup;
        private readonly PopupMenuBorder chrome;

        // The layer that took this menu into the tree when it opened, if it wasn't declared in the tree itself.
        private PopupLayer hostLayer;

        public ContextMenu()
        {
            popup = new Popup
            {
                Placement = PlacementMode.Mouse,
                Child = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Padding = new MarginPadding(2),
                    Child = chrome = new PopupMenuBorder(),
                },
            };

            popup.Opened += () => Opened?.Invoke();
            popup.Closed += () => Closed?.Invoke();
            IsOpen.ValueChanged += openChanged;
        }

        /// <summary>Whether the menu is showing. Binds two-way.</summary>
        public Bindable<bool> IsOpen => popup.IsOpen;

        /// <summary>The element the menu opens for. Set to the right-clicked element when opened by right-click.</summary>
        public Drawable PlacementTarget
        {
            get => popup.PlacementTarget;
            set => popup.PlacementTarget = value;
        }

        /// <summary>Where the menu opens: at the mouse (the default) or next to <see cref="PlacementTarget"/>.</summary>
        public PlacementMode Placement
        {
            get => popup.Placement;
            set => popup.Placement = value;
        }

        public float HorizontalOffset
        {
            get => popup.HorizontalOffset;
            set => popup.HorizontalOffset = value;
        }

        public float VerticalOffset
        {
            get => popup.VerticalOffset;
            set => popup.VerticalOffset = value;
        }

        /// <summary>When true, clicking outside or pressing Escape doesn't close the menu.</summary>
        public bool StaysOpen
        {
            get => popup.StaysOpen;
            set => popup.StaysOpen = value;
        }

        public Colour4 Background
        {
            get => chrome.Background;
            set => chrome.Background = value;
        }

        public Colour4 BorderBrush
        {
            get => chrome.BorderBrush;
            set => chrome.BorderBrush = value;
        }

        public event Action Opened;

        public event Action Closed;

        internal override MenuBase ParentLevel => null;

        /// <summary>Opens the menu for <paramref name="target"/>, as right-clicking it does.</summary>
        internal void OpenFor(PopupLayer layer, Drawable target)
        {
            PlacementTarget = target;

            if (Parent == null)
            {
                hostLayer = layer;
                layer.HostContextMenu(this);
            }

            IsOpen.Value = true;
        }

        protected override Drawable CreateItemsHost(Panel panel)
        {
            chrome.Child = panel;
            return popup;
        }

        protected override void CloseCore() => IsOpen.Value = false;

        /// <summary>The menu itself takes no space; its items show in the popup.</summary>
        public override Vector2 Measure(Vector2 availableSize) => Vector2.Zero;

        protected override bool OnKeyDown(KeyDownEvent e) => (IsOpen.Value && HandleMenuKey(e)) || base.OnKeyDown(e);

        private void openChanged(ValueChangedEvent<bool> e)
        {
            if (e.NewValue)
            {
                // Opened from code while not in the drawable tree: join the target's popup layer first,
                // so the popup (which applies this change after this handler) finds its layer.
                if (Parent == null && PlacementTarget != null)
                {
                    hostLayer = PopupLayer.Find(PlacementTarget);
                    hostLayer?.HostContextMenu(this);
                }

                return;
            }

            SetOpenItem(null);
            SetHighlighted(null);
            hostLayer?.ReleaseContextMenu(this);
        }
    }

    /// <summary>
    /// The <c>ContextMenu</c> property every XAML element accepts, like WPF's ContextMenuService:
    /// <c>&lt;ContextMenuService.ContextMenu&gt;&lt;ContextMenu&gt;...&lt;/ContextMenu&gt;&lt;/ContextMenuService.ContextMenu&gt;</c>
    /// or <c>ContextMenu="{StaticResource RowMenu}"</c>. Right-clicking the element opens the menu at the mouse;
    /// when nested elements have menus, the deepest one under the mouse wins.
    /// </summary>
    public static class ContextMenuService
    {
        private static readonly ConditionalWeakTable<Drawable, ContextMenu> menus = new ConditionalWeakTable<Drawable, ContextMenu>();

        public static void SetContextMenu(Drawable element, ContextMenu menu)
        {
            bool known = menus.TryGetValue(element, out _);

            if (menu == null)
            {
                menus.Remove(element);
                return;
            }

            menus.AddOrUpdate(element, menu);

            if (!known)
                PopupLayer.EnsureFor(element);
        }

        public static ContextMenu GetContextMenu(Drawable element) => menus.TryGetValue(element, out var menu) ? menu : null;

        /// <summary>Elements with a context menu, for the popup layer's hit test.</summary>
        internal static IEnumerable<Drawable> Targets
        {
            get
            {
                foreach (var pair in (IEnumerable<KeyValuePair<Drawable, ContextMenu>>)menus)
                    yield return pair.Key;
            }
        }
    }
}
