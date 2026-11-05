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
    }
}
