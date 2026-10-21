using System.Linq;
using Gallery;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;
using Visibility = OsuXaml.Controls.Visibility;

namespace OsuXaml.Tests
{
    /// <summary>Checks that panels lay out again only when something changed, and that every change still reaches them.</summary>
    [HeadlessTest]
    public partial class LayoutInvalidationTests : TestScene
    {
        [Cached]
        private readonly GalleryViewModel viewModel = new GalleryViewModel();

        private static Box box(float width = 0, float height = 0) => new Box { Width = width, Height = height };

        private static Vector2 posOf(Drawable d) => d.ToSpaceOfOtherDrawable(Vector2.Zero, d.Parent.Parent);

        private static (int measures, int arranges) passes(params Panel[] panels) => (panels.Sum(p => p.MeasureCount), panels.Sum(p => p.ArrangeCount));

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void IdleGalleryPageDoesNotLayOutAgain(int tab)
        {
            Panel[] panels = null;
            (int, int) before = default;

            AddStep("create window", () => Child = new MainWindow());
            AddUntilStep("loaded", () => this.ChildrenOfType<MainWindow>().SingleOrDefault()?.IsLoaded == true);
            AddStep("select tab", () => viewModel.CurrentTab.Value = tab);
            AddWaitStep("settle", 30);
            AddStep("record", () =>
            {
                panels = this.ChildrenOfType<Panel>().ToArray();
                before = passes(panels);
            });
            AddWaitStep("idle", 10);
            AddAssert("panels found", () => panels.Length > 10);
            AddAssert("no panel lays out every frame", () => panels.All(p => !p.LaysOutEveryFrame));
            AddAssert("no measure or arrange while idle", () => passes(panels) == before);
        }

        [Test]
        public void TextChangeMovesWhatFollowsThenSettles()
        {
            StackPanel outer = null, row = null;
            TextBlock label = null;
            Box after = null;
            (int, int) before = default;

            AddStep("create", () =>
            {
                Child = outer = new StackPanel { Width = 400 };
                outer.Add(row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 });
                row.Add(label = new TextBlock { Text = "A" });
                row.Add(after = box(20, 20));
            });
            AddWaitStep("settle", 5);
            AddAssert("box after the text", () => Precision.AlmostEquals(posOf(after).X, label.DrawWidth + 10));

            AddStep("lengthen text", () => label.Text = "A much longer label");
            AddUntilStep("box follows the text", () => label.DrawWidth > 100 && Precision.AlmostEquals(posOf(after).X, label.DrawWidth + 10));

            AddStep("record", () => before = passes(outer, row));
            AddWaitStep("idle", 5);
            AddAssert("no layout after settling", () => passes(outer, row) == before);
        }

        [Test]
        public void ChangeInsideAFixedSizeStaysInside()
        {
            StackPanel outer = null, inner = null;
            TextBlock label = null;
            (int measures, int arranges) outerBefore = default;
            int innerBefore = 0;
            float lineHeight = 0;

            AddStep("create", () =>
            {
                Child = outer = new StackPanel { Width = 400 };
                outer.Add(new Border { Width = 200, Height = 60, Child = inner = new StackPanel() });
                inner.Add(label = new TextBlock { Text = "short" });
                outer.Add(box(height: 20));
            });
            AddWaitStep("settle", 5);
            AddStep("record", () =>
            {
                outerBefore = passes(outer);
                innerBefore = inner.ArrangeCount;
                lineHeight = label.DrawHeight;
            });

            AddStep("wrap the text", () =>
            {
                label.TextWrapping = TextWrapping.Wrap;
                label.Text = "a much longer text that has to wrap onto a second line";
            });
            AddUntilStep("inner panel laid out again", () => inner.ArrangeCount > innerBefore && label.DrawHeight > lineHeight * 1.5f);
            AddWaitStep("settle", 3);
            AddAssert("outer panel untouched", () => passes(outer) == outerBefore);
        }

        [Test]
        public void AttachedPropertyChangeRearranges()
        {
            Box child = null;

            AddStep("create", () =>
            {
                var grid = new Grid { Size = new Vector2(200, 100), ColumnDefinitions = GridLengthCollection.Parse("*,*") };
                grid.Add(child = box(40, 20));
                Layout.SetHorizontalAlignment(child, HorizontalAlignment.Left);
                Child = grid;
            });
            AddWaitStep("settle", 3);
            AddAssert("at the left of the first column", () => posOf(child).X == 0);

            AddStep("move to the second column", () => Grid.SetColumn(child, 1));
            AddUntilStep("at the left of the second column", () => posOf(child).X == 100);

            AddStep("align right", () => Layout.SetHorizontalAlignment(child, HorizontalAlignment.Right));
            AddUntilStep("at the right of the second column", () => posOf(child).X == 160);
        }

        [Test]
        public void CollapsingAChildShrinksThePanel()
        {
            StackPanel panel = null;
            Box first = null;

            AddStep("create", () =>
            {
                Child = panel = new StackPanel { Width = 100 };
                panel.Add(first = box(height: 20));
                panel.Add(box(height: 30));
            });
            AddUntilStep("both children", () => panel.DrawHeight == 50);

            AddStep("collapse", () => Layout.SetVisibility(first, Visibility.Collapsed));
            AddUntilStep("one child", () => panel.DrawHeight == 30);

            AddStep("show again", () => Layout.SetVisibility(first, Visibility.Visible));
            AddUntilStep("both again", () => panel.DrawHeight == 50);

            AddStep("resize", () => first.Height = 40);
            AddUntilStep("follows the size", () => panel.DrawHeight == 70);
        }

        [Test]
        public void ElementThatDoesNotReportIsMeasuredEveryFrame()
        {
            StackPanel outer = null, inner = null;
            QuietElement element = null;

            AddStep("create", () =>
            {
                Child = outer = new StackPanel { Width = 100 };
                outer.Add(inner = new StackPanel());
                inner.Add(element = new QuietElement { Desired = new Vector2(0, 20) });
            });
            AddUntilStep("measured", () => outer.DrawHeight == 20);
            AddAssert("both panels lay out every frame", () => inner.LaysOutEveryFrame && outer.LaysOutEveryFrame);

            AddStep("change without telling", () => element.Desired = new Vector2(0, 45));
            AddUntilStep("still followed", () => outer.DrawHeight == 45);
        }

        [Test]
        public void ElementThatReportsIsMeasuredOnlyWhenItChanges()
        {
            StackPanel panel = null;
            ReportingElement element = null;
            (int, int) before = default;

            AddStep("create", () =>
            {
                Child = panel = new StackPanel { Width = 100 };
                panel.Add(element = new ReportingElement { Desired = new Vector2(0, 20) });
            });
            AddUntilStep("measured", () => panel.DrawHeight == 20);
            AddAssert("panel waits for changes", () => !panel.LaysOutEveryFrame);

            AddStep("record", () => before = passes(panel));
            AddWaitStep("idle", 5);
            AddAssert("no layout while idle", () => passes(panel) == before);

            AddStep("report a change", () => element.Desired = new Vector2(0, 45));
            AddUntilStep("followed", () => panel.DrawHeight == 45);
        }

        /// <summary>A custom element that changes its desired size without telling anyone, as elements written before invalidation did.</summary>
        private partial class QuietElement : CompositeDrawable, ILayoutElement
        {
            public Vector2 Desired;

            public Vector2 Measure(Vector2 availableSize) => Desired;
        }

        private partial class ReportingElement : CompositeDrawable, ILayoutElement
        {
            private Vector2 desired;

            public Vector2 Desired
            {
                get => desired;
                set => Layout.SetAffectsMeasure(this, ref desired, value);
            }

            public bool ReportsMeasureChanges => true;

            public Vector2 Measure(Vector2 availableSize) => desired;
        }
    }
}
