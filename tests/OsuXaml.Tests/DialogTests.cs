using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Dialog.ShowDialog with real input: DialogResult, IsDefault/IsCancel, Closing, sizing, popups inside, modality.</summary>
    [HeadlessTest]
    public partial class DialogTests : ManualInputManagerTestScene
    {
        private Window window;
        private Button below;
        private int belowClicks;

        /// <summary>What a XAML dialog with a text box and Save/Cancel buttons compiles to.</summary>
        private sealed partial class SettingsDialog : Dialog
        {
            public readonly TextBox NameBox;
            public readonly Button Save;
            public readonly Button Cancel;
            public readonly ComboBox Theme;
            public int SaveClicks;
            public int CancelClicks;

            public SettingsDialog()
            {
                Title = "Settings";

                NameBox = new TextBox { Width = 260 };
                Theme = new ComboBox { Width = 260 };
                Theme.Add("Dark");
                Theme.Add("Light");

                Save = new Button { Content = "Save" };
                DialogService.SetIsDefault(Save, true);
                Save.Click += () =>
                {
                    SaveClicks++;
                    DialogResult = true;
                };

                Cancel = new Button { Content = "Cancel" };
                DialogService.SetIsCancel(Cancel, true);
                Cancel.Click += () => CancelClicks++;

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                Layout.SetHorizontalAlignment(buttons, HorizontalAlignment.Right);
                buttons.Add(Save);
                buttons.Add(Cancel);

                var panel = new StackPanel { Spacing = 12 };
                panel.Add(new TextBlock { Text = "Name" });
                panel.Add(NameBox);
                panel.Add(Theme);
                panel.Add(buttons);
                Content = panel;
            }
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create window", () =>
            {
                belowClicks = 0;
                below = new Button { Content = "Below", Width = 200, Height = 60 };
                below.Click += () => belowClicks++;

                var panel = new StackPanel { Padding = new MarginPadding(20) };
                panel.Add(below);
                Child = window = new Window { Child = panel };
            });
            AddUntilStep("loaded", () => below.IsLoaded && below.DrawWidth == 200);
        }

        private void show(Func<SettingsDialog> create, Action<SettingsDialog, Task<bool?>> shown)
        {
            AddStep("show", () =>
            {
                var dialog = create();
                shown(dialog, dialog.ShowDialog(below));
            });
            AddUntilStep("open", () => window.PopupLayer.OpenModals.Count == 1 && window.PopupLayer.OpenModals[0].Dialog.IsLoaded
                                       && window.PopupLayer.OpenModals[0].Dialog.Alpha == 1);
        }

        private void click(Func<Drawable> target) => AddStep("click", () =>
        {
            InputManager.MoveMouseTo(target());
            InputManager.Click(MouseButton.Left);
        });

        [Test]
        public void SettingDialogResultClosesWithIt()
        {
            SettingsDialog dialog = null;
            Task<bool?> result = null;

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));

            AddAssert("title shown", () => dialog.ChildrenOfType<TextBlock>().Any(t => t.Text.ToString() == "Settings" && t.IsPresent));
            AddUntilStep("centred", () => (dialog.ScreenSpaceDrawQuad.Centre - window.ScreenSpaceDrawQuad.Centre).Length < 2);
            AddAssert("sized to its content", () => dialog.DrawWidth > 260 && dialog.DrawWidth < 400 && dialog.DrawHeight < 320);
            AddAssert("open", () => dialog.IsOpen && dialog.DialogResult == null && !result.IsCompleted);

            click(() => dialog.Save);
            AddAssert("true", () => result.IsCompleted && result.Result == true && dialog.DialogResult == true && !dialog.IsOpen);
            AddUntilStep("removed", () => !window.PopupLayer.ChildrenOfType<Dialog>().Any() && dialog.Parent == null);

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            AddStep("code sets DialogResult = false", () => dialog.DialogResult = false);
            AddAssert("false", () => result.IsCompleted && result.Result == false);
        }

        [Test]
        public void CancelEscapeTheCloseButtonAndCloseGiveFalse()
        {
            SettingsDialog dialog = null;
            Task<bool?> result = null;

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            click(() => dialog.Cancel);
            AddAssert("IsCancel button closes with false", () => result.IsCompleted && result.Result == false && dialog.CancelClicks == 1);
            AddUntilStep("closed", () => window.PopupLayer.OpenModals.Count == 0);

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("Escape clicks the IsCancel button", () => result.IsCompleted && result.Result == false && dialog.CancelClicks == 1);
            AddUntilStep("closed", () => window.PopupLayer.OpenModals.Count == 0);

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            click(() => dialog.ChildrenOfType<SpriteIcon>().Single(i => i.Icon.Equals(FontAwesome.Solid.Times)));
            AddAssert("close button gives false", () => result.IsCompleted && result.Result == false && dialog.CancelClicks == 0);
            AddUntilStep("closed", () => window.PopupLayer.OpenModals.Count == 0);

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            AddStep("Close()", () => dialog.Close());
            AddAssert("false", () => result.IsCompleted && result.Result == false);
        }

        [Test]
        public void EnterClicksTheDefaultButtonAndBelowIsBlocked()
        {
            SettingsDialog dialog = null;
            Task<bool?> result = null;

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            AddUntilStep("the dialog has focus", () => dialog.HasFocus);
            click(() => below);
            AddAssert("nothing below reacts", () => belowClicks == 0 && !result.IsCompleted && !below.IsHovered);
            AddAssert("clicking outside keeps focus in the dialog", () => dialog.HasFocus);

            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("default button clicked", () => dialog.SaveClicks == 1 && result.IsCompleted && result.Result == true);
            AddUntilStep("closed", () => window.PopupLayer.OpenModals.Count == 0);

            click(() => below);
            AddAssert("input reaches the app again", () => belowClicks == 1);
        }

        [Test]
        public void EnterInATextBoxClicksTheDefaultButton()
        {
            SettingsDialog dialog = null;
            Task<bool?> result = null;

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            click(() => dialog.NameBox);
            AddUntilStep("text box focused", () => dialog.NameBox.HasFocus);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("the first Escape only leaves the text box", () => !dialog.NameBox.HasFocus && dialog.IsOpen);

            click(() => dialog.NameBox);
            AddUntilStep("text box focused", () => dialog.NameBox.HasFocus);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddUntilStep("default button clicked", () => dialog.SaveClicks == 1 && result.IsCompleted && result.Result == true);
        }

        [Test]
        public void EditingKeysReachTextBoxesInTheDialog()
        {
            SettingsDialog dialog = null;

            show(() => new SettingsDialog(), (d, _) => dialog = d);
            AddStep("put text in the box", () => dialog.NameBox.Text.Value = "abc");
            click(() => dialog.NameBox);
            AddUntilStep("text box focused", () => dialog.NameBox.HasFocus);
            AddStep("End, Backspace", () =>
            {
                InputManager.Key(Key.End);
                InputManager.Key(Key.BackSpace);
            });
            AddAssert("a character deleted", () => dialog.NameBox.Text.Value == "ab");
            AddStep("Home, Delete", () =>
            {
                InputManager.Key(Key.Home);
                InputManager.Key(Key.Delete);
            });
            AddAssert("the first character deleted", () => dialog.NameBox.Text.Value == "b");
            AddStep("put more text", () => dialog.NameBox.Text.Value = "hello");
            AddStep("Ctrl+A, Backspace", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.A);
                InputManager.ReleaseKey(Key.ControlLeft);
                InputManager.Key(Key.BackSpace);
            });
            AddAssert("select all and delete work", () => dialog.NameBox.Text.Value == string.Empty && dialog.IsOpen);
        }

        [Test]
        public void ClosingCanKeepTheDialogOpen()
        {
            SettingsDialog dialog = null;
            Task<bool?> result = null;
            int closing = 0, closed = 0;

            show(() =>
            {
                var d = new SettingsDialog();
                d.Closing += e => e.Cancel = ++closing == 1;
                d.Closed += () => closed++;
                return d;
            }, (d, r) => (dialog, result) = (d, r));

            AddStep("DialogResult = true", () => dialog.DialogResult = true);
            AddAssert("still open, result forgotten", () => closing == 1 && closed == 0 && dialog.IsOpen && dialog.DialogResult == null && !result.IsCompleted);
            AddStep("DialogResult = true again", () => dialog.DialogResult = true);
            AddAssert("closed with true", () => closing == 2 && closed == 1 && result.Result == true);
        }

        [Test]
        public void DropDownsInsideOpenAboveTheDialog()
        {
            SettingsDialog dialog = null;
            Task<bool?> result = null;

            show(() => new SettingsDialog(), (d, r) => (dialog, result) = (d, r));
            click(() => dialog.Theme);
            AddUntilStep("drop-down open in the dialog's layer", () => dialog.Host.PopupLayer.OpenPopups.Count == 1);
            click(() => dialog.Host.PopupLayer.ChildrenOfType<ComboBoxItem>().Single(i => i.Content as string == "Light"));
            AddAssert("item picked", () => dialog.Theme.SelectedItem.Value as string == "Light" && dialog.IsOpen);

            click(() => dialog.Theme);
            AddUntilStep("drop-down open again", () => dialog.Host.PopupLayer.OpenPopups.Count == 1);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("only the drop-down closed", () => dialog.Host.PopupLayer.OpenPopups.Count == 0 && dialog.IsOpen && !result.IsCompleted);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("then the dialog", () => result.IsCompleted && result.Result == false);
        }

        [Test]
        public void BigContentStaysInsideTheWindowAndShowingTwiceThrows()
        {
            Dialog dialog = null;
            Task<bool?> result = null;

            AddStep("show a huge dialog", () =>
            {
                dialog = new Dialog { Title = "Huge", Content = new Border { Width = 5000, Height = 5000, Background = Theme.Surface } };
                result = dialog.ShowDialog(below);
            });
            AddUntilStep("open", () => dialog.IsLoaded && dialog.DrawWidth > 0);
            AddAssert("within the window less a margin", () => dialog.DrawWidth <= window.DrawWidth - 48 + 0.5f && dialog.DrawHeight <= window.DrawHeight - 48 + 0.5f);
            AddAssert("showing again throws", () =>
            {
                try
                {
                    dialog.ShowDialog(below);
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            });
            AddStep("close", () => dialog.Close());
            AddAssert("false", () => result.Result == false);
        }
    }
}
