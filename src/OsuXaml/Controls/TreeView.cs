using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A hierarchical list, like WPF's TreeView. Bind <c>ItemsSource</c> and give a <c>HierarchicalDataTemplate</c> as
    /// <c>ItemTemplate</c> (its <c>ItemsSource="{Bind Children}"</c> names each node's children; observable children lists
    /// keep the tree in sync), or nest <c>&lt;TreeViewItem Header="..."&gt;</c> elements. <see cref="SelectedItem"/> is the
    /// selected data item (or the TreeViewItem, for declared items) and binds two-way. With focus, Up/Down move the
    /// selection, Right expands or enters a node, Left collapses or goes to the parent, Home/End jump to the ends.
    /// <para>
    /// As in WPF, a TreeView creates every node of each expanded level unless <c>VirtualizingPanel.IsVirtualizing="True"</c>
    /// is set on it. Then each level is a <see cref="VirtualizingStackPanel"/>: nodes get containers only while they are in
    /// view, and the selected node and its ancestors keep theirs. A node that scrolls away and back keeps its expanded state
    /// (through <see cref="IsExpandedBinding"/>, or remembered by item), and <see cref="TreeViewItem.ExpandSubtree"/> expands
    /// nodes as they come into view instead of creating the whole subtree.
    /// </para>
    /// </summary>
    public partial class TreeView : ItemsControl
    {
        private readonly Border chrome;

        // Data items whose nodes were expanded when a virtualizing level let go of their containers, and items asked to expand
        // their whole subtree before they had one; the value says which.
        private readonly ConditionalWeakTable<object, StrongBox<bool>> expansionMemory = new ConditionalWeakTable<object, StrongBox<bool>>();

        private ScrollViewer scroll;
        private TreeViewItem selectedContainer;
        private bool syncing;

        // The node a virtualizing tree is scrolling to; it and its ancestors keep their containers until the scroll settles.
        private TreeViewItem bringIntoViewTarget;

        public TreeView()
        {
            chrome = new Border
            {
                Background = Theme.Mantle,
                CornerRadius = 6,
                Padding = new MarginPadding(4),
            };

            SelectedItem.ValueChanged += e => onSelectedItemChanged(e.NewValue);
        }

        /// <summary>The selected data item (the TreeViewItem itself for declared items), or null. Binds two-way.</summary>
        public Bindable<object> SelectedItem { get; } = new Bindable<object>();

        /// <summary>Raised after the selection changes, with the new <see cref="SelectedItem"/>.</summary>
        public event Action<object> SelectedItemChanged;

        /// <summary>The container of the selected item, when it has been created.</summary>
        public TreeViewItem SelectedContainer => selectedContainer;

        /// <summary>
        /// Links every generated node's <see cref="TreeViewItem.IsExpanded"/> to a <c>Bindable&lt;bool&gt;</c> member of its item,
        /// two-way: <c>IsExpandedBinding="{Bind IsExpanded}"</c> (what an ItemContainerStyle setter does in WPF).
        /// </summary>
        public ItemBinding IsExpandedBinding { get; set; }

        /// <summary>How far each level is indented.</summary>
        public float Indent { get; set; } = 16;

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

        public new float BorderThickness
        {
            get => chrome.BorderThickness;
            set => chrome.BorderThickness = value;
        }

        public new float CornerRadius
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

        /// <summary>Every node whose header is showing (its ancestors are expanded), top to bottom. A virtualizing tree lists the nodes that have containers.</summary>
        public IEnumerable<TreeViewItem> VisibleItems => TreeViewItem.Visible(RealizedContainers);

        /// <summary>The container of <paramref name="item"/> anywhere in the tree, if it has been created.</summary>
        public TreeViewItem ContainerFromDataItem(object item)
            => item == null ? null : TreeViewItem.All(RealizedContainers).FirstOrDefault(n => Equals(n.DataItem, item));

        /// <summary>
        /// Scrolls until the header of <paramref name="item"/> (a data item or TreeViewItem) is visible. A virtualizing tree reaches
        /// nodes that have containers and top-level items; a nested item whose node hasn't been created can't be found.
        /// </summary>
        public void ScrollIntoView(object item)
        {
            var container = item as TreeViewItem ?? ContainerFromDataItem(item);

            var top = container;
            while (top?.ParentItem != null)
                top = top.ParentItem;

            int index = top == null ? -1 : IndexFromContainer(top);

            if (index >= 0)
            {
                // Nodes out of view don't update, so the top level (which always does) keeps scrolling until the header is in view
                // and has stopped moving as the nodes around it lay out.
                bringIntoViewTarget = container;

                if (BringIndexIntoView(index, _ => container.Parent != null ? container.HeaderRow : null))
                    return;

                bringIntoViewTarget = null;
            }
            else if (BringIndexIntoView(IndexOf(item), headerOf))
            {
                // A top-level item whose node hadn't been created.
                return;
            }

            if (container == null || scroll == null)
                return;

            // A node added this frame has no position until its panel arranges it, so scroll once layout has run.
            ScheduleAfterChildren(() =>
            {
                if (container.Parent != null)
                    scroll.ScrollIntoView(container.HeaderRow);
            });
        }

        protected override Drawable CreateItemsHost(Panel panel)
        {
            // A panel filling the viewport would give the scroll content no height, so it grows with its rows instead.
            panel.RelativeSizeAxes = Axes.X;
            panel.Height = 0;

            chrome.Child = scroll = new ScrollViewer { Child = panel };
            return chrome;
        }

        protected override Panel CreateDefaultItemsPanel() => CreateLevelPanel();

        protected override bool IsItemItsOwnContainer(object item) => item is Drawable;

        protected override Drawable CreateContainer() => new TreeViewItem();

        protected override void PrepareContainer(Drawable container, object item) => TreeViewItem.Prepare(this, null, container, item, ItemTemplate);

        protected override void ClearContainer(Drawable container, object item) => ContainerCleared(container, IsReleasingContainer);

        private protected override bool KeepContainerRealized(Drawable container) => KeepsNode(container);

        internal override float TypicalLength(Drawable container, float length) => TreeViewItem.TypicalLengthOf(container, length);

        /// <summary>The panel for one level of nodes: a VirtualizingStackPanel only when IsVirtualizing was set to True on the tree.</summary>
        internal Panel CreateLevelPanel()
        {
            if (VirtualizingPanel.IsVirtualizingSet(this) != true)
                return new StackPanel();

            var panel = new VirtualizingStackPanel();
            VirtualizingPanel.SetVirtualizationMode(panel, VirtualizingPanel.GetVirtualizationMode(this));
            return panel;
        }

        /// <summary>Whether a virtualizing level must keep <paramref name="container"/>: it holds the selected node, or the node being scrolled into view.</summary>
        internal bool KeepsNode(Drawable container)
        {
            if (!(container is TreeViewItem node))
                return false;

            if (selectedContainer != null && selectedContainer.IsDescendantOf(node))
                return true;

            if (!IsBringingIntoView)
                bringIntoViewTarget = null;

            return bringIntoViewTarget != null && bringIntoViewTarget.IsDescendantOf(node);
        }

        /// <summary>
        /// A node is leaving the tree, or, when <paramref name="released"/>, only its container is, because it scrolled out of a
        /// virtualizing level. Its container and those below it will be thrown away or show other items, so which of them were
        /// expanded is remembered by item.
        /// </summary>
        internal void ContainerCleared(Drawable container, bool released)
        {
            if (released && container is TreeViewItem node)
            {
                foreach (var n in TreeViewItem.All(new Drawable[] { node }))
                    n.RememberExpansion();
            }

            ContainerRemoved(container);
        }

        /// <summary>Remembers that the node of <paramref name="item"/> is expanded, or should expand its whole subtree, until it gets a container.</summary>
        internal void RememberExpansion(object item, bool subtree)
        {
            if (item != null)
                expansionMemory.AddOrUpdate(item, new StrongBox<bool>(subtree));
        }

        internal void ForgetExpansion(object item)
        {
            if (item != null)
                expansionMemory.Remove(item);
        }

        /// <summary>Whether the node of <paramref name="item"/> was remembered as expanded, and whether to expand its subtree; the memory is used up.</summary>
        internal bool TakeExpansion(object item, out bool subtree)
        {
            subtree = false;

            if (item == null || !expansionMemory.TryGetValue(item, out var memory))
                return false;

            expansionMemory.Remove(item);
            subtree = memory.Value;
            return true;
        }

        /// <summary>Selects <paramref name="container"/> (null clears the selection) and updates <see cref="SelectedItem"/>.</summary>
        public void SelectContainer(TreeViewItem container)
        {
            if (selectedContainer == container)
                return;

            var previous = selectedContainer;
            selectedContainer = container;

            previous?.SetSelected(false);
            container?.SetSelected(true);

            syncing = true;
            SelectedItem.Value = container?.DataItem;
            syncing = false;

            SelectedItemChanged?.Invoke(SelectedItem.Value);
        }

        /// <summary>A node was just prepared; it may be the item the selection is waiting for.</summary>
        internal void ContainerPrepared(TreeViewItem container)
        {
            if (container.IsSelected.Value)
                SelectContainer(container);
            else if (selectedContainer == null && SelectedItem.Value != null && Equals(container.DataItem, SelectedItem.Value))
                SelectContainer(container);
        }

        /// <summary>A node is leaving the tree; if it holds the selection, the selection is cleared.</summary>
        internal void ContainerRemoved(Drawable container)
        {
            if (selectedContainer == null || !(container is TreeViewItem removed))
                return;

            if (selectedContainer.IsDescendantOf(removed))
                SelectContainer(null);
        }

        internal void Focus() => GetContainingFocusManager()?.ChangeFocus(this);

        private void onSelectedItemChanged(object item)
        {
            if (syncing)
                return;

            // Set from code or a binding: select the matching node if it exists; one created later picks the selection up.
            var container = item as TreeViewItem ?? ContainerFromDataItem(item);
            var previous = selectedContainer;

            selectedContainer = container;
            previous?.SetSelected(false);
            container?.SetSelected(true);

            SelectedItemChanged?.Invoke(item);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!HasFocus)
                return base.OnKeyDown(e);

            var first = nodeAt(this, 0, 1);
            if (first == null)
                return base.OnKeyDown(e);

            // Moves go through the items rather than the containers, so they reach nodes a virtualizing tree hasn't created yet.
            var current = selectedContainer;
            TreeViewItem target;

            switch (e.Key)
            {
                case Key.Up:
                    target = current == null ? first : previousVisible(current) ?? current;
                    break;

                case Key.Down:
                    target = current == null ? first : nextVisible(current) ?? current;
                    break;

                case Key.Home:
                    target = first;
                    break;

                case Key.End:
                    target = lastVisible(nodeAt(this, Items.Count - 1, -1));
                    break;

                case Key.Right when current != null:
                    if (!current.HasItems)
                        return true;

                    if (!current.IsExpanded.Value)
                    {
                        current.IsExpanded.Value = true;
                        return true;
                    }

                    target = firstChild(current) ?? current;
                    break;

                case Key.Left when current != null:
                    if (current.IsExpanded.Value && current.HasItems)
                    {
                        current.IsExpanded.Value = false;
                        return true;
                    }

                    target = current.ParentItem ?? current;
                    break;

                case Key.KeypadPlus when current != null:
                    current.IsExpanded.Value = true;
                    return true;

                case Key.KeypadMinus when current != null:
                    current.IsExpanded.Value = false;
                    return true;

                default:
                    return base.OnKeyDown(e);
            }

            SelectContainer(target);
            ScrollIntoView(target);
            return true;
        }

        private static Drawable headerOf(Drawable container) => (container as TreeViewItem)?.HeaderRow ?? container;

        /// <summary>The level <paramref name="node"/> is an item of: its parent node, or the tree.</summary>
        private static ItemsControl ownerOf(TreeViewItem node) => (ItemsControl)node.ParentItem ?? node.ParentTreeView;

        /// <summary>The node of item <paramref name="index"/> of <paramref name="owner"/>, or the nearest one from there in the direction of <paramref name="step"/>.</summary>
        private static TreeViewItem nodeAt(ItemsControl owner, int index, int step)
        {
            for (; index >= 0 && index < owner.Items.Count; index += step)
            {
                // Drawables other than TreeViewItems are shown as they are, but aren't nodes.
                if (owner.Items[index] is Drawable && !(owner.Items[index] is TreeViewItem))
                    continue;

                if (owner.RealizeIndex(index) is TreeViewItem node)
                    return node;
            }

            return null;
        }

        private static TreeViewItem firstChild(TreeViewItem node) => node.ShowsChildren() ? nodeAt(node, 0, 1) : null;

        /// <summary>The node below <paramref name="node"/>: its first child, else the next sibling of it or of the nearest ancestor that has one.</summary>
        private static TreeViewItem nextVisible(TreeViewItem node)
        {
            var child = firstChild(node);
            if (child != null)
                return child;

            for (var n = node; n != null; n = n.ParentItem)
            {
                var owner = ownerOf(n);
                int index = owner?.IndexFromContainer(n) ?? -1;
                if (index < 0)
                    return null;

                var sibling = nodeAt(owner, index + 1, 1);
                if (sibling != null)
                    return sibling;
            }

            return null;
        }

        /// <summary>The node above <paramref name="node"/>: the last visible node under its previous sibling, else its parent.</summary>
        private static TreeViewItem previousVisible(TreeViewItem node)
        {
            var owner = ownerOf(node);
            int index = owner?.IndexFromContainer(node) ?? -1;
            if (index < 0)
                return null;

            var sibling = nodeAt(owner, index - 1, -1);
            return sibling != null ? lastVisible(sibling) : node.ParentItem;
        }

        /// <summary>The lowest visible node under <paramref name="node"/>, or the node itself when collapsed.</summary>
        private static TreeViewItem lastVisible(TreeViewItem node)
        {
            while (node != null && node.ShowsChildren())
            {
                var last = nodeAt(node, node.Items.Count - 1, -1);
                if (last == null)
                    break;

                node = last;
            }

            return node;
        }
    }

    /// <summary>
    /// One node of a <see cref="TreeView"/>: a header row (expander chevron, indentation, <see cref="Header"/>) and child nodes
    /// that slide open while <see cref="IsExpanded"/>. Children of data items are created the first time the node expands.
    /// Double-clicking the header or clicking the chevron toggles it.
    /// </summary>
    public partial class TreeViewItem : ItemsControl
    {
        private const float expander_size = 16;
        private const float header_vertical_padding = 3;

        private readonly HeaderRowDrawable headerRow;
        private readonly Container childrenArea;

        // A field, so osu!framework's disposal also unbinds the children list copy made by the hierarchical template.
        private BindingScope childrenBindings;

        private IEnumerable pendingChildren;
        private INotifyCollectionChanged pendingObserved;
        private DataTemplate preparedTemplate;
        private object preparedItem;
        private Bindable<bool> expandedSource;
        private Panel childPanel;
        private float expansion;
        private int level;

        // What the last measurement went by (header indent and expansion), checked each frame as the expansion animates.
        private Vector2 measuredFrom = new Vector2(float.NaN);
        private bool expandSubtreeOnLoad;
        private bool preparing;

        public TreeViewItem()
        {
            headerRow = new HeaderRowDrawable(this);
            childrenArea = new Container
            {
                RelativeSizeAxes = Axes.X,
                Masking = true,
                Alpha = 0,
            };

            IsExpanded.ValueChanged += e => onExpandedChanged(e.NewValue);
            IsSelected.ValueChanged += e => onIsSelectedChanged(e.NewValue);
        }

        /// <summary>The header: text, a drawable, or an object shown through <see cref="HeaderTemplate"/>.</summary>
        public object Header
        {
            get => headerRow.Presenter.Content;
            set => headerRow.Presenter.Content = value;
        }

        public DataTemplate HeaderTemplate
        {
            get => headerRow.Presenter.ContentTemplate;
            set => headerRow.Presenter.ContentTemplate = value;
        }

        /// <summary>Whether the children are showing. Binds two-way.</summary>
        public Bindable<bool> IsExpanded { get; } = new Bindable<bool>();

        /// <summary>Whether this node is the tree's selected node. Binds two-way; setting it selects the node.</summary>
        public Bindable<bool> IsSelected { get; } = new Bindable<bool>();

        public event Action Expanded;

        public event Action Collapsed;

        /// <summary>The tree this node belongs to, once its parent has prepared it.</summary>
        public TreeView ParentTreeView { get; private set; }

        /// <summary>The node above this one, or null at the top level.</summary>
        public TreeViewItem ParentItem { get; private set; }

        /// <summary>The data item this node shows; the node itself when it was declared as a TreeViewItem.</summary>
        public object DataItem { get; private set; }

        /// <summary>Depth in the tree: 0 for top-level nodes.</summary>
        public int Level
        {
            get => level;
            private set
            {
                level = value;
                headerRow.Indent = value * (ParentTreeView?.Indent ?? 16);
            }
        }

        /// <summary>Whether the node has children (created or not yet created).</summary>
        public bool HasItems => pendingChildren != null ? pendingChildren.Cast<object>().Any() : Items.Count > 0;

        /// <summary>The child nodes that exist, in order. In a virtualizing tree, the ones that have containers.</summary>
        public IEnumerable<TreeViewItem> ChildItems => RealizedContainers.OfType<TreeViewItem>();

        /// <summary>The header row, e.g. to scroll it into view.</summary>
        public Drawable HeaderRow => headerRow;

        /// <summary>Expands this node and every node below it. In a virtualizing tree, nodes below expand as they get containers.</summary>
        public void ExpandSubtree()
        {
            IsExpanded.Value = true;

            // Create the children now: a child created by this node's own expansion only creates its children when it loads.
            realizeChildren();

            if (LoadState < LoadState.Ready)
            {
                expandSubtreeOnLoad = true;
                return;
            }

            expandChildSubtrees();
        }

        internal bool IsDescendantOf(TreeViewItem ancestor)
        {
            for (var node = this; node != null; node = node.ParentItem)
            {
                if (node == ancestor)
                    return true;
            }

            return false;
        }

        internal void SetSelected(bool selected)
        {
            IsSelected.Value = selected;
            headerRow.UpdateState();
        }

        /// <summary>Whether the node is expanded and has children, creating its list of children if it hasn't yet.</summary>
        internal bool ShowsChildren()
        {
            if (!IsExpanded.Value)
                return false;

            realizeChildren();
            return Items.Count > 0;
        }

        /// <summary>Called as a virtualizing level lets go of this node's container: an expanded node of a data item is remembered by the tree.</summary>
        internal void RememberExpansion()
        {
            // Declared nodes keep their own state, and a bound one's lives in the view model.
            if (ParentTreeView == null || ReferenceEquals(DataItem, this) || expandedSource != null)
                return;

            if (IsExpanded.Value)
                ParentTreeView.RememberExpansion(DataItem, false);
            else
                ParentTreeView.ForgetExpansion(DataItem);
        }

        /// <summary>Points a container at its item, for both the tree's top level and a node's children.</summary>
        internal static void Prepare(TreeView tree, TreeViewItem parent, Drawable container, object item, DataTemplate template)
        {
            if (!(container is TreeViewItem node))
                return;

            node.ParentTreeView = tree;
            node.ParentItem = parent;
            node.Level = parent == null ? 0 : parent.Level + 1;

            if (ReferenceEquals(container, item))
                node.DataItem = node;
            else if (!ReferenceEquals(node.preparedItem, item) || !ReferenceEquals(node.preparedTemplate, template))
                node.present(tree, item, template);

            tree?.ContainerPrepared(node);
        }

        /// <summary>Shows <paramref name="item"/>: in a new node, a recycled one that showed another item, or with a new template.</summary>
        private void present(TreeView tree, object item, DataTemplate template)
        {
            bool newItem = !ReferenceEquals(preparedItem, item);
            preparing = true;

            try
            {
                // The previous item's children go first, so the new template isn't applied to them.
                if (newItem && ItemsSource != null)
                    ItemsSource = null;

                preparedItem = item;
                preparedTemplate = template;
                DataItem = item;
                HeaderTemplate = template;
                Header = item;
                ItemTemplate = template;

                var source = tree?.IsExpandedBinding?.GetSource(item) as Bindable<bool>;

                if (source != expandedSource)
                {
                    if (expandedSource != null)
                        IsExpanded.UnbindFrom(expandedSource);

                    expandedSource = source;

                    if (source != null)
                        IsExpanded.BindTo(source);
                }

                // A node shown again after scrolling away is expanded as it was; a recycled one drops the state of its previous item.
                bool subtree = false;

                if (newItem && tree != null)
                {
                    bool remembered = tree.TakeExpansion(item, out subtree);

                    if (source == null)
                        IsExpanded.Value = remembered;
                }

                setChildren(template as IHierarchicalTemplate, item);

                if (subtree)
                    ExpandSubtree();
            }
            finally
            {
                preparing = false;
            }
        }

        /// <summary>Every node below <paramref name="containers"/> whose ancestors are expanded, depth first.</summary>
        internal static IEnumerable<TreeViewItem> Visible(IEnumerable<Drawable> containers)
        {
            foreach (var node in containers.OfType<TreeViewItem>())
            {
                yield return node;

                if (!node.IsExpanded.Value)
                    continue;

                foreach (var child in Visible(node.RealizedContainers))
                    yield return child;
            }
        }

        /// <summary>Every created node below <paramref name="containers"/>, depth first.</summary>
        internal static IEnumerable<TreeViewItem> All(IEnumerable<Drawable> containers)
        {
            foreach (var node in containers.OfType<TreeViewItem>())
            {
                yield return node;

                foreach (var child in All(node.RealizedContainers))
                    yield return child;
            }
        }

        /// <summary>Expands the subtree of every child: now for those that have containers, and as it gets one for the rest.</summary>
        private void expandChildSubtrees()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                object item = Items[i];

                if (ContainerFromIndex(i) is TreeViewItem child)
                    child.ExpandSubtree();
                else if (item is TreeViewItem declared)
                    declared.ExpandSubtree();
                else if (!(item is Drawable) && ParentTreeView != null)
                {
                    ParentTreeView.RememberExpansion(item, true);

                    if (ParentTreeView.IsExpandedBinding?.GetSource(item) is Bindable<bool> expanded)
                        expanded.Value = true;
                }
            }
        }

        private void setChildren(IHierarchicalTemplate hierarchical, object item)
        {
            stopObservingPending();
            childrenBindings?.UnbindAll();

            if (ItemsSource != null)
                ItemsSource = null;

            if (hierarchical == null)
            {
                pendingChildren = null;
                updateExpander();
                return;
            }

            childrenBindings = new BindingScope();
            pendingChildren = hierarchical.GetChildren(item, childrenBindings);

            // Until the node first expands, only watch whether there are children, for the chevron.
            if (pendingChildren is INotifyCollectionChanged observable)
            {
                pendingObserved = observable;
                pendingObserved.CollectionChanged += onPendingChanged;
            }

            if (IsLoaded && IsExpanded.Value)
                realizeChildren();
            else
                updateExpander();
        }

        private void realizeChildren()
        {
            if (pendingChildren == null)
                return;

            var children = pendingChildren;
            stopObservingPending();
            pendingChildren = null;
            ItemsSource = children;
        }

        private void stopObservingPending()
        {
            if (pendingObserved != null)
                pendingObserved.CollectionChanged -= onPendingChanged;

            pendingObserved = null;
        }

        private void onPendingChanged(object sender, NotifyCollectionChangedEventArgs e) => updateExpander();

        protected override Drawable CreateItemsHost(Panel panel)
        {
            childPanel = panel;

            // The children grow with their content; the item shows as much of them as its expansion allows.
            panel.RelativeSizeAxes = Axes.X;
            panel.Height = 0;
            childrenArea.Child = panel;

            var body = new Container { RelativeSizeAxes = Axes.Both };
            body.Add(childrenArea);
            body.Add(headerRow);
            return body;
        }

        protected override Panel CreateDefaultItemsPanel() => ParentTreeView?.CreateLevelPanel() ?? new StackPanel();

        protected override bool IsItemItsOwnContainer(object item) => item is Drawable;

        protected override Drawable CreateContainer() => new TreeViewItem();

        protected override void PrepareContainer(Drawable container, object item) => Prepare(ParentTreeView, this, container, item, ItemTemplate);

        protected override void ClearContainer(Drawable container, object item) => ParentTreeView?.ContainerCleared(container, IsReleasingContainer);

        private protected override bool KeepContainerRealized(Drawable container) => ParentTreeView?.KeepsNode(container) == true;

        internal override float TypicalLength(Drawable container, float length) => TypicalLengthOf(container, length);

        /// <summary>A node counts as its header row, whether or not it is expanded.</summary>
        internal static float TypicalLengthOf(Drawable container, float length) => container is TreeViewItem node ? Math.Min(length, node.headerHeight) : length;

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();
            updateExpander();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (IsExpanded.Value)
            {
                realizeChildren();
                expansion = 1;
            }

            headerRow.SetExpanded(IsExpanded.Value, false);
            headerRow.UpdateState();
            updateExpander();

            if (expandSubtreeOnLoad)
            {
                expandSubtreeOnLoad = false;
                expandChildSubtrees();
            }
        }

        private void onExpandedChanged(bool expanded)
        {
            if (!IsLoaded)
                return;

            // A recycled node taking on the state of its new item jumps to it; its children are set up after.
            if (preparing)
            {
                ClearTransforms(false, nameof(expansion));
                expansion = expanded ? 1 : 0;
                headerRow.SetExpanded(expanded, false);
                return;
            }

            if (expanded)
                realizeChildren();
            else if (ParentTreeView?.SelectedContainer is TreeViewItem selected && selected != this && selected.IsDescendantOf(this))
                ParentTreeView.SelectContainer(this);

            this.TransformTo(nameof(expansion), expanded ? 1f : 0f, 250, Easing.OutQuint);
            headerRow.SetExpanded(expanded, true);

            if (expanded)
                Expanded?.Invoke();
            else
                Collapsed?.Invoke();
        }

        private void onIsSelectedChanged(bool selected)
        {
            headerRow.UpdateState();

            var tree = ParentTreeView;
            if (tree == null)
                return;

            if (selected)
                tree.SelectContainer(this);
            else if (tree.SelectedContainer == this)
                tree.SelectContainer(null);
        }

        private void updateExpander() => headerRow.ShowExpander(HasItems);

        private float headerHeight => Math.Max(expander_size, headerRow.Presenter.Measure(new Vector2(float.PositiveInfinity)).Y) + header_vertical_padding * 2;

        private float headerWidth => headerRow.ContentOffset + headerRow.Presenter.Measure(new Vector2(float.PositiveInfinity)).X + 8;

        public override Vector2 Measure(Vector2 availableSize)
        {
            var size = new Vector2(headerWidth, headerHeight);

            if (expansion > 0 && childPanel != null)
            {
                var children = childPanel.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
                size.X = Math.Max(size.X, children.X);
                size.Y += children.Y * expansion;
            }

            return size;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            var from = new Vector2(headerRow.ContentOffset, expansion);

            if (from != measuredFrom)
            {
                measuredFrom = from;
                Layout.InvalidateMeasure(this);
            }

            float header = headerHeight;
            headerRow.Height = header;

            childrenArea.Y = header;
            childrenArea.Height = Math.Max(0, DrawHeight - header);

            // Fully collapsed children are not present, so they skip updates and input.
            childrenArea.Alpha = expansion > 0 ? 1 : 0;
        }

        protected override void Dispose(bool isDisposing)
        {
            stopObservingPending();
            childrenBindings?.UnbindAll();
            base.Dispose(isDisposing);
        }

        /// <summary>The clickable header row: highlight, indentation, expander and header content.</summary>
        private partial class HeaderRowDrawable : CompositeDrawable
        {
            public readonly ContentPresenter Presenter;

            private readonly TreeViewItem item;
            private readonly Box highlight;
            private readonly ExpanderDrawable expander;
            private bool hovered;
            private float indent;

            public HeaderRowDrawable(TreeViewItem item)
            {
                this.item = item;

                RelativeSizeAxes = Axes.X;
                Masking = true;
                CornerRadius = 4;

                InternalChildren = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Accent, Alpha = 0 },
                    expander = new ExpanderDrawable(item)
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(expander_size),
                    },
                    Presenter = new ContentPresenter
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        VerticalContentAlignment = VerticalAlignment.Center,
                    },
                };

                Indent = 0;
            }

            public float Indent
            {
                get => indent;
                set
                {
                    indent = value;
                    expander.X = 4 + value;
                    Presenter.X = ContentOffset;
                }
            }

            /// <summary>Where the header content starts.</summary>
            public float ContentOffset => 4 + indent + expander_size + 4;

            public void SetExpanded(bool expanded, bool animate) => expander.SetExpanded(expanded, animate);

            public void ShowExpander(bool show) => expander.FadeTo(show ? 1 : 0, IsLoaded ? 150 : 0, Easing.OutQuint);

            public void UpdateState()
            {
                if (!IsLoaded)
                    return;

                bool selected = item.IsSelected.Value;
                highlight.FadeColour(selected ? Theme.Accent : Theme.Text, 100);
                highlight.FadeTo(selected ? 0.28f : hovered ? 0.08f : 0, 100, Easing.OutQuint);
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                UpdateState();
            }

            protected override bool OnHover(HoverEvent e)
            {
                hovered = true;
                UpdateState();
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hovered = false;
                UpdateState();
            }

            protected override bool OnClick(ClickEvent e)
            {
                var tree = item.ParentTreeView;
                tree?.Focus();

                if (tree != null)
                    tree.SelectContainer(item);
                else
                    item.IsSelected.Value = true;

                return true;
            }

            protected override bool OnDoubleClick(DoubleClickEvent e)
            {
                if (!item.HasItems)
                    return false;

                item.IsExpanded.Value = !item.IsExpanded.Value;
                return true;
            }
        }

        /// <summary>The chevron that toggles a node; it points down while expanded.</summary>
        private partial class ExpanderDrawable : CompositeDrawable
        {
            private readonly TreeViewItem item;
            private readonly SpriteIcon chevron;

            public ExpanderDrawable(TreeViewItem item)
            {
                this.item = item;
                Alpha = 0;

                InternalChild = chevron = new SpriteIcon
                {
                    Icon = FontAwesome.Solid.ChevronRight,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(9),
                    Colour = Theme.SubtleText,
                };
            }

            public void SetExpanded(bool expanded, bool animate) => chevron.RotateTo(expanded ? 90 : 0, animate ? 200 : 0, Easing.OutQuint);

            protected override bool OnHover(HoverEvent e)
            {
                chevron.FadeColour(Theme.Text, 100, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                chevron.FadeColour(Theme.SubtleText, 250, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                if (Alpha == 0)
                    return false;

                item.IsExpanded.Value = !item.IsExpanded.Value;
                return true;
            }

            protected override bool OnDoubleClick(DoubleClickEvent e) => Alpha > 0;
        }
    }
}
