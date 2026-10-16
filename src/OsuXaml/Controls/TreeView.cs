using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
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
    /// </summary>
    public partial class TreeView : ItemsControl
    {
        private readonly Border chrome;
        private ScrollViewer scroll;
        private TreeViewItem selectedContainer;
        private bool syncing;

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

        /// <summary>Every node whose header is showing (its ancestors are expanded), top to bottom.</summary>
        public IEnumerable<TreeViewItem> VisibleItems => TreeViewItem.Visible(Containers);

        /// <summary>The container of <paramref name="item"/> anywhere in the tree, if it has been created.</summary>
        public TreeViewItem ContainerFromDataItem(object item)
            => item == null ? null : TreeViewItem.All(Containers).FirstOrDefault(n => Equals(n.DataItem, item));

        /// <summary>Scrolls until the header of <paramref name="item"/> (a data item or TreeViewItem) is visible.</summary>
        public void ScrollIntoView(object item)
        {
            var container = item as TreeViewItem ?? ContainerFromDataItem(item);
            if (container != null)
                scroll?.ScrollIntoView(container.HeaderRow);
        }

        protected override Drawable CreateItemsHost(Panel panel)
        {
            // A panel filling the viewport would give the scroll content no height, so it grows with its rows instead.
            panel.RelativeSizeAxes = Axes.X;
            panel.Height = 0;

            chrome.Child = scroll = new ScrollViewer { Child = panel };
            return chrome;
        }

        protected override bool IsItemItsOwnContainer(object item) => item is Drawable;

        protected override Drawable CreateContainer() => new TreeViewItem();

        protected override void PrepareContainer(Drawable container, object item) => TreeViewItem.Prepare(this, null, container, item, ItemTemplate);

        protected override void ClearContainer(Drawable container, object item) => ContainerRemoved(container);

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

            var visible = VisibleItems.ToList();
            if (visible.Count == 0)
                return base.OnKeyDown(e);

            var current = selectedContainer;
            int index = current == null ? -1 : visible.IndexOf(current);
            TreeViewItem target;

            switch (e.Key)
            {
                case Key.Up:
                    target = visible[Math.Max(0, index - 1)];
                    break;

                case Key.Down:
                    target = visible[Math.Min(visible.Count - 1, index + 1)];
                    break;

                case Key.Home:
                    target = visible[0];
                    break;

                case Key.End:
                    target = visible[^1];
                    break;

                case Key.Right when current != null:
                    if (!current.HasItems)
                        return true;

                    if (!current.IsExpanded.Value)
                    {
                        current.IsExpanded.Value = true;
                        return true;
                    }

                    target = current.ChildItems.FirstOrDefault() ?? current;
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
        private bool expandSubtreeOnLoad;

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

        /// <summary>The child nodes that exist, in order.</summary>
        public IEnumerable<TreeViewItem> ChildItems => Containers.OfType<TreeViewItem>();

        /// <summary>The header row, e.g. to scroll it into view.</summary>
        public Drawable HeaderRow => headerRow;

        /// <summary>Expands this node and every node below it.</summary>
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

            foreach (var child in ChildItems)
                child.ExpandSubtree();
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
            {
                node.preparedItem = item;
                node.preparedTemplate = template;
                node.DataItem = item;
                node.HeaderTemplate = template;
                node.Header = item;
                node.ItemTemplate = template;
                node.setChildren(template as IHierarchicalTemplate, item);

                if (tree?.IsExpandedBinding?.GetSource(item) is Bindable<bool> expanded && expanded != node.expandedSource)
                {
                    node.expandedSource = expanded;
                    node.IsExpanded.BindTo(expanded);
                }
            }

            tree?.ContainerPrepared(node);
        }

        /// <summary>Every node below <paramref name="containers"/> whose ancestors are expanded, depth first.</summary>
        internal static IEnumerable<TreeViewItem> Visible(IEnumerable<Drawable> containers)
        {
            foreach (var node in containers.OfType<TreeViewItem>())
            {
                yield return node;

                if (!node.IsExpanded.Value)
                    continue;

                foreach (var child in Visible(node.Containers))
                    yield return child;
            }
        }

        /// <summary>Every created node below <paramref name="containers"/>, depth first.</summary>
        internal static IEnumerable<TreeViewItem> All(IEnumerable<Drawable> containers)
        {
            foreach (var node in containers.OfType<TreeViewItem>())
            {
                yield return node;

                foreach (var child in All(node.Containers))
                    yield return child;
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

        protected override bool IsItemItsOwnContainer(object item) => item is Drawable;

        protected override Drawable CreateContainer() => new TreeViewItem();

        protected override void PrepareContainer(Drawable container, object item) => Prepare(ParentTreeView, this, container, item, ItemTemplate);

        protected override void ClearContainer(Drawable container, object item) => ParentTreeView?.ContainerRemoved(container);

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

                foreach (var child in ChildItems)
                    child.ExpandSubtree();
            }
        }

        private void onExpandedChanged(bool expanded)
        {
            if (!IsLoaded)
                return;

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
