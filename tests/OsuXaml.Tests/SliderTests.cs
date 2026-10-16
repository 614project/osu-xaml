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
    /// <summary>Slider range, snapping, keys and orientation with real input, plus ProgressBar's indeterminate and vertical modes.</summary>
    [HeadlessTest]
    public partial class SliderTests : ManualInputManagerTestScene
    {
        [Test]
        public void ValueKeepsTheSlidersOwnRangeAndCoercesTheSource()
        {
            var volume = new BindableDouble(5);
            Slider slider = null;
            double lastChange = double.NaN;

            AddStep("create", () =>
            {
                slider = new Slider { Width = 200, Minimum = 0, Maximum = 10 };

                // A view model bindable with no range of its own, bound after the range is set (XAML attribute order).
                XamlBinding.TwoWay(slider.Value, volume);
                slider.ValueChanged += v => lastChange = v;
                Child = slider;
            });

            AddAssert("range kept", () => slider.Minimum == 0 && slider.Maximum == 10 && slider.Value.Value == 5);
            AddStep("view model goes out of range", () => volume.Value = 50);
            AddAssert("coerced both ways", () => slider.Value.Value == 10 && volume.Value == 10 && lastChange == 10);
            AddAssert("source range untouched", () => volume.MaxValue == double.MaxValue);
        }

        [Test]
        public void KeysMoveBySmallAndLargeChangeAndSnapToTicks()
        {
            Slider slider = null;

            AddStep("create", () => Child = slider = new Slider { Width = 200, Minimum = 0, Maximum = 10, SmallChange = 0.5, LargeChange = 3 });
            AddStep("hover", () => InputManager.MoveMouseTo(slider));

            AddStep("right", () => InputManager.Key(Key.Right));
            AddAssert("small change", () => slider.Value.Value == 0.5);
            AddStep("page up", () => InputManager.Key(Key.PageUp));
            AddAssert("large change", () => slider.Value.Value == 3.5);
            AddStep("end", () => InputManager.Key(Key.End));
            AddAssert("maximum", () => slider.Value.Value == 10);
            AddStep("home", () => InputManager.Key(Key.Home));
            AddAssert("minimum", () => slider.Value.Value == 0);

            AddStep("snap to ticks of 2.5", () =>
            {
                slider.TickFrequency = 2.5;
                slider.IsSnapToTickEnabled = true;
                slider.TickPlacement = TickPlacement.BottomRight;
            });
            AddStep("right", () => InputManager.Key(Key.Right));
            AddAssert("next tick", () => slider.Value.Value == 2.5);
            AddStep("click at 40%", () =>
            {
                InputManager.MoveMouseTo(at(slider, 0.4f, 0.5f));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("snapped to the nearest tick", () => slider.Value.Value == 5);
            AddUntilStep("three inner ticks drawn", () => slider.ChildrenOfType<Box>().Count(b => b.Alpha > 0.3f && b.Alpha < 0.4f) == 3);

            AddStep("code sets an off-tick value", () => slider.Value.Value = 3.3);
            AddAssert("not snapped, like WPF", () => slider.Value.Value == 3.3);
        }

        [Test]
        public void VerticalSliderTakesItsValueFromTheMouseHeight()
        {
            Slider slider = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Height = 200 };
                panel.Add(slider = new Slider { Orientation = Orientation.Vertical });
                Child = panel;
            });

            AddUntilStep("tall and thin", () => slider.DrawWidth == 20 && slider.DrawHeight == 200);
            AddStep("click near the top", () =>
            {
                InputManager.MoveMouseTo(at(slider, 0.5f, 0.1f));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("high value", () => slider.Value.Value > 85 && slider.Value.Value < 95);

            AddStep("press", () => InputManager.PressButton(MouseButton.Left));
            AddStep("drag down", () => InputManager.MoveMouseTo(at(slider, 0.5f, 0.5f)));
            AddStep("drag past the bottom", () => InputManager.MoveMouseTo(at(slider, 0.5f, 1.2f)));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("minimum", () => slider.Value.Value == 0);

            AddStep("up arrow", () => InputManager.Key(Key.Up));
            AddAssert("up increases", () => slider.Value.Value == 1);
        }

        [Test]
        public void ProgressBarFractionIgnoresAttributeOrder()
        {
            ProgressBar bar = null;

            AddStep("value before maximum", () => Child = bar = new ProgressBar { Width = 200, Value = 30, Maximum = 50 });
            AddAssert("60%", () => bar.Fraction == 0.6f);
            AddUntilStep("fill drawn at 60%", () => Precision(fillOf(bar).DrawWidth, 120));
        }

        [Test]
        public void IndeterminateProgressSweepsAndStops()
        {
            ProgressBar bar = null;
            float firstX = 0;

            AddStep("create", () => Child = bar = new ProgressBar { Width = 200, Value = 50, IsIndeterminate = true });

            Box segment() => bar.ChildrenOfType<Box>().Single(b => b.RelativePositionAxes == Axes.Both);

            AddUntilStep("segment showing", () => segment().Alpha == 1);
            AddStep("note position", () => firstX = segment().X);
            AddUntilStep("segment moves", () => System.Math.Abs(segment().X - firstX) > 0.2f);
            AddAssert("animation loops", () => segment().Transforms.Any());

            AddStep("determinate", () => bar.IsIndeterminate = false);
            AddUntilStep("segment hidden, fill back", () => segment().Alpha == 0 && !segment().Transforms.Any());
        }

        [Test]
        public void VerticalProgressBarFillsUpwards()
        {
            ProgressBar bar = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Height = 100 };
                panel.Add(bar = new ProgressBar { Orientation = Orientation.Vertical, Value = 25 });
                Child = panel;
            });

            AddUntilStep("thin and tall", () => bar.DrawWidth == 8 && bar.DrawHeight == 100);
            AddAssert("fill is a quarter of the height at the bottom", () => fillOf(bar).Anchor == Anchor.BottomLeft && Precision(fillOf(bar).DrawHeight, 25));
        }

        /// <summary>A point inside <paramref name="d"/> at the given fractions of its size, in screen space.</summary>
        private static Vector2 at(Drawable d, float x, float y) => d.ToScreenSpace(new Vector2(d.DrawWidth * x, d.DrawHeight * y));

        // The track, the fill and the indeterminate segment, in that order.
        private static Box fillOf(ProgressBar bar) => bar.ChildrenOfType<Box>().ElementAt(1);

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 1;
    }
}
