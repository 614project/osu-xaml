using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using NUnit.Framework;
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
    }
}
