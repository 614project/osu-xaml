using System.ComponentModel;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;
using Person = OsuXaml.Tests.ListsTestData.Person;

namespace OsuXaml.Tests
{
    /// <summary>DataGrid written the way a WPF developer would, checked through the generator.</summary>
    [TestFixture]
    public class DataGridGeneratorTests
    {
        [Test]
        public void ColumnsOfEveryKind()
        {
            string source = ListsTestData.Compiles(@"
  <DataGrid ItemsSource=""{Binding People}"" SelectedItem=""{Binding Selected}"" SelectedIndex=""{Binding SelectedIndex}""
            AutoGenerateColumns=""False"" AlternatingRowBackground=""#232334"" RowBackground=""#1e1e2e"" GridLinesVisibility=""Horizontal""
            HeadersVisibility=""Column"" CanUserSortColumns=""True"" CanUserResizeColumns=""False"" IsReadOnly=""{Bind ReadOnly}""
            SelectionMode=""Extended"" Sorting=""OnSorting"">
    <DataGrid.Columns>
      <DataGridTextColumn Header=""Name"" Binding=""{Binding Name}"" Width=""*"" />
      <DataGridTextColumn Header=""Age"" Binding=""{Binding Age, Mode=TwoWay}"" Width=""80"" SortDirection=""Descending"" StringFormat=""{}{0} yrs"" />
      <DataGridTextColumn Header=""Email"" Binding=""Email"" IsReadOnly=""True"" FontSize=""13"" Foreground=""#a6adc8"" />
      <DataGridCheckBoxColumn Header=""Active"" Binding=""{Binding Active}"" Width=""Auto"" />
      <DataGridTemplateColumn Header=""Actions"" SortMemberPath=""Age"" CanUserSort=""False"">
        <DataGridTemplateColumn.CellTemplate>
          <DataTemplate>
            <Button Content=""Remove"" Click=""{Binding Remove, Source=Root}"" />
          </DataTemplate>
        </DataGridTemplateColumn.CellTemplate>
        <DataGridTemplateColumn.CellEditingTemplate>
          <DataTemplate>
            <TextBox Text=""{Binding Name}"" />
          </DataTemplate>
        </DataGridTemplateColumn.CellEditingTemplate>
      </DataGridTemplateColumn>
    </DataGrid.Columns>
  </DataGrid>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.DataGridTextColumn()"));
            Assert.That(source, Does.Contain(".Columns.Add(e"), "columns fill the Columns collection");
            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Person, string>(\"Name\""));
            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Person, bool>(\"Active\""));
            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Person, string>(\"Email\""), "a plain path works too");
            Assert.That(source, Does.Contain(".CellTemplate = t"));
            Assert.That(source, Does.Contain(".CellEditingTemplate = t"));
            Assert.That(source, Does.Contain("StringFormat = \"{0} yrs\""), "the {} escape is stripped");
            Assert.That(source, Does.Contain("XamlBinding.TwoWayObject(this.xamlBindings, e0.SelectedItem, dataContext.Selected)"));
            Assert.That(source, Does.Contain("XamlBinding.OneWay(this.xamlBindings, dataContext.ReadOnly"), "IsReadOnly binds one-way");
        }

        [Test]
        public void HeaderAsElementAndBoundHeader()
        {
            ListsTestData.Compiles(@"
  <DataGrid ItemsSource=""{Bind People}"">
    <DataGrid.Columns>
      <DataGridTextColumn Binding=""{Bind Name}"" Header=""{Bind Title}"" MinWidth=""60"" MaxWidth=""300"" />
      <DataGridCheckBoxColumn Binding=""{Bind Active}"">
        <DataGridCheckBoxColumn.Header>
          <TextBlock Text=""On"" FontWeight=""Bold"" />
        </DataGridCheckBoxColumn.Header>
      </DataGridCheckBoxColumn>
    </DataGrid.Columns>
  </DataGrid>");
        }

        [Test]
        public void BindingWithoutItemsSourceIsReported()
        {
            var diagnostic = GeneratorRunner.SingleError(ListsTestData.CODE_BEHIND, ListsTestData.Page(@"
  <DataGrid>
    <DataGrid.Columns>
      <DataGridTextColumn Binding=""{Bind Name}"" />
    </DataGrid.Columns>
  </DataGrid>"));

            Assert.That(diagnostic.Id, Is.EqualTo("OX0009"));
        }
    }

    /// <summary>DataGrid at runtime: columns, editing, check boxes, sorting, alternation and selection.</summary>
    [HeadlessTest]
    public partial class DataGridTests : TestScene
    {
        private DataGridTextColumn nameColumn, ageColumn, emailColumn;
        private DataGridCheckBoxColumn activeColumn;
        private DataGridTemplateColumn templateColumn;

        private DataGrid createGrid(BindableList<Person> people, BindingScope scope)
        {
            var grid = new DataGrid
            {
                Size = new Vector2(500, 250),
                ItemsSource = XamlBinding.ListCopy(scope, people),
                AlternatingRowBackground = Colour4.FromHex("232334"),
            };

            grid.Columns.Add(nameColumn = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding, Width = GridLength.Parse("*") });
            grid.Columns.Add(ageColumn = new DataGridTextColumn { Binding = ListsTestData.AgeBinding, Width = GridLength.Parse("70") });
            grid.Columns.Add(emailColumn = new DataGridTextColumn { Header = "Email", Binding = ListsTestData.EmailBinding });
            grid.Columns.Add(activeColumn = new DataGridCheckBoxColumn { Header = "Active", Binding = ListsTestData.ActiveBinding });
            grid.Columns.Add(templateColumn = new DataGridTemplateColumn
            {
                Header = "Badge",
                CellTemplate = new DataTemplate<Person>((p, s) =>
                {
                    var text = new TextBlock();
                    XamlBinding.OneWay(s, p.Name, (v, _) => text.Text = "[" + v + "]");
                    return text;
                }),
                CellEditingTemplate = new DataTemplate<Person>((p, s) =>
                {
                    var box = new TextBox();
                    XamlBinding.TwoWay(box.Text, p.Name);
                    return box;
                }),
            });

            return grid;
        }

        private static string cellText(DataGrid grid, Person person, DataGridColumn column) => ListsTestData.TextOf(grid.CellFor(person, column));

        [Test]
        public void ColumnsShowAndFollowTheItems()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;

            AddStep("create", () => Child = grid = createGrid(people, scope));
            AddUntilStep("laid out", () => nameColumn.ActualWidth > 0);

            AddAssert("text cells", () => cellText(grid, people[1], nameColumn) == "Ann" && cellText(grid, people[1], ageColumn) == "29");
            AddAssert("template cell", () => cellText(grid, people[1], templateColumn) == "[Ann]");
            AddAssert("header falls back to the bound member", () => ListsTestData.TextOf(grid.ColumnHeaders.Headers.ElementAt(1)) == "Age");
            AddAssert("pixel and star widths", () => ageColumn.ActualWidth == 70
                                                     && ListsTestData.Near(nameColumn.ActualWidth, grid.DrawWidth - 2 - 70 - emailColumn.ActualWidth - activeColumn.ActualWidth - templateColumn.ActualWidth));

            AddStep("view model changes", () =>
            {
                people[1].Name.Value = "Anna";
                people[1].Age.Value = 30;
            });
            AddAssert("cells follow", () => cellText(grid, people[1], nameColumn) == "Anna" && cellText(grid, people[1], ageColumn) == "30"
                                            && cellText(grid, people[1], templateColumn) == "[Anna]");

            AddStep("add and remove rows", () =>
            {
                people.RemoveAt(0);
                people.Add(new Person("Eve", 22));
            });
            AddAssert("rows follow", () => grid.Items.Count == 3 && cellText(grid, people[2], nameColumn) == "Eve");
            AddAssert("rows alternate", () => Enumerable.Range(0, 3).All(i => ((DataGridRow)grid.ContainerFromIndex(i)).AlternationIndex == i % 2)
                                              && ((DataGridRow)grid.ContainerFromIndex(1)).Background == Colour4.FromHex("232334")
                                              && ((DataGridRow)grid.ContainerFromIndex(2)).Background == Colour4.Transparent);
        }

        [Test]
        public void CheckBoxColumnBindsTwoWay()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;

            CheckBox checkBox(int i) => (CheckBox)grid.CellFor(people[i], activeColumn).PresentedContent;

            AddStep("create", () => Child = grid = createGrid(people, scope));
            AddAssert("shows values", () => checkBox(0).IsChecked.Value == true && checkBox(1).IsChecked.Value == false);

            AddStep("click Ann's check box", () => checkBox(1).TriggerClick());
            AddAssert("view model follows", () => people[1].Active.Value);

            AddStep("view model clears Cid", () => people[0].Active.Value = false);
            AddAssert("check box follows", () => checkBox(0).IsChecked.Value == false);

            AddStep("make grid read-only and click", () =>
            {
                grid.IsReadOnly = true;
                checkBox(1).TriggerClick();
            });
            AddAssert("value kept", () => people[1].Active.Value);
        }

        [Test]
        public void TextCellsEditAndCommitOrCancel()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridCell cell = null;
            DataGridCell edited = null;

            TextBox editor() => cell.PresentedContent as TextBox;

            AddStep("create", () =>
            {
                Child = grid = createGrid(people, scope);
                grid.CellEditEnding += c => edited = c;
            });

            AddStep("begin editing Ann's name", () =>
            {
                cell = grid.CellFor(people[1], nameColumn);
                cell.BeginEdit();
            });
            AddAssert("editor shows the value", () => cell.IsEditing && grid.EditingCell == cell && editor()?.Text.Value == "Ann");
            AddUntilStep("editor has focus", () => editor()?.HasFocus == true);

            AddStep("type and commit", () =>
            {
                editor().Text.Value = "Annie";
                cell.CommitEdit();
            });
            AddAssert("written back", () => people[1].Name.Value == "Annie" && !cell.IsEditing && cellText(grid, people[1], nameColumn) == "Annie");
            AddAssert("edit ending raised", () => edited == cell && grid.EditingCell == null);

            AddStep("edit and cancel", () =>
            {
                cell.BeginEdit();
                editor().Text.Value = "nope";
                cell.CancelEdit();
            });
            AddAssert("unchanged", () => people[1].Name.Value == "Annie" && cellText(grid, people[1], nameColumn) == "Annie");

            AddStep("edit age with bad text", () =>
            {
                cell = grid.CellFor(people[1], ageColumn);
                cell.BeginEdit();
                editor().Text.Value = "old";
            });
            AddAssert("commit refused, still editing", () => !cell.CommitEdit() && cell.IsEditing && people[1].Age.Value == 29);
            AddStep("fix and commit", () =>
            {
                editor().Text.Value = "31";
                cell.CommitEdit();
            });
            AddAssert("number written", () => people[1].Age.Value == 31 && cellText(grid, people[1], ageColumn) == "31");

            AddAssert("plain members can't be edited", () => !grid.CellFor(people[1], emailColumn).BeginEdit());
            AddStep("read-only column", () => nameColumn.IsReadOnly = true);
            AddAssert("can't edit", () => !grid.CellFor(people[1], nameColumn).BeginEdit());

            AddStep("template column edits through its template", () =>
            {
                cell = grid.CellFor(people[2], templateColumn);
                cell.BeginEdit();
                editor().Text.Value = "Bobby";
            });
            AddAssert("template editor bound two-way", () => people[2].Name.Value == "Bobby");

            AddStep("editing another cell commits the first", () => grid.CellFor(people[0], ageColumn).BeginEdit());
            AddAssert("only one cell edits", () => !cell.IsEditing && grid.EditingCell == grid.CellFor(people[0], ageColumn));

            AddStep("select another row", () => grid.SelectedItem.Value = people[2]);
            AddAssert("edit finished", () => grid.EditingCell == null);
        }

        [Test]
        public void HeaderClickSortsAndKeepsSelection()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridColumn sorting = null;

            string order() => string.Join(",", grid.Items.Cast<Person>().Select(p => p.Name.Value));

            AddStep("create", () =>
            {
                Child = grid = createGrid(people, scope);
                grid.Sorting += c => sorting = c;
                grid.SelectedItem.Value = people[2];
            });

            AddStep("click Age header", () => grid.ColumnHeaders.Headers.ElementAt(1).TriggerClick());
            AddAssert("sorted by age", () => order() == "Ann,Bob,Cid" && sorting == ageColumn && ageColumn.SortDirection == ListSortDirection.Ascending);
            AddAssert("selection follows the item", () => grid.SelectedItem.Value == people[2] && grid.SelectedIndex.Value == 1
                                                          && ((DataGridRow)grid.ContainerFromIndex(1)).IsSelected);
            AddAssert("source untouched", () => string.Join(",", people.Select(p => p.Name.Value)) == "Cid,Ann,Bob");
            AddAssert("alternation follows the new order", () => ((DataGridRow)grid.RowFromItem(people[2])).AlternationIndex == 1);

            AddStep("click again", () => grid.ColumnHeaders.Headers.ElementAt(1).TriggerClick());
            AddAssert("descending", () => order() == "Cid,Bob,Ann");

            AddStep("age changes, then insert", () => people.Add(new Person("Dee", 38)));
            AddAssert("new row in place", () => order() == "Cid,Dee,Bob,Ann");

            AddStep("check box column sorts by its binding", () => grid.ColumnHeaders.Headers.ElementAt(3).TriggerClick());
            AddAssert("inactive first", () => grid.Items.Cast<Person>().Select(p => p.Active.Value).SequenceEqual(new[] { false, false, true, true }));

            AddStep("template column without a sort member", () => grid.ColumnHeaders.Headers.ElementAt(4).TriggerClick());
            AddAssert("not sorted by it", () => templateColumn.SortDirection == null && activeColumn.SortDirection == ListSortDirection.Ascending);

            AddStep("sorting turned off", () =>
            {
                grid.CanUserSortColumns = false;
                grid.ColumnHeaders.Headers.First().TriggerClick();
            });
            AddAssert("ignored", () => nameColumn.SortDirection == null);
        }

        [Test]
        public void GridLinesAndHeadersCanBeHidden()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            float firstRowTop = 0;

            AddStep("create", () => Child = grid = createGrid(people, scope));
            AddUntilStep("laid out", () => grid.ColumnHeaders.DrawHeight > 0);
            AddStep("remember row position", () => firstRowTop = grid.ContainerFromIndex(0).ScreenSpaceDrawQuad.TopLeft.Y);

            AddStep("hide headers", () => grid.HeadersVisibility = DataGridHeadersVisibility.None);
            AddUntilStep("rows move up", () => grid.ContainerFromIndex(0).ScreenSpaceDrawQuad.TopLeft.Y < firstRowTop - 10);

            AddStep("vertical lines only", () => grid.GridLinesVisibility = DataGridGridLinesVisibility.Vertical);
            AddAssert("row presenters draw vertical lines", () => ((DataGridRow)grid.ContainerFromIndex(0)).CellsPresenter.ShowVerticalLines);

            AddStep("fixed row height", () => grid.RowHeight = 40);
            AddUntilStep("rows are 40 high", () => ListsTestData.Near(grid.ContainerFromIndex(0).DrawHeight, 40));
        }
    }

    /// <summary>DataGrid driven by real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class DataGridInputTests : ManualInputManagerTestScene
    {
        [Test]
        public void DoubleClickEditsAndEnterCommits()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridTextColumn nameColumn = null;
            DataGridCheckBoxColumn activeColumn = null;

            DataGridCell nameCell(int i) => grid.CellFor(people[i], nameColumn);

            AddStep("create", () =>
            {
                grid = new DataGrid { Size = new Vector2(400, 200), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(nameColumn = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding, Width = GridLength.Parse("*") });
                grid.Columns.Add(activeColumn = new DataGridCheckBoxColumn { Header = "Active", Binding = ListsTestData.ActiveBinding });
                Child = grid;
            });
            AddUntilStep("laid out", () => nameColumn.ActualWidth > 0);

            AddStep("click Ann's row", () =>
            {
                InputManager.MoveMouseTo(nameCell(1));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("selected", () => grid.SelectedItem.Value == people[1]);

            AddStep("click Bob's check box", () =>
            {
                InputManager.MoveMouseTo(grid.CellFor(people[2], activeColumn).PresentedContent);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("toggled and selected Bob", () => !people[2].Active.Value && grid.SelectedItem.Value == people[2]);

            AddStep("double-click Ann's name", () =>
            {
                InputManager.MoveMouseTo(nameCell(1));
                InputManager.Click(MouseButton.Left);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("editing with focus", () => nameCell(1).IsEditing && (nameCell(1).PresentedContent as TextBox)?.HasFocus == true);

            AddStep("type and press Enter", () =>
            {
                ((TextBox)nameCell(1).PresentedContent).Text.Value = "Annika";
                InputManager.Key(Key.Enter);
            });
            AddUntilStep("committed", () => !nameCell(1).IsEditing && people[1].Name.Value == "Annika");

            AddStep("F2 edits the selected row", () => InputManager.Key(Key.F2));
            AddUntilStep("editing", () => nameCell(1).IsEditing && (nameCell(1).PresentedContent as TextBox)?.HasFocus == true);

            AddStep("type and press Escape", () =>
            {
                ((TextBox)nameCell(1).PresentedContent).Text.Value = "nope";
                InputManager.Key(Key.Escape);
            });
            AddUntilStep("cancelled", () => !nameCell(1).IsEditing && people[1].Name.Value == "Annika");

            AddStep("press Down", () => InputManager.Key(Key.Down));
            AddAssert("next row selected", () => grid.SelectedItem.Value == people[2]);

            AddStep("click the Name header", () =>
            {
                InputManager.MoveMouseTo(grid.ColumnHeaders.Headers.First());
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("sorted", () => string.Join(",", grid.Items.Cast<Person>().Select(p => p.Name.Value)) == "Annika,Bob,Cid");
        }

        [Test]
        public void DraggingAHeaderEdgeResizesTheColumn()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridTextColumn nameColumn = null;
            float before = 0;

            AddStep("create", () =>
            {
                grid = new DataGrid { Size = new Vector2(400, 200), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(nameColumn = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding });
                grid.Columns.Add(new DataGridTextColumn { Header = "Age", Binding = ListsTestData.AgeBinding, Width = GridLength.Parse("*") });
                Child = grid;
            });
            AddUntilStep("laid out", () => nameColumn.ActualWidth > 0);

            AddStep("drag the edge 60 pixels right", () =>
            {
                before = nameColumn.ActualWidth;
                var header = grid.ColumnHeaders.Headers.First();
                InputManager.MoveMouseTo(header.ToScreenSpace(new Vector2(header.DrawWidth - 1, header.DrawHeight / 2)));
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(header.ToScreenSpace(new Vector2(header.DrawWidth + 30, header.DrawHeight / 2)));
                InputManager.MoveMouseTo(header.ToScreenSpace(new Vector2(header.DrawWidth + 59, header.DrawHeight / 2)));
            });
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddUntilStep("column wider", () => nameColumn.Width.Type == GridUnitType.Pixel && ListsTestData.Near(nameColumn.ActualWidth, before + 60));
            AddAssert("not sorted by the drag", () => nameColumn.SortDirection == null);
        }
    }
}
