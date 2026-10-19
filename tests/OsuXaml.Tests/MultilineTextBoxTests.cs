using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>TextBox with AcceptsReturn and TextWrapping, driven with real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class MultilineTextBoxTests : ManualInputManagerTestScene
    {
        private partial class TypingTextBox : TextBox
        {
            public void Type(string text) => InsertString(text);

            public bool Multiline => IsMultiline;
        }

        [Resolved]
        private Clipboard clipboard { get; set; }

        private TypingTextBox box;
        private TextBlock below;

        private void create(System.Action<TypingTextBox> setup, float width = 300)
        {
            AddStep("create", () =>
            {
                box = new TypingTextBox();
                setup(box);

                var panel = new StackPanel { Width = width };
                panel.Add(box);
                panel.Add(below = new TextBlock { Text = "below" });
                Child = panel;
            });

            AddUntilStep("loaded", () => box.IsLoaded && box.DrawWidth > 0);
        }

        private void focus() => AddStep("click into the box", () =>
        {
            InputManager.MoveMouseTo(box);
            InputManager.Click(MouseButton.Left);
        });

        private void type(string text) => AddStep($"type \"{text}\"", () => box.Type(text));

        private void press(Key key, params Key[] modifiers) => AddStep($"press {string.Join("+", modifiers.Append(key))}", () =>
        {
            foreach (var modifier in modifiers)
                InputManager.PressKey(modifier);

            InputManager.Key(key);

            foreach (var modifier in modifiers)
                InputManager.ReleaseKey(modifier);
        });

        private TextBlock lineText => box.ChildrenOfType<TextBlock>().Single();

        [Test]
        public void EnterStartsANewLineAndTheBoxGrows()
        {
            create(b => b.AcceptsReturn = true);
            focus();
            type("one");
            press(Key.Enter);
            type("two");

            AddAssert("a line break between them", () => box.Text.Value == "one\ntwo");
            AddAssert("still editing", () => box.HasFocus);
            AddUntilStep("grows to two lines", () => box.DrawHeight > 50);
            AddAssert("the panel moves what's below", () => below.ScreenSpaceDrawQuad.TopLeft.Y >= box.ScreenSpaceDrawQuad.BottomLeft.Y - 1);

            press(Key.BackSpace);
            press(Key.BackSpace);
            press(Key.BackSpace);
            press(Key.BackSpace);
            AddAssert("back to one line", () => box.Text.Value == "one");
            AddUntilStep("shrinks again", () => box.DrawHeight == 36);
        }

        [Test]
        public void EnterCommitsWithoutAcceptsReturn()
        {
            create(_ => { });
            focus();
            type("x");
            press(Key.Enter);

            AddAssert("committed and left", () => !box.HasFocus && box.Text.Value == "x");
            AddAssert("single row, fixed height", () => !box.Multiline && box.DrawHeight == 36);
        }

        [Test]
        public void UpAndDownKeepTheColumn()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.Text.Value = "abcdef\nxy\nabcdef";
            });
            focus();
            press(Key.Home, Key.ControlLeft);
            press(Key.Right);
            press(Key.Right);
            press(Key.Right);
            press(Key.Down);
            press(Key.Down);
            type("|");

            // The short middle line doesn't pull the caret to its end for good.
            AddAssert("same column two lines down", () => box.Text.Value == "abcdef\nxy\nabc|def");

            press(Key.Up);
            type("#");
            AddAssert("up stops at the end of the short line", () => box.Text.Value == "abcdef\nxy#\nabc|def");
        }

        [Test]
        public void HomeAndEndWorkOnTheLine()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.Text.Value = "first\nsecond";
            });
            focus();
            press(Key.End, Key.ControlLeft);
            press(Key.Home);
            type(">");
            AddAssert("start of the second line", () => box.Text.Value == "first\n>second");

            press(Key.Home, Key.ControlLeft);
            press(Key.End);
            type("<");
            AddAssert("end of the first line", () => box.Text.Value == "first<\n>second");

            press(Key.End, Key.ShiftLeft);
            AddAssert("shift+End selects nothing more at the end", () => box.SelectedText == string.Empty);
            press(Key.Home, Key.ShiftLeft);
            AddAssert("shift+Home selects the line", () => box.SelectedText == "first<");
        }

        [Test]
        public void ClicksAndDoubleClicksLandOnTheLineClicked()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.Text.Value = "aaaa\nbbbb cccc";
            });
            AddUntilStep("two lines", () => box.DrawHeight > 50);

            AddStep("click the start of the second line", () =>
            {
                var line = lineText.ScreenSpaceDrawQuad;
                InputManager.MoveMouseTo(new Vector2(line.TopLeft.X + 1, line.TopLeft.Y + line.Height * 0.75f));
                InputManager.Click(MouseButton.Left);
            });
            type("|");
            AddAssert("caret went there", () => box.Text.Value == "aaaa\n|bbbb cccc");

            AddStep("double-click the last word", () =>
            {
                var line = lineText.ScreenSpaceDrawQuad;
                InputManager.MoveMouseTo(new Vector2(line.TopRight.X - 2, line.TopLeft.Y + line.Height * 0.75f));
                InputManager.Click(MouseButton.Left);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("selects the word", () => box.SelectedText == "cccc");
        }

        [Test]
        public void WrapsLongLinesAndGrows()
        {
            create(b =>
            {
                b.TextWrapping = TextWrapping.Wrap;
                b.Text.Value = "The quick brown fox jumps over the lazy dog, again and again and again";
            }, 160);

            AddUntilStep("several lines", () => box.Multiline && box.DrawHeight > 36 * 2);
            AddAssert("no breaks added to the text", () => !box.Text.Value.Contains('\n'));
            AddAssert("within the box", () => lineText.DrawWidth <= box.DrawWidth - 20);
        }

        [Test]
        public void LineBreaksBindTwoWayAndSurviveLoading()
        {
            var text = new Bindable<string>("one\ntwo");

            create(b =>
            {
                b.AcceptsReturn = true;
                XamlBinding.TwoWay(b.Text, text);
            });

            AddAssert("kept through loading", () => box.Text.Value == "one\ntwo" && text.Value == "one\ntwo");
            AddUntilStep("two lines", () => box.DrawHeight > 50);

            AddStep("view model sets \\r\\n", () => text.Value = "a\r\nb\rc");
            AddAssert("box has \\n", () => box.Text.Value == "a\nb\nc");
            AddAssert("so does the view model", () => text.Value == "a\nb\nc");

            focus();
            press(Key.End, Key.ControlLeft);
            press(Key.Enter);
            type("d");
            AddAssert("typed break reaches the view model", () => text.Value == "a\nb\nc\nd");
        }

        [Test]
        public void TextWithBreaksIsMultilineAtAFixedHeight()
        {
            create(b => b.Text.Value = "one\ntwo");

            AddAssert("in lines", () => box.Multiline);
            AddAssert("keeps its height", () => box.DrawHeight == 36);

            AddStep("one line again", () => box.Text.Value = "one");
            AddUntilStep("back to a single row", () => !box.Multiline);
        }

        [Test]
        public void CopyAndPasteKeepLineBreaks()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.Text.Value = "a\nb";
            });
            focus();
            press(Key.A, Key.ControlLeft);
            press(Key.C, Key.ControlLeft);
            AddAssert("copied with \\n", () => clipboard.GetText() == "a\nb");

            AddStep("put two lines on the clipboard", () => clipboard.SetText("x\r\ny"));
            press(Key.End, Key.ControlLeft);
            press(Key.V, Key.ControlLeft);
            AddAssert("pasted as lines", () => box.Text.Value == "a\nbx\ny");
        }

        [Test]
        public void ScrollsToTheCaretAndWithTheWheel()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.Height = 80;
                b.Text.Value = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line {i}"));
            });

            AddAssert("fixed height", () => box.DrawHeight == 80);

            float top = 0;
            AddWaitStep("settle", 2);
            AddStep("remember where the text starts", () => top = lineText.ScreenSpaceDrawQuad.TopLeft.Y);

            focus();
            press(Key.End, Key.ControlLeft);
            AddUntilStep("scrolled to the last line", () => lineText.ScreenSpaceDrawQuad.BottomLeft.Y <= box.ScreenSpaceDrawQuad.BottomLeft.Y + 1);

            AddStep("wheel up", () =>
            {
                InputManager.MoveMouseTo(box);
                InputManager.ScrollVerticalBy(20);
            });
            AddUntilStep("back at the top", () => System.Math.Abs(lineText.ScreenSpaceDrawQuad.TopLeft.Y - top) < 1);
        }

        [Test]
        public void ScrollBarShowsWhileTheLinesDontFit()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.Height = 80;
                b.Text.Value = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line {i}"));
            });

            ScrollBar bar() => box.ChildrenOfType<ScrollBar>().Single(s => s.Orientation == Orientation.Vertical);

            float top = 0;
            AddUntilStep("a vertical bar", () => bar().IsPresent && bar().DrawHeight > 50);
            AddAssert("no horizontal one", () => !box.ChildrenOfType<ScrollBar>().Single(s => s.Orientation == Orientation.Horizontal).IsPresent);
            AddStep("remember where the text starts", () => top = lineText.ScreenSpaceDrawQuad.TopLeft.Y);

            AddStep("drag the thumb to the bottom", () =>
            {
                InputManager.MoveMouseTo(bar().Thumb);
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(bar().Thumb.ScreenSpaceDrawQuad.Centre + new Vector2(0, 200));
                InputManager.ReleaseButton(MouseButton.Left);
            });
            AddUntilStep("scrolled to the last line", () => System.Math.Abs(lineText.ScreenSpaceDrawQuad.BottomLeft.Y - (box.ScreenSpaceDrawQuad.BottomLeft.Y - 10)) < 1);
            AddAssert("the caret stayed put", () => box.SelectedText == string.Empty);

            AddStep("wheel up", () =>
            {
                InputManager.MoveMouseTo(box);
                InputManager.ScrollVerticalBy(20);
            });
            AddUntilStep("the bar follows", () => bar().Value.Value == 0 && System.Math.Abs(lineText.ScreenSpaceDrawQuad.TopLeft.Y - top) < 1);

            AddStep("one line", () => box.Text.Value = "one");
            AddUntilStep("bar hidden while everything fits", () => !bar().IsPresent);

            AddStep("Visible", () => box.VerticalScrollBarVisibility = ScrollBarVisibility.Visible);
            AddUntilStep("always shown", () => bar().IsPresent);

            AddStep("Hidden, with lines that don't fit", () =>
            {
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                box.Text.Value = string.Join("\n", Enumerable.Range(1, 12));
            });
            AddWaitStep("lay out", 2);
            AddAssert("never shown", () => !bar().IsPresent);
        }

        [Test]
        public void HorizontalScrollBarForLongLines()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                b.Text.Value = "short\nThe quick brown fox jumps over the lazy dog, again and again and again";
            });

            ScrollBar bar() => box.ChildrenOfType<ScrollBar>().Single(s => s.Orientation == Orientation.Horizontal);

            AddUntilStep("a horizontal bar", () => bar().IsPresent && bar().DrawWidth > 200);
            AddStep("scroll it to the end", () => bar().ScrollToMaximum());
            AddUntilStep("the long line's end is in view", () => lineText.ScreenSpaceDrawQuad.TopRight.X <= box.ScreenSpaceDrawQuad.TopRight.X);
        }

        [Test]
        public void MinAndMaxLinesLimitGrowing()
        {
            create(b =>
            {
                b.AcceptsReturn = true;
                b.MinLines = 3;
                b.MaxLines = 4;
            });

            float threeLines = 0;
            AddUntilStep("three lines tall while empty", () => (threeLines = box.DrawHeight) > 36 + 30);

            AddStep("ten lines", () => box.Text.Value = string.Join("\n", Enumerable.Range(1, 10)));
            AddUntilStep("stops at four lines", () => box.DrawHeight > threeLines && box.DrawHeight < threeLines + 25);
        }
    }
}
