using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>MessageBox.Show as WPF code calls it: modal backdrop, buttons, Enter/Escape, stacking, focus, threads.</summary>
    [HeadlessTest]
    public partial class MessageBoxTests : ManualInputManagerTestScene
    {
        private Window window;
        private Button below;
        private TextBox textBox;
        private int belowClicks;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create window", () =>
            {
                belowClicks = 0;
                below = new Button { Content = "Below", Width = 200, Height = 60 };
                below.Click += () => belowClicks++;
                textBox = new TextBox { Width = 200 };

                var panel = new StackPanel { Spacing = 10, Padding = new MarginPadding(20) };
                panel.Add(below);
                panel.Add(textBox);
                Child = window = new Window { Child = panel };
            });
            AddUntilStep("loaded", () => below.IsLoaded && below.DrawWidth == 200);
        }

        private MessageBoxDialog top => window.PopupLayer.OpenModals.LastOrDefault()?.Dialog as MessageBoxDialog;

        private static Button button(Drawable root, string text) => root.ChildrenOfType<Button>().Single(b => b.Content as string == text);

        private void waitOpen(int count = 1) => AddUntilStep($"{count} box(es) open", () => window.PopupLayer.OpenModals.Count == count && top.IsLoaded && top.Alpha == 1);

        [Test]
        public void ShowsACentredCardAndReturnsTheClickedButton()
        {
            Task<MessageBoxResult> result = null;

            AddStep("show", () => result = MessageBox.Show(below, "Delete 3 files?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning));
            waitOpen();

            AddAssert("caption, message and warning icon", () => top.Title == "Confirm"
                                                                && top.ChildrenOfType<TextBlock>().Any(t => t.Text.ToString() == "Delete 3 files?")
                                                                && top.ChildrenOfType<SpriteIcon>().Any(i => i.Icon.Equals(FontAwesome.Solid.ExclamationTriangle)));
            AddAssert("Yes then No, right-aligned", () =>
            {
                var yes = button(top, "Yes");
                var no = button(top, "No");
                return yes.ScreenSpaceDrawQuad.TopLeft.X < no.ScreenSpaceDrawQuad.TopLeft.X
                       && System.Math.Abs(no.ScreenSpaceDrawQuad.TopRight.X - (top.ScreenSpaceDrawQuad.TopRight.X - 20)) < 2;
            });
            AddAssert("default button accented, the other plain", () => button(top, "Yes").Background == Theme.Accent && button(top, "No").Background == Theme.Surface);
            AddUntilStep("centred in the window", () => (top.ScreenSpaceDrawQuad.Centre - window.ScreenSpaceDrawQuad.Centre).Length < 2);
            AddUntilStep("window dimmed", () => window.PopupLayer.OpenModals[0].ChildrenOfType<Box>().First().Alpha == 0.5f);

            AddStep("click No", () =>
            {
                InputManager.MoveMouseTo(button(top, "No"));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("task completed with No", () => result.IsCompleted && result.Result == MessageBoxResult.No);
            AddAssert("no longer modal", () => window.PopupLayer.OpenModals.Count == 0);
            AddUntilStep("animates out and leaves the tree", () => !window.PopupLayer.ChildrenOfType<ModalHost>().Any());
        }

        [Test]
        public void EnterPicksTheDefaultAndEscapeTheCancelButton()
        {
            Task<MessageBoxResult> result = null;

            void check(MessageBoxButton buttons, MessageBoxResult defaultResult, Key key, MessageBoxResult expected)
            {
                AddStep($"show {buttons}", () => result = MessageBox.Show(below, "Message", "Caption", buttons, MessageBoxImage.Question, defaultResult));
                waitOpen();
                AddStep($"press {key}", () => InputManager.Key(key));
                AddAssert($"{expected}", () => result.IsCompleted && result.Result == expected);
                AddUntilStep("closed", () => window.PopupLayer.OpenModals.Count == 0);
            }

            check(MessageBoxButton.OKCancel, MessageBoxResult.None, Key.Enter, MessageBoxResult.OK);
            check(MessageBoxButton.YesNoCancel, MessageBoxResult.No, Key.Enter, MessageBoxResult.No);
            check(MessageBoxButton.OKCancel, MessageBoxResult.None, Key.Escape, MessageBoxResult.Cancel);
            check(MessageBoxButton.YesNo, MessageBoxResult.None, Key.Escape, MessageBoxResult.No);
            check(MessageBoxButton.OK, MessageBoxResult.None, Key.Escape, MessageBoxResult.OK);
            check(MessageBoxButton.YesNoCancel, MessageBoxResult.None, Key.Escape, MessageBoxResult.Cancel);
        }

        [Test]
        public void TheBoxIsModal()
        {
            Task<MessageBoxResult> result = null;

            AddStep("focus the text box", () =>
            {
                InputManager.MoveMouseTo(textBox);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("text box focused", () => textBox.HasFocus);

            AddStep("show", () => result = MessageBox.Show(window, "Modal", "Caption", MessageBoxButton.OKCancel));
            waitOpen();
            AddUntilStep("focus moved into the box", () => !textBox.HasFocus && top.HasFocus);

            AddStep("type", () => InputManager.Key(Key.A));
            AddAssert("nothing typed below", () => textBox.Text.Value == string.Empty);

            AddStep("click the button below, outside the card", () =>
            {
                InputManager.MoveMouseTo(below);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("button below not clicked, box still open", () => belowClicks == 0 && !result.IsCompleted && window.PopupLayer.OpenModals.Count == 1);
            AddAssert("button below not hovered", () => !below.IsHovered);
            AddAssert("no tooltips or context menus for the app below", () => window.PopupLayer.OpenPopups.Count == 0);

            AddStep("click Cancel", () =>
            {
                InputManager.MoveMouseTo(button(top, "Cancel"));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("cancelled", () => result.Result == MessageBoxResult.Cancel);
            AddUntilStep("focus back in the text box", () => textBox.HasFocus);

            AddStep("click the button below", () =>
            {
                InputManager.MoveMouseTo(below);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("input reaches the app again", () => belowClicks == 1);
        }

        [Test]
        public void BoxesStackAndOnlyTheTopOneTakesInput()
        {
            Task<MessageBoxResult> first = null, second = null;

            AddStep("show the first", () => first = MessageBox.Show(below, "First", "One", MessageBoxButton.OKCancel));
            waitOpen();
            AddStep("show a second from inside the first", () => second = MessageBox.Show(top, "Second", "Two", MessageBoxButton.YesNo));
            AddUntilStep("second open above the first", () =>
            {
                var firstHost = window.PopupLayer.OpenModals.SingleOrDefault();
                var nested = firstHost?.PopupLayer.OpenModals.SingleOrDefault()?.Dialog as MessageBoxDialog;
                return nested != null && nested.Text == "Second" && nested.IsLoaded && nested.Alpha == 1;
            });

            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("only the top box closed", () => second.Result == MessageBoxResult.No && !first.IsCompleted);
            AddUntilStep("first box focused again", () => top.HasFocus);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("first box answered", () => first.Result == MessageBoxResult.OK);
        }

        [Test]
        public void BoxesShownTogetherKeepFocusInTheTopOne()
        {
            Task<MessageBoxResult> first = null, second = null;

            AddStep("focus the text box", () =>
            {
                InputManager.MoveMouseTo(textBox);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("text box focused", () => textBox.HasFocus);

            AddStep("show two at once", () =>
            {
                first = MessageBox.Show(below, "First", "One", MessageBoxButton.OKCancel);
                second = MessageBox.Show(below, "Second", "Two", MessageBoxButton.YesNo);
            });
            waitOpen(2);
            AddUntilStep("the second has focus", () => top.Text == "Second" && top.HasFocus);

            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("the second answered No", () => second.Result == MessageBoxResult.No && !first.IsCompleted);
            AddUntilStep("focus goes to the first box, not the text box under it", () => top.Text == "First" && top.HasFocus && !textBox.HasFocus);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("the first answered OK", () => first.Result == MessageBoxResult.OK);
            AddUntilStep("focus back in the text box", () => textBox.HasFocus);
        }

        [Test]
        public void RemovingTheWindowAnswersOpenDialogs()
        {
            Task<MessageBoxResult> box = null;
            Task<bool?> dialog = null;

            AddStep("show a box and a dialog", () =>
            {
                box = MessageBox.Show(below, "Unsaved", "Box", MessageBoxButton.YesNoCancel);
                dialog = new Dialog { Title = "Dialog", Content = "Content" }.ShowDialog(below);
            });
            AddUntilStep("both open", () => window.PopupLayer.OpenModals.Count == 2 && window.PopupLayer.OpenModals.All(m => m.Dialog.IsLoaded));
            AddStep("remove the window", () => Clear());
            AddUntilStep("the box answers None", () => box.IsCompleted && box.Result == MessageBoxResult.None);
            AddUntilStep("the dialog answers false", () => dialog.IsCompleted && dialog.Result == false);
        }

        [Test]
        public void LongTextWrapsAtAReadableWidthAndLinesBreak()
        {
            const string sentence = "This is a rather long message that keeps going so that it has to wrap onto several lines instead of stretching across the window.";

            AddStep("show", () => MessageBox.Show(below, sentence + "\nSecond paragraph.", "Long", MessageBoxButton.OK, MessageBoxImage.Information));
            waitOpen();
            AddUntilStep("laid out", () => top.DrawWidth > 0);
            AddAssert("card stays narrow", () => top.DrawWidth < 560 && top.DrawWidth > 300);
            AddAssert("the long line wraps", () =>
            {
                var lines = top.ChildrenOfType<TextBlock>().Where(t => t.Text.ToString() == sentence).ToArray();
                return lines.Length == 1 && lines[0].DrawHeight >= 2 * lines[0].FontSize && lines[0].DrawWidth <= 441;
            });
            AddAssert("line break makes a second paragraph", () => top.ChildrenOfType<TextBlock>().Any(t => t.Text.ToString() == "Second paragraph."));
            AddStep("Enter", () => InputManager.Key(Key.Enter));
        }

        [Test]
        public void ShowWithoutAnOwnerFromAnotherThreadAndWithACallback()
        {
            Task<MessageBoxResult> result = null;
            MessageBoxResult called = MessageBoxResult.None;

            // A statement lambda, so Task.Run doesn't unwrap the result task and wait for the box to close.
            AddStep("show from a worker thread without an owner", () => Task.Run(() => { result = MessageBox.Show("From a worker", "Thread"); }).Wait());
            waitOpen();
            AddAssert("in this window", () => top.Text == "From a worker");
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddUntilStep("OK", () => result.IsCompleted && result.Result == MessageBoxResult.OK);
            AddUntilStep("closed", () => window.PopupLayer.OpenModals.Count == 0);

            AddStep("show with a callback", () => MessageBox.Show(below, "Callback", "Caption", MessageBoxButton.YesNo, MessageBoxImage.Error, r => called = r));
            waitOpen();
            AddAssert("error icon", () => top.ChildrenOfType<SpriteIcon>().Any(i => i.Icon.Equals(FontAwesome.Solid.TimesCircle)));
            AddStep("click the close button", () =>
            {
                var close = top.ChildrenOfType<SpriteIcon>().Single(i => i.Icon.Equals(FontAwesome.Solid.Times));
                InputManager.MoveMouseTo(close);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("callback got No", () => called == MessageBoxResult.No);
        }
    }
}
