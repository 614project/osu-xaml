using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using ListSortDirection = System.ComponentModel.ListSortDirection;
using Visibility = OsuXaml.Controls.Visibility;

namespace OsuXaml.Controls
{
    /// <summary>
    /// What a ListView <see cref="GridViewColumn"/> and a <see cref="DataGridColumn"/> share: a header, a width
    /// (<c>Width="120"</c>, <c>"Auto"</c>, <c>"*"</c> or <c>"2*"</c>) that every row lays its cells out by, and sorting.
    /// </summary>
    public abstract class TableColumn
    {
        private object header;
        private DataTemplate headerTemplate;
        private ListSortDirection? sortDirection;

        /// <summary>Raised when the header, its template or the sort direction changes, so the header cell can follow.</summary>
        internal event Action HeaderChanged;

        /// <summary>The header content: text, a drawable, or an object shown through <see cref="HeaderTemplate"/>.</summary>
        public object Header
        {
            get => header;
            set
            {
                header = value;
                HeaderChanged?.Invoke();
            }
        }

        public DataTemplate HeaderTemplate
        {
            get => headerTemplate;
            set
            {
                headerTemplate = value;
                HeaderChanged?.Invoke();
            }
        }

        /// <summary>Pixels, <c>Auto</c> (the widest cell or header) or a star share of the space left. Auto by default.</summary>
        public GridLength Width { get; set; } = new GridLength(0, GridUnitType.Auto);

        public float MinWidth { get; set; } = 20;

        public float MaxWidth { get; set; } = float.PositiveInfinity;

        /// <summary>The width the column was given in the last layout pass.</summary>
        public float ActualWidth { get; internal set; }

        /// <summary>Hidden and Collapsed columns both take no space.</summary>
        public Visibility Visibility { get; set; }

        /// <summary>Whether dragging the right edge of the header resizes the column.</summary>
        public bool CanUserResize { get; set; } = true;

        /// <summary>
        /// The member rows are sorted by when the header is clicked: <c>SortMemberPath="Age"</c> compiles to a typed accessor.
        /// DataGrid bound columns sort by their Binding when this isn't set.
        /// </summary>
        public ItemBinding SortMemberPath { get; set; }

        /// <summary>The current sort of this column, or null. Setting it in XAML sorts the rows when the control loads.</summary>
        public ListSortDirection? SortDirection
        {
            get => sortDirection;
            set
            {
                sortDirection = value;
                HeaderChanged?.Invoke();
            }
        }

        internal bool IsVisible => Visibility == Visibility.Visible;

        /// <summary>The accessor rows are sorted by, or null when the column can't sort.</summary>
        internal virtual ItemBinding SortKey => SortMemberPath;

        /// <summary>What the header shows: <see cref="Header"/>, or a fallback such as the bound member's name.</summary>
        internal virtual object EffectiveHeader => Header;
    }

    /// <summary>Solves column widths once per frame; every row of a table reads them.</summary>
    internal sealed class ColumnLayout
    {
        public readonly IReadOnlyList<TableColumn> Columns;

        public float[] Widths = Array.Empty<float>();
        public float[] Offsets = Array.Empty<float>();
        public float Total;

        public ColumnLayout(IReadOnlyList<TableColumn> columns)
        {
            Columns = columns;
        }

        /// <summary>
        /// Pixel columns take their width, Auto columns their widest cell, and star columns share what is left.
        /// With unbounded space (a table sizing to its content) star columns size to their content too, as in Grid.
        /// </summary>
        public void Solve(float available, Func<int, float> desired)
        {
            int count = Columns.Count;

            if (Widths.Length != count)
            {
                Widths = new float[count];
                Offsets = new float[count];
            }

            float used = 0, totalWeight = 0;

            for (int i = 0; i < count; i++)
            {
                var column = Columns[i];
                var width = column.Width;

                if (!column.IsVisible)
                    Widths[i] = 0;
                else if (width.Type == GridUnitType.Star)
                {
                    totalWeight += width.Value;
                    continue;
                }
                else
                    Widths[i] = clamp(column, width.Type == GridUnitType.Pixel ? width.Value : desired(i));

                used += Widths[i];
            }

            if (totalWeight > 0)
            {
                float unit = 0;

                if (float.IsInfinity(available))
                {
                    for (int i = 0; i < count; i++)
                    {
                        var width = Columns[i].Width;
                        if (Columns[i].IsVisible && width.Type == GridUnitType.Star && width.Value > 0)
                            unit = Math.Max(unit, desired(i) / width.Value);
                    }
                }
                else
                    unit = Math.Max(0, available - used) / totalWeight;

                for (int i = 0; i < count; i++)
                {
                    if (Columns[i].IsVisible && Columns[i].Width.Type == GridUnitType.Star)
                        Widths[i] = clamp(Columns[i], unit * Columns[i].Width.Value);
                }
            }

            Total = 0;

            for (int i = 0; i < count; i++)
            {
                Offsets[i] = Total;
                Total += Widths[i];
                Columns[i].ActualWidth = Widths[i];
            }
        }

        private static float clamp(TableColumn column, float width) => Math.Clamp(width, Math.Min(column.MinWidth, column.MaxWidth), column.MaxWidth);
    }

    /// <summary>
    /// One row of a table (a ListView GridView row, a DataGrid row, or the header row): one cell per column,
    /// each in a <see cref="Slot"/> placed at the column's offset and width from the table's shared layout.
    /// </summary>
    public abstract partial class TableRowPresenter : CompositeDrawable, ILayoutElement
    {
        private readonly List<Slot> cells = new List<Slot>();
        private readonly List<Box> lines = new List<Box>();
        private Colour4 lineColour = Theme.Border;
        private bool built;

        internal ColumnLayout ColumnLayout { get; set; }

        /// <summary>The cells, in column order, once loaded.</summary>
        public IEnumerable<Drawable> Cells => cells.Select(c => c.Content);

        /// <summary>Space between a cell's edges and its content.</summary>
        protected virtual MarginPadding CellPadding => default;

        /// <summary>A fixed row height, or 0 to fit the tallest cell.</summary>
        public float RowHeight { get; set; }

        /// <summary>Draws a line at the right edge of every column.</summary>
        public bool ShowVerticalLines { get; set; }

        public Colour4 LineColour
        {
            get => lineColour;
            set
            {
                lineColour = value;
                foreach (var line in lines)
                    line.Colour = value;
            }
        }

        /// <summary>Creates the cell for <paramref name="column"/>.</summary>
        protected abstract Drawable CreateCell(TableColumn column);

        /// <summary>
        /// Called before the cells are thrown away: when the columns change, or with <paramref name="disposing"/> set when the row goes away.
        /// </summary>
        protected virtual void ClearCells(bool disposing)
        {
        }

        public Drawable CellAt(int column) => column >= 0 && column < cells.Count ? cells[column].Content : null;

        [BackgroundDependencyLoader]
        private void load()
        {
            built = true;
            buildCells();
        }

        /// <summary>Recreates every cell, after the columns changed.</summary>
        internal void RebuildCells()
        {
            if (!built)
                return;

            ClearCells(false);

            foreach (var slot in cells)
                RemoveInternal(slot, true);
            foreach (var line in lines)
                RemoveInternal(line, true);

            cells.Clear();
            lines.Clear();
            buildCells();
        }

        private void buildCells()
        {
            foreach (var column in ColumnLayout.Columns)
            {
                var cell = CreateCell(column) ?? new Container();

                // Text and other content-sized cells sit on the row's centre line, as in WPF's default cell style.
                var props = Layout.Get(cell);
                if (props.VerticalAlignment == VerticalAlignment.Stretch && Layout.AutoSizeAxes(cell).HasFlag(Axes.Y))
                    props.VerticalAlignment = VerticalAlignment.Center;

                var slot = new Slot(cell);
                cells.Add(slot);
                AddInternal(slot);
            }

            foreach (var _ in ColumnLayout.Columns)
            {
                var line = new Box { Width = 1, RelativeSizeAxes = Axes.Y, Origin = Anchor.TopRight, Colour = lineColour, Alpha = 0 };
                lines.Add(line);
                AddInternal(line);
            }
        }

        /// <summary>The width the cell in <paramref name="column"/> would like, including the cell padding.</summary>
        internal float DesiredWidth(int column)
        {
            if (column >= cells.Count)
                return 0;

            return cells[column].Measure(new Vector2(float.PositiveInfinity)).X + CellPadding.TotalHorizontal;
        }

        public Vector2 Measure(Vector2 availableSize)
        {
            var widths = ColumnLayout.Widths;
            var padding = CellPadding;

            if (RowHeight > 0)
                return new Vector2(ColumnLayout.Total, RowHeight);

            float height = 0;

            for (int i = 0; i < cells.Count && i < widths.Length; i++)
            {
                if (!ColumnLayout.Columns[i].IsVisible)
                    continue;

                var desired = cells[i].Measure(new Vector2(Math.Max(0, widths[i] - padding.TotalHorizontal), float.PositiveInfinity));
                height = Math.Max(height, desired.Y);
            }

            return new Vector2(ColumnLayout.Total, height + padding.TotalVertical);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            var widths = ColumnLayout.Widths;
            var offsets = ColumnLayout.Offsets;
            var padding = CellPadding;

            for (int i = 0; i < cells.Count; i++)
            {
                var slot = cells[i];
                bool visible = i < widths.Length && ColumnLayout.Columns[i].IsVisible && widths[i] > 0;

                slot.Alpha = visible ? 1 : 0;
                lines[i].Alpha = visible && ShowVerticalLines ? 1 : 0;

                if (!visible)
                    continue;

                float width = Math.Max(0, widths[i] - padding.TotalHorizontal);
                slot.Arrange(new Vector2(offsets[i] + padding.Left, padding.Top), new Vector2(width, Math.Max(0, DrawHeight - padding.TotalVertical)));

                // Clip only cells that overflow their column; masking every cell would cost draw batching for nothing.
                slot.Masking = slot.Measure(new Vector2(float.PositiveInfinity)).X > width + 0.5f;

                lines[i].X = offsets[i] + widths[i];
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (built)
                ClearCells(true);

            base.Dispose(isDisposing);
        }
    }

    /// <summary>The header row of a ListView GridView or a DataGrid: one <see cref="TableColumnHeader"/> per column.</summary>
    public partial class ColumnHeadersPresenter : TableRowPresenter
    {
        private readonly Box background;
        private readonly Box bottomLine;

        public ColumnHeadersPresenter()
        {
            ShowVerticalLines = true;

            AddInternal(background = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Surface, Depth = float.MaxValue });
            AddInternal(bottomLine = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Colour = Theme.Border,
                Depth = float.MinValue,
            });
        }

        public Colour4 Background
        {
            get => background.Colour;
            set => background.Colour = value;
        }

        public Colour4 BottomLineColour
        {
            get => bottomLine.Colour;
            set => bottomLine.Colour = value;
        }

        /// <summary>Called when a header is clicked; tables sort here.</summary>
        internal Action<TableColumn> HeaderClicked;

        /// <summary>Whether the user may resize columns by dragging header edges.</summary>
        internal Func<bool> CanResize = () => true;

        /// <summary>Whether a column shows hover feedback and sort arrows for clicking.</summary>
        internal Func<TableColumn, bool> CanSort = _ => false;

        public IEnumerable<TableColumnHeader> Headers => Cells.OfType<TableColumnHeader>();

        protected override Drawable CreateCell(TableColumn column) => new TableColumnHeader(column, this);

        protected override void ClearCells(bool disposing)
        {
            if (disposing)
                return;

            foreach (var header in Headers)
                header.DetachHeader();
        }
    }

    /// <summary>
    /// The header of one column: shows <see cref="TableColumn.Header"/>, sorts on click when the table allows it,
    /// and resizes the column when its right edge is dragged (double-clicking the edge sizes it to its content).
    /// </summary>
    public partial class TableColumnHeader : ContentControl
    {
        private const float sort_icon_space = 16;

        private readonly ColumnHeadersPresenter owner;
        private readonly Box hover;
        private readonly SpriteIcon sortIcon;

        internal TableColumnHeader(TableColumn column, ColumnHeadersPresenter owner)
        {
            Column = column;
            this.owner = owner;

            Padding = new MarginPadding { Left = 8, Right = 8, Vertical = 6 };
            VerticalContentAlignment = VerticalAlignment.Center;
            Foreground = Theme.SubtleText;

            Chrome.Add(hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 });

            AddInternal(sortIcon = new SpriteIcon
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -6,
                Size = new Vector2(10),
                Colour = Theme.Accent,
                Alpha = 0,
            });

            AddInternal(new ResizeGrip(this));

            ContentTemplate = column.HeaderTemplate;
            Content = column.EffectiveHeader;
        }

        public TableColumn Column { get; }

        protected override Drawable CreateTextContent(LocalisableString text)
            => new TextBlock { Text = text, Colour = Foreground, FontSize = 14, FontWeight = FontWeight.Bold };

        public override Vector2 Measure(Vector2 availableSize)
            => base.Measure(availableSize) + new Vector2(Column.SortDirection != null ? sort_icon_space : 0, 0);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Column.HeaderChanged += onHeaderChanged;
            applySortIcon(0);
        }

        /// <summary>Hands a drawable header back before this header cell is thrown away, so the column can show it again.</summary>
        internal void DetachHeader() => Content = null;

        private void onHeaderChanged()
        {
            // Columns are plain objects whose setters may run on any thread; apply on ours.
            Schedule(() =>
            {
                if (!ReferenceEquals(ContentTemplate, Column.HeaderTemplate))
                    ContentTemplate = Column.HeaderTemplate;

                Content = Column.EffectiveHeader;
                applySortIcon(150);
            });
        }

        private void applySortIcon(double duration)
        {
            sortIcon.Icon = Column.SortDirection == ListSortDirection.Descending ? FontAwesome.Solid.CaretDown : FontAwesome.Solid.CaretUp;
            sortIcon.FadeTo(Column.SortDirection != null ? 1 : 0, duration, Easing.OutQuint);
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (owner.CanSort(Column))
                hover.FadeTo(0.06f, 100, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(250, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (owner.CanSort(Column))
                hover.FadeTo(0.15f).FadeTo(0.06f, 300, Easing.OutQuint);

            owner.HeaderClicked?.Invoke(Column);
            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            Column.HeaderChanged -= onHeaderChanged;
            base.Dispose(isDisposing);
        }

        /// <summary>The draggable right edge of a header.</summary>
        private partial class ResizeGrip : CompositeDrawable
        {
            private readonly TableColumnHeader header;
            private readonly Box indicator;
            private float startWidth;

            public ResizeGrip(TableColumnHeader header)
            {
                this.header = header;

                Anchor = Anchor.TopRight;
                Origin = Anchor.TopCentre;
                RelativeSizeAxes = Axes.Y;
                Width = 8;

                InternalChild = indicator = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Colour = Theme.Accent,
                    Alpha = 0,
                };
            }

            private bool canResize => header.Column.CanUserResize && header.owner.CanResize();

            protected override bool OnHover(HoverEvent e)
            {
                if (canResize)
                    indicator.FadeTo(0.8f, 100, Easing.OutQuint);
                return canResize;
            }

            protected override void OnHoverLost(HoverLostEvent e) => indicator.FadeOut(250, Easing.OutQuint);

            protected override bool OnDragStart(DragStartEvent e)
            {
                if (!canResize)
                    return false;

                startWidth = header.Column.ActualWidth;
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                // The header row doesn't move while one of its columns resizes, so measure the drag there.
                var start = header.owner.ToLocalSpace(e.ScreenSpaceMouseDownPosition);
                var now = header.owner.ToLocalSpace(e.ScreenSpaceMousePosition);
                var column = header.Column;

                column.Width = new GridLength(Math.Clamp(startWidth + now.X - start.X, Math.Min(column.MinWidth, column.MaxWidth), column.MaxWidth), GridUnitType.Pixel);
            }

            protected override bool OnClick(ClickEvent e) => canResize;

            protected override bool OnDoubleClick(DoubleClickEvent e)
            {
                if (!canResize)
                    return false;

                header.Column.Width = new GridLength(0, GridUnitType.Auto);
                return true;
            }
        }
    }

    /// <summary>
    /// A sorted, observable view over an items source, so sorting a table never reorders the view model's list.
    /// Without a sort it mirrors the source. Changes to an observable source are applied in place, and re-sorting
    /// moves items rather than resetting, so the table keeps its rows (and their selection and editing state).
    /// </summary>
    internal sealed class SortedItemsView : IEnumerable, INotifyCollectionChanged, IDisposable
    {
        private readonly IEnumerable source;
        private readonly INotifyCollectionChanged observed;
        private readonly List<object> items = new List<object>();
        private Comparison<object> comparison;

        public SortedItemsView(IEnumerable source)
        {
            this.source = source;
            observed = source as INotifyCollectionChanged;

            if (observed != null)
                observed.CollectionChanged += onSourceChanged;

            items.AddRange(source.Cast<object>());
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public IEnumerator GetEnumerator() => items.GetEnumerator();

        /// <summary>Sorts by <paramref name="by"/> (stable, so equal items keep their source order), or restores the source order for null.</summary>
        public void Sort(Comparison<object> by)
        {
            comparison = by;
            var target = ordered(source.Cast<object>());

            for (int i = 0; i < target.Count && i < items.Count; i++)
            {
                object wanted = target[i];
                int from = items.FindIndex(i, x => Equals(x, wanted));

                if (from < 0 || from == i)
                    continue;

                object item = items[from];
                items.RemoveAt(from);
                items.Insert(i, item);
                raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, i, from));
            }
        }

        private List<object> ordered(IEnumerable<object> sequence)
            => comparison == null ? sequence.ToList() : sequence.OrderBy(x => x, Comparer<object>.Create(comparison)).ToList();

        private void onSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                {
                    int index = e.NewStartingIndex;

                    foreach (object item in e.NewItems)
                    {
                        int at = comparison != null ? sortedIndexOf(item) : index < 0 || index > items.Count ? items.Count : index++;
                        items.Insert(at, item);
                        raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, at));
                    }

                    break;
                }

                case NotifyCollectionChangedAction.Remove:
                    foreach (object item in e.OldItems)
                        remove(item, comparison == null ? e.OldStartingIndex : -1);
                    break;

                case NotifyCollectionChangedAction.Replace:
                {
                    foreach (object item in e.OldItems)
                        remove(item, comparison == null ? e.OldStartingIndex : -1);

                    int index = e.NewStartingIndex;

                    foreach (object item in e.NewItems)
                    {
                        int at = comparison != null ? sortedIndexOf(item) : index < 0 || index > items.Count ? items.Count : index++;
                        items.Insert(at, item);
                        raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, at));
                    }

                    break;
                }

                case NotifyCollectionChangedAction.Move when comparison != null:
                    // The sort decides the order; a move in the source changes nothing here.
                    break;

                case NotifyCollectionChangedAction.Move when e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0:
                    for (int i = 0; i < e.OldItems.Count; i++)
                    {
                        int from = e.OldStartingIndex + i, to = e.NewStartingIndex + i;
                        if (from >= items.Count || from == to)
                            continue;

                        object item = items[from];
                        items.RemoveAt(from);
                        to = Math.Clamp(to, 0, items.Count);
                        items.Insert(to, item);
                        raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, to, from));
                    }

                    break;

                default:
                    items.Clear();
                    items.AddRange(ordered(source.Cast<object>()));
                    raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    break;
            }
        }

        private void remove(object item, int hint)
        {
            int index = hint >= 0 && hint < items.Count && Equals(items[hint], item) ? hint : items.FindIndex(x => Equals(x, item));
            if (index < 0)
                return;

            items.RemoveAt(index);
            raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        }

        /// <summary>Where a new item goes: after every item that sorts before or equal to it.</summary>
        private int sortedIndexOf(object item)
        {
            int low = 0, high = items.Count;

            while (low < high)
            {
                int mid = (low + high) / 2;

                if (comparison(items[mid], item) <= 0)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }

        private void raise(NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(this, e);

        public void Dispose()
        {
            if (observed != null)
                observed.CollectionChanged -= onSourceChanged;
        }
    }

    /// <summary>Sorting shared by ListView and DataGrid.</summary>
    internal static class TableSorting
    {
        /// <summary>Compares member values the way a user expects a column to sort: numbers by value, text by culture, nulls first.</summary>
        public static int CompareValues(object a, object b)
        {
            if (a == null || b == null)
                return a == null ? b == null ? 0 : -1 : 1;

            if (a is string sa && b is string sb)
                return string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);

            if (a is IComparable comparable && a.GetType() == b.GetType())
                return comparable.CompareTo(b);

            return string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }

        public static Comparison<object> Comparison(ItemBinding key, ListSortDirection direction)
        {
            int sign = direction == ListSortDirection.Descending ? -1 : 1;
            return (x, y) => sign * CompareValues(key.GetValue(x), key.GetValue(y));
        }

        /// <summary>The next direction for a header click: ascending first, then flipping.</summary>
        public static ListSortDirection Next(ListSortDirection? current)
            => current == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
    }

    /// <summary>Keeps a table's rows sorted by the column whose <see cref="TableColumn.SortDirection"/> is set.</summary>
    internal sealed class TableSorter : IDisposable
    {
        private readonly Func<IEnumerable<TableColumn>> columns;
        private SortedItemsView view;

        public TableSorter(Func<IEnumerable<TableColumn>> columns)
        {
            this.columns = columns;
        }

        public IEnumerable Wrap(IEnumerable source)
        {
            view?.Dispose();
            view = new SortedItemsView(source);
            Apply();
            return view;
        }

        /// <summary>Sorts by <paramref name="column"/> alone (null restores the source order).</summary>
        public void Sort(TableColumn column, ListSortDirection? direction)
        {
            foreach (var other in columns())
            {
                if (!ReferenceEquals(other, column) && other.SortDirection != null)
                    other.SortDirection = null;
            }

            if (column != null)
                column.SortDirection = direction;

            Apply();
        }

        public void Apply()
        {
            var sorted = columns().FirstOrDefault(c => c.SortDirection != null && c.SortKey != null);
            view?.Sort(sorted == null ? null : TableSorting.Comparison(sorted.SortKey, sorted.SortDirection.Value));
        }

        public void Dispose() => view?.Dispose();
    }
}
