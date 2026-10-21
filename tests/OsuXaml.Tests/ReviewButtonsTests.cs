using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;
using TextBox = OsuXaml.Controls.TextBox;

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
                XamlBinding.TwoWayNullable(new BindingScope(), box.IsChecked, locked);
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
            AddAssert("value unchanged, clicks still reported", () => box.IsChecked.Value == true && locked.Value && clicks == 3);
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

        [Test]
        public void ContentKeepsItsMarginAndSizeWhenSwappedOut()
        {
            Border border = null;
            Container content = null;

            AddStep("create", () =>
            {
                content = new Container { Height = 50, Margin = new MarginPadding(10) };
                Child = border = new Border { Size = new Vector2(200, 100), Content = content };
            });
            AddUntilStep("stretched inside the margin", () => content.IsLoaded && near(content.DrawWidth, 180));

            AddStep("swap the content out and back", () =>
            {
                border.Content = null;
                border.Content = content;
            });
            AddUntilStep("still inside the margin", () => near(content.DrawWidth, 180) && near(content.ScreenSpaceDrawQuad.TopLeft.X, border.ScreenSpaceDrawQuad.TopLeft.X + 10));

            AddStep("take it out", () => border.Content = null);
            AddAssert("its own margin and size are back", () => content.Parent == null && content.Margin.Equals(new MarginPadding(10)) && content.RelativeSizeAxes == Axes.None);
        }

        [Test]
        public void ContentAlignmentCanChangeAfterLoad()
        {
            Border border = null;
            TextBlock text = null, own = null;

            AddStep("create", () =>
            {
                Child = border = new Border
                {
                    Size = new Vector2(300, 60),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Content = text = new TextBlock { Text = "content" },
                };
            });
            AddUntilStep("centred", () => text.DrawWidth > 0 && near(text.ScreenSpaceDrawQuad.Centre.X, border.ScreenSpaceDrawQuad.Centre.X));

            AddStep("align right", () => border.HorizontalContentAlignment = HorizontalAlignment.Right);
            AddUntilStep("text at the right edge", () => near(text.ScreenSpaceDrawQuad.TopRight.X, border.ScreenSpaceDrawQuad.TopRight.X));

            AddStep("replace the content", () => border.Content = own = new TextBlock { Text = "own alignment" });
            AddAssert("the old content doesn't keep the control's alignment", () => Layout.GetHorizontalAlignment(text) == HorizontalAlignment.Stretch);

            AddStep("content with its own alignment", () =>
            {
                own = new TextBlock { Text = "own alignment" };
                Layout.SetHorizontalAlignment(own, HorizontalAlignment.Center);
                border.Content = own;
                border.HorizontalContentAlignment = HorizontalAlignment.Left;
            });
            AddUntilStep("keeps its own", () => own.DrawWidth > 0 && near(own.ScreenSpaceDrawQuad.Centre.X, border.ScreenSpaceDrawQuad.Centre.X));
        }

        [Test]
        public void LabelAccessKeyGoesAwayWithItsText()
        {
            Label label = null;
            TextBox box = null, other = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300, Spacing = 8 };
                panel.Add(label = new Label { Content = "_Name:" });
                panel.Add(box = new TextBox());
                panel.Add(other = new TextBox());
                label.Target = box;
                Child = panel;
            });
            AddUntilStep("access key parsed", () => label.AccessKey == Key.N);

            AddStep("replace the text with an element", () => label.Content = new TextBlock { Text = "Name:" });
            AddAssert("no access key", () => label.AccessKey == null);

            AddStep("focus the other box", () =>
            {
                InputManager.MoveMouseTo(other);
                InputManager.Click(MouseButton.Left);
            });
            AddStep("press Alt+N", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.N);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("target not focused", () => !box.HasFocus && other.HasFocus);

            AddStep("text again", () => label.Content = "_Address:");
            AddAssert("new access key", () => label.AccessKey == Key.A);
            AddStep("no content", () => label.Content = null);
            AddAssert("no access key without content", () => label.AccessKey == null);
        }

        [Test]
        public void CheckBoxPaddingIsKept()
        {
            CheckBox box = null, plain = null;

            AddStep("create", () =>
            {
                box = new CheckBox { Content = "padded", Padding = new MarginPadding { Vertical = 10, Left = 6 } };
                plain = new CheckBox { Content = "padded" };
                var panel = new StackPanel { Width = 300 };
                panel.Add(box);
                panel.Add(plain);
                Child = panel;
            });
            AddUntilStep("laid out", () => box.PresentedContent?.DrawHeight > 0 && plain.PresentedContent?.DrawHeight > 0);
            AddWaitStep("settle", 3);
            AddAssert("padding still set", () => box.Padding.Top == 10 && box.Padding.Left == 6);
            AddAssert("vertical padding around the text", () => near(box.DrawHeight, box.PresentedContent.DrawHeight + 20));
            AddAssert("left padding between the box and the text", () =>
                near(box.PresentedContent.ScreenSpaceDrawQuad.TopLeft.X - box.ScreenSpaceDrawQuad.TopLeft.X,
                    plain.PresentedContent.ScreenSpaceDrawQuad.TopLeft.X - plain.ScreenSpaceDrawQuad.TopLeft.X + 6));
        }

        [Test]
        public void TabControlWithALockedSelectionIgnoresTabClicks()
        {
            TabControl tabs = null;
            TabItem[] items = null;
            var locked = new BindableInt(1);

            AddStep("create", () =>
            {
                items = new[] { new TabItem { Header = "A" }, new TabItem { Header = "B" }, new TabItem { Header = "C" } };
                tabs = new TabControl { Size = new Vector2(400, 300) };
                foreach (var item in items)
                    tabs.Add(item);
                tabs.SelectedIndex.BindTo(locked);
                locked.Disabled = true;
                Child = tabs;
            });
            AddUntilStep("loaded", () => items[0].DrawWidth > 0);

            AddStep("click A", () =>
            {
                InputManager.MoveMouseTo(items[0]);
                InputManager.Click(MouseButton.Left);
            });
            AddStep("IsSelected on C", () => items[2].IsSelected = true);
            AddStep("keys", () =>
            {
                InputManager.Key(Key.Right);
                InputManager.Key(Key.Home);
            });
            AddAssert("still B", () => tabs.SelectedIndex.Value == 1 && tabs.SelectedItem.Value == items[1]);
        }

        [Test]
        public void RepeatButtonReleasedWhileOutOfTheTreeStopsRepeating()
        {
            RepeatButton button = null;
            Container holder = null;
            int clicks = 0, afterReAdd = 0;

            AddStep("create", () =>
            {
                clicks = 0;
                holder = new Container { RelativeSizeAxes = Axes.Both };
                holder.Add(button = new RepeatButton { Content = "+", Delay = 0, Interval = 20 });
                button.Click += () => clicks++;
                Child = holder;
            });
            AddUntilStep("loaded", () => button.IsLoaded && button.DrawWidth > 0);

            AddStep("press", () =>
            {
                InputManager.MoveMouseTo(button);
                InputManager.PressButton(MouseButton.Left);
            });
            AddUntilStep("repeating", () => clicks >= 3);

            AddStep("take it out of the tree", () => holder.Remove(button, false));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddStep("put it back under the pointer", () => holder.Add(button));
            AddWaitStep("let a repeat come due", 3);
            AddStep("count", () => afterReAdd = clicks);
            AddWaitStep("wait", 10);
            AddAssert("no clicks without a press", () => clicks == afterReAdd && !button.IsPressed);
        }

        [Test]
        public void ToggleButtonKeepsTheCheckedTextColourWhenForegroundChanges()
        {
            ToggleButton toggle = null;

            AddStep("create checked", () => Child = toggle = new ToggleButton { Content = "Bold", IsChecked = { Value = true } });
            AddUntilStep("checked colour", () => (toggle.PresentedContent as TextBlock)?.Colour == Theme.AccentText);

            AddStep("change Foreground", () => toggle.Foreground = Colour4.Red);
            AddAssert("still the checked colour", () => ((TextBlock)toggle.PresentedContent).Colour == Theme.AccentText);

            AddStep("uncheck", () => toggle.IsChecked.Value = false);
            AddUntilStep("new Foreground", () => ((TextBlock)toggle.PresentedContent).Colour == Colour4.Red);
        }

        [Test]
        public void TemplatedCheckBoxDoesNotDrawItsDefaultBox()
        {
            CheckBox box = null;
            Border templateRoot = null;

            AddStep("create", () =>
            {
                box = new CheckBox
                {
                    Content = new Container { Size = new Vector2(40, 8) },
                    Template = new ControlTemplate<CheckBox>((c, _) =>
                    {
                        var presenter = new ContentPresenter();
                        templateRoot = new Border { Background = Colour4.Red, Padding = new MarginPadding(1), Child = presenter };
                        c.AttachPresenter(presenter);
                        return templateRoot;
                    }),
                };
                Child = box;
            });
            AddUntilStep("laid out", () => box.PresentedContent?.DrawWidth > 0);
            AddAssert("default box hidden", () => box.ChildrenOfType<SpriteIcon>().All(icon => !icon.Parent.IsPresent));
            AddAssert("sized to the template", () => near(box.DrawHeight, templateRoot.DrawHeight) && templateRoot.DrawHeight < 18);

            AddStep("remove the template", () => box.Template = null);
            AddUntilStep("default box back", () => box.ChildrenOfType<SpriteIcon>().All(icon => icon.Parent.IsPresent));
        }

        [Test]
        public void HeaderTextFollowsForegroundChanges()
        {
            Expander expander = null;
            GroupBox group = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300 };
                panel.Add(expander = new Expander { Header = "Details", Content = "Body" });
                panel.Add(group = new GroupBox { Header = "Account", Content = "Body" });
                Child = panel;
            });
            AddUntilStep("loaded", () => expander.PresentedHeader != null && group.PresentedHeader != null);

            AddStep("change Foreground", () =>
            {
                expander.Foreground = Colour4.Red;
                group.Foreground = Colour4.Red;
            });
            AddAssert("expander header follows, like its content", () => ((TextBlock)expander.PresentedHeader).Colour == Colour4.Red && ((TextBlock)expander.PresentedContent).Colour == Colour4.Red);
            AddAssert("header text unchanged", () => ((TextBlock)expander.PresentedHeader).Text == "Details");
            AddAssert("group box header keeps its own style", () => ((TextBlock)group.PresentedHeader).Colour == Theme.SubtleText);
        }
    }
}
