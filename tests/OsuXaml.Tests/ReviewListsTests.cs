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
    }
}
