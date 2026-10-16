using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>ToggleButton, RadioButton and RepeatButton driven by real mouse input in a headless host.</summary>
    [HeadlessTest]
    public partial class ToggleButtonTests : ManualInputManagerTestScene
    {
        private void click(Drawable target)
        {
            InputManager.MoveMouseTo(target);
            InputManager.Click(MouseButton.Left);
        }

        [Test]
        public void ToggleButtonFlipsBindsAndRaisesEvents()
        {
            var bold = new BindableBool();
            ToggleButton toggle = null;
            int checkedCount = 0, uncheckedCount = 0, clicks = 0;

            AddStep("create", () =>
            {
                toggle = new ToggleButton { Content = "Bold" };
                XamlBinding.TwoWay(toggle.IsChecked, bold);
                toggle.Checked += () => checkedCount++;
                toggle.Unchecked += () => uncheckedCount++;
                toggle.Click += () => clicks++;
                Child = toggle;
            });

            AddStep("click", () => click(toggle));
            AddAssert("checked and view model follows", () => toggle.IsChecked.Value && bold.Value);
            AddAssert("Checked then Click raised", () => checkedCount == 1 && uncheckedCount == 0 && clicks == 1);
            AddUntilStep("text switches to the checked colour", () => ((TextBlock)toggle.PresentedContent).Colour == Theme.AccentText);

            AddStep("click again", () => click(toggle));
            AddAssert("unchecked", () => !toggle.IsChecked.Value && !bold.Value && uncheckedCount == 1 && clicks == 2);
            AddUntilStep("text back to the normal colour", () => ((TextBlock)toggle.PresentedContent).Colour == Theme.Text);

            AddStep("view model checks it", () => bold.Value = true);
            AddAssert("control follows and raises Checked", () => toggle.IsChecked.Value && checkedCount == 2);

            AddStep("disable", () => toggle.IsEnabled = false);
            AddStep("click while disabled", () => click(toggle));
            AddAssert("nothing changes", () => toggle.IsChecked.Value && clicks == 2);
        }

        [Test]
        public void RadioButtonsAreExclusiveWithinTheirParentPanel()
        {
            RadioButton[] first = null, second = null;
            var small = new BindableBool(true);

            AddStep("create two panels", () =>
            {
                first = Enumerable.Range(0, 3).Select(i => new RadioButton { Content = $"A{i}" }).ToArray();
                second = Enumerable.Range(0, 2).Select(i => new RadioButton { Content = $"B{i}" }).ToArray();
                XamlBinding.TwoWay(first[0].IsChecked, small);

                var a = new StackPanel { Spacing = 4 };
                var b = new StackPanel { Spacing = 4 };
                foreach (var r in first) a.Add(r);
                foreach (var r in second) b.Add(r);

                second[0].IsChecked.Value = true;

                var root = new StackPanel { Spacing = 20 };
                root.Add(a);
                root.Add(b);
                Child = root;
            });

            AddAssert("view model checked the first", () => first[0].IsChecked.Value);
            AddStep("click A1", () => click(first[1]));
            AddAssert("A1 checked, A0 unchecked", () => first[1].IsChecked.Value && !first[0].IsChecked.Value);
            AddAssert("view model follows", () => !small.Value);
            AddAssert("other panel untouched", () => second[0].IsChecked.Value);

            AddStep("click A1 again", () => click(first[1]));
            AddAssert("stays checked", () => first[1].IsChecked.Value);

            AddStep("code checks A2", () => first[2].IsChecked.Value = true);
            AddAssert("only A2", () => first.Count(r => r.IsChecked.Value) == 1 && first[2].IsChecked.Value);
            AddAssert("layout: indicator plus text", () => first[0].DrawHeight >= 20 && first[0].DrawWidth > 28);
        }

        [Test]
        public void GroupNameGroupsAcrossPanels()
        {
            RadioButton left = null, right = null, loose = null;

            AddStep("create", () =>
            {
                var a = new StackPanel();
                var b = new StackPanel();
                a.Add(left = new RadioButton { Content = "Left", GroupName = "side" });
                a.Add(loose = new RadioButton { Content = "No group" });
                b.Add(right = new RadioButton { Content = "Right", GroupName = "side" });

                var root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30 };
                root.Add(a);
                root.Add(b);
                Child = root;
            });

            AddStep("check loose, then left", () =>
            {
                loose.IsChecked.Value = true;
                click(left);
            });
            AddAssert("named group doesn't touch unnamed siblings", () => left.IsChecked.Value && loose.IsChecked.Value);
            AddStep("click right", () => click(right));
            AddAssert("same name in another panel unchecks left", () => right.IsChecked.Value && !left.IsChecked.Value && loose.IsChecked.Value);
        }

        [Test]
        public void RepeatButtonRepeatsWhileHeld()
        {
            RepeatButton button = null;
            int clicks = 0;

            AddStep("create", () =>
            {
                clicks = 0;
                Child = button = new RepeatButton { Content = "+", Delay = 1500, Interval = 20 };
                button.Click += () => clicks++;
            });

            AddStep("press", () =>
            {
                InputManager.MoveMouseTo(button);
                InputManager.PressButton(MouseButton.Left);
            });
            AddAssert("fires once on press, then waits for the delay", () => clicks == 1 && button.IsPressed);
            AddUntilStep("repeats while held", () => clicks >= 5);

            int afterRelease = 0;
            AddStep("release", () =>
            {
                InputManager.ReleaseButton(MouseButton.Left);
                afterRelease = clicks;
            });
            AddWaitStep("wait", 10);
            AddAssert("stops on release, no extra click", () => clicks == afterRelease && !button.IsPressed);

            AddStep("shorter delay", () => button.Delay = 0);
            AddStep("press", () => InputManager.PressButton(MouseButton.Left));
            AddStep("move off", () => InputManager.MoveMouseTo(button, new Vector2(0, 200)));
            AddStep("count", () => afterRelease = clicks);
            AddWaitStep("wait", 20);
            AddAssert("paused while the pointer is away", () => clicks == afterRelease);
            AddStep("move back", () => InputManager.MoveMouseTo(button));
            AddUntilStep("resumes", () => clicks > afterRelease);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        }
    }
}
