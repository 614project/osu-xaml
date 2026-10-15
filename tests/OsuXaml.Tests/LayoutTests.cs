using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>Checks panel geometry in a headless host, the way WPF would lay the same tree out.</summary>
    [HeadlessTest]
    public partial class LayoutTests : TestScene
    {
        private static Box box(float width = 0, float height = 0) => new Box { Width = width, Height = height };

        private static Vector2 posOf(Drawable d) => d.ToSpaceOfOtherDrawable(Vector2.Zero, d.Parent.Parent);

        [Test]
        public void StackPanelStacksWithSpacingAndStretchesAcross()
        {
            Box a = null, b = null, c = null;
            StackPanel panel = null;

            AddStep("create", () => Child = panel = new StackPanel { Width = 200, Spacing = 10 });
            AddStep("add children", () =>
            {
                panel.Add(a = box(height: 20));
                panel.Add(b = box(height: 30));
                panel.Add(c = box(50, 10));
            });
            AddAssert("vertical positions", () => posOf(a).Y == 0 && posOf(b).Y == 30 && posOf(c).Y == 70);
            AddAssert("stretched width", () => a.DrawWidth == 200);
            AddAssert("explicit width centred", () => c.DrawWidth == 50 && posOf(c).X == 75);
            AddAssert("sized to content height", () => panel.DrawHeight == 80);
        }

        [Test]
        public void GridSizesPixelAutoAndStarTracks()
        {
            Box header = null, left = null, right = null;

            AddStep("create", () =>
            {
                var grid = new Grid
                {
                    Size = new Vector2(400, 300),
                    RowDefinitions = GridLengthCollection.Parse("Auto,*"),
                    ColumnDefinitions = GridLengthCollection.Parse("100,*,2*"),
                };

                grid.Add(header = box(height: 40));
                Grid.SetColumnSpan(header, 3);
                grid.Add(left = box());
                Grid.SetRow(left, 1);
                grid.Add(right = box());
                Grid.SetRow(right, 1);
                Grid.SetColumn(right, 2);

                Child = grid;
            });

            AddAssert("header spans all columns", () => header.DrawWidth == 400 && header.DrawHeight == 40);
            AddAssert("auto row then star row", () => posOf(left).Y == 40 && left.DrawHeight == 260);
            AddAssert("pixel column", () => left.DrawWidth == 100);
            AddAssert("2* column is two thirds of the rest", () => posOf(right).X == 200 && right.DrawWidth == 200);
        }

        [Test]
        public void DockPanelDocksAndFills()
        {
            Box top = null, left = null, fill = null;

            AddStep("create", () =>
            {
                var dock = new DockPanel { Size = new Vector2(300, 200) };
                dock.Add(top = box(height: 30));
                DockPanel.SetDock(top, Dock.Top);
                dock.Add(left = box(width: 50));
                DockPanel.SetDock(left, Dock.Left);
                dock.Add(fill = box());
                Child = dock;
            });

            AddAssert("top bar", () => top.DrawWidth == 300 && top.DrawHeight == 30);
            AddAssert("left bar below top", () => posOf(left).Y == 30 && left.DrawHeight == 170);
            AddAssert("last child fills", () => posOf(fill).X == 50 && fill.DrawSize == new Vector2(250, 170));
        }

        [Test]
        public void AlignmentPlacesFixedSizeChildren()
        {
            Box child = null;

            AddStep("create", () =>
            {
                var grid = new Grid { Size = new Vector2(200, 100) };
                grid.Add(child = box(40, 20));
                Layout.SetHorizontalAlignment(child, HorizontalAlignment.Right);
                Layout.SetVerticalAlignment(child, VerticalAlignment.Bottom);
                Child = grid;
            });

            AddAssert("bottom right", () => posOf(child) == new Vector2(160, 80));
        }

        [Test]
        public void WrapPanelWrapsAtTheEdge()
        {
            Box[] boxes = null;

            AddStep("create", () =>
            {
                var wrap = new WrapPanel { Width = 100, Spacing = 10 };
                boxes = Enumerable.Range(0, 3).Select(_ => box(40, 20)).ToArray();
                foreach (var b in boxes)
                    wrap.Add(b);
                Child = wrap;
            });

            AddAssert("two per line", () => posOf(boxes[1]).X == 50 && posOf(boxes[2]) == new Vector2(0, 30));
        }

        [Test]
        public void BorderSizesToChildPlusPaddingInAStackPanel()
        {
            Border border = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300, Orientation = Orientation.Horizontal };
                panel.Add(border = new Border { Padding = new MarginPadding(10), BorderThickness = 2, Child = box(50, 20) });
                Child = panel;
            });

            AddAssert("border width = child + padding + border", () => border.DrawWidth == 74);
            AddAssert("border height stretches to the row", () => border.DrawHeight == 44);
        }
    }
}
