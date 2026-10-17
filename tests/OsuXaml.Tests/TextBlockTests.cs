using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using OsuXaml.Controls;

namespace OsuXaml.Tests
{
    /// <summary>TextBlock wrapping and trimming against the width its layout offers.</summary>
    [HeadlessTest]
    public partial class TextBlockTests : TestScene
    {
        private const string long_text = "The quick brown fox jumps over the lazy dog, again and again and again";

        [Test]
        public void WrapsAtThePanelWidthAndUnwrapsWhenWider()
        {
            StackPanel panel = null;
            TextBlock plain = null, wrapped = null;

            AddStep("create", () =>
            {
                panel = new StackPanel { Width = 150 };
                panel.Add(plain = new TextBlock { Text = long_text });
                panel.Add(wrapped = new TextBlock { Text = long_text, TextWrapping = TextWrapping.Wrap });
                Child = panel;
            });

            AddUntilStep("wraps within the panel", () => wrapped.DrawWidth <= 150 && wrapped.DrawHeight > plain.DrawHeight * 2);
            AddAssert("plain text stays on one line", () => plain.DrawWidth > 150);
            AddAssert("panel grows with the lines", () => panel.DrawHeight >= plain.DrawHeight + wrapped.DrawHeight - 1);

            AddStep("widen the panel", () => panel.Width = 3000);
            AddUntilStep("one line again", () => Precision(wrapped.DrawHeight, plain.DrawHeight) && Precision(wrapped.DrawWidth, plain.DrawWidth));
        }

        [Test]
        public void WrapsAtTheWidthOfItsGridCell()
        {
            Grid grid = null;
            TextBlock label = null, wrapped = null, below = null;

            AddStep("create", () =>
            {
                grid = new Grid
                {
                    Width = 400,
                    ColumnDefinitions = GridLengthCollection.Parse("100,*,*"),
                    RowDefinitions = GridLengthCollection.Parse("Auto,Auto"),
                };

                grid.Add(label = new TextBlock { Text = "Label" });
                grid.Add(wrapped = new TextBlock { Text = long_text, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(wrapped, 1);
                grid.Add(below = new TextBlock { Text = "Below" });
                Grid.SetRow(below, 1);
                Child = grid;
            });

            // The star columns share 300 pixels, so the text wraps at 150 rather than at the grid's 400.
            AddUntilStep("wraps at the star column width", () => wrapped.DrawWidth <= 150 && wrapped.DrawHeight > label.DrawHeight * 2);
            AddAssert("the Auto row grows with the lines", () => below.ScreenSpaceDrawQuad.TopLeft.Y >= wrapped.ScreenSpaceDrawQuad.BottomLeft.Y - 1);
        }

        [Test]
        public void WrapsBetweenWords()
        {
            TextBlock twoWords = null, partOfThird = null, wrapped = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel();
                panel.Add(twoWords = new TextBlock { Text = "alpha beta" });
                panel.Add(partOfThird = new TextBlock { Text = "alpha beta ga" });
                Child = panel;
            });
            AddUntilStep("measured", () => partOfThird.DrawWidth > twoWords.DrawWidth + 10);

            AddStep("wrap at the width of \"alpha beta ga\"", () =>
            {
                var panel = new StackPanel { Width = partOfThird.DrawWidth };
                panel.Add(wrapped = new TextBlock { Text = "alpha beta gamma", TextWrapping = TextWrapping.Wrap });
                Child = panel;
            });

            // Breaking at the edge would leave "alpha beta ga" on the first line; WPF moves the whole of "gamma" down.
            AddUntilStep("two lines", () => wrapped.IsLoaded && wrapped.DrawHeight > twoWords.DrawHeight * 1.5f);
            AddAssert("first line ends after \"beta\"", () => wrapped.DrawWidth < partOfThird.DrawWidth - 5);
        }

        [Test]
        public void TrimsWithAnEllipsis()
        {
            TextBlock trimmed = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 120 };
                panel.Add(trimmed = new TextBlock { Text = long_text, TextTrimming = TextTrimming.CharacterEllipsis });
                Child = panel;
            });

            AddUntilStep("truncated to the width", () => trimmed.IsTruncated && trimmed.DrawWidth <= 120);
        }

        [Test]
        public void WrappingTextInAHorizontalStackPanelKeepsOneLine()
        {
            TextBlock plain = null, wrapped = null;

            AddStep("create", () =>
            {
                var column = new StackPanel();
                column.Add(plain = new TextBlock { Text = long_text });

                // A horizontal StackPanel offers unlimited width, as in WPF.
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Add(wrapped = new TextBlock { Text = long_text, TextWrapping = TextWrapping.Wrap });
                column.Add(row);

                Child = column;
            });

            AddUntilStep("laid out", () => wrapped.DrawWidth > 0);
            AddAssert("single line", () => Precision(wrapped.DrawHeight, plain.DrawHeight) && Precision(wrapped.DrawWidth, plain.DrawWidth));
        }

        [Test]
        public void TextStillSizesItselfAfterLayout()
        {
            TextBlock text = null;
            float shortWidth = 0;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 2000 };
                panel.Add(text = new TextBlock { Text = "Hi" });
                Child = panel;
            });

            AddUntilStep("measured", () => text.DrawWidth > 0);
            AddStep("remember width", () => shortWidth = text.DrawWidth);
            AddStep("longer text", () => text.Text = long_text);
            AddUntilStep("grows with its text, so the layout didn't pin its width", () => text.DrawWidth > shortWidth * 5);
        }

        [Test]
        public void ExplicitWidthWrapsOutsidePanels()
        {
            TextBlock text = null, line = null;

            AddStep("create", () => Child = new Container
            {
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    line = new TextBlock { Text = "x" },
                    text = new TextBlock { Width = 100, Text = long_text, TextWrapping = TextWrapping.Wrap },
                },
            });

            AddUntilStep("several lines", () => text.DrawHeight > line.DrawHeight * 2 && text.DrawWidth == 100);
        }

        [Test]
        public void LineBreaksInTheTextStartNewLines()
        {
            TextBlock one = null, two = null, crlf = null, trailing = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel();
                panel.Add(one = new TextBlock { Text = "alpha" });
                panel.Add(two = new TextBlock { Text = "alpha\nbeta" });
                panel.Add(crlf = new TextBlock { Text = "alpha\r\nbeta" });
                panel.Add(trailing = new TextBlock { Text = "alpha\n" });
                Child = panel;
            });

            AddUntilStep("laid out", () => two.DrawHeight > 0 && one.DrawHeight > 0);
            AddAssert("two lines, as wide as the longer one", () => Precision(two.DrawHeight, one.DrawHeight * 2) && Precision(two.DrawWidth, one.DrawWidth));
            AddAssert("\\r\\n is one break", () => Precision(crlf.DrawHeight, two.DrawHeight) && Precision(crlf.DrawWidth, two.DrawWidth));
            AddAssert("a closing break adds an empty line", () => Precision(trailing.DrawHeight, one.DrawHeight * 2));
        }

        [Test]
        public void InlinesStyleTheirTextAndSpellIt()
        {
            TextBlock plain = null, styled = null;
            Run run = null;
            Bold bold = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel();
                panel.Add(plain = new TextBlock { Text = "plain bold" });

                styled = new TextBlock();
                styled.AddText("plain ");
                styled.Add(bold = new Bold());
                bold.Add(run = new Run("bold"));
                panel.Add(styled);

                Child = panel;
            });

            AddUntilStep("laid out", () => styled.DrawWidth > 0 && plain.DrawWidth > 0);
            AddAssert("Text spells the inlines", () => styled.Text.ToString() == "plain bold");
            AddAssert("bold glyphs come from the bold font", () => fontOf(styled, 0).EndsWith("-Regular") && fontOf(styled, 9).EndsWith("-Bold"));
            AddAssert("same line height", () => Precision(styled.DrawHeight, plain.DrawHeight));

            AddStep("unbold", () => bold.FontWeight = FontWeight.Regular);
            AddUntilStep("regular, as wide as plain text", () => fontOf(styled, 9).EndsWith("-Regular") && Precision(styled.DrawWidth, plain.DrawWidth));

            AddStep("change the run", () => run.Text = "bold and more");
            AddUntilStep("Text follows", () => styled.Text.ToString() == "plain bold and more" && styled.DrawWidth > plain.DrawWidth + 20);

            AddStep("set Text instead", () => styled.Text = "other");
            AddUntilStep("shows the new text", () => styled.DrawWidth < plain.DrawWidth);
        }

        [Test]
        public void LinesAreAsTallAsTheirLargestFont()
        {
            TextBlock text = null;

            AddStep("create", () =>
            {
                text = new TextBlock();
                text.Add(new Run("Title") { FontSize = 32 });
                text.Add(new LineBreak());
                text.AddText("small print");
                text.Add(new LineBreak());
                var panel = new StackPanel();
                panel.Add(text);
                Child = panel;
            });

            AddUntilStep("laid out", () => text.DrawHeight > 0);
            AddAssert("32 + 16 + an empty 16 line", () => Precision(text.DrawHeight, 32 + 16 + 16));
            AddAssert("Text has the breaks", () => text.Text.ToString() == "Title\nsmall print\n");
        }

        [Test]
        public void TrimsEachLine()
        {
            TextBlock trimmed = null, line = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 120 };
                panel.Add(line = new TextBlock { Text = "x" });
                panel.Add(trimmed = new TextBlock { Text = long_text + "\nshort\n" + long_text, TextTrimming = TextTrimming.CharacterEllipsis });
                Child = panel;
            });

            AddUntilStep("truncated to the width", () => trimmed.IsTruncated && trimmed.DrawWidth <= 120);
            AddAssert("still three lines", () => Precision(trimmed.DrawHeight, line.DrawHeight * 3));
        }

        [Test]
        public void ColoursAndDecorationsDraw()
        {
            TextBlock text = null;

            AddStep("create", () =>
            {
                text = new TextBlock { TextDecorations = TextDecorations.Strikethrough, Shadow = true };
                text.Add(new Run("red ") { Foreground = Colour4.Red });
                text.Add(new Underline { Inlines = { new Italic { Inlines = { "underlined italic" } } } });
                text.Add(new LineBreak());
                text.Add(new Span { Foreground = Colour4.Green, TextDecorations = TextDecorations.Underline, Inlines = { "green" } });
                Child = text;
            });

            // The draw node builds colours and lines from the layout every frame; a few frames shows it holds together.
            AddWaitStep("draw a few frames", 5);
            AddAssert("laid out on two lines", () => text.DrawHeight > 30 && text.Text.ToString() == "red underlined italic\ngreen");
        }

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 1;

        private static string fontOf(TextBlock text, int glyph) => text.LaidOut.GlyphStyles[glyph].Font.FontName;
    }
}
