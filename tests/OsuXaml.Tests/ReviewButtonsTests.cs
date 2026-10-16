using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Regression tests for defects found while reviewing the button, tab, headered and bar controls.</summary>
    [HeadlessTest]
    public partial class ReviewButtonsTests : ManualInputManagerTestScene
    {
        private static bool near(float a, float b) => System.Math.Abs(a - b) < 1;

        [Test]
        public void CheckBoxBoundToADisabledBindableIgnoresClicks()
        {
            var locked = new BindableBool(true);
            CheckBox box = null;
            int clicks = 0;

            AddStep("create", () =>
            {
                clicks = 0;
                box = new CheckBox { Content = "Locked setting" };
                box.IsChecked.BindTo(locked);
                box.Click += () => clicks++;
                locked.Disabled = true;
                Child = box;
            });
            AddUntilStep("loaded", () => box.IsLoaded && box.DrawWidth > 0);

            AddStep("click", () =>
            {
                InputManager.MoveMouseTo(box);
                InputManager.Click(MouseButton.Left);
            });
            AddStep("Space", () => InputManager.Key(Key.Space));
            AddStep("Toggle()", () => box.Toggle());
            AddAssert("value unchanged, clicks still reported", () => box.IsChecked.Value && locked.Value && clicks == 3);
        }

        [Test]
        public void RepeatButtonFirstRepeatComesAfterDelay()
        {
            RepeatButton button = null;
            int clicks = 0;
            double pressedAt = 0;

            AddStep("create", () =>
            {
                clicks = 0;
                Child = button = new RepeatButton { Content = "+", Delay = 100, Interval = 3000 };
                button.Click += () => clicks++;
            });
            AddUntilStep("loaded", () => button.IsLoaded && button.DrawWidth > 0);

            AddStep("press", () =>
            {
                InputManager.MoveMouseTo(button);
                InputManager.PressButton(MouseButton.Left);
                pressedAt = Time.Current;
            });
            AddUntilStep("first repeat", () => clicks >= 2);
            AddAssert("it came after Delay, not Delay + Interval", () => Time.Current - pressedAt < 1500);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        }
    }
}
