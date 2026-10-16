using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Thumb drag events and the standalone ScrollBar (thumb length, dragging, paging, arrows, wheel, coercion) with real mouse input.</summary>
    [HeadlessTest]
    public partial class ScrollBarTests : ManualInputManagerTestScene
    {
        [Test]
        public void ThumbReportsDragsInItsOwnCoordinates()
        {
            Thumb thumb = null;
            DragStartedEventArgs started = null;
            DragCompletedEventArgs completed = null;
            var deltas = new List<Vector2>();

            AddStep("create", () =>
            {
                var canvas = new Canvas { Size = new Vector2(400, 300) };
                canvas.Add(thumb = new Thumb { Size = new Vector2(40) });
                Canvas.SetLeft(thumb, 20);
                Canvas.SetTop(thumb, 20);

                // The WPF pattern: move the thumb by each change.
                thumb.DragStarted += e => started = e;
                thumb.DragDelta += e =>
                {
                    deltas.Add(new Vector2((float)e.HorizontalChange, (float)e.VerticalChange));
                    Canvas.SetLeft(thumb, Layout.Get(thumb).Left + (float)e.HorizontalChange);
                    Canvas.SetTop(thumb, Layout.Get(thumb).Top + (float)e.VerticalChange);
                };
                thumb.DragCompleted += e => completed = e;
                Child = canvas;
            });

            AddUntilStep("placed", () => thumb.IsLoaded && thumb.DrawWidth == 40);
            AddStep("hover", () => InputManager.MoveMouseTo(thumb));
            AddUntilStep("brightens", () => Precision(fill(thumb).Alpha, 0.85f, 0.01f));

            AddStep("press 5px into the thumb", () =>
            {
                InputManager.MoveMouseTo(thumb.ToScreenSpace(new Vector2(5)));
                InputManager.PressButton(MouseButton.Left);
            });
            AddAssert("started at the grab offset", () => started != null && Precision((float)started.HorizontalOffset, 5) && Precision((float)started.VerticalOffset, 5));
            AddAssert("dragging", () => thumb.IsDragging);
            AddUntilStep("opaque while dragged", () => fill(thumb).Alpha == 1);

            AddStep("move 30 right and 12 down", () => InputManager.MoveMouseTo(thumb.ToScreenSpace(new Vector2(35, 17))));
            AddUntilStep("thumb followed", () => Precision(Layout.Get(thumb).Left, 50) && Precision(Layout.Get(thumb).Top, 32));
            AddStep("move 20 more right", () => InputManager.MoveMouseTo(thumb.ToScreenSpace(new Vector2(25, 5))));
            AddUntilStep("followed again", () => Precision(Layout.Get(thumb).Left, 70) && Precision(Layout.Get(thumb).Top, 32));
            AddAssert("each delta was the movement since the last", () => deltas.Sum(d => d.X) is var x && Precision(x, 50));

            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("completed where it was grabbed", () => completed != null && !completed.Canceled && Precision((float)completed.HorizontalChange, 0) && !thumb.IsDragging);
        }

        [Test]
        public void ThumbThatStaysPutReportsTheTotalDistance()
        {
            Thumb thumb = null;
            DragDeltaEventArgs last = null;
            DragCompletedEventArgs completed = null;

            AddStep("create", () =>
            {
                Child = thumb = new Thumb { Size = new Vector2(40), Position = new Vector2(50) };
                thumb.DragDelta += e => last = e;
                thumb.DragCompleted += e => completed = e;
            });

            AddStep("press", () =>
            {
                InputManager.MoveMouseTo(thumb);
                InputManager.PressButton(MouseButton.Left);
            });
            AddStep("move", () => InputManager.MoveMouseTo(thumb.ToScreenSpace(new Vector2(20 + 40, 20 - 10))));
            AddStep("move further", () => InputManager.MoveMouseTo(thumb.ToScreenSpace(new Vector2(20 + 60, 20 - 10))));
            AddAssert("change is from the grab point", () => Precision((float)last.HorizontalChange, 60) && Precision((float)last.VerticalChange, -10));

            AddStep("cancel", () => thumb.CancelDrag());
            AddAssert("completed as cancelled", () => completed.Canceled && !thumb.IsDragging);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        }

        [Test]
        public void ThumbLengthAndPositionFollowViewportAndValue()
        {
            ScrollBar bar = null;

            AddStep("create", () => Child = bar = new ScrollBar { Height = 200, Maximum = 100, ViewportSize = 100 });

            // 14px arrows at both ends leave a 172px track; a viewport as large as the range makes the thumb half of it.
            AddUntilStep("thumb is half the track", () => Precision(bar.Thumb.DrawHeight, 86));
            AddAssert("at the top of the track", () => Precision(bar.Thumb.Y, 14));
            AddAssert("thin and inset", () => bar.DrawWidth == 14 && bar.Thumb.DrawWidth < 14);

            AddStep("value 50", () => bar.Value.Value = 50);
            AddAssert("moved half the travel at once", () => Precision(bar.Thumb.Y, 14 + 43));

            AddStep("no viewport", () => bar.ViewportSize = 0);
            AddAssert("small fixed thumb", () => Precision(bar.Thumb.DrawHeight, 20));

            AddStep("nothing to scroll", () => bar.Maximum = 0);
            AddAssert("value coerced, thumb hidden", () => bar.Value.Value == 0 && bar.Thumb.Alpha == 0);
        }

        [Test]
        public void DraggingTheThumbScrollsTwoWay()
        {
            ScrollBar bar = null;
            var offset = new BindableDouble();
            var events = new List<ScrollEventType>();

            AddStep("create", () =>
            {
                Child = bar = new ScrollBar { Height = 200, Maximum = 100, ViewportSize = 100 };
                XamlBinding.TwoWay(bar.Value, offset);
                bar.Scroll += e => events.Add(e.ScrollEventType);
            });
            AddUntilStep("laid out", () => Precision(bar.Thumb.DrawHeight, 86));

            AddStep("grab the thumb", () =>
            {
                InputManager.MoveMouseTo(bar.Thumb);
                InputManager.PressButton(MouseButton.Left);
            });
            // The thumb travels 86px for the whole range.
            AddStep("drag down half the travel", () => InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 14 + 43 + 43))));
            AddAssert("value and source follow", () => Precision((float)bar.Value.Value, 50, 0.6f) && offset.Value == bar.Value.Value);
            AddAssert("thumb under the pointer", () => Precision(bar.Thumb.Y, 14 + 43, 0.6f));
            AddStep("drag far past the end", () => InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 400))));
            AddAssert("stops at the maximum", () => bar.Value.Value == 100);
            AddStep("back up to the middle", () => InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 14 + 43 + 43))));
            AddAssert("follows from where it was grabbed", () => Precision((float)bar.Value.Value, 50, 0.6f));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("ThumbTrack, then ThumbPosition and EndScroll", () => events.First() == ScrollEventType.ThumbTrack
                                                                      && events.TakeLast(2).SequenceEqual(new[] { ScrollEventType.ThumbPosition, ScrollEventType.EndScroll }));

            AddStep("view model scrolls", () => offset.Value = 20);
            AddAssert("bar follows", () => bar.Value.Value == 20 && Precision(bar.Thumb.Y, 14 + 0.2f * 86));
        }

        [Test]
        public void PressingTheTrackPagesUntilTheThumbReachesThePointer()
        {
            ScrollBar bar = null;

            AddStep("create", () => Child = bar = new ScrollBar { Height = 200, Maximum = 100, ViewportSize = 100 });
            AddUntilStep("laid out", () => Precision(bar.Thumb.DrawHeight, 86));

            AddStep("click below the thumb", () =>
            {
                InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 180)));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("paged by LargeChange", () => bar.Value.Value == 10);

            AddStep("click above the thumb", () =>
            {
                InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 18)));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("paged back", () => bar.Value.Value == 0);

            // The thumb's end passes y=120 at value 30 (14 + 0.3 * 86 + 86 = 125.8).
            AddStep("hold the track at y=120", () =>
            {
                InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 120)));
                InputManager.PressButton(MouseButton.Left);
            });
            AddUntilStep("repeats until the thumb is under the pointer", () => bar.Value.Value == 30);
            AddWaitStep("hold a little longer", 5);
            AddAssert("then stops", () => bar.Value.Value == 30);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        }

        [Test]
        public void ArrowsWheelAndCoercion()
        {
            ScrollBar bar = null;

            AddStep("create with the value before the range", () => Child = bar = new ScrollBar { Height = 200, Value = { Value = 500 }, Maximum = 1000, SmallChange = 5 });
            AddAssert("value kept once the range allows it", () => bar.Value.Value == 500);

            AddStep("click the down arrow", () =>
            {
                InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 193)));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("stepped by SmallChange", () => bar.Value.Value == 505);
            AddStep("click the up arrow", () =>
            {
                InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(7, 7)));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("stepped back", () => bar.Value.Value == 500);

            AddStep("wheel down", () =>
            {
                InputManager.MoveMouseTo(bar);
                InputManager.ScrollVerticalBy(-1);
            });
            AddAssert("three steps a notch", () => bar.Value.Value == 515);

            AddStep("shrink the range", () => bar.Maximum = 100);
            AddAssert("coerced", () => bar.Value.Value == 100);
            AddStep("grow it again", () => bar.Maximum = 1000);
            AddAssert("the requested value comes back, like WPF", () => bar.Value.Value == 515);

            AddStep("hide arrows", () => bar.ShowArrows = false);
            AddUntilStep("track spans the bar", () => Precision(bar.Thumb.Y, 0.515f * (200 - 20)));
        }

        [Test]
        public void HorizontalBarInAPanel()
        {
            ScrollBar bar = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300 };
                panel.Add(bar = new ScrollBar { Orientation = Orientation.Horizontal, Maximum = 10, ViewportSize = 10, SmallChange = 1 });
                Child = panel;
            });

            AddUntilStep("wide and thin", () => bar.DrawWidth == 300 && bar.DrawHeight == 14);
            AddAssert("thumb half the track", () => Precision(bar.Thumb.DrawWidth, (300 - 28) / 2f));
            AddStep("click the right arrow", () =>
            {
                InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(293, 7)));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("stepped", () => bar.Value.Value == 1);
            AddStep("grab the thumb", () =>
            {
                InputManager.MoveMouseTo(bar.Thumb);
                InputManager.PressButton(MouseButton.Left);
            });
            AddStep("drag right past the end", () => InputManager.MoveMouseTo(bar.ToScreenSpace(new Vector2(500, 7))));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("at the end", () => bar.Value.Value == 10);
        }

        private static Box fill(Thumb thumb) => thumb.ChildrenOfType<Box>().Single();

        private static bool Precision(float a, float b, float tolerance = 1) => System.Math.Abs(a - b) < tolerance;
    }
}
