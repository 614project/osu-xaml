using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>ScrollViewer measuring, arranging and scrolling on each axis, including wheel routing.</summary>
    [HeadlessTest]
    public partial class ScrollViewerTests : ManualInputManagerTestScene
    {
        private static StackPanel column(int count, float rowHeight)
        {
            var panel = new StackPanel();
            for (int i = 0; i < count; i++)
                panel.Add(new Box { Height = rowHeight, Colour = i % 2 == 0 ? Colour4.Gray : Colour4.DarkGray });
            return panel;
        }

        private static StackPanel row(int count, float columnWidth)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < count; i++)
                panel.Add(new Box { Width = columnWidth, Colour = i % 2 == 0 ? Colour4.Gray : Colour4.DarkGray });
            return panel;
        }

        [Test]
        public void TallContentScrollsVertically()
        {
            ScrollViewer viewer = null;
            StackPanel content = null;

            AddStep("create", () => Child = viewer = new ScrollViewer { Size = new Vector2(200, 100), Child = content = column(10, 30) });

            AddUntilStep("content measured", () => viewer.ExtentHeight == 300);
            AddAssert("content keeps the viewport width", () => content.DrawWidth == 200 && viewer.ExtentWidth == 200);
            AddAssert("scrolls the overflow", () => viewer.ScrollableHeight == 200 && viewer.ScrollableWidth == 0);

            AddStep("scroll to end", () => viewer.ScrollToEnd(false));
            AddUntilStep("offset at the end", () => viewer.VerticalOffset == 200);
            AddAssert("content moved up", () => Precision(content.ScreenSpaceDrawQuad.TopLeft.Y, viewer.ScreenSpaceDrawQuad.TopLeft.Y - 200));

            AddStep("scroll first row into view", () => viewer.ScrollIntoView(firstChild(content), false));
            AddUntilStep("back at the top", () => viewer.VerticalOffset == 0);
        }

        [Test]
        public void ShortContentFillsTheViewport()
        {
            ScrollViewer viewer = null;
            StackPanel content = null;

            AddStep("create", () => Child = viewer = new ScrollViewer { Size = new Vector2(200, 100), Child = content = column(2, 30) });

            AddUntilStep("stretched to the viewport", () => content.DrawHeight == 100 && content.DrawWidth == 200);
            AddAssert("nothing to scroll", () => viewer.ScrollableHeight == 0);
        }

        [Test]
        public void HorizontalModeArrangesWideContent()
        {
            ScrollViewer viewer = null;
            StackPanel content = null;

            AddStep("create", () => Child = viewer = new ScrollViewer
            {
                Size = new Vector2(200, 100),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Child = content = row(5, 100),
            });

            AddUntilStep("wide content measured", () => viewer.ExtentWidth == 500 && content.DrawWidth == 500);
            AddAssert("height follows the viewport", () => content.DrawHeight == 100 && viewer.ScrollableHeight == 0);
            AddAssert("measure is limited to what is offered", () => viewer.Measure(new Vector2(200, 100)).X == 200);
            AddAssert("unlimited width measures the content", () => viewer.Measure(new Vector2(float.PositiveInfinity, 100)).X == 500);

            AddStep("scroll right", () => viewer.ScrollToRightEnd(false));
            AddUntilStep("horizontal offset", () => viewer.HorizontalOffset == 300 && viewer.VerticalOffset == 0);
        }

        [Test]
        public void BothAxesScrollIndependently()
        {
            ScrollViewer viewer = null;
            int scrollChanges = 0;

            AddStep("create", () =>
            {
                Child = viewer = new ScrollViewer
                {
                    Size = new Vector2(200, 100),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Child = new Box { Size = new Vector2(500, 400) },
                };
                viewer.ScrollChanged += () => scrollChanges++;
            });

            AddUntilStep("both extents", () => viewer.ScrollableWidth == 300 && viewer.ScrollableHeight == 300);

            AddStep("scroll both", () =>
            {
                viewer.ScrollToVerticalOffset(150, false);
                viewer.ScrollToHorizontalOffset(100, false);
            });
            AddUntilStep("offsets", () => viewer.VerticalOffset == 150 && viewer.HorizontalOffset == 100);
            AddAssert("ScrollChanged raised", () => scrollChanges > 0);

            AddStep("home", () => viewer.ScrollToHome(false));
            AddUntilStep("back at the corner", () => viewer.VerticalOffset == 0 && viewer.HorizontalOffset == 0);
        }

        [Test]
        public void WheelScrollsVerticallyAndShiftWheelHorizontally()
        {
            ScrollViewer viewer = null;

            AddStep("create", () => Child = viewer = new ScrollViewer
            {
                Size = new Vector2(200, 100),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Child = new Box { Size = new Vector2(500, 400) },
            });

            AddUntilStep("laid out", () => viewer.ScrollableHeight == 300);
            AddStep("wheel down", () =>
            {
                InputManager.MoveMouseTo(viewer);
                InputManager.ScrollVerticalBy(-1);
            });
            AddUntilStep("vertical only", () => viewer.VerticalOffset > 10 && viewer.HorizontalOffset == 0);

            AddStep("shift + wheel", () =>
            {
                InputManager.PressKey(Key.LShift);
                InputManager.ScrollVerticalBy(-1);
                InputManager.ReleaseKey(Key.LShift);
            });
            AddUntilStep("horizontal", () => viewer.HorizontalOffset > 10);
        }

        [Test]
        public void WheelGoesToTheOuterViewerWhenTheInnerOneHasNothingToScroll()
        {
            ScrollViewer outer = null, inner = null;

            AddStep("create", () =>
            {
                var content = new StackPanel();
                content.Add(inner = new ScrollViewer { Height = 80, Child = column(2, 20) });
                content.Add(new Box { Height = 600 });
                Child = outer = new ScrollViewer { Size = new Vector2(200, 200), Child = content };
            });

            AddUntilStep("laid out", () => outer.ScrollableHeight > 0 && inner.DrawWidth == 200);
            AddStep("wheel over the inner viewer", () =>
            {
                InputManager.MoveMouseTo(inner);
                InputManager.ScrollVerticalBy(-1);
            });
            AddUntilStep("outer scrolled", () => outer.VerticalOffset > 10 && inner.VerticalOffset == 0);
        }

        private static Drawable firstChild(StackPanel panel)
        {
            foreach (var child in panel.Children)
                return child;

            return null;
        }

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 1;
    }
}
