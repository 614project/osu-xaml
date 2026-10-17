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
    /// out by <see cref="ItemsPanel"/> (a vertical StackPanel by default). With a <see cref="VirtualizingPanel"/> as the
    /// items panel, containers exist only for the items that panel shows; <see cref="ContainerFromIndex"/> returns null for the rest.
    /// </summary>
    public partial class ItemsControl : CompositeDrawable, ILayoutElement
    {
        private readonly List<object> localItems = new List<object>();
        private readonly List<object> items = new List<object>();

        // One entry per item: its container, or null while a virtualizing panel hasn't realized it.
        private readonly List<Drawable> containers = new List<Drawable>();

        // Containers a recycling list let go of, ready to show another item.
        private readonly Stack<Drawable> recycled = new Stack<Drawable>();

        private IEnumerable itemsSource;
        private IEnumerable itemsView;
        private INotifyCollectionChanged observed;
        private DataTemplate itemTemplate;
        private Panel itemsPanel;
        private VirtualizingPanel virtualizingPanel;
        private bool recycling;
        private bool releasing;
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
                itemsView = value == null ? null : CreateItemsView(value);
                observed = itemsView as INotifyCollectionChanged;

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
                {
                    if (containers[i] != null)
                        PrepareContainer(containers[i], items[i]);
                }
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

        /// <summary>
        /// Whether containers are created only for the items in view: the items panel is a <see cref="VirtualizingPanel"/>
        /// and <see cref="VirtualizingPanel.GetIsVirtualizing"/> wasn't turned off. Known once the control loads.
        /// </summary>
        public bool IsVirtualizing => virtualizingPanel != null;

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
                insertItems(items.Count, new[] { item });
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
                removeRange(items.IndexOf(item), 1);
                ItemsHaveChanged();
            }

            return true;
        }

        /// <summary>The container of the item at <paramref name="index"/>, or null when there is none (out of range, or not realized).</summary>
        public Drawable ContainerFromIndex(int index) => index >= 0 && index < containers.Count ? containers[index] : null;

        public Drawable ContainerFromItem(object item) => ContainerFromIndex(IndexOf(item));

        public int IndexFromContainer(Drawable container) => container == null ? -1 : containers.IndexOf(container);

        public object ItemFromContainer(Drawable container)
        {
            int index = IndexFromContainer(container);
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

        /// <summary>One entry per item, in item order: its container, or null where a virtualizing panel hasn't realized one.</summary>
        protected IReadOnlyList<Drawable> Containers => containers;

        /// <summary>The indices of the items that have a container, in order. Every item unless the control virtualizes.</summary>
        protected IEnumerable<int> RealizedIndices => virtualizingPanel?.RealizedIndices ?? Enumerable.Range(0, containers.Count);

        /// <summary>The containers that exist, in item order.</summary>
        protected IEnumerable<Drawable> RealizedContainers => RealizedIndices.Select(i => containers[i]).Where(c => c != null);

        /// <summary>The panel the containers are in, once loaded.</summary>
        protected Panel Panel => itemsPanel;

        /// <summary>
        /// The sequence shown for <paramref name="source"/>, called whenever <see cref="ItemsSource"/> is set.
        /// Controls that sort return a view over the source; an observable view keeps the control in sync.
        /// </summary>
        protected virtual IEnumerable CreateItemsView(IEnumerable source) => source;

        /// <summary>The panel used when <see cref="ItemsPanel"/> isn't set.</summary>
        protected virtual Panel CreateDefaultItemsPanel() => new StackPanel();

        /// <summary>Wraps the items panel, e.g. in a ScrollViewer. The result fills the control.</summary>
        protected virtual Drawable CreateItemsHost(Panel panel) => panel;

        /// <summary>Whether <paramref name="item"/> can be shown directly, without a container.</summary>
        protected virtual bool IsItemItsOwnContainer(object item) => item is Drawable;

        /// <summary>Creates the container that presents one data item.</summary>
        protected virtual Drawable CreateContainer() => new ContentPresenter();

        /// <summary>
        /// Points a container at its item. Called again when <see cref="ItemTemplate"/> changes, and, in a recycling list,
        /// for a container that showed another item before.
        /// </summary>
        protected virtual void PrepareContainer(Drawable container, object item)
        {
            if (ReferenceEquals(container, item) || !(container is ContentControl content))
                return;

            content.ContentTemplate = itemTemplate;
            content.Content = item;
        }

        /// <summary>
        /// Called after a container is prepared and added to the panel for the item at <paramref name="index"/>: when the item
        /// is added, or, in a virtualizing list, each time it scrolls into view.
        /// </summary>
        protected virtual void OnContainerRealized(Drawable container, int index)
        {
        }

        /// <summary>
        /// Called when a container is about to stop showing its item: the item was removed, or (see <see cref="IsReleasingContainer"/>)
        /// it scrolled out of a virtualizing list's view.
        /// </summary>
        protected virtual void ClearContainer(Drawable container, object item)
        {
        }

        /// <summary>Set during <see cref="ClearContainer"/> when the item stays and only its container goes, because it scrolled out of view.</summary>
        private protected bool IsReleasingContainer => releasing;

        /// <summary>Whether a virtualizing panel must keep <paramref name="container"/> even when it is out of view.</summary>
        private protected virtual bool KeepContainerRealized(Drawable container) => false;

        /// <summary>Scrolls a virtualizing list until the item at <paramref name="index"/> is in view, creating its container now. False when not virtualizing.</summary>
        private protected bool BringIndexIntoView(int index, Func<Drawable, Drawable> part = null)
        {
            if (virtualizingPanel == null || index < 0 || index >= items.Count)
                return false;

            virtualizingPanel.BringIndexIntoView(index, part);
            return true;
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

            if (itemsPanel is VirtualizingPanel panel && VirtualizingPanel.GetIsVirtualizing(this) && VirtualizingPanel.GetIsVirtualizing(panel))
            {
                virtualizingPanel = panel;
                virtualizingPanel.Attach(this);
                recycling = VirtualizingPanel.GetVirtualizationMode(this) == VirtualizationMode.Recycling
                            || VirtualizingPanel.GetVirtualizationMode(panel) == VirtualizationMode.Recycling;
            }

            host = CreateItemsHost(itemsPanel);

            if (!ReferenceEquals(host, itemsPanel) || host.RelativeSizeAxes == Axes.None)
                host.RelativeSizeAxes = Axes.Both;

            AddInternal(host);

            built = true;
            rebuild();
        }

        #region Containers for a virtualizing panel

        internal int ItemCount => items.Count;

        internal Drawable ContainerAt(int index) => containers[index];

        internal bool KeepsContainerRealized(Drawable container) => KeepContainerRealized(container);

        /// <summary>Creates (or recycles) the container for the item at <paramref name="index"/> and adds it to the panel.</summary>
        internal Drawable RealizeContainer(int index)
        {
            object item = items[index];
            var container = IsItemItsOwnContainer(item) ? (Drawable)item : recycled.Count > 0 ? recycled.Pop() : CreateContainer();

            containers[index] = container;
            PrepareContainer(container, item);
            itemsPanel.Add(container);
            OnContainerRealized(container, index);
            return container;
        }

        /// <summary>Takes the container of an item that scrolled out of view away; the item stays.</summary>
        internal void ReleaseContainer(int index)
        {
            var container = containers[index];
            if (container == null)
                return;

            releasing = true;

            try
            {
                ClearContainer(container, items[index]);
            }
            finally
            {
                releasing = false;
            }

            containers[index] = null;
            discard(container, items[index]);
        }

        #endregion

        /// <summary>
        /// Shows the items of the source again. Items that stay keep their containers, so a reset (a re-sort, or a new list
        /// holding the same objects) keeps their selection, edits and measured size; containers of items that left are cleared.
        /// </summary>
        private void rebuild()
        {
            IEnumerable source = itemsView ?? localItems;
            var next = source.Cast<object>().ToList();

            var oldItems = items.ToArray();
            var oldContainers = containers.ToArray();

            // Matched by reference: a container shows one object, and an equal but different object needs its own.
            var byItem = new Dictionary<object, Queue<int>>(ReferenceEqualityComparer.Instance);
            var nulls = new Queue<int>();

            for (int i = 0; i < oldItems.Length; i++)
            {
                if (oldItems[i] == null)
                    nulls.Enqueue(i);
                else
                {
                    if (!byItem.TryGetValue(oldItems[i], out var queue))
                        byItem[oldItems[i]] = queue = new Queue<int>();

                    queue.Enqueue(i);
                }
            }

            var previousIndex = new int[next.Count];
            var kept = new bool[oldItems.Length];

            for (int j = 0; j < next.Count; j++)
            {
                object item = next[j];
                var queue = item == null ? nulls : byItem.GetValueOrDefault(item);
                previousIndex[j] = queue != null && queue.Count > 0 ? queue.Dequeue() : -1;

                if (previousIndex[j] >= 0)
                    kept[previousIndex[j]] = true;
            }

            for (int i = 0; i < oldItems.Length; i++)
            {
                if (!kept[i] && oldContainers[i] != null)
                {
                    ClearContainer(oldContainers[i], oldItems[i]);
                    discard(oldContainers[i], oldItems[i]);
                }
            }

            items.Clear();
            items.AddRange(next);
            containers.Clear();

            foreach (int i in previousIndex)
                containers.Add(i >= 0 ? oldContainers[i] : null);

            if (virtualizingPanel != null)
                virtualizingPanel.OnItemsReset(previousIndex);
            else
            {
                for (int j = 0; j < containers.Count; j++)
                {
                    if (containers[j] == null)
                        createContainer(j, false);
                }

                itemsPanel.ReorderChildren(containers);
            }

            ItemsHaveChanged();
        }

        private void onCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!built)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    insertItems(e.NewStartingIndex < 0 ? items.Count : e.NewStartingIndex, e.NewItems);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    removeItems(e.OldStartingIndex, e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    removeItems(e.OldStartingIndex, e.OldItems);
                    insertItems(e.NewStartingIndex < 0 ? items.Count : e.NewStartingIndex, e.NewItems);
                    break;

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

        private void insertItems(int index, IList newItems)
        {
            index = Math.Clamp(index, 0, items.Count);

            if (virtualizingPanel != null)
            {
                // Containers come later, for the items the panel shows.
                items.InsertRange(index, newItems.Cast<object>());
                containers.InsertRange(index, Enumerable.Repeat<Drawable>(null, newItems.Count));
                virtualizingPanel.OnItemsInserted(index, newItems.Count);
                return;
            }

            foreach (object item in newItems)
            {
                items.Insert(index, item);
                containers.Insert(index, null);
                createContainer(index++, true);
            }
        }

        /// <summary>Creates the container of a list that doesn't virtualize; <paramref name="inPlace"/> inserts it at its index, otherwise it is appended for a later reorder.</summary>
        private void createContainer(int index, bool inPlace)
        {
            object item = items[index];
            var container = IsItemItsOwnContainer(item) ? (Drawable)item : CreateContainer();

            containers[index] = container;
            PrepareContainer(container, item);

            if (inPlace)
                itemsPanel.Insert(index, container);
            else
                itemsPanel.Add(container);

            OnContainerRealized(container, index);
        }

        private void removeItems(int start, IList oldItems)
        {
            // Clear and RemoveRange report one run of items; removing it at once keeps clearing a long list linear.
            if (start >= 0 && start + oldItems.Count <= items.Count && Enumerable.Range(0, oldItems.Count).All(i => Equals(items[start + i], oldItems[i])))
            {
                removeRange(start, oldItems.Count);
                return;
            }

            foreach (object item in oldItems)
            {
                // RemoveAll reports scattered items without a starting index, so match by item.
                int index = start >= 0 && start < items.Count && Equals(items[start], item) ? start : IndexOf(item);

                if (index >= 0)
                    removeRange(index, 1);
            }
        }

        private void removeRange(int start, int count)
        {
            for (int i = start; i < start + count; i++)
            {
                if (containers[i] != null)
                {
                    ClearContainer(containers[i], items[i]);
                    discard(containers[i], items[i]);
                }
            }

            items.RemoveRange(start, count);
            containers.RemoveRange(start, count);
            virtualizingPanel?.OnItemsRemoved(start, count);
        }

        /// <summary>Takes a cleared container out of the panel.</summary>
        private void discard(Drawable container, object item)
        {
            // A drawable item belongs to whoever added it (it may be added again), so only generated containers are disposed or reused.
            if (ReferenceEquals(container, item))
                itemsPanel.Remove(container, false);
            else if (recycling)
            {
                itemsPanel.Remove(container, false);
                recycled.Push(container);
            }
            else
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

            to = Math.Clamp(to, 0, items.Count);
            items.Insert(to, item);
            containers.Insert(to, container);

            if (virtualizingPanel != null)
                virtualizingPanel.OnItemMoved(from, to);
            else
                itemsPanel.ReorderChildren(containers);
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

            // Recycled containers are out of the tree, so nothing else disposes them.
            while (recycled.Count > 0)
                recycled.Pop().Dispose();

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
        private int selectionDepth;
        private object raisedItem;
        private int selectionVersion;
        private int raisedVersion;

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
            selectionDepth++;

            try
            {
                foreach (int i in RealizedIndices)
                {
                    if (Containers[i] is ISelectableItem selectable)
                        selectable.IsSelected = IsSelected(Items[i]);
                }

                OnSelectionChanged();
            }
            finally
            {
                selectionDepth--;
            }

            // Keeping SelectedItem, SelectedIndex (and SelectedItems) in step re-enters here, and item changes that leave the
            // selection alone end here too; the event reports the settled selection once, and only when it really changed.
            if (selectionDepth > 0 || IsUpdatingSelection)
                return;

            if (Equals(raisedItem, SelectedItem.Value) && raisedVersion == selectionVersion)
                return;

            raisedItem = SelectedItem.Value;
            raisedVersion = selectionVersion;
            SelectionChanged?.Invoke();
        }

        /// <summary>Multi-select controls call this when selection state other than <see cref="SelectedItem"/> changes.</summary>
        protected void SelectionStateChanged() => selectionVersion++;

        /// <summary>Whether a multi-step selection update is under way; <see cref="SelectionChanged"/> waits for it to finish.</summary>
        protected virtual bool IsUpdatingSelection => false;

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
            // Once they do, an item the list doesn't have is still kept, but the index no longer claims another item.
            if (index >= 0 || SelectedItem.Value == null || itemsKnown)
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
            else if (index < 0 || itemsKnown)
                SelectedItem.Value = null;

            syncing = false;
            SelectionHasChanged();
        }

        /// <summary>Whether the items have been created, so an item or index they don't have really isn't there.</summary>
        private bool itemsKnown => LoadState >= LoadState.Ready;

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
