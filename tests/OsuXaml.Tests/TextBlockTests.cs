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

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 1;
    }
}
