using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Shows a list of items, like WPF's ItemsControl. Items come from <see cref="ItemsSource"/> (any IEnumerable;
    /// observable ones such as BindableList stay in sync) or from child elements in XAML. Each item gets a container
    /// (<see cref="CreateContainer"/>) that presents it through <see cref="ItemTemplate"/>, and the containers are laid
    /// out by <see cref="ItemsPanel"/> (a vertical StackPanel by default).
    /// </summary>
    public partial class ItemsControl : CompositeDrawable, ILayoutElement
    {
        private readonly List<object> localItems = new List<object>();
        private readonly List<object> items = new List<object>();
        private readonly List<Drawable> containers = new List<Drawable>();

        private IEnumerable itemsSource;
        private INotifyCollectionChanged observed;
        private DataTemplate itemTemplate;
        private Panel itemsPanel;
        private Drawable host;
        private bool built;

        private bool sizeToContentX;
        private bool sizeToContentY;

        /// <summary>The items to show. Observable collections (BindableList, ObservableCollection) update the control.</summary>
        public IEnumerable ItemsSource
        {
            get => itemsSource;
            set
            {
                if (ReferenceEquals(itemsSource, value))
                    return;

                if (observed != null)
                    observed.CollectionChanged -= onCollectionChanged;

                itemsSource = value;
                observed = value as INotifyCollectionChanged;

                if (observed != null)
                    observed.CollectionChanged += onCollectionChanged;

                if (built)
                    rebuild();
            }
        }

        /// <summary>The items currently shown, in order.</summary>
        public IReadOnlyList<object> Items => items;

        /// <summary>Builds the drawable for each data item. Drawable items are shown as they are.</summary>
        public DataTemplate ItemTemplate
        {
            get => itemTemplate;
            set
            {
                itemTemplate = value;

                if (!built)
                    return;

                for (int i = 0; i < containers.Count; i++)
                    PrepareContainer(containers[i], items[i]);
            }
        }

        /// <summary>
        /// Shows each item as the text of one of its members. XAML compiles <c>DisplayMemberPath="Name"</c>
        /// into a text template that binds <c>Name</c>, so this is the same as setting <see cref="ItemTemplate"/>.
        /// </summary>
        public DataTemplate DisplayMemberPath
        {
            get => itemTemplate;
            set => ItemTemplate = value;
        }

        /// <summary>The panel that lays out the item containers. Set before the control loads.</summary>
        public Panel ItemsPanel
        {
            get => itemsPanel;
            set
            {
                if (built)
                    throw new InvalidOperationException("ItemsPanel must be set before the control loads.");

                itemsPanel = value;
            }
        }

        /// <summary>Raised after the items change.</summary>
        public event Action ItemsChanged;

        /// <summary>Adds an item (XAML child elements end up here). Not allowed while <see cref="ItemsSource"/> is set.</summary>
        public void Add(object item)
        {
            if (itemsSource != null)
                throw new InvalidOperationException("Items can't be added while ItemsSource is set.");

            localItems.Add(item);

            if (built)
            {
                insertItem(items.Count, item);
                ItemsHaveChanged();
            }
        }

        /// <summary>Removes an item added with <see cref="Add"/>.</summary>
        public bool RemoveItem(object item)
        {
            if (itemsSource != null)
                throw new InvalidOperationException("Items can't be removed while ItemsSource is set.");

            if (!localItems.Remove(item))
                return false;

            if (built)
            {
                removeAt(items.IndexOf(item));
                ItemsHaveChanged();
            }

            return true;
        }

        public Drawable ContainerFromIndex(int index) => index >= 0 && index < containers.Count ? containers[index] : null;

        public Drawable ContainerFromItem(object item) => ContainerFromIndex(IndexOf(item));

        public int IndexFromContainer(Drawable container) => containers.IndexOf(container);

        public object ItemFromContainer(Drawable container)
        {
            int index = containers.IndexOf(container);
            return index < 0 ? null : items[index];
        }

        public int IndexOf(object item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (Equals(items[i], item))
                    return i;
            }

            return -1;
        }

        /// <summary>The containers currently shown, in item order.</summary>
        protected IReadOnlyList<Drawable> Containers => containers;

        /// <summary>The panel the containers are in, once loaded.</summary>
        protected Panel Panel => itemsPanel;

        /// <summary>The panel used when <see cref="ItemsPanel"/> isn't set.</summary>
        protected virtual Panel CreateDefaultItemsPanel() => new StackPanel();

        /// <summary>Wraps the items panel, e.g. in a ScrollViewer. The result fills the control.</summary>
        protected virtual Drawable CreateItemsHost(Panel panel) => panel;

        /// <summary>Whether <paramref name="item"/> can be shown directly, without a container.</summary>
        protected virtual bool IsItemItsOwnContainer(object item) => item is Drawable;

        /// <summary>Creates the container that presents one data item.</summary>
        protected virtual Drawable CreateContainer() => new ContentPresenter();

        /// <summary>Points a container at its item. Called again when <see cref="ItemTemplate"/> changes.</summary>
        protected virtual void PrepareContainer(Drawable container, object item)
        {
            if (ReferenceEquals(container, item) || !(container is ContentControl content))
                return;

            content.ContentTemplate = itemTemplate;
            content.Content = item;
        }

        /// <summary>Called when a container is about to be removed.</summary>
        protected virtual void ClearContainer(Drawable container, object item)
        {
        }

        /// <summary>Called after the items change, before <see cref="ItemsChanged"/>.</summary>
        protected virtual void OnItemsChanged()
        {
        }

        protected void ItemsHaveChanged()
        {
            OnItemsChanged();
            ItemsChanged?.Invoke();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            itemsPanel ??= CreateDefaultItemsPanel();
            itemsPanel.RelativeSizeAxes = Axes.Both;

            host = CreateItemsHost(itemsPanel);

            if (!ReferenceEquals(host, itemsPanel) || host.RelativeSizeAxes == Axes.None)
                host.RelativeSizeAxes = Axes.Both;

            AddInternal(host);

            built = true;
            rebuild();
        }

        private void rebuild()
        {
            while (items.Count > 0)
                removeAt(items.Count - 1);

            IEnumerable source = itemsSource ?? localItems;

            foreach (object item in source)
                insertItem(items.Count, item);

            ItemsHaveChanged();
        }

        private void onCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!built)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                {
                    int index = e.NewStartingIndex < 0 ? items.Count : e.NewStartingIndex;

                    foreach (object item in e.NewItems)
                        insertItem(index++, item);

                    break;
                }

                case NotifyCollectionChangedAction.Remove:
                    removeItems(e.OldStartingIndex, e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Replace:
                {
                    removeItems(e.OldStartingIndex, e.OldItems);

                    int index = e.NewStartingIndex < 0 ? items.Count : e.NewStartingIndex;

                    foreach (object item in e.NewItems)
                        insertItem(index++, item);

                    break;
                }

                case NotifyCollectionChangedAction.Move when e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0:
                    for (int i = 0; i < e.OldItems.Count; i++)
                        move(e.OldStartingIndex + i, e.NewStartingIndex + i);

                    break;

                default:
                    rebuild();
                    return;
            }

            ItemsHaveChanged();
        }

        private void removeItems(int start, IList oldItems)
        {
            foreach (object item in oldItems)
            {
                // RemoveAll reports scattered items without a starting index, so match by item.
                int index = start >= 0 && start < items.Count && Equals(items[start], item) ? start : IndexOf(item);

                if (index >= 0)
                    removeAt(index);
            }
        }

        private void insertItem(int index, object item)
        {
            index = Math.Clamp(index, 0, items.Count);

            var container = IsItemItsOwnContainer(item) ? (Drawable)item : CreateContainer();

            items.Insert(index, item);
            containers.Insert(index, container);

            PrepareContainer(container, item);
            itemsPanel.Insert(index, container);
        }

        private void removeAt(int index)
        {
            var container = containers[index];
            ClearContainer(container, items[index]);

            items.RemoveAt(index);
            containers.RemoveAt(index);
            itemsPanel.Remove(container, true);
        }

        private void move(int from, int to)
        {
            if (from == to || from < 0 || from >= items.Count)
                return;

            object item = items[from];
            var container = containers[from];

            items.RemoveAt(from);
            containers.RemoveAt(from);
            itemsPanel.Remove(container, false);

            to = Math.Clamp(to, 0, items.Count);
            items.Insert(to, item);
            containers.Insert(to, container);
            itemsPanel.Insert(to, container);
        }

        public virtual Vector2 Measure(Vector2 availableSize)
        {
            if (host is ILayoutElement element)
                return element.Measure(availableSize);

            return itemsPanel?.Measure(availableSize) ?? Vector2.Zero;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && !AutoSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && !AutoSizeAxes.HasFlag(Axes.Y) && Height == 0;
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
        }

        protected override void Dispose(bool isDisposing)
        {
            if (observed != null)
                observed.CollectionChanged -= onCollectionChanged;

            observed = null;
            base.Dispose(isDisposing);
        }
    }

    /// <summary>A container that shows whether its item is selected.</summary>
    public interface ISelectableItem
    {
        bool IsSelected { get; set; }
    }

    /// <summary>
    /// An <see cref="ItemsControl"/> with one selected item, like WPF's Selector. <see cref="SelectedItem"/> and
    /// <see cref="SelectedIndex"/> are bindables that stay in sync with each other and bind two-way.
    /// </summary>
    public partial class Selector : ItemsControl
    {
        private bool syncing;

        public Selector()
        {
            SelectedItem.ValueChanged += _ => fromItem();
            SelectedIndex.ValueChanged += _ => fromIndex();
        }

        /// <summary>The selected item, or null. Binds two-way, also to a typed view model bindable.</summary>
        public Bindable<object> SelectedItem { get; } = new Bindable<object>();

        /// <summary>The index of the selected item, or -1.</summary>
        public Bindable<int> SelectedIndex { get; } = new Bindable<int>(-1);

        /// <summary>Raised after the selection changes.</summary>
        public event Action SelectionChanged;

        /// <summary>Selects <paramref name="item"/>, or clears the selection for null.</summary>
        public void Select(object item) => SelectedItem.Value = item;

        /// <summary>Selects the item a container presents. Containers call this when clicked.</summary>
        public virtual void SelectContainer(Drawable container, UIEvent e = null)
        {
            int index = IndexFromContainer(container);
            if (index >= 0)
                SelectedIndex.Value = index;
        }

        /// <summary>Called after the selection changes, before <see cref="SelectionChanged"/>.</summary>
        protected virtual void OnSelectionChanged()
        {
        }

        protected override void PrepareContainer(Drawable container, object item)
        {
            base.PrepareContainer(container, item);

            if (container is ISelectableItem selectable)
                selectable.IsSelected = IsSelected(item);
        }

        /// <summary>Whether <paramref name="item"/> is selected. Multi-select controls override this.</summary>
        protected virtual bool IsSelected(object item) => SelectedItem.Value != null && Equals(item, SelectedItem.Value);

        /// <summary>Re-applies <see cref="ISelectableItem.IsSelected"/> to every container and raises the events.</summary>
        protected void SelectionHasChanged()
        {
            for (int i = 0; i < Containers.Count; i++)
            {
                if (Containers[i] is ISelectableItem selectable)
                    selectable.IsSelected = IsSelected(Items[i]);
            }

            OnSelectionChanged();
            SelectionChanged?.Invoke();
        }

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();
            reconcile();
        }

        private void fromItem()
        {
            if (syncing)
                return;

            syncing = true;
            int index = IndexOf(SelectedItem.Value);

            // Before the items exist (e.g. SelectedItem bound before ItemsSource) the item is kept and resolved later.
            if (index >= 0 || SelectedItem.Value == null)
                SelectedIndex.Value = index;

            syncing = false;
            SelectionHasChanged();
        }

        private void fromIndex()
        {
            if (syncing)
                return;

            syncing = true;
            int index = SelectedIndex.Value;

            if (index >= 0 && index < Items.Count)
                SelectedItem.Value = Items[index];
            else if (index < 0)
                SelectedItem.Value = null;

            syncing = false;
            SelectionHasChanged();
        }

        /// <summary>Keeps the selection valid after the items change.</summary>
        private void reconcile()
        {
            syncing = true;

            object item = SelectedItem.Value;
            int index = item == null ? -1 : IndexOf(item);

            if (index >= 0)
                SelectedIndex.Value = index;
            else if (item == null && SelectedIndex.Value >= 0 && SelectedIndex.Value < Items.Count)
                SelectedItem.Value = Items[SelectedIndex.Value];
            else if (item != null || SelectedIndex.Value >= Items.Count)
            {
                // The selected item went away.
                SelectedItem.Value = null;
                SelectedIndex.Value = -1;
            }

            syncing = false;
            SelectionHasChanged();
        }
    }
}
