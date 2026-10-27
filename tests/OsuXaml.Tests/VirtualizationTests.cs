using System;
using System.Collections.Generic;
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
    /// <summary>Virtualization settings written the way a WPF developer would, checked through the generator.</summary>
    [TestFixture]
    public class VirtualizationGeneratorTests
    {
        [Test]
        public void AttachedPropertiesOnListsAndTrees()
        {
            string source = ListsTestData.Compiles(@"
<StackPanel>
  <ListBox ItemsSource=""{Bind People}"" VirtualizingPanel.IsVirtualizing=""False"" Height=""200"" />
  <ListView ItemsSource=""{Bind People}"" VirtualizingStackPanel.VirtualizationMode=""Recycling"" Height=""200"" />
  <TreeView ItemsSource=""{Bind Roots}"" VirtualizingStackPanel.IsVirtualizing=""True"" Height=""200"" />
  <ItemsControl ItemsSource=""{Bind People}"">
    <ItemsControl.ItemsPanel>
      <VirtualizingStackPanel Spacing=""4"" />
    </ItemsControl.ItemsPanel>
  </ItemsControl>
</StackPanel>");

            // VirtualizingStackPanel.X resolves to the setter it inherits from VirtualizingPanel, as in WPF.
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.VirtualizingPanel.SetIsVirtualizing("));
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.VirtualizingPanel.SetVirtualizationMode("));
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.VirtualizationMode.Recycling"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.VirtualizingStackPanel"));
        }
    }

    /// <summary>UI virtualization: lists create containers only for the rows in view, and behave as if every row existed.</summary>
    [HeadlessTest]
    public partial class VirtualizationTests : ManualInputManagerTestScene
    {
        private static BindableList<string> numbers(int count) => new BindableList<string>(Enumerable.Range(0, count).Select(i => $"Item {i}"));

        private static VirtualizingStackPanel panel(ItemsControl list) => list.ItemsPanel as VirtualizingStackPanel;

        private static ScrollViewer scroll(Drawable list) => list.ChildrenOfType<ScrollViewer>().First();

        private static string textOf(Drawable container) => ListsTestData.TextOf(container);

        /// <summary>Every row that has a container shows its own item.</summary>
        private static bool rowsShowTheirItems(ItemsControl list)
            => panel(list).RealizedIndices.All(i => textOf(list.ContainerFromIndex(i)) == (string)list.Items[i]);

        /// <summary>The realized row whose top is the highest one still inside the viewport.</summary>
        private static int topVisibleIndex(ItemsControl list)
        {
            float top = scroll(list).ScreenSpaceDrawQuad.TopLeft.Y;
            return panel(list).RealizedIndices.Where(i => list.ContainerFromIndex(i).ScreenSpaceDrawQuad.TopLeft.Y >= top - 0.5f).DefaultIfEmpty(-1).Min();
        }

        private static float screenTop(ItemsControl list, int index) => list.ContainerFromIndex(index).ScreenSpaceDrawQuad.TopLeft.Y;

        private static bool inView(ItemsControl list, int index)
        {
            var row = list.ContainerFromIndex(index)?.ScreenSpaceDrawQuad;
            var viewport = scroll(list).ScreenSpaceDrawQuad;
            return row != null && row.Value.TopLeft.Y >= viewport.TopLeft.Y - 1 && row.Value.BottomLeft.Y <= viewport.BottomLeft.Y + 1;
        }

        [Test]
        public void OnlyTheRowsInViewHaveContainers()
        {
            ListBox list = null;

            AddStep("create 10,000 rows", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(10000) });
            AddUntilStep("first rows realized", () => list.ContainerFromIndex(0) != null);

            AddAssert("virtualizing", () => list.IsVirtualizing);
            AddAssert("about a screenful of rows", () => panel(list).RealizedCount is > 8 and < 40);
            AddAssert("far rows have none", () => list.ContainerFromIndex(5000) == null && list.ContainerFromIndex(9999) == null);
            AddAssert("rows show their items", () => rowsShowTheirItems(list));
            AddUntilStep("the scroll extent covers every row", () => scroll(list).ExtentHeight > 10000 * 20);
        }

        /// <summary>A control library's list, built on ListBox in another assembly, reaching rows through the protected helpers.</summary>
        private partial class DerivedList : ListBox
        {
            public bool Bring(int index) => BringIndexIntoView(index);

            public Drawable Realize(int index) => RealizeIndex(index);
        }

        [Test]
        public void DerivedListsCanRealizeAndBringRowsIntoView()
        {
            DerivedList list = null;
            bool realizedNow = false;

            AddStep("create 10,000 rows", () => Child = list = new DerivedList { Size = new Vector2(200, 300), ItemsSource = numbers(10000) });
            AddUntilStep("first rows realized", () => list.ContainerFromIndex(0) != null);

            // The container goes again in the panel's next update, as it's out of view, so it's checked straight away.
            AddStep("realize a far row", () =>
            {
                var realized = list.Realize(7000);
                realizedNow = realized != null && list.ContainerFromIndex(7000) == realized && list.IndexFromContainer(realized) == 7000;
            });
            AddAssert("it had a container at once", () => realizedNow);

            AddStep("bring another into view", () => list.Bring(9000));
            AddUntilStep("it is in view", () => inView(list, 9000));
        }

        [Test]
        public void ScrollingRealizesNewRowsAndReleasesOldOnes()
        {
            ListBox list = null;

            AddStep("create", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(10000) });
            AddUntilStep("realized", () => list.ContainerFromIndex(0) != null);

            AddStep("jump to the middle", () => scroll(list).ScrollToVerticalOffset(scroll(list).ScrollableHeight / 2, false));
            AddUntilStep("top rows released", () => list.ContainerFromIndex(0) == null);
            AddUntilStep("a middle row is in view", () => topVisibleIndex(list) is > 4000 and < 6000);
            AddAssert("still a screenful", () => panel(list).RealizedCount < 40);
            AddAssert("rows show their items", () => rowsShowTheirItems(list));

            AddStep("back to the top", () => scroll(list).ScrollToTop(false));
            AddUntilStep("first row back", () => inView(list, 0) && textOf(list.ContainerFromIndex(0)) == "Item 0");
        }

        [Test]
        public void KeyboardAndScrollIntoViewReachRowsThatDontExistYet()
        {
            ListBox list = null;

            AddStep("create", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(10000) });
            AddUntilStep("realized", () => list.ContainerFromIndex(0) != null);

            AddStep("focus, select the first", () =>
            {
                GetContainingFocusManager()?.ChangeFocus(list);
                list.SelectedIndex.Value = 0;
            });
            AddStep("End", () => InputManager.Key(Key.End));
            AddAssert("last selected", () => list.SelectedIndex.Value == 9999);
            AddUntilStep("last row in view and selected", () => inView(list, 9999) && ((ListBoxItem)list.ContainerFromIndex(9999)).IsSelected);

            AddStep("Up", () => InputManager.Key(Key.Up));
            AddUntilStep("row above in view", () => list.SelectedIndex.Value == 9998 && inView(list, 9998));

            AddStep("ScrollIntoView(Item 4321)", () => list.ScrollIntoView("Item 4321"));
            AddAssert("its container exists at once", () => list.ContainerFromIndex(4321) != null);
            AddUntilStep("it scrolls into view", () => inView(list, 4321));

            AddStep("Home", () => InputManager.Key(Key.Home));
            AddUntilStep("first row in view", () => list.SelectedIndex.Value == 0 && inView(list, 0));
        }

        [Test]
        public void SelectionSurvivesRowsScrollingAway()
        {
            ListBox list = null;

            AddStep("create", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(2000), SelectionMode = SelectionMode.Extended });
            AddUntilStep("realized", () => list.ContainerFromIndex(3) != null);

            AddStep("select rows 2 and 3", () => list.SelectedItems.AddRange(new object[] { "Item 2", "Item 3" }));
            AddStep("scroll away", () => scroll(list).ScrollToBottom(false));
            AddUntilStep("their containers are gone", () => list.ContainerFromIndex(3) == null);
            AddAssert("still selected", () => list.SelectedItems.Count == 2 && list.SelectedItem.Value as string == "Item 2");

            AddStep("scroll back", () => scroll(list).ScrollToTop(false));
            AddUntilStep("new containers show the selection", () =>
                list.ContainerFromIndex(3) is ListBoxItem { IsSelected: true } && list.ContainerFromIndex(2) is ListBoxItem { IsSelected: true }
                                                                                && list.ContainerFromIndex(4) is ListBoxItem { IsSelected: false });
        }

        [Test]
        public void CorrectedRowHeightsDontMoveWhatIsInView()
        {
            ListBox list = null;
            int anchor = -1;
            float anchorTop = 0;

            // Rows in the second half are three times as tall, so landing there corrects the estimate made from the first rows.
            var template = new DataTemplate<string>((s, _) => new osu.Framework.Graphics.Containers.Container
            {
                RelativeSizeAxes = Axes.X,
                Height = int.Parse(s.Substring(5)) < 5000 ? 20 : 60,
            });

            AddStep("create", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(10000), ItemTemplate = template });
            AddUntilStep("realized", () => list.ContainerFromIndex(0) != null);

            AddStep("jump three quarters down", () => scroll(list).ScrollToVerticalOffset(scroll(list).ScrollableHeight * 0.75, false));
            AddUntilStep("tall rows in view", () => topVisibleIndex(list) >= 5000);
            AddWaitStep("settle", 3);

            AddStep("note the top row", () =>
            {
                anchor = topVisibleIndex(list);
                anchorTop = screenTop(list, anchor);
            });
            AddWaitStep("a few frames", 5);
            AddAssert("it hasn't moved", () => topVisibleIndex(list) == anchor && Math.Abs(screenTop(list, anchor) - anchorTop) < 0.5f);

            // The rows this reveals above are measured taller than the estimate; the scroll position absorbs the difference.
            AddStep("scroll up 120px", () => scroll(list).ScrollToVerticalOffset(scroll(list).VerticalOffset - 120, false));
            AddWaitStep("settle", 3);
            AddAssert("rows above were measured", () => panel(list).RealizedIndices.Count(i => i < anchor) >= 4);
            AddAssert("the row moved down by exactly the scroll", () => Math.Abs(screenTop(list, anchor) - (anchorTop + 120)) < 0.5f);
        }

        [Test]
        public void ChangesWhileScrolledKeepRowsInStep()
        {
            var items = numbers(5000);
            ListBox list = null;

            AddStep("create", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = items });
            AddUntilStep("realized", () => list.ContainerFromIndex(0) != null);
            AddStep("jump to the middle", () => scroll(list).ScrollToVerticalOffset(scroll(list).ScrollableHeight / 2, false));
            AddUntilStep("middle rows realized", () => list.ContainerFromIndex(0) == null && topVisibleIndex(list) > 2000);

            AddStep("remove the first 100", () => items.RemoveRange(0, 100));
            AddAssert("rows still show their items", () => list.Items.Count == 4900 && rowsShowTheirItems(list));

            AddStep("insert 50 at the top", () =>
            {
                for (int i = 0; i < 50; i++)
                    items.Insert(i, $"New {i}");
            });
            AddAssert("rows still show their items", () => list.Items.Count == 4950 && rowsShowTheirItems(list));

            AddStep("move a visible item to the end", () => items.Move(list.IndexOf(list.Items[topVisibleIndex(list)]), items.Count - 1));
            AddUntilStep("rows still show their items", () => rowsShowTheirItems(list));

            AddStep("clear", () => items.Clear());
            AddAssert("no rows", () => list.Items.Count == 0 && panel(list).RealizedCount == 0);

            AddStep("add again", () => items.AddRange(numbers(3)));
            AddUntilStep("three rows", () => panel(list).RealizedCount == 3 && rowsShowTheirItems(list));
        }

        [Test]
        public void TurningVirtualizationOffCreatesEveryContainer()
        {
            ListBox list = null;

            AddStep("create with IsVirtualizing off", () =>
            {
                list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(300) };
                VirtualizingPanel.SetIsVirtualizing(list, false);
                Child = list;
            });
            AddUntilStep("loaded", () => list.IsLoaded);
            AddAssert("not virtualizing", () => !list.IsVirtualizing);
            AddAssert("every row exists", () => Enumerable.Range(0, 300).All(i => list.ContainerFromIndex(i) != null));
            AddUntilStep("laid out as a stack", () => list.ContainerFromIndex(299).ScreenSpaceDrawQuad.TopLeft.Y > list.ContainerFromIndex(298).ScreenSpaceDrawQuad.TopLeft.Y);
        }

        [Test]
        public void RecyclingReusesContainers()
        {
            ListBox list = null;
            HashSet<Drawable> first = null;

            AddStep("create with recycling", () =>
            {
                list = new ListBox { Size = new Vector2(200, 300), ItemsSource = numbers(2000) };
                VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
                Child = list;
            });
            AddUntilStep("realized", () => list.ContainerFromIndex(0) != null);
            AddStep("note the containers", () => first = panel(list).RealizedIndices.Select(i => list.ContainerFromIndex(i)).ToHashSet());

            AddStep("scroll to the end", () => scroll(list).ScrollToBottom(false));
            AddUntilStep("last row in view", () => inView(list, 1999));
            AddAssert("the same containers show other items", () => panel(list).RealizedIndices.Count(i => first.Contains(list.ContainerFromIndex(i))) > 5);
            AddAssert("rows show their items", () => rowsShowTheirItems(list));
        }

        [Test]
        public void ItemsControlVirtualizesWithAVirtualizingStackPanelInAScrollViewer()
        {
            ItemsControl items = null;
            ScrollViewer viewer = null;

            AddStep("create", () =>
            {
                items = new ItemsControl { ItemsSource = numbers(5000), ItemsPanel = new VirtualizingStackPanel { Spacing = 4 } };
                Child = viewer = new ScrollViewer { Size = new Vector2(200, 300), Child = items };
            });
            AddUntilStep("realized", () => items.ContainerFromIndex(0) != null);
            AddAssert("only a screenful", () => items.IsVirtualizing && panel(items).RealizedCount < 40);

            AddStep("scroll to the end", () => viewer.ScrollToBottom(false));
            AddUntilStep("last item realized", () => items.ContainerFromIndex(4999) != null && items.ContainerFromIndex(0) == null);
            AddAssert("rows show their items", () => rowsShowTheirItems(items));
        }

        [Test]
        public void SortingAndEditingALargeDataGrid()
        {
            var people = new BindableList<Person>(Enumerable.Range(0, 5000).Select(i => new Person($"P{(i * 7919) % 5000:D4}", i % 90)));
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridTextColumn name = null;
            Person picked = null;

            AddStep("create", () =>
            {
                grid = new DataGrid { Size = new Vector2(400, 300), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(name = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding, Width = GridLength.Parse("*") });
                grid.Columns.Add(new DataGridTextColumn { Header = "Age", Binding = ListsTestData.AgeBinding });
                Child = grid;
            });
            AddUntilStep("rows realized", () => grid.RowFromItem(people[0]) != null);
            AddAssert("only a screenful", () => panel(grid).RealizedCount < 40);

            AddStep("select one", () => grid.SelectedItem.Value = picked = people[2]);
            AddStep("sort by name", () => grid.Sort(name));
            AddAssert("sorted", () => ((Person)grid.Items[0]).Name.Value == "P0000" && ((Person)grid.Items[4999]).Name.Value == "P4999");
            AddAssert("selection kept", () => grid.SelectedItem.Value == picked && grid.SelectedIndex.Value == grid.IndexOf(picked));
            AddUntilStep("top row shows the first name", () => ListsTestData.TextOf(grid.CellFor(grid.Items[0], name)) == "P0000");

            AddStep("alternate rows", () => grid.AlternatingRowBackground = Colour4.Red);
            AddAssert("alternation by item index", () => panel(grid).RealizedIndices.All(i => ((DataGridRow)grid.ContainerFromIndex(i)).AlternationIndex == i % 2));

            AddStep("edit the first row", () => grid.CellFor(grid.Items[0], name).BeginEdit());
            AddUntilStep("editor focused", () => grid.EditingCell?.PresentedContent is TextBox { HasFocus: true });
            AddStep("scroll to the end", () => scroll(grid).ScrollToBottom(false));
            AddUntilStep("last rows realized", () => grid.ContainerFromIndex(4999) != null);
            AddAssert("the row being edited is kept", () => grid.EditingCell != null && grid.ContainerFromIndex(0) != null);
            AddAssert("alternation by item index", () => panel(grid).RealizedIndices.All(i => ((DataGridRow)grid.ContainerFromIndex(i)).AlternationIndex == i % 2));

            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddUntilStep("edit ended, the row is let go", () => grid.EditingCell == null && grid.ContainerFromIndex(0) == null);

            AddStep("sort descending", () => grid.Sort(name, ListSortDirection.Descending));
            AddUntilStep("the end of the list shows the first name", () => ListsTestData.TextOf(grid.CellFor(grid.Items[4999], name)) == "P0000");
        }

        [Test]
        public void AutoColumnsKeepTheWidestCellTheyHaveShown()
        {
            var people = new BindableList<Person>(Enumerable.Range(0, 1000).Select(i => new Person(i == 900 ? "A much longer name than the others" : $"P{i}", i)));
            var scope = new BindingScope();
            DataGrid grid = null;
            DataGridTextColumn name = null;
            float narrow = 0;

            AddStep("create", () =>
            {
                grid = new DataGrid { Size = new Vector2(500, 300), ItemsSource = XamlBinding.ListCopy(scope, people) };
                grid.Columns.Add(name = new DataGridTextColumn { Header = "Name", Binding = ListsTestData.NameBinding });
                Child = grid;
            });
            AddUntilStep("sized", () => name.ActualWidth > 0);
            AddStep("note the width", () => narrow = name.ActualWidth);

            AddStep("scroll to the long name", () => grid.ScrollIntoView(people[900]));
            AddUntilStep("the column widens", () => name.ActualWidth > narrow + 50);
            AddStep("back to the top", () => scroll(grid).ScrollToTop(false));
            AddUntilStep("long name released", () => grid.ContainerFromIndex(900) == null);
            AddAssert("the column stays wide", () => name.ActualWidth > narrow + 50);

            AddStep("set a pixel width", () => name.Width = GridLength.Parse("100"));
            AddUntilStep("pixel width", () => ListsTestData.Near(name.ActualWidth, 100));
            AddStep("back to Auto", () => name.Width = GridLength.Parse("Auto"));
            AddUntilStep("measured afresh from the rows in view", () => name.ActualWidth < narrow + 20);
        }

        [Test]
        public void SizeCacheOffsets()
        {
            var cache = new ItemSizeCache { Spacing = 2 };
            cache.Insert(0, 6);

            Assert.That(cache.Estimate, Is.EqualTo(ItemSizeCache.DefaultEstimate));
            Assert.That(cache.OffsetOf(3), Is.EqualTo(3 * (24 + 2)));

            cache.Set(0, 10);
            cache.Set(1, 30);
            cache.Set(2, ItemSizeCache.Collapsed);

            // Measured 10 and 30, so the estimate is 20; the collapsed item takes neither length nor spacing.
            Assert.That(cache.Estimate, Is.EqualTo(20));
            Assert.That(cache.OffsetOf(2), Is.EqualTo(10 + 2 + 30 + 2));
            Assert.That(cache.OffsetOf(3), Is.EqualTo(cache.OffsetOf(2)));
            Assert.That(cache.OffsetOf(4), Is.EqualTo(44 + 20 + 2));
            Assert.That(cache.Extent, Is.EqualTo(10 + 30 + 3 * 20 + 4 * 2));

            Assert.That(cache.IndexAt(-5), Is.EqualTo(0));
            Assert.That(cache.IndexAt(11), Is.EqualTo(0));
            Assert.That(cache.IndexAt(12), Is.EqualTo(1));
            Assert.That(cache.IndexAt(44), Is.EqualTo(3), "the visible item at a collapsed item's offset");
            Assert.That(cache.IndexAt(1000), Is.EqualTo(5));

            cache.Remove(0, 1);
            Assert.That(cache.OffsetOf(1), Is.EqualTo(32));
            Assert.That(cache.Estimate, Is.EqualTo(30));

            cache.Move(0, 4);
            Assert.That(cache.OffsetOf(4), Is.EqualTo(3 * (30 + 2)));

            cache.Remap(new[] { 4, -1 });
            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.LengthOf(0), Is.EqualTo(30));
            Assert.That(cache.IsKnown(1), Is.False);

            // An expanded tree node is long, but suggests its header length for the others.
            cache.Insert(2, 1);
            cache.Set(1, 300, 30);
            Assert.That(cache.Estimate, Is.EqualTo(30));
            Assert.That(cache.Extent, Is.EqualTo(30 + 300 + 30 + 2 * 2));
        }

        #region TreeView

        private static readonly HierarchicalDataTemplate<Node> node_template = new HierarchicalDataTemplate<Node>(
            (n, _) => new TextBlock { Text = n.Label },
            (n, scope) => XamlBinding.ListCopy(scope, n.Children));

        private static readonly ItemBinding<Node, bool> expanded_binding = new ItemBinding<Node, bool>("IsExpanded", n => n.IsExpanded, n => n.IsExpanded.Value);

        /// <summary><paramref name="count"/> nodes labelled under <paramref name="prefix"/>, each with <paramref name="children"/>(its label) children.</summary>
        private static List<Node> nodes(string prefix, int count, Func<string, IEnumerable<Node>> children = null)
            => Enumerable.Range(0, count).Select(i =>
            {
                var node = new Node($"{prefix}{i}");
                if (children != null)
                    node.Children.AddRange(children(node.Label));
                return node;
            }).ToList();

        private static TreeView virtualizingTree(IEnumerable<Node> roots, VirtualizationMode mode = VirtualizationMode.Standard, bool bindExpanded = false)
        {
            var tree = new TreeView
            {
                Size = new Vector2(260, 300),
                ItemTemplate = node_template,
                IsExpandedBinding = bindExpanded ? expanded_binding : null,
                ItemsSource = new BindableList<Node>(roots),
            };

            VirtualizingPanel.SetIsVirtualizing(tree, true);
            VirtualizingPanel.SetVirtualizationMode(tree, mode);
            return tree;
        }

        private static TreeViewItem nodeOf(TreeView tree, Node item) => tree.ContainerFromDataItem(item);

        private static int nodeCount(TreeView tree) => tree.VisibleItems.Count();

        private static bool headerInView(TreeView tree, Node item)
        {
            var header = nodeOf(tree, item)?.HeaderRow.ScreenSpaceDrawQuad;
            var viewport = scroll(tree).ScreenSpaceDrawQuad;
            return header != null && header.Value.TopLeft.Y >= viewport.TopLeft.Y - 1 && header.Value.BottomLeft.Y <= viewport.BottomLeft.Y + 1;
        }

        [Test]
        public void TreeViewVirtualizesOnlyWhenAskedTo()
        {
            TreeView tree = null;

            AddStep("create a plain tree of 300 roots", () => Child = tree = new TreeView
            {
                Size = new Vector2(260, 300),
                ItemTemplate = node_template,
                ItemsSource = new BindableList<Node>(nodes("Root ", 300)),
            });
            AddUntilStep("loaded", () => tree.IsLoaded);
            AddAssert("every node exists", () => !tree.IsVirtualizing && nodeCount(tree) == 300);

            AddStep("create with IsVirtualizing", () => Child = tree = virtualizingTree(nodes("Root ", 5000)));
            AddUntilStep("first node realized", () => tree.ContainerFromIndex(0) != null);
            AddAssert("only a screenful", () => tree.IsVirtualizing && nodeCount(tree) < 40 && tree.ContainerFromIndex(4000) == null);
        }

        [Test]
        public void ExpandedLevelsVirtualizeToo()
        {
            TreeView tree = null;
            List<Node> roots = null;

            AddStep("create", () =>
            {
                roots = nodes("Root ", 1000);
                roots[1].Children.AddRange(nodes("Root 1/", 10000));
                Child = tree = virtualizingTree(roots);
            });
            AddUntilStep("realized", () => tree.ContainerFromIndex(1) != null);

            AddStep("expand the second root", () => nodeOf(tree, roots[1]).IsExpanded.Value = true);
            AddUntilStep("its first children show", () => headerInView(tree, roots[1].Children[3]));
            AddAssert("only a screenful of its children", () => nodeCount(tree) < 60 && nodeOf(tree, roots[1].Children[5000]) == null);
            AddAssert("the scroll extent counts unseen roots as headers", () => scroll(tree).ExtentHeight < 12000 * 30);

            AddStep("scroll into the middle of the children", () => scroll(tree).ScrollToVerticalOffset(roots[1].Children.Count * 10, false));
            AddUntilStep("the roots above are let go", () => tree.ContainerFromIndex(0) == null);
            AddAssert("the expanded root stays, it holds the view", () => tree.ContainerFromIndex(1) == nodeOf(tree, roots[1]));
            AddAssert("middle children realized", () => tree.VisibleItems.Any(n => n.DataItem is Node { Label: var label } && label.StartsWith("Root 1/4", StringComparison.Ordinal)));
            AddAssert("still a screenful", () => nodeCount(tree) < 60);
        }

        [Test]
        public void KeyboardWalksNodesThatDontExistYet()
        {
            TreeView tree = null;
            List<Node> roots = null;

            AddStep("create", () =>
            {
                roots = nodes("Root ", 3000, label => nodes(label + "/", 3));
                roots[0].IsExpanded.Value = true;
                roots[^1].IsExpanded.Value = true;
                Child = tree = virtualizingTree(roots, bindExpanded: true);
            });
            AddUntilStep("realized", () => tree.ContainerFromIndex(0) != null);
            AddStep("focus", () => GetContainingFocusManager()?.ChangeFocus(tree));

            AddStep("End", () => InputManager.Key(Key.End));
            AddAssert("the last child of the last root", () => tree.SelectedItem.Value == roots[^1].Children[2]);
            AddUntilStep("scrolled to it", () => headerInView(tree, roots[^1].Children[2]));
            AddAssert("the first root was let go", () => tree.ContainerFromIndex(0) == null);

            AddStep("Up four times", () =>
            {
                for (int i = 0; i < 4; i++)
                    InputManager.Key(Key.Up);
            });
            AddAssert("the root before the last", () => tree.SelectedItem.Value == roots[^2]);
            AddUntilStep("in view", () => headerInView(tree, roots[^2]));

            AddStep("Home, Down", () =>
            {
                InputManager.Key(Key.Home);
                InputManager.Key(Key.Down);
            });
            AddAssert("into the first root's children", () => tree.SelectedItem.Value == roots[0].Children[0]);
            AddUntilStep("in view", () => headerInView(tree, roots[0].Children[0]));

            AddStep("scroll away", () => scroll(tree).ScrollToBottom(false));
            AddUntilStep("the first roots are let go", () => tree.ContainerFromIndex(1) == null);
            AddAssert("the selected node and its parent stay", () => tree.SelectedContainer == nodeOf(tree, roots[0].Children[0]) && tree.ContainerFromIndex(0) != null);

            AddStep("Down", () => InputManager.Key(Key.Down));
            AddAssert("the next child", () => tree.SelectedItem.Value == roots[0].Children[1]);
            AddUntilStep("scrolled back to it", () => headerInView(tree, roots[0].Children[1]));

            AddStep("ScrollIntoView a far root", () => tree.ScrollIntoView(roots[2000]));
            AddUntilStep("in view", () => headerInView(tree, roots[2000]));
        }

        [Test]
        public void NodesKeepTheirExpansionWhenTheyScrollBack([Values] VirtualizationMode mode)
        {
            TreeView tree = null;
            List<Node> roots = null;

            AddStep("create", () => Child = tree = virtualizingTree(roots = nodes("Root ", 2000, label => nodes(label + "/", 2)), mode));
            AddUntilStep("realized", () => tree.ContainerFromIndex(2) != null);

            AddStep("expand the third root", () => nodeOf(tree, roots[2]).IsExpanded.Value = true);
            AddUntilStep("its children show", () => headerInView(tree, roots[2].Children[1]));

            AddStep("scroll to the end", () => scroll(tree).ScrollToBottom(false));
            AddUntilStep("the third root is let go", () => tree.ContainerFromIndex(2) == null && tree.ContainerFromIndex(1999) != null);
            AddAssert("nodes shown at the end are collapsed", () => tree.VisibleItems.All(n => !n.IsExpanded.Value));

            AddStep("scroll back", () => scroll(tree).ScrollToTop(false));
            AddUntilStep("the third root is expanded again", () => nodeOf(tree, roots[2])?.IsExpanded.Value == true && headerInView(tree, roots[2].Children[1]));
            AddAssert("the others are not", () => tree.VisibleItems.Where(n => n.Level == 0 && n.DataItem != roots[2]).All(n => !n.IsExpanded.Value));
        }

        [Test]
        public void ExpandSubtreeExpandsNodesAsTheyAppear()
        {
            TreeView tree = null;
            List<Node> roots = null;

            AddStep("create 50 × 50 × 50 nodes", () => Child = tree = virtualizingTree(roots = nodes("R", 50, a => nodes(a + "/", 50, b => nodes(b + "/", 50)))));
            AddUntilStep("realized", () => tree.ContainerFromIndex(0) != null);

            AddStep("expand the first subtree", () => nodeOf(tree, roots[0]).ExpandSubtree());
            AddUntilStep("its first grandchildren show", () => headerInView(tree, roots[0].Children[0].Children[2]));
            AddAssert("only a screenful exists", () => nodeCount(tree) < 80);

            AddStep("scroll down", () => scroll(tree).ScrollToVerticalOffset(60 * 25, false));
            AddUntilStep("the next child is expanded as it appears", () => nodeOf(tree, roots[0].Children[1])?.IsExpanded.Value == true
                                                                           && headerInView(tree, roots[0].Children[1].Children[20]));
            AddAssert("other roots untouched", () => nodeOf(tree, roots[1]) == null || !nodeOf(tree, roots[1]).IsExpanded.Value);
        }

        [Test]
        public void ScrollIntoViewReachesNestedNodesThatDontExistYet([Values] bool virtualizing)
        {
            TreeView tree = null;
            List<Node> roots = null;
            Node target = null;

            AddStep("create", () =>
            {
                roots = nodes("R", virtualizing ? 2000 : 200, a => nodes(a + "/", 10, b => nodes(b + "/", 10)));
                target = roots[^50].Children[3].Children[7];
                tree = virtualizingTree(roots, bindExpanded: true);
                VirtualizingPanel.SetIsVirtualizing(tree, virtualizing);
                Child = tree;
            });
            AddUntilStep("realized", () => tree.ContainerFromIndex(0) != null);
            AddAssert("the target's node doesn't exist", () => nodeOf(tree, target) == null && nodeOf(tree, roots[^50].Children[3]) == null);

            AddStep("ScrollIntoView a grandchild", () => tree.ScrollIntoView(target));
            AddUntilStep("in view", () => headerInView(tree, target));
            AddAssert("its ancestors are expanded", () => roots[^50].IsExpanded.Value && roots[^50].Children[3].IsExpanded.Value && !target.IsExpanded.Value);
            AddAssert("its siblings show around it", () => headerInView(tree, roots[^50].Children[3].Children[6]));
            AddWaitStep("let the nodes around it lay out", 10);
            AddAssert("still in view", () => headerInView(tree, target));

            if (virtualizing)
                AddAssert("still only a screenful", () => nodeCount(tree) < 60);

            AddStep("ScrollIntoView the first root", () => tree.ScrollIntoView(roots[0]));
            AddUntilStep("back at the top", () => headerInView(tree, roots[0]));
        }

        [Test]
        public void ScrollIntoViewReachesDeclaredNodesInCollapsedParents()
        {
            TreeView tree = null;
            TreeViewItem file = null, folder = null;

            AddStep("create 30 folders, each with a subfolder and a file", () =>
            {
                tree = new TreeView { Size = new Vector2(200, 100) };

                for (int i = 0; i < 30; i++)
                {
                    var f = new TreeViewItem { Header = $"Folder {i}" };
                    var sub = new TreeViewItem { Header = $"Sub {i}" };
                    var leaf = new TreeViewItem { Header = $"File {i}" };
                    sub.Add(leaf);
                    f.Add(sub);
                    tree.Add(f);

                    if (i == 25)
                        (folder, file) = (f, leaf);
                }

                // Before the tree loads: it scrolls once it has.
                tree.ScrollIntoView(file);
                Child = tree;
            });
            AddUntilStep("in view", () =>
            {
                var header = file.HeaderRow.ScreenSpaceDrawQuad;
                var viewport = scroll(tree).ScreenSpaceDrawQuad;
                return file.IsLoaded && header.TopLeft.Y >= viewport.TopLeft.Y - 1 && header.BottomLeft.Y <= viewport.BottomLeft.Y + 1;
            });
            AddAssert("its folders are expanded", () => folder.IsExpanded.Value && file.ParentItem.IsExpanded.Value && file.ParentItem.ParentItem == folder);
        }

        [Test]
        public void ScrollIntoViewIgnoresItemsNotInTheTree()
        {
            TreeView tree = null;
            List<Node> roots = null;

            AddStep("create, with a node that contains itself", () =>
            {
                roots = nodes("R", 100, a => nodes(a + "/", 3));
                roots[0].Children.Add(roots[0]);
                Child = tree = virtualizingTree(roots);
            });
            AddUntilStep("realized", () => tree.ContainerFromIndex(0) != null);

            AddStep("ScrollIntoView an unknown item", () => tree.ScrollIntoView(new Node("elsewhere")));
            AddWaitStep("wait", 5);
            AddAssert("nothing moved or expanded", () => scroll(tree).VerticalOffset == 0 && tree.VisibleItems.All(n => !n.IsExpanded.Value));
        }

        #endregion
    }
}
