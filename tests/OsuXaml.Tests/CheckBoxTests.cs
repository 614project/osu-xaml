using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK.Input;

namespace OsuXaml.Tests
{
    [HeadlessTest]
    public partial class CheckBoxTests : ManualInputManagerTestScene
    {
        [Test]
        public void ClickAndSpaceToggleTheBoundValue()
        {
            var bound = new BindableBool();
            var events = new List<string>();
            var withText = new CheckBox { Content = "Enable notifications" };
            var bare = new CheckBox();
            Layout.SetHorizontalAlignment(bare, HorizontalAlignment.Left);

            // A view model's Bindable<bool> binds to the bool? IsChecked as generated XAML does.
            XamlBinding.TwoWayNullable(new BindingScope(), withText.IsChecked, bound);
            withText.Checked += () => events.Add("checked");
            withText.Unchecked += () => events.Add("unchecked");

            AddStep("create", () =>
            {
                var panel = new StackPanel { Spacing = 10 };
                panel.Add(withText);
                panel.Add(bare);
                Child = panel;
            });
            AddUntilStep("laid out", () => withText.DrawWidth > 0);

            AddAssert("label beside the box", () => withText.DrawWidth > 120 && (withText.PresentedContent as TextBlock)?.Text.ToString() == "Enable notifications");
            AddAssert("a bare check box is just the box", () => bare.DrawWidth < 20 && bare.DrawHeight >= 18);

            AddStep("click the label", () =>
            {
                InputManager.MoveMouseTo(withText.PresentedContent);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("checked and focused", () => bound.Value && withText.HasFocus);

            AddStep("Space", () => InputManager.Key(Key.Space));
            AddAssert("unchecked", () => !bound.Value);
            AddAssert("events", () => string.Join(",", events) == "checked,unchecked");

            AddStep("view model checks it", () => bound.Value = true);
            AddAssert("follows", () => withText.IsChecked.Value == true);

            AddStep("disable and click", () =>
            {
                withText.IsEnabled = false;
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("unchanged", () => bound.Value);
        }
    

        [Test]
        public void ThreeStateCyclesThroughIndeterminate()
        {
            var events = new List<string>();
            var nullable = new Bindable<bool?>(false);
            var plain = new BindableBool(true);
            CheckBox threeState = null;
            CheckBox twoState = null;

            AddStep("create", () =>
            {
                events.Clear();
                threeState = new CheckBox { Content = "Select all", IsThreeState = true };
                twoState = new CheckBox { Content = "Bound to a bool" };

                threeState.IsChecked.BindTo(nullable);
                threeState.Checked += () => events.Add("checked");
                threeState.Unchecked += () => events.Add("unchecked");
                threeState.Indeterminate += () => events.Add("indeterminate");

                XamlBinding.TwoWayNullable(new BindingScope(), twoState.IsChecked, plain);

                var panel = new StackPanel { Spacing = 10 };
                panel.Add(threeState);
                panel.Add(twoState);
                Child = panel;
            });
            AddUntilStep("laid out", () => threeState.DrawWidth > 0);

            AddStep("click three times", () =>
            {
                threeState.TriggerClick();
                threeState.TriggerClick();
                threeState.TriggerClick();
            });
            AddAssert("unchecked, checked, indeterminate, unchecked", () => string.Join(",", events) == "checked,indeterminate,unchecked" && nullable.Value == false);

            AddStep("view model sets indeterminate", () => nullable.Value = null);
            AddAssert("box is indeterminate", () => threeState.IsChecked.Value == null && events[^1] == "indeterminate");

            AddStep("two-state box shown indeterminate from code", () => twoState.IsChecked.Value = null);
            AddAssert("bool source keeps its value", () => plain.Value);
            AddStep("click it", () => twoState.TriggerClick());
            AddAssert("indeterminate goes to unchecked, as in WPF", () => twoState.IsChecked.Value == false && !plain.Value);
            AddStep("click again", () => twoState.TriggerClick());
            AddAssert("checked, never indeterminate", () => twoState.IsChecked.Value == true && plain.Value);
            AddStep("click again", () => twoState.TriggerClick());
            AddAssert("unchecked", () => twoState.IsChecked.Value == false && !plain.Value);
        }
    }
}
