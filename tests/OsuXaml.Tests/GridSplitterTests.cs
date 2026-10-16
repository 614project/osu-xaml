using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osu.Framework.Utils;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>GridSplitter written the way a WPF developer would, through the generator.</summary>
    [TestFixture]
    public class GridSplitterGeneratorTests
    {
        [Test]
        public void SplitterInItsOwnColumnAndRow()
        {
            string source = ShapesTestData.Compiles(@"
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width=""200"" MinWidth=""80"" />
      <ColumnDefinition Width=""Auto"" />
      <ColumnDefinition Width=""*"" />
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
      <RowDefinition Height=""*"" />
      <RowDefinition Height=""Auto"" />
      <RowDefinition Height=""100"" />
    </Grid.RowDefinitions>
    <Border Background=""#313244"" />
    <GridSplitter Grid.Column=""1"" Grid.RowSpan=""3"" Width=""5"" HorizontalAlignment=""Stretch"" Background=""#45475a"" DragCompleted=""OnSplitterDone"" />
    <GridSplitter Grid.Row=""1"" Grid.Column=""2"" Height=""5"" HorizontalAlignment=""Stretch"" ResizeDirection=""Rows"" ResizeBehavior=""PreviousAndNext"" DragIncrement=""10"" KeyboardIncrement=""20"" />
  </Grid>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.GridSplitter()"));
            Assert.That(source, Does.Contain("ResizeDirection = global::OsuXaml.Controls.GridResizeDirection.Rows"));
            Assert.That(source, Does.Contain(".DragCompleted += () => this.OnSplitterDone()"));
        }
    }

    /// <summary>GridSplitter at runtime: which tracks it picks and how a drag changes their lengths.</summary>
    [HeadlessTest]
    public partial class GridSplitterTests : TestScene
    {
        private Grid grid;
        private GridSplitter splitter;

        private void createColumns(string columns, System.Action<GridSplitter> setup = null)
        {
            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(405, 100), ColumnDefinitions = GridLengthCollection.Parse(columns) };
                grid.Add(new Box());
                grid.Add(splitter = new GridSplitter());
                Grid.SetColumn(splitter, 1);
                setup?.Invoke(splitter);
                var right = new Box();
                grid.Add(right);
                Grid.SetColumn(right, 2);
                Child = grid;
            });

            AddUntilStep("laid out", () => grid.ColumnDefinitions[1].ActualLength == 5);
        }

        private float width(int column) => grid.ColumnDefinitions[column].ActualLength;

        [Test]
        public void DefaultSplitterIsAVerticalBarResizingColumns()
        {
            createColumns("*,Auto,*");

            AddAssert("five pixels wide and full height", () => splitter.DrawWidth == 5 && splitter.DrawHeight == 100);
            AddAssert("resizes columns", () => splitter.EffectiveResizeDirection == GridResizeDirection.Columns);
            AddAssert("star columns share the rest", () => width(0) == 200 && width(2) == 200);
        }

        [Test]
        public void DraggingStarColumnsKeepsThemProportional()
        {
            createColumns("*,Auto,*");

            AddStep("drag 50 right", () =>
            {
                Assert.That(splitter.BeginResize(), Is.True);
                splitter.ResizeTo(50);
                splitter.EndResize();
            });
            AddUntilStep("left grew, right shrank", () => width(0) == 250 && width(2) == 150);
            AddAssert("both stay star", () => grid.ColumnDefinitions[0].Length.IsStar && grid.ColumnDefinitions[2].Length.IsStar);

            AddStep("grid gets wider", () => grid.Width = 805);
            AddUntilStep("the split scales with it", () => width(0) == 500 && width(2) == 300);
        }

        [Test]
        public void PixelColumnBecomesTheDraggedSize()
        {
            createColumns("150,Auto,*");

            AddStep("drag 30 left", () =>
            {
                splitter.BeginResize();
                splitter.ResizeTo(-30);
                splitter.EndResize();
            });
            AddUntilStep("pixel column shrank", () => width(0) == 120 && width(2) == 280);
            AddAssert("left is a pixel length", () => grid.ColumnDefinitions[0].Length.IsAbsolute && grid.ColumnDefinitions[0].Length.Value == 120);
            AddAssert("right is still star", () => grid.ColumnDefinitions[2].Length.IsStar);
        }

        [Test]
        public void LimitsStopTheDrag()
        {
            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(405, 100) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 150 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 100 });
                grid.Add(splitter = new GridSplitter());
                Grid.SetColumn(splitter, 1);
                Child = grid;
            });
            AddUntilStep("laid out", () => width(0) == 200);

            AddStep("drag far left", () =>
            {
                splitter.BeginResize();
                splitter.ResizeTo(-500);
            });
            AddUntilStep("stopped at the left minimum", () => width(0) == 150 && width(2) == 250);
            AddStep("drag far right", () => splitter.ResizeTo(500));
            AddUntilStep("stopped at the right minimum", () => width(0) == 300 && width(2) == 100);
            AddStep("end", () => splitter.EndResize());
        }

        [Test]
        public void EscapeCancelsAndRestoresLengths()
        {
            createColumns("150,Auto,*");

            AddStep("drag then cancel", () =>
            {
                splitter.BeginResize();
                splitter.ResizeTo(80);
                splitter.CancelResize();
            });
            AddUntilStep("back to the original", () => width(0) == 150 && grid.ColumnDefinitions[0].Length.Value == 150);
            AddAssert("no longer dragging", () => !splitter.IsDragging);
        }

        [Test]
        public void DragIncrementRoundsTheOffset()
        {
            createColumns("150,Auto,*", s => s.DragIncrement = 25);

            AddStep("drag 37", () =>
            {
                splitter.BeginResize();
                splitter.ResizeTo(37);
                splitter.EndResize();
            });
            AddUntilStep("rounded to one step", () => width(0) == 175);
        }

        [Test]
        public void RightAlignedSplitterResizesItsOwnColumn()
        {
            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("100,*") };
                grid.Add(new Box());
                grid.Add(splitter = new GridSplitter());
                Layout.SetHorizontalAlignment(splitter, HorizontalAlignment.Right);
                Child = grid;
            });
            AddUntilStep("laid out", () => width(0) == 100);
            AddAssert("sits on the right edge of its column", () => splitter.ToSpaceOfOtherDrawable(Vector2.Zero, grid).X == 95);

            AddStep("drag 40", () =>
            {
                Assert.That(splitter.BeginResize(), Is.True);
                splitter.ResizeTo(40);
                splitter.EndResize();
            });
            AddUntilStep("current and next resized", () => width(0) == 140 && width(1) == 260);
        }

        [Test]
        public void HorizontalBarResizesRows()
        {
            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(200, 305), RowDefinitions = GridLengthCollection.Parse("*,Auto,2*") };
                grid.Add(splitter = new GridSplitter { Height = 5 });
                Grid.SetRow(splitter, 1);
                Child = grid;
            });
            AddUntilStep("laid out", () => grid.RowDefinitions[1].ActualLength == 5);
            AddAssert("full width bar", () => splitter.DrawWidth == 200 && splitter.DrawHeight == 5);
            AddAssert("resizes rows", () => splitter.EffectiveResizeDirection == GridResizeDirection.Rows);

            AddStep("drag 100 down", () =>
            {
                splitter.BeginResize();
                splitter.ResizeTo(100);
                splitter.EndResize();
            });
            AddUntilStep("rows moved", () => grid.RowDefinitions[0].ActualLength == 200 && grid.RowDefinitions[2].ActualLength == 100);
        }

        [Test]
        public void SplitterOutsideAGridDoesNothing()
        {
            AddStep("create", () => Child = splitter = new GridSplitter { Height = 100 });
            AddAssert("cannot begin", () => !splitter.BeginResize());
        }
    }

    /// <summary>GridSplitter driven by real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class GridSplitterInputTests : ManualInputManagerTestScene
    {
        private Grid grid;
        private GridSplitter splitter;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create", () =>
            {
                grid = new Grid
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(405, 100),
                    ColumnDefinitions = GridLengthCollection.Parse("*,Auto,*"),
                };
                grid.Add(splitter = new GridSplitter());
                Grid.SetColumn(splitter, 1);
                Child = grid;
            });
            AddUntilStep("laid out", () => grid.ColumnDefinitions[0].ActualLength == 200);
        }

        private float width(int column) => grid.ColumnDefinitions[column].ActualLength;

        [Test]
        public void MouseDragMovesTheSplit()
        {
            AddStep("press on the splitter", () =>
            {
                InputManager.MoveMouseTo(splitter);
                InputManager.PressButton(MouseButton.Left);
            });
            AddStep("drag 60 right", () => InputManager.MoveMouseTo(grid.ToScreenSpace(grid.ToLocalSpace(splitter.ScreenSpaceDrawQuad.Centre) + new Vector2(60, 0))));
            AddUntilStep("dragging", () => splitter.IsDragging);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddUntilStep("split moved", () => Precision.AlmostEquals(width(0), 260, 1) && Precision.AlmostEquals(width(2), 140, 1));
            AddAssert("drag finished, splitter focused", () => !splitter.IsDragging && splitter.HasFocus);
        }

        [Test]
        public void ArrowKeysMoveAFocusedSplitter()
        {
            AddStep("click the splitter", () =>
            {
                InputManager.MoveMouseTo(splitter);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("focused", () => splitter.HasFocus);
            AddStep("press left", () => InputManager.Key(Key.Left));
            AddUntilStep("moved 10 left", () => width(0) == 190 && width(2) == 210);
            AddStep("press left again", () => InputManager.Key(Key.Left));
            AddUntilStep("still follows the keys after moving from under the pointer", () => width(0) == 180 && width(2) == 220);

            AddStep("click elsewhere and press right", () =>
            {
                InputManager.MoveMouseTo(Vector2.Zero);
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.Right);
            });
            AddAssert("unfocused, unhovered splitter ignores keys", () => width(0) == 180);
        }
    }
}
