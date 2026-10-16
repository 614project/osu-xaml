using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using osu.Framework.Graphics;

namespace OsuXaml.Controls
{
    /// <summary>How a <see cref="ListView"/> lays out its items. <see cref="GridView"/> is the one built in.</summary>
    public abstract class ViewBase
    {
    }

    /// <summary>
    /// Shows <see cref="ListView"/> items as rows of cells under a header row, like WPF's GridView:
    /// <c>&lt;ListView.View&gt;&lt;GridView&gt;&lt;GridViewColumn Header="Name" DisplayMemberBinding="{Bind Name}" Width="160" /&gt;</c>.
    /// </summary>
    public class GridView : ViewBase
    {
        /// <summary>The columns, in display order. XAML child elements of the GridView are added here.</summary>
        public ObservableCollection<GridViewColumn> Columns { get; } = new ObservableCollection<GridViewColumn>();

        public void Add(GridViewColumn column) => Columns.Add(column);
    }

    /// <summary>
    /// One column of a <see cref="GridView"/>. Cells show <see cref="CellTemplate"/>, else <see cref="DisplayMemberBinding"/>
    /// (<c>DisplayMemberBinding="{Bind Name}"</c> compiles to a text template), else the item's text.
    /// </summary>
    public class GridViewColumn : TableColumn
    {
        /// <summary>Shows one member of each item as text.</summary>
        public DataTemplate DisplayMemberBinding { get; set; }

        /// <summary>Builds each cell; wins over <see cref="DisplayMemberBinding"/>.</summary>
        public DataTemplate CellTemplate { get; set; }
    }

    /// <summary>
    /// A <see cref="ListBox"/> whose items can be shown through a <see cref="View"/>, like WPF's ListView.
    /// With a <see cref="GridView"/>, a header row stays above the scrolling rows and every row lays its cells out
    /// in the shared column widths; rows keep ListBox selection, hover and keyboard navigation. Dragging a header edge
    /// resizes its column, and clicking the header of a column with <see cref="TableColumn.SortMemberPath"/> sorts the
    /// rows (the view model's list keeps its order). Without a View it behaves exactly like a ListBox.
    /// </summary>
    public partial class ListView : ListBox
    {
        private readonly TableSorter sorter;

        private ViewBase view;
        private GridView gridView;
        private ColumnLayout layout;
        private ColumnHeadersPresenter headers;
        private bool hostCreated;
        private bool paddingSet;
        private bool widthFromContent;

        public ListView()
        {
            sorter = new TableSorter(() => gridView?.Columns ?? (IEnumerable<TableColumn>)Array.Empty<TableColumn>());
        }

        /// <summary>How items are shown, e.g. a <see cref="GridView"/>. Set before the control loads.</summary>
        public ViewBase View
        {
            get => view;
            set
            {
                if (hostCreated && !ReferenceEquals(view, value))
                    throw new InvalidOperationException("View must be set before the ListView loads.");

                view = value;
            }
        }

        /// <summary>Raised when a GridView column header is clicked, before any sorting.</summary>
        public event Action<GridViewColumn> ColumnHeaderClick;

        public new MarginPadding Padding
        {
            get => base.Padding;
            set
            {
                base.Padding = value;
                paddingSet = true;
            }
        }

        /// <summary>The GridView header row, once loaded with a GridView.</summary>
        public ColumnHeadersPresenter ColumnHeaders => headers;

        /// <summary>Sorts the rows by <paramref name="column"/>'s <see cref="TableColumn.SortMemberPath"/>, or restores the source order for null.</summary>
        public void Sort(GridViewColumn column, ListSortDirection? direction = ListSortDirection.Ascending) => sorter.Sort(column, direction);

        /// <summary>The row presenter showing <paramref name="item"/>, in GridView mode.</summary>
        public GridViewRowPresenter RowFromItem(object item) => (ContainerFromItem(item) as ListViewItem)?.RowPresenter;

        protected override IEnumerable CreateItemsView(IEnumerable source) => sorter.Wrap(source);

        protected override Drawable CreateItemsHost(Panel panel)
        {
            var host = base.CreateItemsHost(panel);
            hostCreated = true;
            gridView = view as GridView;

            if (gridView == null || !(host is ContentControl chrome))
                return host;

            // The header runs edge to edge; rows bring their own spacing.
            if (!paddingSet)
                base.Padding = default;

            layout = new ColumnLayout(gridView.Columns);
            headers = new ColumnHeadersPresenter
            {
                ColumnLayout = layout,
                HeaderClicked = onHeaderClicked,
                CanSort = c => c.SortKey != null,
            };

            var scroll = chrome.Child;
            var dock = new DockPanel();
            dock.Add(headers);
            DockPanel.SetDock(headers, Dock.Top);
            dock.Add(scroll);
            chrome.Child = dock;

            gridView.Columns.CollectionChanged += onColumnsChanged;

            // Runs before the first rows are created, so a column declared with SortDirection starts sorted.
            sorter.Apply();
            return host;
        }

        protected override Drawable CreateContainer() => new ListViewItem();

        protected override void PrepareContainer(Drawable container, object item)
        {
            if (layout == null || !(container is ListViewItem row) || ReferenceEquals(container, item))
            {
                base.PrepareContainer(container, item);
                return;
            }

            row.Owner = this;
            row.PresentRow(layout, item);
            row.IsSelected = IsSelected(item);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            widthFromContent = !RelativeSizeAxes.HasFlag(Axes.X) && Width == 0;
        }

        protected override void Update()
        {
            base.Update();

            if (layout == null)
                return;

            // Before the rows update, so they arrange their cells with this frame's widths.
            bool unbounded = widthFromContent && !RelativeSizeAxes.HasFlag(Axes.X);
            layout.Solve(unbounded ? float.PositiveInfinity : Panel.DrawWidth, desiredWidth);
        }

        private float desiredWidth(int column)
        {
            float max = headers.DesiredWidth(column);

            foreach (var container in Containers)
            {
                if (container is ListViewItem { RowPresenter: { } row })
                    max = Math.Max(max, row.DesiredWidth(column));
            }

            return max;
        }

        private void onHeaderClicked(TableColumn column)
        {
            ColumnHeaderClick?.Invoke((GridViewColumn)column);

            if (column.SortKey != null)
                sorter.Sort(column, TableSorting.Next(column.SortDirection));
        }

        private void onColumnsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            headers.RebuildCells();

            foreach (var container in Containers)
                (container as ListViewItem)?.RowPresenter?.RebuildCells();

            sorter.Apply();
        }

        protected override void Dispose(bool isDisposing)
        {
            if (gridView != null)
                gridView.Columns.CollectionChanged -= onColumnsChanged;

            sorter.Dispose();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>One row of a <see cref="ListView"/>. In GridView mode its content is a <see cref="GridViewRowPresenter"/>.</summary>
    public partial class ListViewItem : ListBoxItem
    {
        /// <summary>The cells of this row, in GridView mode.</summary>
        public GridViewRowPresenter RowPresenter { get; private set; }

        internal void PresentRow(ColumnLayout layout, object item)
        {
            if (RowPresenter != null && ReferenceEquals(RowPresenter.Item, item))
                return;

            var old = RowPresenter;

            // Cells bring their own horizontal padding, so they line up with the headers.
            Padding = new MarginPadding { Vertical = 1 };
            ContentTemplate = null;
            Content = RowPresenter = new GridViewRowPresenter(item) { ColumnLayout = layout };

            // Drawable content belongs to whoever set it, so the replaced row is ours to dispose.
            old?.Dispose();
        }
    }

    /// <summary>The cells of one GridView row, laid out in the columns' shared widths.</summary>
    public partial class GridViewRowPresenter : TableRowPresenter
    {
        // A field, so osu!framework's disposal also unbinds the cell templates' bindings.
        private readonly BindingScope cellBindings = new BindingScope();

        public GridViewRowPresenter(object item)
        {
            Item = item;
        }

        /// <summary>The data item this row shows.</summary>
        public object Item { get; }

        protected override MarginPadding CellPadding => new MarginPadding { Horizontal = 8, Vertical = 5 };

        protected override Drawable CreateCell(TableColumn column)
        {
            var gridColumn = (GridViewColumn)column;
            var template = gridColumn.CellTemplate ?? gridColumn.DisplayMemberBinding;

            if (template != null && template.CanBuild(Item))
                return template.Build(Item, cellBindings);

            return new TextBlock { Text = Item?.ToString() ?? string.Empty };
        }

        protected override void ClearCells(bool disposing) => cellBindings.UnbindAll();
    }
}
