using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>Regression tests for reported issues, one region per issue.</summary>
    [HeadlessTest]
    public partial class IssueRegressionTests : TestScene
    {
        #region #13: moving several items at once

        /// <summary>A list that can raise one Move for a block of items, which ObservableCollection never does.</summary>
        private sealed class BlockList : IEnumerable, INotifyCollectionChanged
        {
            private readonly List<object> items;

            public BlockList(params object[] items) => this.items = items.ToList();

            public event NotifyCollectionChangedEventHandler CollectionChanged;

            public IEnumerator GetEnumerator() => items.GetEnumerator();

            /// <summary>Moves <paramref name="count"/> items from <paramref name="from"/> so the first of them ends up at <paramref name="to"/>.</summary>
            public void MoveBlock(int from, int count, int to)
            {
                var block = items.GetRange(from, count);
                items.RemoveRange(from, count);
                items.InsertRange(to, block);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, block, to, from));
            }

            public override string ToString() => string.Join(",", items);
        }

        private static string order(IEnumerable items) => string.Join(",", items.Cast<object>());

        [TestCase(1, 2, 3, "A,D,E,B,C")]
        [TestCase(3, 2, 1, "A,D,E,B,C")]
        [TestCase(0, 3, 2, "D,E,A,B,C")]
        [TestCase(2, 3, 0, "C,D,E,A,B")]
        public void ABlockMoveKeepsTheOrder(int from, int count, int to, string expected)
        {
            var source = new BlockList("A", "B", "C", "D", "E");
            ListBox list = null;

            AddStep("create", () => Child = list = new ListBox { Size = new Vector2(200, 300), ItemsSource = source });
            AddStep("move a block", () => source.MoveBlock(from, count, to));
            AddAssert("source as expected", () => source.ToString() == expected);
            AddAssert("items follow the source", () => order(list.Items) == expected);
            AddAssert("containers follow the items", () =>
                order(Enumerable.Range(0, 5).Select(i => ((ContentControl)list.ContainerFromIndex(i)).Content)) == expected);
        }

        [TestCase(1, 2, 3, "A,D,E,B,C")]
        [TestCase(3, 2, 1, "A,D,E,B,C")]
        public void ABlockMoveKeepsTheOrderOfAnUnsortedTableView(int from, int count, int to, string expected)
        {
            var source = new BlockList("A", "B", "C", "D", "E");
            var view = new SortedItemsView(source);
            var mirrored = view.Cast<object>().ToList();

            // The moves the view raises, applied one by one, must give what the view holds.
            view.CollectionChanged += (_, e) =>
            {
                Assert.That(e.Action, Is.EqualTo(NotifyCollectionChangedAction.Move));
                object item = mirrored[e.OldStartingIndex];
                mirrored.RemoveAt(e.OldStartingIndex);
                mirrored.Insert(e.NewStartingIndex, item);
            };

            source.MoveBlock(from, count, to);

            Assert.That(order(view), Is.EqualTo(expected));
            Assert.That(order(mirrored), Is.EqualTo(expected));
            view.Dispose();
        }

        #endregion

        #region #14: a slider's value set before its range

        [Test]
        public void SliderValueBoundBeforeTheRangeKeepsTheSource()
        {
            var score = new osu.Framework.Bindables.BindableDouble(150);
            Slider slider = null;

            AddStep("bind, then set the range (XAML attribute order)", () =>
            {
                slider = new Slider { Width = 200 };
                XamlBinding.TwoWay(slider.Value, score);
                slider.Minimum = 0;
                slider.Maximum = 200;
                Child = slider;
            });
            AddAssert("source kept", () => score.Value == 150 && slider.Value.Value == 150 && slider.Current.Value == 150);
        }

        [Test]
        public void SliderValueSetBeforeTheRangeInCode()
        {
            Slider slider = null;

            AddStep("create", () => Child = slider = new Slider { Width = 200, Value = { Value = 30 }, Minimum = 50, Maximum = 80 });
            AddAssert("coerced to the minimum once loaded", () => slider.Value.Value == 50);

            AddStep("value above the range", () => slider.Value.Value = 120);
            AddAssert("coerced to the maximum", () => slider.Value.Value == 80);
            AddStep("widen the range", () => slider.Maximum = 150);
            AddAssert("the value asked for comes back", () => slider.Value.Value == 120 && slider.Current.Value == 120);
        }

        [Test]
        public void SliderValueOutOfTheFinalRangeIsCoercedOnLoad()
        {
            var score = new osu.Framework.Bindables.BindableDouble(150);
            Slider slider = null;

            AddStep("bind, range stays 0..100", () =>
            {
                slider = new Slider { Width = 200 };
                XamlBinding.TwoWay(slider.Value, score);
                Child = slider;
            });
            AddAssert("coerced both ways", () => score.Value == 100 && slider.Value.Value == 100);
        }

        #endregion

        #region #15: containers waiting in the recycling pool

        private sealed class Thing
        {
            public readonly Bindable<string> Name;
            public readonly BindableBool IsExpanded = new BindableBool();
            public readonly BindableList<Thing> Children = new BindableList<Thing>();

            public Thing(string name, params Thing[] children)
            {
                Name = new Bindable<string>(name);
                Children.AddRange(children);
            }

            public override string ToString() => Name.Value;
        }

        /// <summary>A template that binds a text to the item's name and keeps every text it built.</summary>
        private static DataTemplate<Thing> tracking(List<TextBlock> built) => new DataTemplate<Thing>((thing, scope) =>
        {
            var text = new TextBlock();
            var name = thing.Name.GetBoundCopy();
            scope.Add(name);
            name.BindValueChanged(e => text.Text = e.NewValue, true);
            built.Add(text);
            return text;
        });

        private static BindableList<Thing> things(int count) => new BindableList<Thing>(Enumerable.Range(0, count).Select(i => new Thing("T" + i)));

        private void checkRecycledContainersLetGo(System.Func<BindableList<Thing>, List<TextBlock>, ItemsControl> create)
        {
            var source = things(5);
            var built = new List<TextBlock>();
            ItemsControl control = null;
            Thing removed = null;

            AddStep("create with recycling", () =>
            {
                control = create(source, built);
                VirtualizingPanel.SetVirtualizationMode(control, VirtualizationMode.Recycling);
                Child = control;
            });
            AddUntilStep("rows exist", () => control.ContainerFromIndex(4) != null && built.Count >= 5);
            AddStep("remove the first item", () =>
            {
                removed = source[0];
                source.RemoveAt(0);
            });
            AddStep("rename the removed item", () => removed.Name.Value = "renamed");
            AddAssert("nothing shows the removed item", () => built.All(t => t.Text.ToString() != "renamed"));

            AddStep("add an item", () => source.Add(new Thing("new")));
            AddUntilStep("a reused row shows it", () => control.ContainerFromIndex(4) != null && built.Any(t => t.Parent != null && t.Text.ToString() == "new"));
        }

        [Test]
        public void RecycledListBoxItemsLetGoOfTheirItems() => checkRecycledContainersLetGo((source, built) => new ListBox
        {
            Size = new Vector2(200, 300),
            ItemTemplate = tracking(built),
            ItemsSource = XamlBinding.ListCopy(new BindingScope(), source),
        });

        [Test]
        public void RecycledGridViewRowsLetGoOfTheirItems() => checkRecycledContainersLetGo((source, built) => new ListView
        {
            Size = new Vector2(300, 300),
            View = new GridView { Columns = { new GridViewColumn { Header = "Name", CellTemplate = tracking(built) } } },
            ItemsSource = XamlBinding.ListCopy(new BindingScope(), source),
        });

        [Test]
        public void RecycledDataGridRowsLetGoOfTheirItems() => checkRecycledContainersLetGo((source, built) =>
        {
            var grid = new DataGrid
            {
                Size = new Vector2(300, 300),
                AutoGenerateColumns = false,
                ItemsSource = XamlBinding.ListCopy(new BindingScope(), source),
            };
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Name", CellTemplate = tracking(built) });
            return grid;
        });

        [Test]
        public void RecycledTreeViewNodesLetGoOfTheirItems()
        {
            var child = new Thing("child");
            var removed = new Thing("removed", child);
            var roots = new BindableList<Thing>(new[] { removed, new Thing("kept") });
            TreeView tree = null;
            TreeViewItem oldNode = null;

            AddStep("create with recycling", () =>
            {
                tree = new TreeView
                {
                    Size = new Vector2(260, 300),
                    ItemTemplate = new HierarchicalDataTemplate<Thing>((t, _) => new TextBlock { Text = t.Name.Value }, (t, scope) => XamlBinding.ListCopy(scope, t.Children)),
                    IsExpandedBinding = new ItemBinding<Thing, bool>("IsExpanded", t => t.IsExpanded, t => t.IsExpanded.Value),
                    ItemsSource = XamlBinding.ListCopy(new BindingScope(), roots),
                };
                VirtualizingPanel.SetIsVirtualizing(tree, true);
                VirtualizingPanel.SetVirtualizationMode(tree, VirtualizationMode.Recycling);
                Child = tree;
            });
            AddUntilStep("nodes exist", () => tree.ContainerFromDataItem(removed) != null);
            AddStep("remove the first root", () =>
            {
                oldNode = tree.ContainerFromDataItem(removed);
                roots.RemoveAt(0);
            });
            AddStep("expand the removed item and give it a child", () =>
            {
                removed.IsExpanded.Value = true;
                removed.Children.Add(new Thing("late"));
            });
            AddAssert("the pooled node doesn't follow it", () => !oldNode.IsExpanded.Value && oldNode.DataItem == null && !oldNode.HasItems);
            AddStep("add a root", () => roots.Add(new Thing("new")));
            AddUntilStep("the new root shows", () => tree.ContainerFromDataItem(roots[^1]) != null);
            AddAssert("kept root unchanged", () => tree.ContainerFromDataItem(roots[0])?.DataItem == roots[0]);
        }

        #endregion
    }
}
