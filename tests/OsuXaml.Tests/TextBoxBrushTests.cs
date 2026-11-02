using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osuTK;
using TextBox = OsuXaml.Controls.TextBox;
using PasswordBox = OsuXaml.Controls.PasswordBox;

namespace OsuXaml.Tests
{
    /// <summary>
    /// The text box's brushes and selection API (Foreground, PlaceholderForeground, SelectionBrush, SelectionOpacity,
    /// SelectionTextBrush, CaretBrush, SelectionStart/SelectionLength/CaretIndex/Select), the LineGap and AnimateCaret hooks, and
    /// PasswordBox.RevealPassword.
    /// </summary>
    [HeadlessTest]
    public partial class TextBoxBrushTests : ManualInputManagerTestScene
    {
        private static readonly Colour4 red = new Colour4(255, 0, 0, 255);
        private static readonly Colour4 green = new Colour4(0, 255, 0, 255);
        private static readonly Colour4 blue = new Colour4(0, 0, 255, 255);
        private static readonly Colour4 yellow = new Colour4(255, 255, 0, 255);

        private partial class BrushTextBox : TextBox
        {
            public int CaretAnimations;

            public float? Gap;

            public void Type(string text) => InsertString(text);

            public IReadOnlyList<Drawable> Characters => TextFlow.Children;

            public SpriteText PlaceholderSprite => Placeholder;

            public Drawable CaretDrawable => this.ChildrenOfType<Caret>().First();

            protected override float LineGap => Gap ?? base.LineGap;

            protected override void AnimateCaret(Drawable caret)
            {
                CaretAnimations++;
                base.AnimateCaret(caret);
            }
        }

        private partial class RevealingPasswordBox : PasswordBox
        {
            public void Type(string text) => InsertString(text);

            public string Drawn => string.Concat(TextFlow.ChildrenOfType<SpriteText>().Select(t => t.Text.ToString()));
        }

        private static Colour4 colourOf(Drawable d) => (Colour4)d.Colour;

        private static bool near(Colour4 a, Colour4 b) => System.Math.Abs(a.R - b.R) < 0.01f && System.Math.Abs(a.G - b.G) < 0.01f
                                                          && System.Math.Abs(a.B - b.B) < 0.01f && System.Math.Abs(a.A - b.A) < 0.01f;

        private void focus(System.Func<Drawable> d) => AddStep("click into the box", () =>
        {
            InputManager.MoveMouseTo(d());
            InputManager.Click(osuTK.Input.MouseButton.Left);
        });

        [Test]
        public void ForegroundColoursTheCharactersAndRecoloursThem()
        {
            BrushTextBox box = null;

            AddStep("create", () => Child = box = new BrushTextBox { Width = 200, Foreground = red });
            AddStep("type", () => box.Type("abc"));
            AddUntilStep("characters in the foreground", () => box.Characters.Count == 3 && box.Characters.All(c => near(colourOf(c), red)));

            AddStep("new foreground", () => box.Foreground = green);
            AddUntilStep("recoloured", () => box.Characters.All(c => near(colourOf(c), green)));
            AddAssert("text unchanged", () => box.Text.Value == "abc");
        }

        [Test]
        public void PlaceholderForegroundColoursThePlaceholder()
        {
            BrushTextBox box = null;

            AddStep("create", () => Child = box = new BrushTextBox { Width = 200, PlaceholderText = "Name" });
            AddStep("placeholder colour", () => box.PlaceholderForeground = yellow);
            AddAssert("placeholder drawn in it", () => near(colourOf(box.PlaceholderSprite), yellow) && box.PlaceholderForeground == yellow);
        }

        [Test]
        public void SelectApiAndSelectionTextBrush()
        {
            BrushTextBox box = null;

            AddStep("create", () => Child = box = new BrushTextBox { Width = 200, Foreground = red, SelectionTextBrush = yellow });
            AddStep("type", () => box.Type("abcd"));
            focus(() => box);
            AddStep("select b and c", () => box.Select(1, 2));
            AddAssert("selection reported", () => box.SelectionStart == 1 && box.SelectionLength == 2 && box.CaretIndex == 3);
            AddUntilStep("selected characters in the selection text brush",
                () => near(colourOf(box.Characters[1]), yellow) && near(colourOf(box.Characters[2]), yellow)
                      && near(colourOf(box.Characters[0]), red) && near(colourOf(box.Characters[3]), red));

            AddStep("caret index clears the selection", () => box.CaretIndex = 4);
            AddAssert("nothing selected", () => box.SelectionLength == 0 && box.SelectionStart == 4);
            AddUntilStep("all in the foreground again", () => box.Characters.All(c => near(colourOf(c), red)));

            AddStep("select past the end", () => box.Select(2, 99));
            AddAssert("clamped to the text", () => box.SelectionStart == 2 && box.SelectionLength == 2);

            AddStep("lose focus", () => InputManager.ChangeFocus(null));
            AddUntilStep("unfocused selection isn't coloured", () => box.Characters.All(c => near(colourOf(c), red)));
        }

        [Test]
        public void CaretAndSelectionTakeTheirBrushes()
        {
            BrushTextBox box = null;

            AddStep("create", () => Child = box = new BrushTextBox { Width = 200, SelectionBrush = blue, SelectionOpacity = 1, CaretBrush = green });
            AddStep("type", () => box.Type("abcd"));
            focus(() => box);
            AddStep("select", () => box.Select(0, 2));
            AddUntilStep("selection in the selection brush, solid", () => near(colourOf(box.CaretDrawable), blue) && box.CaretDrawable.Alpha > 0.99f);

            AddStep("caret only", () => box.Select(1, 0));
            AddUntilStep("caret in the caret brush", () => near(colourOf(box.CaretDrawable), green));
            AddAssert("caret animation hook ran", () => box.CaretAnimations > 0);
        }

        [Test]
        public void LineGapSpacesLines()
        {
            BrushTextBox normal = null;
            BrushTextBox wide = null;

            AddStep("create", () => Child = new osu.Framework.Graphics.Containers.FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = osu.Framework.Graphics.Containers.FillDirection.Vertical,
                Children = new Drawable[]
                {
                    normal = new BrushTextBox { Width = 200, AcceptsReturn = true, Text = { Value = "one\ntwo\nthree" } },
                    wide = new BrushTextBox { Width = 200, AcceptsReturn = true, Gap = 20, Text = { Value = "one\ntwo\nthree" } },
                },
            });

            AddUntilStep("lines laid out", () => normal.DrawHeight > 40 && wide.DrawHeight > 40);
            AddAssert("two gaps of 20 instead of the default", () =>
            {
                float defaultGap = System.MathF.Round(normal.FontSize * 0.25f);
                return System.Math.Abs(wide.DrawHeight - normal.DrawHeight - 2 * (20 - defaultGap)) < 0.5f;
            });
        }

        [Test]
        public void RevealPasswordShowsThePasswordWithoutChangingIt()
        {
            RevealingPasswordBox box = null;
            int changes = 0;

            AddStep("create", () => Child = box = new RevealingPasswordBox { Width = 200 });
            AddStep("type", () => box.Type("abc"));
            AddUntilStep("masked", () => box.Drawn == "•••");

            AddStep("count changes", () =>
            {
                changes = 0;
                box.Password.ValueChanged += _ => changes++;
            });
            AddStep("reveal", () => box.RevealPassword = true);
            AddUntilStep("plain text drawn", () => box.Drawn == "abc");
            AddStep("hide", () => box.RevealPassword = false);
            AddUntilStep("masked again", () => box.Drawn == "•••");
            AddAssert("password never changed", () => changes == 0 && box.Password.Value == "abc" && !box.RevealPassword);
        }
    }
}
