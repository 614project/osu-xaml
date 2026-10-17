using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>The slider's track, fill, thumb and tick geometry, and grabbing the thumb with real mouse input.</summary>
    [HeadlessTest]
    public partial class SliderLookTests : ManualInputManagerTestScene
    {
        // The thumb is 18 wide, so its centre travels from 9 to width - 9.
        private const float radius = 9;

        [Test]
        public void ThumbAndFillSitAtTheValue()
        {
            Slider slider = null;

            AddStep("create", () => Child = slider = new Slider { Width = 200, Value = { Value = 50 } });

            AddUntilStep("thumb centred on the value", () => Precision(centreOf(ring(slider)).X, local(slider, radius + 0.5f * (200 - 2 * radius), 0).X));
            AddAssert("thumb is vertically centred", () => Precision(centreOf(ring(slider)).Y, centreOf(slider).Y));
            AddUntilStep("fill ends under the thumb centre", () => Precision(fill(slider).ScreenSpaceDrawQuad.TopRight.X, centreOf(ring(slider)).X));
            AddAssert("track is thin", () => fill(slider).DrawHeight <= 6 && slider.DrawHeight == 20);

            AddStep("minimum", () => slider.Value.Value = 0);
            AddUntilStep("thumb at the start, inside the slider", () => Precision(ring(slider).ScreenSpaceDrawQuad.TopLeft.X, slider.ScreenSpaceDrawQuad.TopLeft.X));
            AddStep("maximum", () => slider.Value.Value = 100);
            AddUntilStep("thumb at the end, inside the slider", () => Precision(ring(slider).ScreenSpaceDrawQuad.TopRight.X, slider.ScreenSpaceDrawQuad.TopRight.X));
        }

        [Test]
        public void ThumbGrowsOnHoverAndWhileDragged()
        {
            Slider slider = null;

            AddStep("create", () => Child = slider = new Slider { Width = 200, Value = { Value = 50 } });
            AddStep("mouse away", () => InputManager.MoveMouseTo(slider, new Vector2(0, 100)));
            AddUntilStep("idle dot", () => Precision(dot(slider).Scale.X, 0.55f, 0.01f));

            AddStep("hover", () => InputManager.MoveMouseTo(slider));
            AddUntilStep("dot grows", () => Precision(dot(slider).Scale.X, 0.7f, 0.01f));

            AddStep("press", () => InputManager.PressButton(MouseButton.Left));
            AddUntilStep("dot grows further", () => Precision(dot(slider).Scale.X, 0.8f, 0.01f));
            AddStep("release and leave", () =>
            {
                InputManager.ReleaseButton(MouseButton.Left);
                InputManager.MoveMouseTo(slider, new Vector2(0, 100));
            });
            AddUntilStep("back to idle", () => Precision(dot(slider).Scale.X, 0.55f, 0.01f));
        }

        [Test]
        public void GrabbingTheThumbDragsRelativeToTheGrabPoint()
        {
            Slider slider = null;

            AddStep("create", () => Child = slider = new Slider { Width = 200, Value = { Value = 50 } });
            AddUntilStep("thumb placed", () => Precision(centreOf(ring(slider)).X, local(slider, 100, 0).X));

            // Grab 4px right of the thumb's centre, then move a quarter of the usable width.
            AddStep("grab the thumb off-centre", () =>
            {
                InputManager.MoveMouseTo(local(slider, 100 + 4, 10));
                InputManager.PressButton(MouseButton.Left);
            });
            AddAssert("pressing the thumb doesn't move it", () => slider.Value.Value == 50);
            AddStep("drag right", () => InputManager.MoveMouseTo(local(slider, 100 + 4 + (200 - 2 * radius) / 4, 10)));
            AddAssert("moved by the drag distance", () => Precision((float)slider.Value.Value, 75, 0.5f));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("value kept", () => Precision((float)slider.Value.Value, 75, 0.5f));

            AddStep("click the track at 25%", () =>
            {
                InputManager.MoveMouseTo(local(slider, radius + 0.25f * (200 - 2 * radius), 10));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("jumps to the click", () => Precision((float)slider.Value.Value, 25, 0.5f));
        }

        [Test]
        public void TicksGetTheirOwnRowsClearOfTheThumb()
        {
            Slider slider = null;

            AddStep("create", () => Child = slider = new Slider { Width = 200, TickPlacement = TickPlacement.BottomRight, TickFrequency = 25, Value = { Value = 50 } });

            AddUntilStep("ticks drawn", () => ticks(slider).Length == 3);
            AddAssert("slider grew a row for them", () => slider.DrawHeight > 20);
            AddAssert("ticks below the thumb", () => ticks(slider).All(t => t.ScreenSpaceDrawQuad.TopLeft.Y >= ring(slider).ScreenSpaceDrawQuad.BottomLeft.Y));
            AddAssert("ticks line up with thumb positions", () => Precision(centreOf(ticks(slider)[1]).X, local(slider, 100, 0).X));

            AddStep("ticks on both sides", () => slider.TickPlacement = TickPlacement.Both);
            AddUntilStep("two rows", () => ticks(slider).Length == 6 && slider.DrawHeight > 30);
            AddAssert("top ticks above the thumb", () => ticks(slider).Count(t => t.ScreenSpaceDrawQuad.BottomLeft.Y <= ring(slider).ScreenSpaceDrawQuad.TopLeft.Y) == 3);

            AddStep("no ticks", () => slider.TickPlacement = TickPlacement.None);
            AddUntilStep("back to the plain size", () => ticks(slider).Length == 0 && slider.DrawHeight == 20);
        }

        [Test]
        public void VerticalSliderFillsFromTheBottomWithTicksBeside()
        {
            Slider slider = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Height = 200 };
                panel.Add(slider = new Slider { Orientation = Orientation.Vertical, TickPlacement = TickPlacement.TopLeft, TickFrequency = 50, Value = { Value = 25 } });
                Child = panel;
            });

            AddUntilStep("tall, widened for the ticks", () => slider.DrawHeight == 200 && slider.DrawWidth == 27);
            AddUntilStep("thumb a quarter of the way up", () => Precision(centreOf(ring(slider)).Y, local(slider, 0, 200 - radius - 0.25f * (200 - 2 * radius)).Y));
            AddAssert("fill rises from the bottom to the thumb", () =>
                Precision(fill(slider).ScreenSpaceDrawQuad.BottomLeft.Y, slider.ScreenSpaceDrawQuad.BottomLeft.Y)
                && Precision(fill(slider).ScreenSpaceDrawQuad.TopLeft.Y, centreOf(ring(slider)).Y));
            AddAssert("tick left of the thumb", () => ticks(slider).Length == 1 && ticks(slider)[0].ScreenSpaceDrawQuad.TopRight.X <= ring(slider).ScreenSpaceDrawQuad.TopLeft.X);
        }

        // The ring is the thumb's 18px circle; the dot inside it scales with the hover state.
        private static Circle ring(Slider slider) => slider.ChildrenOfType<Circle>().First();

        private static Circle dot(Slider slider) => slider.ChildrenOfType<Circle>().ElementAt(1);

        // The fill is the only box that stretches across the track's thin axis alone.
        private static Box fill(Slider slider) => slider.ChildrenOfType<Box>().Single(b => b.RelativeSizeAxes == (slider.Orientation == Orientation.Vertical ? Axes.X : Axes.Y));

        private static Box[] ticks(Slider slider) => slider.ChildrenOfType<Box>().Where(b => b.Alpha > 0.3f && b.Alpha < 0.4f).ToArray();

        private static Vector2 centreOf(Drawable d) => d.ScreenSpaceDrawQuad.Centre;

        private static Vector2 local(Drawable d, float x, float y) => d.ToScreenSpace(new Vector2(x, y));

        private static bool Precision(float a, float b, float tolerance = 1) => System.Math.Abs(a - b) < tolerance;
    }
}
