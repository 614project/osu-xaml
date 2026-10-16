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

            withText.IsChecked.BindTo(bound);
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
            AddAssert("follows", () => withText.IsChecked.Value);

            AddStep("disable and click", () =>
            {
                withText.IsEnabled = false;
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("unchanged", () => bound.Value);
        }
    }
}
