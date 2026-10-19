using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>A dialog is modal for the whole game: the other windows dim and take no clicks, hover, keys, menus or tooltips.</summary>
    [HeadlessTest]
    public partial class CrossWindowModalTests : ManualInputManagerTestScene
    {
        private Container windows;
        private Window left, right;
        private Button leftButton, rightButton;
        private TextBox rightTextBox;
        private ComboBox rightCombo;
        private KeyCatcher rightKeys;
        private int rightClicks;

        /// <summary>Takes every key press without focus, as access keys and key bindings do.</summary>
        private partial class KeyCatcher : Drawable
        {
            public int Presses;

            protected override bool OnKeyDown(KeyDownEvent e)
            {
                Presses++;
                return true;
            }
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create two windows side by side", () =>
            {
                rightClicks = 0;

                leftButton = new Button { Content = "Left", Width = 160, Height = 50 };
                var leftPanel = new StackPanel { Padding = new MarginPadding(20) };
                leftPanel.Add(leftButton);

                rightButton = new Button { Content = "Right", Width = 160, Height = 50 };
                rightButton.Click += () => rightClicks++;
                ToolTipService.SetToolTip(rightButton, "Right tooltip");
                rightTextBox = new TextBox { Width = 160 };
                rightCombo = new ComboBox { Width = 160 };
                rightCombo.Add("One");
                rightCombo.Add("Two");

                var rightPanel = new StackPanel { Spacing = 10, Padding = new MarginPadding(20) };
                rightPanel.Add(rightButton);
                rightPanel.Add(rightTextBox);
                rightPanel.Add(rightCombo);
                rightPanel.Add(rightKeys = new KeyCatcher());

                // The right window comes later, so it is in front of the left one for input.
                Child = windows = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        left = new Window { Width = 0.5f, Child = leftPanel },
                        right = new Window { Width = 0.5f, X = 0.5f, RelativePositionAxes = Axes.X, Child = rightPanel },
                    },
                };
            });
            AddUntilStep("loaded", () => rightCombo.IsLoaded && rightCombo.DrawWidth == 160 && leftButton.DrawWidth == 160);
        }

        private static MessageBoxDialog top(Window window) => window.PopupLayer.OpenModals.LastOrDefault()?.Dialog as MessageBoxDialog;

        // The windows only exist once the set-up steps have run, so steps look them up when they run.
        private void waitOpen(System.Func<Window> window) =>
            AddUntilStep("box open", () => window().PopupLayer.OpenModals.Count == 1 && top(window()).IsLoaded && top(window()).Alpha == 1);

        private static float coverDim(Window window) =>
            window.PopupLayer.ChildrenOfType<PopupLayer.ModalCover>().Single(c => c.Parent == window.PopupLayer).ChildrenOfType<Box>().Single().Alpha;

        private void click(string name, System.Func<Drawable> target) => AddStep(name, () =>
        {
            InputManager.MoveMouseTo(target());
            InputManager.Click(MouseButton.Left);
        });

        [Test]
        public void ADialogInOneWindowBlocksTheOthers()
        {
            Task<MessageBoxResult> result = null;

            click("focus the right text box", () => rightTextBox);
            AddUntilStep("right text box focused", () => rightTextBox.HasFocus);

            AddStep("show a box over the left window", () => result = MessageBox.Show(leftButton, "Left", "Caption", MessageBoxButton.OKCancel));
            waitOpen(() => left);
            AddAssert("the right window is blocked by it", () => right.PopupLayer.BlockingModal == left.PopupLayer.OpenModals[0] && left.PopupLayer.BlockingModal == null);
            AddUntilStep("the right window dims like the left", () => coverDim(right) == ModalHost.Dim && coverDim(left) == 0);
            AddUntilStep("focus moved into the box", () => top(left).HasFocus && !rightTextBox.HasFocus);

            AddStep("type", () => InputManager.Key(Key.A));
            AddAssert("nothing in the right window got the key", () => rightTextBox.Text.Value == string.Empty && rightKeys.Presses == 0);

            click("click the right button", () => rightButton);
            AddAssert("not clicked, not hovered, box still open", () => rightClicks == 0 && !rightButton.IsHovered && !result.IsCompleted);
            AddUntilStep("focus stays in the box", () => top(left).HasFocus);

            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("the box got it", () => result.IsCompleted && result.Result == MessageBoxResult.Cancel && rightKeys.Presses == 0);
            AddAssert("the right window is free again", () => right.PopupLayer.BlockingModal == null);
            AddUntilStep("and lights up", () => coverDim(right) == 0);
            AddUntilStep("focus back in the right text box", () => rightTextBox.HasFocus);

            click("click the right button", () => rightButton);
            AddAssert("clicked", () => rightClicks == 1);
            AddStep("press F1", () => InputManager.Key(Key.F1));
            AddAssert("keys reach the right window again", () => rightKeys.Presses == 1);
        }

        [Test]
        public void MenusCloseAndTooltipsStayHiddenInTheOtherWindows()
        {
            double hoveredAt = 0;

            click("open the right drop-down", () => rightCombo);
            AddUntilStep("open", () => right.PopupLayer.OpenPopups.Count == 1);

            AddStep("show a box over the left window", () => MessageBox.Show(leftButton, "Left", "Caption", MessageBoxButton.OK));
            waitOpen(() => left);
            AddUntilStep("the drop-down closed", () => right.PopupLayer.OpenPopups.Count == 0 && !rightCombo.IsDropDownOpen.Value);

            AddStep("hover the right button", () =>
            {
                InputManager.MoveMouseTo(rightButton);
                hoveredAt = Time.Current;
            });
            AddUntilStep("a second passes", () => Time.Current - hoveredAt > 1000);
            AddAssert("no tooltip", () => right.PopupLayer.ChildrenOfType<ToolTipHost>().Single().Current == null);

            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddUntilStep("the tooltip shows once the box is gone", () => right.PopupLayer.ChildrenOfType<ToolTipHost>().Single().Current != null);
        }

        [Test]
        public void TheNewestDialogInTheGameTakesTheInput()
        {
            Task<MessageBoxResult> first = null, second = null;

            AddStep("show a box over the left window", () => first = MessageBox.Show(leftButton, "First", "Left", MessageBoxButton.OKCancel));
            waitOpen(() => left);
            AddStep("then one over the right window", () => second = MessageBox.Show(rightButton, "Second", "Right", MessageBoxButton.YesNo));
            waitOpen(() => right);

            AddAssert("the newest blocks the left window, box and all", () => left.PopupLayer.BlockingModal == right.PopupLayer.OpenModals[0] && right.PopupLayer.BlockingModal == null);
            AddUntilStep("the left window is covered over its box", () => coverDim(left) == ModalHost.Dim && coverDim(right) == 0);
            AddUntilStep("the newest has focus", () => top(right).HasFocus);

            click("click the left box's OK", () => top(left).ChildrenOfType<Button>().Single(b => b.Content as string == "OK"));
            AddAssert("the older box doesn't react", () => !first.IsCompleted && !second.IsCompleted);
            AddUntilStep("focus stays in the newest", () => top(right).HasFocus);

            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("the newest answered", () => second.IsCompleted && second.Result == MessageBoxResult.Yes && !first.IsCompleted);
            AddAssert("the left box has the input again", () => left.PopupLayer.BlockingModal == null && right.PopupLayer.BlockingModal == left.PopupLayer.OpenModals[0]);
            AddUntilStep("and focus", () => top(left).HasFocus);
            AddUntilStep("covers swap", () => coverDim(left) == 0 && coverDim(right) == ModalHost.Dim);

            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("the older answered", () => first.IsCompleted && first.Result == MessageBoxResult.Cancel);
            AddAssert("nothing is blocked", () => left.PopupLayer.BlockingModal == null && right.PopupLayer.BlockingModal == null);
        }

        [Test]
        public void ADialogOverTheWholeGameBlocksWindowsWithoutDimmingThemTwice()
        {
            Task<MessageBoxResult> result = null;
            TextBlock outside = null;
            ModalHost host = null;
            double shownAt = 0;

            AddStep("an element outside the windows", () => windows.Add(outside = new TextBlock { Text = "Outside", Y = 300 }));
            AddUntilStep("loaded", () => outside.IsLoaded);
            AddStep("show a box owned by it", () =>
            {
                result = MessageBox.Show(outside, "Game", "Caption", MessageBoxButton.OK);
                shownAt = Time.Current;
            });
            AddUntilStep("it opens in the game's layer", () => (host = right.PopupLayer.BlockingModal) != null && host.Dialog.IsLoaded
                                                               && !ModalHost.IsWithin(host, left) && !ModalHost.IsWithin(host, right));
            AddAssert("both windows are blocked", () => left.PopupLayer.BlockingModal == host);
            AddUntilStep("the open animation has played", () => Time.Current - shownAt > 2 * ModalHost.OpenDuration);
            AddAssert("its backdrop dims them; the windows add no dim of their own", () => coverDim(left) == 0 && coverDim(right) == 0);

            AddStep("close it", () => ((Dialog)host.Dialog).Close());
            AddUntilStep("answered", () => result.IsCompleted);
            AddAssert("nothing is blocked", () => left.PopupLayer.BlockingModal == null && right.PopupLayer.BlockingModal == null);
        }

        [Test]
        public void ADialogOnlyCountsWhileItsWindowIsInTheGame()
        {
            Task<MessageBoxResult> result = null;

            AddStep("show a box over the left window", () => result = MessageBox.Show(leftButton, "Left", "Caption", MessageBoxButton.OKCancel));
            waitOpen(() => left);

            AddStep("take the left window out", () => windows.Remove(left, false));
            AddAssert("the right window is free", () => right.PopupLayer.BlockingModal == null && !result.IsCompleted);
            click("click the right button", () => rightButton);
            AddAssert("clicked", () => rightClicks == 1);

            AddStep("put it back", () => windows.Add(left));
            AddUntilStep("blocked again by the open box", () => right.PopupLayer.BlockingModal == left.PopupLayer.OpenModals[0]);

            AddStep("dispose the left window", () => windows.Remove(left, true));
            AddUntilStep("the box answers None", () => result.IsCompleted && result.Result == MessageBoxResult.None);
            AddAssert("the right window is free", () => right.PopupLayer.BlockingModal == null);
            click("click the right button", () => rightButton);
            AddAssert("clicked", () => rightClicks == 2);
        }
    }
}
