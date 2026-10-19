using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>TextBlock alignment, font families, links among the text and drawables among the text.</summary>
    [HeadlessTest]
    public partial class RichTextTests : ManualInputManagerTestScene
    {
        private const string long_text = "The quick brown fox jumps over the lazy dog, again and again and again";

        /// <summary>Where a glyph is on screen.</summary>
        private static RectangleF glyph(TextBlock text, int index)
        {
            var rectangle = FrameworkInternals.Characters(text)[index].DrawRectangle;
            var quad = text.ToScreenSpace(rectangle);
            return new RectangleF(quad.TopLeft, quad.BottomRight - quad.TopLeft);
        }

        private static float left(TextBlock text) => text.ScreenSpaceDrawQuad.TopLeft.X;

        [Test]
        public void AlignmentPlacesLinesAcrossTheWidth()
        {
            TextBlock plain = null, centred = null, right = null, justified = null, wrapped = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300 };
                panel.Add(plain = new TextBlock { Text = "ab" });
                panel.Add(centred = new TextBlock { Text = "ab", TextAlignment = TextAlignment.Center });
                panel.Add(right = new TextBlock { Text = "ab", TextAlignment = TextAlignment.Right });
                panel.Add(wrapped = new TextBlock { Text = long_text, TextWrapping = TextWrapping.Wrap });
                panel.Add(justified = new TextBlock { Text = long_text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Justify });
                Child = panel;
            });

            AddUntilStep("laid out", () => plain.DrawWidth > 0 && centred.DrawWidth > 0 && justified.DrawHeight > plain.DrawHeight);
            AddAssert("aligned text takes the panel's width", () => Precision(centred.DrawWidth, 300) && Precision(right.DrawWidth, 300));
            AddAssert("centred", () => Precision(glyph(centred, 0).Left - left(centred), glyph(plain, 0).Left - left(plain) + (300 - plain.DrawWidth) / 2));
            AddAssert("right-aligned", () => Precision(glyph(right, 1).Left - left(right), glyph(plain, 1).Left - left(plain) + 300 - plain.DrawWidth));

            AddAssert("justified lines end at the edge, their spaces widened", () =>
            {
                var line = justified.LaidOut.Lines[0];
                int middle = (line.Start + line.End) / 2;
                return Precision(glyph(justified, lastGlyph(justified, line)).Right - left(justified), 300)
                       && glyph(justified, middle).Left - left(justified) > glyph(wrapped, middle).Left - left(wrapped) + 0.1f;
            });
            AddAssert("but not the last line", () =>
            {
                var lines = justified.LaidOut.Lines;
                return Precision(glyph(justified, lines[^1].End - 1).Right - left(justified), glyph(wrapped, wrapped.LaidOut.Lines[^1].End - 1).Right - left(wrapped));
            });

            AddStep("wider panel", () => ((StackPanel)centred.Parent.Parent).Width = 500);
            AddUntilStep("centred in the new width", () => Precision(centred.DrawWidth, 500)
                                                              && Precision(glyph(centred, 0).Left - left(centred), glyph(plain, 0).Left - left(plain) + (500 - plain.DrawWidth) / 2));
        }

        private static int lastGlyph(TextBlock text, TextLine line)
        {
            var characters = FrameworkInternals.Characters(text);
            int last = line.End - 1;

            while (last > line.Start && char.IsWhiteSpace(characters[last].Character))
                last--;

            return last;
        }

        [Test]
        public void AlignedTextInAHorizontalStackPanelAlignsToItsWidestLine()
        {
            TextBlock text = null, wide = null;

            AddStep("create", () =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Add(text = new TextBlock { Text = "a much longer line\nshort", TextAlignment = TextAlignment.Right });
                var column = new StackPanel();
                column.Add(wide = new TextBlock { Text = "a much longer line" });
                column.Add(row);
                Child = column;
            });

            AddUntilStep("laid out", () => text.DrawWidth > 0 && wide.DrawWidth > 0);
            AddAssert("as wide as the widest line", () => Precision(text.DrawWidth, wide.DrawWidth));
            AddAssert("short line ends where the long one does", () =>
            {
                var lines = text.LaidOut.Lines;
                return Precision(glyph(text, lines[0].End - 1).Right, glyph(text, lines[1].End - 1).Right);
            });
        }

        [Test]
        public void FontFamiliesChooseTheFont()
        {
            TextBlock regular = null, condensed = null, listed = null, mixed = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel();
                panel.Add(regular = new TextBlock { Text = long_text });
                panel.Add(condensed = new TextBlock { Text = long_text, FontFamily = "RobotoCondensed" });
                panel.Add(listed = new TextBlock { Text = long_text, FontFamily = "'Segoe UI', Roboto" });

                mixed = new TextBlock();
                mixed.AddText("plain ");
                mixed.Add(new Run("narrow") { FontFamily = "RobotoCondensed" });
                panel.Add(mixed);
                Child = panel;
            });

            AddUntilStep("laid out", () => regular.DrawWidth > 0 && condensed.DrawWidth > 0 && mixed.DrawWidth > 0);
            AddAssert("condensed is narrower", () => condensed.Font.Family == "RobotoCondensed" && condensed.DrawWidth < regular.DrawWidth - 20);
            AddAssert("the first of a list is used", () => listed.FontFamily == "Segoe UI");
            AddAssert("a family it lacks falls back to a font with glyphs", () => Precision(listed.DrawHeight, regular.DrawHeight) && FrameworkInternals.Characters(listed).All(c => c.Texture != null || c.Character == ' '));
            AddAssert("a run picks its own family", () => mixed.LaidOut.GlyphStyles[0].Font.FontName.StartsWith("Roboto-")
                                                          && mixed.LaidOut.GlyphStyles[^1].Font.FontName.StartsWith("RobotoCondensed-"));
        }

        [Test]
        public void LinksAmongTheTextHoverAndClick()
        {
            TextBlock text = null;
            Hyperlink link = null;
            string navigated = null;
            int clicks = 0;

            AddStep("create", () =>
            {
                text = new TextBlock { TextWrapping = TextWrapping.Wrap };
                text.AddText("Read ");
                text.Add(link = new Hyperlink { Text = "the docs", NavigateUri = "https://example.com/docs" });
                text.AddText(" first.");
                link.RequestNavigate += uri => navigated = uri;
                link.Click += () => clicks++;

                var panel = new StackPanel { Width = 300 };
                panel.Add(text);
                Child = panel;
            });

            AddUntilStep("laid out", () => text.DrawWidth > 0);
            AddAssert("the link's text is part of the text", () => text.Text.ToString() == "Read the docs first.");

            AddStep("hover the link", () => InputManager.MoveMouseTo(glyph(text, 6).Centre));
            AddAssert("hovered", () => text.LaidOut.GlyphStyles[6].Link.Hovered);

            AddStep("click it", () => InputManager.Click(MouseButton.Left));
            AddAssert("navigated", () => navigated == "https://example.com/docs" && clicks == 1);

            AddStep("click plain text", () =>
            {
                InputManager.MoveMouseTo(glyph(text, 1).Centre);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("nothing happened", () => clicks == 1 && !text.LaidOut.GlyphStyles[6].Link.Hovered);

            AddStep("change the link's text", () => link.Text = "the full documentation");
            AddUntilStep("text follows", () => text.Text.ToString() == "Read the full documentation first.");

            AddStep("disable it", () => link.IsEnabled = false);
            AddStep("click it", () =>
            {
                InputManager.MoveMouseTo(glyph(text, 6).Centre);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("disabled links don't click", () => clicks == 1);
        }

        [Test]
        public void PlainTextDoesNotTakeTheMouse()
        {
            TextBlock text = null;
            Button button = null;
            int clicks = 0;

            AddStep("create", () =>
            {
                button = new Button { Content = text = new TextBlock { Text = "Press" } };
                button.Click += () => clicks++;
                var panel = new StackPanel { Width = 200 };
                panel.Add(button);
                Child = panel;
            });

            AddUntilStep("laid out", () => text.DrawWidth > 0);
            AddStep("click the text", () =>
            {
                InputManager.MoveMouseTo(text);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("the button got it", () => clicks == 1 && !text.HandlePositionalInput);
        }

        [Test]
        public void DrawablesAmongTheTextTakeTheirRoom()
        {
            TextBlock text = null, plain = null;
            Box box = null;
            Button button = null;
            int clicks = 0;

            AddStep("create", () =>
            {
                var panel = new StackPanel();
                panel.Add(plain = new TextBlock { Text = "AB" });

                text = new TextBlock();
                text.AddText("A");
                text.Add(new InlineUIContainer(box = new Box { Size = new Vector2(20, 40) }));
                text.AddText("B");
                text.Add(button = new Button { Content = "Go" });
                button.Click += () => clicks++;
                panel.Add(text);
                Child = panel;
            });

            AddUntilStep("laid out", () => text.DrawWidth > plain.DrawWidth + 20 && box.IsPresent);
            AddAssert("the box sits between A and B", () =>
            {
                var boxQuad = box.ScreenSpaceDrawQuad;
                return Precision(boxQuad.TopLeft.X, glyph(text, 0).Left + FrameworkInternals.Characters(text)[0].XAdvance - FrameworkInternals.Characters(text)[0].XOffset)
                       && boxQuad.TopRight.X <= glyph(text, 2).Left + 1;
            });
            AddAssert("on the baseline, and the line is taller", () =>
            {
                var line = text.LaidOut.Lines[0];
                return Precision(box.ScreenSpaceDrawQuad.BottomLeft.Y, text.ToScreenSpace(new Vector2(0, line.Baseline)).Y) && text.DrawHeight > 40;
            });

            AddStep("widen the box", () => box.Width = 60);
            AddUntilStep("B moves along", () => glyph(text, 2).Left > box.ScreenSpaceDrawQuad.TopRight.X - 1 && box.ScreenSpaceDrawQuad.Width > 59);

            AddUntilStep("the button is shown", () => button.IsPresent && button.DrawWidth > 0);
            AddStep("click the button", () =>
            {
                InputManager.MoveMouseTo(button);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("it works", () => clicks == 1);

            AddStep("hide the text", () => text.Alpha = 0);
            AddUntilStep("its drawables hide too", () => !box.IsPresent || !box.Parent.IsPresent);
        }

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 1;
    }
}
