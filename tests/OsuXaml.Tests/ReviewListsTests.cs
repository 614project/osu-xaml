using System.ComponentModel;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;
using Node = OsuXaml.Tests.ListsTestData.Node;
using Person = OsuXaml.Tests.ListsTestData.Person;

namespace OsuXaml.Tests
{
    /// <summary>Review tests for the list controls: ItemsControl, ListBox, ListView, DataGrid and TreeView.</summary>
    [HeadlessTest]
    public partial class ReviewListsTests : TestScene
    {
        private static ListBox letters(SelectionMode mode)
        {
            var list = new ListBox { Size = new Vector2(200, 200), SelectionMode = mode };
            foreach (string s in new[] { "a", "b", "c" })
                list.Add(s);
            return list;
        }

        private static bool shownSelected(ListBox list, int index) => ((ListBoxItem)list.ContainerFromIndex(index)).IsSelected;

        private static string order(ItemsControl list) => string.Join(",", list.Items.Cast<Person>().Select(p => p.Name.Value));

        [Test]
        public void SortByNullRestoresTheSourceOrder()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            ListView list = null;
            DataGrid grid = null;
            GridViewColumn listName = null;
            DataGridTextColumn gridName = null;

            AddStep("create", () =>
            {
                var view = new GridView();
                view.Add(listName = new GridViewColumn { Header = "Name", SortMemberPath = ListsTestData.NameBinding, DisplayMemberBinding = ListsTestData.Text(p => p.Name) });
                list = new ListView { Size = new Vector2(300, 150), ItemsSource = XamlBinding.ListCopy(scope, people), View = view };

                grid = new DataGrid { Size = new Vector2(300, 150), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(gridName = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding });

                var panel = new StackPanel();
                panel.Add(list);
                panel.Add(grid);
                Child = panel;
            });
            AddUntilStep("loaded", () => list.IsLoaded && grid.IsLoaded);

            AddStep("sort both by name", () =>
            {
                list.Sort(listName);
                grid.Sort(gridName);
            });
            AddAssert("sorted", () => order(list) == "Ann,Bob,Cid" && order(grid) == "Ann,Bob,Cid");

            AddStep("ListView.Sort(null)", () => list.Sort(null));
            AddAssert("ListView back in source order", () => order(list) == "Cid,Ann,Bob" && listName.SortDirection == null);

            AddStep("DataGrid.Sort(null)", () => grid.Sort(null));
            AddAssert("DataGrid back in source order", () => order(grid) == "Cid,Ann,Bob" && gridName.SortDirection == null);
        }

        [Test]
        public void RemovingTheEditedRowEndsTheEdit()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridTextColumn name = null;
            Person ann = null;

            AddStep("create", () =>
            {
                grid = new DataGrid { Size = new Vector2(400, 200), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(name = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding, Width = GridLength.Parse("*") });
                Child = grid;
            });
            AddUntilStep("laid out", () => name.ActualWidth > 0);

            AddStep("edit Ann's name", () =>
            {
                ann = people[1];
                grid.SelectedItem.Value = ann;
                var cell = grid.CellFor(ann, name);
                cell.BeginEdit();
                ((TextBox)cell.PresentedContent).Text.Value = "typed";
            });
            AddStep("view model removes Ann", () => people.Remove(ann));
            AddAssert("nothing is being edited", () => grid.EditingCell == null);

            AddStep("edit Bob's name", () => grid.CellFor(people[1], name).BeginEdit());
            AddAssert("the removed item was not written", () => ann.Name.Value == "Ann");
            AddAssert("Bob's cell edits", () => grid.EditingCell == grid.CellFor(people[1], name) && grid.EditingCell.IsEditing);
        }

        [Test]
        public void ReplacingItemsSourceOrColumnsEndsTheEdit()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridTextColumn name = null;

            AddStep("create", () =>
            {
                grid = new DataGrid { Size = new Vector2(400, 200), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(name = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding, Width = GridLength.Parse("*") });
                Child = grid;
            });
            AddUntilStep("laid out", () => name.ActualWidth > 0);

            AddStep("edit, then add a column", () =>
            {
                grid.CellFor(people[0], name).BeginEdit();
                grid.Columns.Add(new DataGridTextColumn { Header = "Age", Binding = ListsTestData.AgeBinding });
            });
            AddAssert("nothing is being edited", () => grid.EditingCell == null);

            AddStep("edit, then replace the items", () =>
            {
                grid.CellFor(people[0], name).BeginEdit();
                grid.ItemsSource = XamlBinding.ListCopy(scope, ListsTestData.People());
            });
            AddAssert("nothing is being edited", () => grid.EditingCell == null);
            AddStep("edit a new row", () => grid.CellFor(grid.Items[0], name).BeginEdit());
            AddAssert("new cell edits", () => grid.EditingCell == grid.CellFor(grid.Items[0], name));
        }

        [Test]
        public void DeclaredItemsCanBeRemovedAndAddedAgain()
        {
            ListBox list = null;
            ListBoxItem first = null;

            AddStep("create", () =>
            {
                list = new ListBox { Size = new Vector2(200, 200) };
                list.Add(first = new ListBoxItem { Content = "first" });
                list.Add(new ListBoxItem { Content = "second" });
                Child = list;
            });
            AddUntilStep("loaded", () => first.IsLoaded);

            AddStep("move first to the end", () =>
            {
                list.RemoveItem(first);
                list.Add(first);
            });
            AddAssert("first is the last item", () => list.Items.Count == 2 && list.Items[1] == first && list.ContainerFromIndex(1) == first);
            AddUntilStep("first is shown again", () => first.IsPresent && first.Parent != null && first.IsAlive);
            AddStep("click it", () => first.TriggerClick());
            AddAssert("selected", () => list.SelectedItem.Value == first && first.IsSelected);
        }
    }
}
