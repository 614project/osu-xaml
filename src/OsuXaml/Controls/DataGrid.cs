using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Testing;
using osuTK.Input;
using ListSortDirection = System.ComponentModel.ListSortDirection;

namespace OsuXaml.Controls
{
    /// <summary>Which grid lines a <see cref="DataGrid"/> draws between cells.</summary>
    public enum DataGridGridLinesVisibility
    {
        All,
        Horizontal,
        Vertical,
        None,
    }

    /// <summary>Which headers a <see cref="DataGrid"/> shows. Row headers aren't drawn, so Row is the same as None.</summary>
    public enum DataGridHeadersVisibility
    {
        All,
        Column,
        Row,
        None,
    }

    /// <summary>
    /// A column of a <see cref="DataGrid"/>. Built-in kinds: <see cref="DataGridTextColumn"/>,
    /// <see cref="DataGridCheckBoxColumn"/> and <see cref="DataGridTemplateColumn"/>; derive from this for more.
    /// </summary>
    public abstract class DataGridColumn : TableColumn
    {
        /// <summary>Stops cells in this column from being edited.</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>Whether clicking the header sorts by this column (when the grid's CanUserSortColumns allows it).</summary>
        public bool CanUserSort { get; set; } = true;

        /// <summary>The grid this column belongs to, once added to its Columns.</summary>
        public DataGrid DataGridOwner { get; internal set; }

        /// <summary>Creates the content that shows <paramref name="item"/>. Bindings it makes go to <paramref name="scope"/>.</summary>
        protected internal abstract Drawable GenerateElement(DataGridCell cell, object item, BindingScope scope);

        /// <summary>Creates the editor for <paramref name="item"/>, or null when the cell can't be edited.</summary>
        protected internal virtual Drawable GenerateEditingElement(DataGridCell cell, object item, BindingScope scope) => null;

        /// <summary>Writes the editor's value back to the item. Returning false keeps the cell editing (e.g. text that doesn't parse).</summary>
        protected internal virtual bool CommitCellEdit(Drawable editingElement, object item) => true;
    }

    /// <summary>A column whose cells show one member of each item, named by <see cref="Binding"/>.</summary>
    public abstract class DataGridBoundColumn : DataGridColumn
    {
        /// <summary>
        /// The member each cell shows: <c>Binding="{Binding Name}"</c> compiles to a typed accessor into each item
        /// (no reflection). A Bindable member is edited in place; a plain one is shown read-only.
        /// </summary>
        public ItemBinding Binding { get; set; }

        internal override ItemBinding SortKey => SortMemberPath ?? Binding;

        internal override object EffectiveHeader => Header ?? Binding?.Path;
    }

    /// <summary>
    /// Shows a member as text. Double-click, F2 or <see cref="DataGrid.BeginEdit"/> edits it in a TextBox when the member
    /// is a <c>Bindable&lt;string&gt;</c> (or a numeric bindable): Enter or clicking away commits, Escape cancels.
    /// </summary>
    public class DataGridTextColumn : DataGridBoundColumn
    {
        /// <summary>Text size; 0 keeps the TextBlock default.</summary>
        public float FontSize { get; set; }

        public FontWeight FontWeight { get; set; }

        public Colour4? Foreground { get; set; }

        /// <summary>Formats the value, like StringFormat on a WPF binding: <c>"{}{0:0.00} kg"</c> or a format string such as <c>"C"</c>.</summary>
        public string StringFormat { get; set; }

        protected internal override Drawable GenerateElement(DataGridCell cell, object item, BindingScope scope)
        {
            var text = new TextBlock();

            if (FontSize > 0)
                text.FontSize = FontSize;
            if (FontWeight != FontWeight.Regular)
                text.FontWeight = FontWeight;
            if (Foreground is Colour4 colour)
                text.Colour = colour;

            if (Binding == null)
                text.Text = item?.ToString() ?? string.Empty;
            else
                Binding.Observe(item, scope, value => text.Text = format(value));

            return text;
        }

        protected internal override Drawable GenerateEditingElement(DataGridCell cell, object item, BindingScope scope)
        {
            var writer = TextEditing.Writer(Binding?.GetSource(item), out string initial);
            return writer == null ? null : new DataGridTextEditor(cell, initial, writer);
        }

        protected internal override bool CommitCellEdit(Drawable editingElement, object item)
            => !(editingElement is DataGridTextEditor editor) || editor.Write();

        private string format(object value)
        {
            if (value == null)
                return string.Empty;

            if (string.IsNullOrEmpty(StringFormat))
                return value.ToString();

            if (StringFormat.Contains('{'))
                return string.Format(CultureInfo.CurrentCulture, StringFormat, value);

            return value is IFormattable formattable ? formattable.ToString(StringFormat, CultureInfo.CurrentCulture) : value.ToString();
        }
    }

    /// <summary>
    /// Shows a <c>Bindable&lt;bool&gt;</c> member as a CheckBox bound two-way; one click toggles it.
    /// A plain bool member, a read-only column or a read-only grid shows the value without letting it change.
    /// </summary>
    public class DataGridCheckBoxColumn : DataGridBoundColumn
    {
        protected internal override Drawable GenerateElement(DataGridCell cell, object item, BindingScope scope)
        {
            var checkBox = new DataGridCheckBox(cell);

            if (Binding?.GetSource(item) is Bindable<bool> bindable)
                checkBox.Current.BindTo(bindable);
            else
            {
                checkBox.AlwaysReadOnly = true;
                Binding?.Observe(item, scope, value => checkBox.Current.Value = value is bool b && b);
            }

            return checkBox;
        }
    }

    /// <summary>
    /// Cells built from <see cref="CellTemplate"/>, and while editing from <see cref="CellEditingTemplate"/>.
    /// Templates bind to the item like any DataTemplate, so two-way bindings inside them edit the view model directly.
    /// </summary>
    public class DataGridTemplateColumn : DataGridColumn
    {
        public DataTemplate CellTemplate { get; set; }

        /// <summary>Shown while the cell is edited (double-click or F2). Without it the cell isn't editable.</summary>
        public DataTemplate CellEditingTemplate { get; set; }

        protected internal override Drawable GenerateElement(DataGridCell cell, object item, BindingScope scope)
            => CellTemplate != null && CellTemplate.CanBuild(item) ? CellTemplate.Build(item, scope) : new TextBlock { Text = item?.ToString() ?? string.Empty };

        protected internal override Drawable GenerateEditingElement(DataGridCell cell, object item, BindingScope scope)
            => CellEditingTemplate != null && CellEditingTemplate.CanBuild(item) ? CellEditingTemplate.Build(item, scope) : null;
    }

    /// <summary>
    /// A table of items with typed columns, like WPF's DataGrid: <c>&lt;DataGrid ItemsSource="{Bind People}"&gt;&lt;DataGrid.Columns&gt;
    /// &lt;DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="*" /&gt;</c>. It has a header row that stays above the
    /// scrolling rows, grid lines, alternating row backgrounds, ListBox selection (SelectedItem, SelectedIndex, SelectionMode,
    /// keyboard navigation), in-place editing, column resizing by dragging header edges, and sorting by clicking a header
    /// (the view model's list keeps its order). Columns are always declared: AutoGenerateColumns is accepted but
    /// does nothing, because generating columns would need reflection.
    /// </summary>
    public partial class DataGrid : ListBox
    {
        private readonly TableSorter sorter;

        private ColumnLayout layout;
        private ColumnHeadersPresenter headers;
        private DataGridCell editingCell;
        private bool widthFromContent;

        private Colour4 rowBackground;
        private bool hasRowBackground;
        private Colour4 alternatingRowBackground;
        private bool hasAlternatingRowBackground;
        private DataGridGridLinesVisibility gridLinesVisibility = DataGridGridLinesVisibility.All;
        private Colour4 horizontalGridLinesBrush = Theme.Border;
        private Colour4 verticalGridLinesBrush = Theme.Border;
        private DataGridHeadersVisibility headersVisibility = DataGridHeadersVisibility.All;
        private float rowHeight;

        public DataGrid()
        {
            sorter = new TableSorter(() => Columns);
            Columns.CollectionChanged += onColumnsChanged;

            Background = Theme.Mantle;
            BorderBrush = Theme.Border;
            BorderThickness = 1;
            CornerRadius = 6;
            Padding = default;
        }

        /// <summary>The columns, in display order: <c>&lt;DataGrid.Columns&gt;</c> in XAML.</summary>
        public ObservableCollection<DataGridColumn> Columns { get; } = new ObservableCollection<DataGridColumn>();

        /// <summary>
        /// Accepted so WPF markup compiles, but columns are never generated: that would need reflection over the item type.
        /// Declare the columns in <see cref="Columns"/>.
        /// </summary>
        public bool AutoGenerateColumns { get; set; }

        /// <summary>Stops every cell from being edited. Check boxes keep showing their value.</summary>
        public bool IsReadOnly { get; set; }

        public bool CanUserSortColumns { get; set; } = true;

        public bool CanUserResizeColumns { get; set; } = true;

        /// <summary>Background of every row (even rows, when <see cref="AlternatingRowBackground"/> is set).</summary>
        public Colour4 RowBackground
        {
            get => rowBackground;
            set
            {
                rowBackground = value;
                hasRowBackground = true;
                updateRows();
            }
        }

        /// <summary>Background of every other row, starting with the second.</summary>
        public Colour4 AlternatingRowBackground
        {
            get => alternatingRowBackground;
            set
            {
                alternatingRowBackground = value;
                hasAlternatingRowBackground = true;
                updateRows();
            }
        }

        public DataGridGridLinesVisibility GridLinesVisibility
        {
            get => gridLinesVisibility;
            set
            {
                gridLinesVisibility = value;
                updateRows();
            }
        }

        public Colour4 HorizontalGridLinesBrush
        {
            get => horizontalGridLinesBrush;
            set
            {
                horizontalGridLinesBrush = value;
                updateRows();
            }
        }

        public Colour4 VerticalGridLinesBrush
        {
            get => verticalGridLinesBrush;
            set
            {
                verticalGridLinesBrush = value;
                updateRows();
            }
        }

        /// <summary>Column headers show for All and Column.</summary>
        public DataGridHeadersVisibility HeadersVisibility
        {
            get => headersVisibility;
            set
            {
                headersVisibility = value;
                updateHeaders();
            }
        }

        /// <summary>A fixed height for every row, or 0 (the default) to fit the content.</summary>
        public float RowHeight
        {
            get => rowHeight;
            set
            {
                rowHeight = value;
                updateRows();
            }
        }

        /// <summary>The header row, once loaded.</summary>
        public ColumnHeadersPresenter ColumnHeaders => headers;

        /// <summary>The cell being edited, or null.</summary>
        public DataGridCell EditingCell => editingCell;

        /// <summary>Raised when a header click is about to sort by a column.</summary>
        public event Action<DataGridColumn> Sorting;

        /// <summary>Raised after an edit is committed, with the cell that was edited.</summary>
        public event Action<DataGridCell> CellEditEnding;

        /// <summary>Sorts the rows by <paramref name="column"/>, or restores the source order for null.</summary>
        public void Sort(DataGridColumn column, ListSortDirection? direction = ListSortDirection.Ascending) => sorter.Sort(column, direction);

        public DataGridRow RowFromItem(object item) => ContainerFromItem(item) as DataGridRow;

        /// <summary>The cell showing <paramref name="column"/> of <paramref name="item"/>.</summary>
        public DataGridCell CellFor(object item, DataGridColumn column)
            => RowFromItem(item)?.CellsPresenter?.CellAt(Columns.IndexOf(column)) as DataGridCell;

        /// <summary>Edits the first editable cell of the selected row, as F2 does.</summary>
        public bool BeginEdit()
        {
            var row = RowFromItem(SelectedItem.Value);
            return row?.CellsPresenter != null && row.CellsPresenter.DataGridCells.Any(c => c.BeginEdit());
        }

        /// <summary>Commits the cell being edited. Returns false when its value couldn't be written (the cell keeps editing).</summary>
        public bool CommitEdit() => editingCell?.CommitEdit() ?? true;

        public void CancelEdit() => editingCell?.CancelEdit();

        protected override IEnumerable CreateItemsView(IEnumerable source) => sorter.Wrap(source);

        protected override Drawable CreateItemsHost(Panel panel)
        {
            var host = base.CreateItemsHost(panel);

            layout = new ColumnLayout(Columns);
            headers = new ColumnHeadersPresenter
            {
                ColumnLayout = layout,
                HeaderClicked = onHeaderClicked,
                CanSort = canSort,
                CanResize = () => CanUserResizeColumns,
            };

            if (host is ContentControl chrome)
            {
                var scroll = chrome.Child;
                var dock = new DockPanel();
                dock.Add(headers);
                DockPanel.SetDock(headers, Dock.Top);
                dock.Add(scroll);
                chrome.Child = dock;
            }

            updateHeaders();

            // Runs before the first rows are created, so a column declared with SortDirection starts sorted.
            sorter.Apply();
            return host;
        }

        protected override bool IsItemItsOwnContainer(object item) => item is DataGridRow;

        protected override Drawable CreateContainer() => new DataGridRow();

        protected override void PrepareContainer(Drawable container, object item)
        {
            if (!(container is DataGridRow row) || ReferenceEquals(container, item))
            {
                base.PrepareContainer(container, item);
                return;
            }

            row.Owner = this;
            row.PresentRow(layout, item);
            row.IsSelected = IsSelected(item);
        }

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();
            updateRows();
        }

        protected override void OnSelectionChanged()
        {
            base.OnSelectionChanged();

            // Moving to another row finishes the edit, as in WPF.
            if (editingCell != null && !editingCell.Row.IsSelected && !editingCell.CommitEdit())
                editingCell.CancelEdit();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            widthFromContent = !RelativeSizeAxes.HasFlag(Axes.X) && Width == 0;
        }

        protected override void Update()
        {
            base.Update();

            // Before the rows update, so they arrange their cells with this frame's widths.
            bool unbounded = widthFromContent && !RelativeSizeAxes.HasFlag(Axes.X);
            layout?.Solve(unbounded ? float.PositiveInfinity : Panel.DrawWidth, desiredWidth);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (HasFocus && e.Key == Key.F2 && !e.Repeat && BeginEdit())
                return true;

            return base.OnKeyDown(e);
        }

        internal void CellEditBegun(DataGridCell cell)
        {
            if (editingCell != null && editingCell != cell && !editingCell.CommitEdit())
                editingCell.CancelEdit();

            editingCell = cell;
        }

        internal void CellEditEnded(DataGridCell cell, bool committed)
        {
            if (editingCell == cell)
                editingCell = null;

            // Enter and Escape leave the editor with nothing focused; the grid takes focus back so F2 and arrows keep working.
            if (GetContainingInputManager()?.FocusedDrawable == null)
                GetContainingFocusManager()?.ChangeFocus(this);

            if (committed)
                CellEditEnding?.Invoke(cell);
        }

        private float desiredWidth(int column)
        {
            float max = headers.DesiredWidth(column);

            foreach (var container in Containers)
            {
                if (container is DataGridRow { CellsPresenter: { } row })
                    max = Math.Max(max, row.DesiredWidth(column));
            }

            return max;
        }

        private bool canSort(TableColumn column)
            => CanUserSortColumns && column.SortKey != null && (!(column is DataGridColumn dataGridColumn) || dataGridColumn.CanUserSort);

        private void onHeaderClicked(TableColumn column)
        {
            if (!canSort(column))
                return;

            Sorting?.Invoke((DataGridColumn)column);
            sorter.Sort(column, TableSorting.Next(column.SortDirection));
        }

        private void onColumnsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (DataGridColumn column in e.OldItems)
                    column.DataGridOwner = null;
            }

            foreach (var column in Columns)
                column.DataGridOwner = this;

            if (layout == null)
                return;

            headers.RebuildCells();

            foreach (var container in Containers)
                (container as DataGridRow)?.CellsPresenter?.RebuildCells();

            sorter.Apply();
        }

        private void updateHeaders()
        {
            if (headers == null)
                return;

            bool visible = headersVisibility == DataGridHeadersVisibility.All || headersVisibility == DataGridHeadersVisibility.Column;
            Layout.SetVisibility(headers, visible ? Visibility.Visible : Visibility.Collapsed);
            headers.LineColour = verticalGridLinesBrush;
        }

        private void updateRows()
        {
            bool horizontal = gridLinesVisibility == DataGridGridLinesVisibility.All || gridLinesVisibility == DataGridGridLinesVisibility.Horizontal;
            bool vertical = gridLinesVisibility == DataGridGridLinesVisibility.All || gridLinesVisibility == DataGridGridLinesVisibility.Vertical;

            for (int i = 0; i < Containers.Count; i++)
            {
                if (!(Containers[i] is DataGridRow row))
                    continue;

                row.AlternationIndex = i % 2;

                if (row.AlternationIndex == 1 && hasAlternatingRowBackground)
                    row.Background = alternatingRowBackground;
                else if (hasRowBackground)
                    row.Background = rowBackground;
                else
                    row.Background = Colour4.Transparent;

                row.SetHorizontalLine(horizontal, horizontalGridLinesBrush);

                if (row.CellsPresenter != null)
                {
                    row.CellsPresenter.ShowVerticalLines = vertical;
                    row.CellsPresenter.LineColour = verticalGridLinesBrush;
                    row.CellsPresenter.RowHeight = rowHeight;
                }
            }

            updateHeaders();
        }

        protected override void Dispose(bool isDisposing)
        {
            sorter.Dispose();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>One row of a <see cref="DataGrid"/>: its cells, a background for alternation, a grid line, and ListBox hover and selection.</summary>
    public partial class DataGridRow : ListBoxItem
    {
        private readonly Box gridLine;

        public DataGridRow()
        {
            CornerRadius = 0;
            Padding = default;

            // Behind the selection highlight (depth 1), so alternating backgrounds don't hide it.
            Chrome.ChangeChildDepth(BackgroundBox, 2);

            Chrome.Add(gridLine = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Colour = Theme.Border,
                Depth = -1,
            });
        }

        /// <summary>The cells of this row.</summary>
        public DataGridCellsPresenter CellsPresenter { get; private set; }

        /// <summary>The data item this row shows.</summary>
        public object Item => CellsPresenter?.Item;

        public DataGrid DataGridOwner => Owner as DataGrid;

        internal void PresentRow(ColumnLayout layout, object item)
        {
            if (CellsPresenter != null && ReferenceEquals(CellsPresenter.Item, item))
                return;

            var old = CellsPresenter;

            ContentTemplate = null;
            Content = CellsPresenter = new DataGridCellsPresenter(this, item) { ColumnLayout = layout };

            // Drawable content belongs to whoever set it, so the replaced cells are ours to dispose.
            old?.Dispose();
        }

        /// <summary>0 for even rows and 1 for odd ones, which get the grid's AlternatingRowBackground.</summary>
        public int AlternationIndex { get; internal set; }

        internal void SetHorizontalLine(bool visible, Colour4 colour)
        {
            gridLine.Alpha = visible ? 1 : 0;
            gridLine.Colour = colour;
        }

        // Rows select on press, so a click that a cell's check box or button consumes still selects its row.
        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Left)
                Owner?.SelectContainer(this, e);

            return false;
        }

        protected override bool OnClick(ClickEvent e) => true;
    }

    /// <summary>The cells of one <see cref="DataGridRow"/>, laid out in the grid's shared column widths.</summary>
    public partial class DataGridCellsPresenter : TableRowPresenter
    {
        internal DataGridCellsPresenter(DataGridRow row, object item)
        {
            Row = row;
            Item = item;
        }

        public DataGridRow Row { get; }

        public object Item { get; }

        public IEnumerable<DataGridCell> DataGridCells => Cells.OfType<DataGridCell>();

        protected override Drawable CreateCell(TableColumn column) => new DataGridCell((DataGridColumn)column, Item, Row);
    }

    /// <summary>
    /// One cell of a <see cref="DataGrid"/>: shows its column's element for the item, and swaps in the column's editor
    /// while editing (double-click, F2, or <see cref="BeginEdit"/>).
    /// </summary>
    public partial class DataGridCell : ContentControl
    {
        // Fields, so osu!framework's disposal also unbinds them.
        private readonly BindingScope displayBindings = new BindingScope();
        private BindingScope editingBindings;

        private readonly Drawable display;
        private Drawable editor;

        internal DataGridCell(DataGridColumn column, object item, DataGridRow row)
        {
            Column = column;
            Item = item;
            Row = row;

            Padding = new MarginPadding { Horizontal = 8, Vertical = 4 };
            VerticalContentAlignment = VerticalAlignment.Center;

            // The row clips cells that overflow their column; masking every cell would only cost draw batching.
            Chrome.Masking = false;

            Content = display = column.GenerateElement(this, item, displayBindings);
        }

        public DataGridColumn Column { get; }

        public object Item { get; }

        public DataGridRow Row { get; }

        public DataGrid DataGridOwner => Row?.DataGridOwner;

        public bool IsEditing => editor != null;

        /// <summary>Whether the column or the whole grid is read-only.</summary>
        public bool IsReadOnly => Column.IsReadOnly || DataGridOwner?.IsReadOnly == true;

        /// <summary>Shows the column's editor. Returns false when the cell is read-only or its column has no editor for the item.</summary>
        public bool BeginEdit()
        {
            if (IsEditing)
                return true;

            if (IsReadOnly)
                return false;

            var scope = new BindingScope();
            var element = Column.GenerateEditingElement(this, Item, scope);

            if (element == null)
                return false;

            DataGridOwner?.CellEditBegun(this);

            editingBindings = scope;
            editor = element;
            Content = editor;

            focusAttempts = 0;
            Schedule(focusEditor);
            return true;
        }

        /// <summary>Writes the edit back and shows the value again. Returns false (and keeps editing) when the value can't be written.</summary>
        public bool CommitEdit()
        {
            if (!IsEditing)
                return true;

            if (!Column.CommitCellEdit(editor, Item))
                return false;

            endEdit(true);
            return true;
        }

        /// <summary>Throws the edit away. Template editors that bind two-way have already written their changes.</summary>
        public void CancelEdit()
        {
            if (IsEditing)
                endEdit(false);
        }

        /// <summary>Called by editors when they finish (Enter, focus loss, Escape); runs next frame, outside the editor's own input handling.</summary>
        internal void RequestEndEdit(bool commit) => Schedule(() =>
        {
            if (!commit)
                CancelEdit();
            else if (!CommitEdit())
                CancelEdit();
        });

        // The framework sends a double-click only to whatever took the first click, so the cell takes clicks rather than the row.
        protected override bool OnClick(ClickEvent e) => true;

        protected override bool OnDoubleClick(DoubleClickEvent e) => BeginEdit();

        private int focusAttempts;

        private void focusEditor()
        {
            if (editor == null)
                return;

            var target = editor.AcceptsFocus ? editor : editor.ChildrenOfType<Drawable>().FirstOrDefault(d => d.AcceptsFocus);

            // A freshly added editor only becomes focusable once its parent has made it alive, a frame or two later.
            if (target != null && GetContainingFocusManager()?.ChangeFocus(target) == false && ++focusAttempts < 10)
                Schedule(focusEditor);
        }

        private void endEdit(bool committed)
        {
            var old = editor;
            editor = null;

            // The display element is kept while editing (its bindings stay live), so it just comes back.
            Content = display;

            editingBindings?.UnbindAll();
            editingBindings = null;
            old.Dispose();

            DataGridOwner?.CellEditEnded(this, committed);
        }

        protected override void Dispose(bool isDisposing)
        {
            // While editing, the display element is detached from the tree, so nothing else would dispose it.
            if (editor != null)
                display.Dispose();

            editingBindings?.UnbindAll();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>A check box in a grid cell; it ignores clicks while its cell is read-only, so the click selects the row instead.</summary>
    internal partial class DataGridCheckBox : CheckBox
    {
        private readonly DataGridCell cell;

        public DataGridCheckBox(DataGridCell cell)
        {
            this.cell = cell;
        }

        /// <summary>Set for members that aren't bindables, which can't be written back.</summary>
        public bool AlwaysReadOnly { get; set; }

        protected override bool OnClick(ClickEvent e) => !AlwaysReadOnly && !cell.IsReadOnly && base.OnClick(e);
    }

    /// <summary>The TextBox a <see cref="DataGridTextColumn"/> cell edits in.</summary>
    internal partial class DataGridTextEditor : TextBox
    {
        private readonly DataGridCell cell;
        private readonly Func<string, bool> write;
        private bool cancelled;

        public DataGridTextEditor(DataGridCell cell, string initial, Func<string, bool> write)
        {
            this.cell = cell;
            this.write = write;

            Height = 28;
            CornerRadius = 4;
            CommitOnFocusLost = true;

            // Line the text up with the read-only text, which has no inner padding.
            Margin = new MarginPadding { Horizontal = -5 };

            Text.Value = initial ?? string.Empty;
            OnCommit += (_, _) => cell.RequestEndEdit(!cancelled);
        }

        public bool Write() => write(Text.Value);

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // TextBox treats Escape as "leave", which commits on focus loss; here it means "throw the edit away".
            if (e.Key == Key.Escape && !e.Repeat)
                cancelled = true;

            return base.OnKeyDown(e);
        }
    }

    /// <summary>Reads and writes bindable members as text, for editing in a TextBox.</summary>
    internal static class TextEditing
    {
        /// <summary>
        /// A writer that parses text into <paramref name="source"/>, or null when it can't be edited as text
        /// (not a bindable of string or a number type, or disabled). Writers return false when the text doesn't parse.
        /// </summary>
        public static Func<string, bool> Writer(object source, out string initial)
        {
            var culture = CultureInfo.CurrentCulture;
            initial = null;

            if (source is IBindable { Disabled: true })
                return null;

            switch (source)
            {
                case Bindable<string> s:
                    initial = s.Value;
                    return text => set(s, text);

                case Bindable<int> i:
                    initial = i.Value.ToString(culture);
                    return text => int.TryParse(text, NumberStyles.Integer, culture, out int v) && set(i, v);

                case Bindable<long> l:
                    initial = l.Value.ToString(culture);
                    return text => long.TryParse(text, NumberStyles.Integer, culture, out long v) && set(l, v);

                case Bindable<float> f:
                    initial = f.Value.ToString(culture);
                    return text => float.TryParse(text, NumberStyles.Float, culture, out float v) && set(f, v);

                case Bindable<double> d:
                    initial = d.Value.ToString(culture);
                    return text => double.TryParse(text, NumberStyles.Float, culture, out double v) && set(d, v);

                case Bindable<decimal> m:
                    initial = m.Value.ToString(culture);
                    return text => decimal.TryParse(text, NumberStyles.Number, culture, out decimal v) && set(m, v);

                default:
                    return null;
            }
        }

        private static bool set<T>(Bindable<T> bindable, T value)
        {
            if (bindable.Disabled)
                return false;

            bindable.Value = value;
            return true;
        }
    }
}
