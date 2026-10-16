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
        public void SelectedItemsCanBeChangedFromCode()
        {
            ListBox list = null;

            AddStep("create", () => Child = list = letters(SelectionMode.Extended));
            AddUntilStep("loaded", () => list.IsLoaded);

            AddStep("select b and c through SelectedItems", () =>
            {
                list.SelectedItems.Add("b");
                list.SelectedItems.Add("c");
            });
            AddAssert("both stay selected", () => list.SelectedItems.SequenceEqual(new object[] { "b", "c" }));
            AddAssert("SelectedItem is the first of them", () => (string)list.SelectedItem.Value == "b" && list.SelectedIndex.Value == 1);
            AddAssert("rows show it", () => !shownSelected(list, 0) && shownSelected(list, 1) && shownSelected(list, 2));

            AddStep("unselect b through SelectedItems", () => list.SelectedItems.Remove("b"));
            AddAssert("only c", () => list.SelectedItems.SequenceEqual(new object[] { "c" }) && (string)list.SelectedItem.Value == "c" && !shownSelected(list, 1));

            AddStep("clear SelectedItems", () => list.SelectedItems.Clear());
            AddAssert("nothing selected", () => list.SelectedItem.Value == null && list.SelectedIndex.Value == -1 && !shownSelected(list, 2));
        }

        [Test]
        public void SelectedItemAndIndexStayInSync()
        {
            ListBox list = null;

            AddStep("create", () => Child = list = letters(SelectionMode.Single));
            AddUntilStep("loaded", () => list.IsLoaded);

            AddStep("select b", () => list.SelectedIndex.Value = 1);
            AddStep("select an item the list doesn't have", () => list.SelectedItem.Value = "z");
            AddAssert("index doesn't claim b", () => list.SelectedIndex.Value == -1 && !shownSelected(list, 1));

            AddStep("select b again", () => list.SelectedIndex.Value = 1);
            AddStep("select an index past the end", () => list.SelectedIndex.Value = 10);
            AddAssert("item doesn't claim b", () => list.SelectedItem.Value == null && !shownSelected(list, 1));
        }

        [Test]
        public void ExpandSubtreeExpandsEveryLevel()
        {
            Node a = null, b = null, c = null, d = null;
            TreeView tree = null;

            AddStep("create", () =>
            {
                d = new Node("d");
                c = new Node("c", d);
                b = new Node("b", c);
                a = new Node("a", b);
                var scope = new BindingScope();

                tree = new TreeView
                {
                    Size = new Vector2(260, 300),
                    ItemTemplate = new HierarchicalDataTemplate<Node>((n, _) => new TextBlock { Text = n.Label }, (n, s) => XamlBinding.ListCopy(s, n.Children)),
                    IsExpandedBinding = new ItemBinding<Node, bool>("IsExpanded", n => n.IsExpanded, n => n.IsExpanded.Value),
                    ItemsSource = XamlBinding.ListCopy(scope, new BindableList<Node>(new[] { a })),
                };
                Child = tree;
            });
            AddUntilStep("loaded", () => tree.ContainerFromDataItem(a)?.IsLoaded == true);

            AddStep("expand the subtree", () => tree.ContainerFromDataItem(a).ExpandSubtree());
            AddUntilStep("every level shows", () => string.Join(",", tree.VisibleItems.Select(n => ((Node)n.DataItem).Label)) == "a,b,c,d");
            AddAssert("every node expanded", () => a.IsExpanded.Value && b.IsExpanded.Value && c.IsExpanded.Value);

            TreeViewItem music = null, rock = null, live = null;
            AddStep("declared items, expanded before they load", () =>
            {
                music = new TreeViewItem { Header = "Music" };
                music.Add(rock = new TreeViewItem { Header = "Rock" });
                rock.Add(live = new TreeViewItem { Header = "Live" });
                live.Add(new TreeViewItem { Header = "song.mp3" });
                music.ExpandSubtree();

                var declared = new TreeView { Size = new Vector2(200, 200) };
                declared.Add(music);
                Child = declared;
            });
            AddUntilStep("every declared level expanded", () => music.IsExpanded.Value && rock.IsExpanded.Value && live.IsExpanded.Value);
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

        [Test]
        public void UpArrowWithAnIndexPastTheEndStaysInTheList()
        {
            ListBox list = null;

            AddStep("create", () => Child = list = letters(SelectionMode.Single));
            AddUntilStep("loaded", () => list.IsLoaded);
            AddStep("focus", () => GetContainingFocusManager()?.ChangeFocus(list));
            AddStep("select an index past the end", () => list.SelectedIndex.Value = 10);
            AddStep("press Up", () => list.TriggerEvent(new osu.Framework.Input.Events.KeyDownEvent(new osu.Framework.Input.States.InputState(), Key.Up)));
            AddAssert("an item in the list is selected", () => list.SelectedIndex.Value >= 0 && list.SelectedIndex.Value < 3 && list.SelectedItem.Value != null);
        }

        [Test]
        public void SelectionChangedIsRaisedOncePerChange()
        {
            var items = new BindableList<string>(new[] { "a", "b", "c" });
            var scope = new BindingScope();
            ListBox list = null;
            int raised = 0;

            AddStep("create", () =>
            {
                list = new ListBox { Size = new Vector2(200, 200), ItemsSource = XamlBinding.ListCopy(scope, items) };
                Child = list;
            });
            AddUntilStep("loaded", () => list.IsLoaded);
            AddStep("count", () => list.SelectionChanged += () => raised++);

            AddStep("click b", () => list.ContainerFromIndex(1).TriggerClick());
            AddAssert("raised once", () => raised == 1);

            AddStep("add an item", () =>
            {
                raised = 0;
                items.Add("d");
            });
            AddAssert("selection didn't change, not raised", () => raised == 0 && (string)list.SelectedItem.Value == "b");

            AddStep("insert before the selection", () =>
            {
                raised = 0;
                items.Insert(0, "z");
            });
            AddAssert("same item selected, not raised", () => raised == 0 && (string)list.SelectedItem.Value == "b" && list.SelectedIndex.Value == 2);

            AddStep("code selects by index", () =>
            {
                raised = 0;
                list.SelectedIndex.Value = 3;
            });
            AddAssert("raised once", () => raised == 1 && (string)list.SelectedItem.Value == "c");

            AddStep("multiple mode toggles b", () =>
            {
                raised = 0;
                list.SelectionMode = SelectionMode.Multiple;
                list.SelectContainer(list.ContainerFromIndex(2));
            });
            AddAssert("raised once", () => raised == 1 && list.SelectedItems.Count == 2);
        }
    }
}
