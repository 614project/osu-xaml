using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK.Input;

namespace OsuXaml.Controls
{
    public enum SelectionMode
    {
        /// <summary>One item at a time.</summary>
        Single,

        /// <summary>Each click toggles an item.</summary>
        Multiple,

        /// <summary>Click selects one item; Ctrl+click toggles, Shift+click selects a range.</summary>
        Extended,
    }

    /// <summary>
    /// A scrollable list of selectable items, like WPF's ListBox. Use <c>ItemsSource</c> with an <c>ItemTemplate</c>
    /// or <c>DisplayMemberPath</c>, or list <c>&lt;ListBoxItem&gt;</c> children. Up/Down/Home/End move the selection
    /// once the list has focus.
    /// </summary>
    public partial class ListBox : Selector
    {
        private readonly Border chrome;
        private ScrollViewer scroll;
        private int anchor = -1;
        private bool updatingSelectedItems;

        public ListBox()
        {
            chrome = new Border
            {
                Background = Theme.Mantle,
                CornerRadius = 6,
                Padding = new MarginPadding(4),
            };

            SelectedItems.BindCollectionChanged((_, _) =>
            {
                SelectionStateChanged();

                if (!updatingSelectedItems)
                    selectedItemsChanged();
            });
        }

        public SelectionMode SelectionMode { get; set; }

        /// <summary>Every selected item. In Single mode this holds at most <see cref="Selector.SelectedItem"/>.</summary>
        public BindableList<object> SelectedItems { get; } = new BindableList<object>();

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

        public float BorderThickness
        {
            get => chrome.BorderThickness;
            set => chrome.BorderThickness = value;
        }

        public float CornerRadius
        {
            get => chrome.CornerRadius;
            set => chrome.CornerRadius = value;
        }

        public new MarginPadding Padding
        {
            get => chrome.Padding;
            set => chrome.Padding = value;
        }

        public override bool AcceptsFocus => true;

        protected override bool IsUpdatingSelection => updatingSelectedItems;

        /// <summary>Scrolls until the item's container is visible.</summary>
        public void ScrollIntoView(object item)
        {
            var container = ContainerFromItem(item);
            if (container == null || scroll == null)
                return;

            // A container added this frame has no position until the panel arranges it, so scroll once layout has run.
            ScheduleAfterChildren(() =>
            {
                if (container.Parent != null)
                    scroll.ScrollIntoView(container);
            });
        }

        public void SelectAll()
        {
            if (SelectionMode == SelectionMode.Single)
                return;

            setSelectedItems(Items.ToArray());
        }

        public void UnselectAll() => setSelectedItems();

        protected override Drawable CreateItemsHost(Panel panel)
        {
            // A panel filling the viewport would give the scroll content no height, so it grows with its rows instead.
            panel.RelativeSizeAxes = Axes.X;
            panel.Height = 0;

            chrome.Child = scroll = new ScrollViewer { Child = panel };
            return chrome;
        }

        protected override bool IsItemItsOwnContainer(object item) => item is ListBoxItem;

        protected override Drawable CreateContainer() => new ListBoxItem();

        protected override void PrepareContainer(Drawable container, object item)
        {
            if (container is ListBoxItem listBoxItem)
                listBoxItem.Owner = this;

            base.PrepareContainer(container, item);
        }

        protected override bool IsSelected(object item)
            => SelectionMode == SelectionMode.Single ? base.IsSelected(item) : SelectedItems.Contains(item);

        public override void SelectContainer(Drawable container, UIEvent e = null)
        {
            GetContainingFocusManager()?.ChangeFocus(this);

            int index = IndexFromContainer(container);
            if (index < 0)
                return;

            object item = Items[index];

            switch (SelectionMode)
            {
                case SelectionMode.Multiple:
                    toggle(item);
                    break;

                case SelectionMode.Extended when e?.ControlPressed == true:
                    toggle(item);
                    anchor = index;
                    break;

                case SelectionMode.Extended when e?.ShiftPressed == true && anchor >= 0 && anchor < Items.Count:
                {
                    int from = System.Math.Min(anchor, index), to = System.Math.Max(anchor, index);
                    setSelectedItems(Items.Skip(from).Take(to - from + 1).ToArray());
                    SelectedItem.Value = item;
                    break;
                }

                default:
                    anchor = index;
                    base.SelectContainer(container, e);
                    break;
            }
        }

        protected override void OnSelectionChanged()
        {
            base.OnSelectionChanged();

            if (updatingSelectedItems)
                return;

            // SelectedItem changed on its own (code, binding, single click): it becomes the only selected item.
            object item = SelectedItem.Value;

            if (item == null ? SelectedItems.Count > 0 : !SelectedItems.Contains(item) || (SelectionMode == SelectionMode.Single && SelectedItems.Count != 1))
            {
                updatingSelectedItems = true;
                SelectedItems.Clear();
                if (item != null)
                    SelectedItems.Add(item);
                updatingSelectedItems = false;
                SelectionHasChanged();
            }
        }

        protected override void OnItemsChanged()
        {
            var gone = SelectedItems.Where(i => IndexOf(i) < 0).ToArray();

            if (gone.Length > 0)
            {
                updatingSelectedItems = true;
                foreach (object item in gone)
                    SelectedItems.Remove(item);
                updatingSelectedItems = false;
            }

            base.OnItemsChanged();
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!HasFocus || Items.Count == 0)
                return base.OnKeyDown(e);

            int index = SelectedIndex.Value;

            switch (e.Key)
            {
                case Key.Up:
                    index = index <= 0 ? 0 : index - 1;
                    break;

                case Key.Down:
                    index = index < 0 ? 0 : System.Math.Min(Items.Count - 1, index + 1);
                    break;

                case Key.Home:
                    index = 0;
                    break;

                case Key.End:
                    index = Items.Count - 1;
                    break;

                case Key.A when e.ControlPressed && SelectionMode != SelectionMode.Single:
                    SelectAll();
                    return true;

                default:
                    return base.OnKeyDown(e);
            }

            // SelectedIndex may still be past the end (set before the items it names exist).
            index = System.Math.Clamp(index, 0, Items.Count - 1);

            anchor = index;
            SelectedIndex.Value = index;
            ScrollIntoView(Items[index]);
            return true;
        }

        /// <summary>SelectedItems was changed from outside: SelectedItem follows it (the first selected item, as in WPF).</summary>
        private void selectedItemsChanged()
        {
            object current = SelectedItem.Value;
            object item = SelectionMode == SelectionMode.Single ? SelectedItems.LastOrDefault()
                : SelectedItems.Contains(current) ? current : SelectedItems.FirstOrDefault();

            updatingSelectedItems = true;
            SelectedItem.Value = item;
            updatingSelectedItems = false;

            SelectionHasChanged();
        }

        private void toggle(object item)
        {
            var selected = SelectedItems.ToList();

            if (!selected.Remove(item))
                selected.Add(item);

            setSelectedItems(selected.ToArray());
        }

        private void setSelectedItems(params object[] selected)
        {
            updatingSelectedItems = true;
            SelectedItems.Clear();
            SelectedItems.AddRange(selected);

            object current = SelectedItem.Value;
            SelectedItem.Value = selected.Contains(current) ? current : selected.LastOrDefault();
            updatingSelectedItems = false;

            SelectionHasChanged();
        }
    }

    /// <summary>One row of a <see cref="ListBox"/>, with hover and selection highlight.</summary>
    public partial class ListBoxItem : ContentControl, ISelectableItem
    {
        private readonly Box highlight;
        private bool isSelected;
        private bool hovered;

        public ListBoxItem()
        {
            CornerRadius = 4;
            Padding = new MarginPadding { Horizontal = 10, Vertical = 6 };
            Chrome.Add(highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Accent, Alpha = 0, Depth = 1 });
        }

        /// <summary>The list this item belongs to; set by the list.</summary>
        public Selector Owner { get; set; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;

                isSelected = value;
                updateState();
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateState();
        }

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateState();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState();
        }

        protected override bool OnClick(ClickEvent e)
        {
            Owner?.SelectContainer(this, e);
            return true;
        }

        private void updateState()
        {
            if (!IsLoaded)
                return;

            highlight.FadeColour(isSelected ? Theme.Accent : Theme.Text, 100);
            highlight.FadeTo(isSelected ? 0.28f : hovered ? 0.08f : 0, 100, Easing.OutQuint);
        }
    }
}
