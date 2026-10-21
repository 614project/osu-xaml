using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>Children spanning several rows or columns, sized into the tracks they span the way WPF does.</summary>
    [HeadlessTest]
    public partial class GridSpanTests : TestScene
    {
        private static Box box(float width = 0, float height = 0) => new Box { Width = width, Height = height };

        private static Box place(Grid grid, Box child, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
        {
            grid.Add(child);
            Grid.SetRow(child, row);
            Grid.SetColumn(child, column);
            Grid.SetRowSpan(child, rowSpan);
            Grid.SetColumnSpan(child, columnSpan);
            return child;
        }

        private static float[] widths(Grid grid) => grid.ColumnDefinitions.Select(d => d.ActualLength).ToArray();

        private static float[] heights(Grid grid) => grid.RowDefinitions.Select(d => d.ActualLength).ToArray();

        [Test]
        public void SpanningHeaderWidensAutoColumnsEqually()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("Auto,Auto,*"), RowDefinitions = GridLengthCollection.Parse("Auto,Auto") };
                place(grid, box(200, 20), columnSpan: 2);
                place(grid, box(50, 20), row: 1);
                place(grid, box(30, 20), row: 1, column: 1);
                Child = grid;
            });

            // 200 needs 120 more than the 50 + 30 the columns already have; each Auto column takes half.
            AddAssert("auto columns hold the header", () => widths(grid) is [110, 90, 200]);
        }

        [Test]
        public void SpacingBetweenSpannedColumnsCountsTowardsTheSpan()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("Auto,Auto,*"), ColumnSpacing = 10 };
                place(grid, box(200, 20), columnSpan: 2);
                Child = grid;
            });

            AddAssert("columns plus the gap make 200", () => widths(grid) is [95, 95, 190]);
        }

        [Test]
        public void SpanningChildWidensAutoRows()
        {
            Grid grid = null;
            Box below = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(200, 300), ColumnDefinitions = GridLengthCollection.Parse("*,*"), RowDefinitions = GridLengthCollection.Parse("Auto,Auto,*") };
                place(grid, box(height: 20));
                place(grid, box(height: 30), row: 1);
                place(grid, box(height: 100), column: 1, rowSpan: 2);
                below = place(grid, box(), row: 2);
                Child = grid;
            });

            AddAssert("auto rows hold the tall child", () => heights(grid) is [45, 55, 200]);
            AddAssert("star row starts below it", () => below.ToSpaceOfOtherDrawable(Vector2.Zero, grid).Y == 100);
        }

        [Test]
        public void SpanThroughAStarColumnLeavesAutoColumnsTight()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(300, 100), ColumnDefinitions = GridLengthCollection.Parse("Auto,*") };
                place(grid, box(50, 20));
                place(grid, box(400, 20), row: 0, columnSpan: 2);
                Child = grid;
            });

            AddAssert("auto column keeps its own content", () => widths(grid) is [50, 250]);
        }

        [Test]
        public void PixelColumnTakesTheExtraBeforeAutoColumns()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("100,Auto,*") };
                place(grid, box(50, 20), column: 1);
                place(grid, box(300, 20), columnSpan: 2);
                Child = grid;
            });

            // WPF keeps Auto columns tight and widens the other columns in the span first, fixed ones included.
            AddAssert("pixel column grows", () => widths(grid) is [250, 50, 100]);
        }

        [Test]
        public void MaxWidthPassesTheRestToTheOtherColumns()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 60 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                place(grid, box(200, 20), columnSpan: 2);
                Child = grid;
            });

            AddAssert("capped column stops at its maximum", () => widths(grid) is [60, 140, 200]);
        }

        [Test]
        public void SpanLargerThanEveryMaximumGrowsTheSmallerColumnsFaster()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 20 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 60 });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                place(grid, box(100, 20), columnSpan: 2);
                Child = grid;
            });

            // Past both maximums (80), the 20 extra goes to the narrower column until both reach 60, as in WPF.
            AddAssert("narrow column catches up", () => widths(grid) is [40, 60, 300]);
        }

        [Test]
        public void GridSizedToContentGivesTheExtraToItsStarColumn()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { ColumnDefinitions = GridLengthCollection.Parse("Auto,*"), RowDefinitions = GridLengthCollection.Parse("Auto") };
                place(grid, box(50, 20));
                place(grid, box(200, 20), columnSpan: 2);
                Child = grid;
            });

            AddUntilStep("grid is as wide as the span", () => grid.DrawWidth == 200);
            AddAssert("auto column stays tight", () => widths(grid) is [50, 150]);
        }

        [Test]
        public void NarrowerSpansAreFittedFirst()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("Auto,Auto,Auto,*") };
                place(grid, box(90, 20), columnSpan: 3);
                place(grid, box(100, 20), row: 0, column: 1, columnSpan: 2);
                Child = grid;
            });

            // The two-column span makes columns 1 and 2 hold 100, which already covers the three-column span.
            AddAssert("wider span adds nothing", () => widths(grid) is [0, 50, 50, 300]);
        }

        [Test]
        public void SpanClippedAtTheLastColumnCountsAsASingleColumn()
        {
            Grid grid = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("*,Auto") };
                place(grid, box(80, 20), column: 1, columnSpan: 3);
                Child = grid;
            });

            AddAssert("auto column fits it", () => widths(grid) is [320, 80]);
        }
    }
}
