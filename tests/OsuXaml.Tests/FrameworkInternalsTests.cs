using NUnit.Framework;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>
    /// Calls each private osu!framework member the controls reach through <see cref="FrameworkInternals"/>, so an osu!framework
    /// update that renames one fails here, by name, rather than somewhere inside text layout or editing.
    /// </summary>
    [HeadlessTest]
    public partial class FrameworkInternalsTests : TestScene
    {
        [Test]
        public void EveryAccessorStillBinds()
        {
            TextBox box = null;
            TextBlock text = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300 };
                panel.Add(box = new TextBox { Text = { Value = "hello" } });
                panel.Add(text = new TextBlock { Text = "abc" });
                Child = panel;
            });

            AddUntilStep("loaded", () => box.IsLoaded && text.IsLoaded);

            AddStep("TextBox selection", () =>
            {
                FrameworkInternals.SelectionStart(box) = 1;
                FrameworkInternals.SelectionEnd(box) = 3;
                FrameworkInternals.CursorAndLayout(box).Invalidate();
            });
            AddAssert("selection placed", () => box.SelectedText == "el");

            AddAssert("IME composition", () => FrameworkInternals.ImeCompositionDrawables(box).Count == 0 && FrameworkInternals.ImeCompositionStart(box) >= 0);

            AddAssert("SpriteText text and glyphs", () => FrameworkInternals.DisplayedText(text) == "abc" && FrameworkInternals.Characters(text).Count == 3);

            AddAssert("glyphs and bounds can be placed", () =>
            {
                var glyphs = FrameworkInternals.Characters(text);
                var glyph = glyphs[0];
                FrameworkInternals.SetDrawRectangle(ref glyph, new RectangleF(1, 2, 3, 4));
                glyphs[0] = glyph;
                FrameworkInternals.SetBounds(text.LaidOut, new Vector2(5, 6));

                bool moved = glyphs[0].DrawRectangle == new RectangleF(1, 2, 3, 4) && text.LaidOut.Bounds == new Vector2(5, 6);

                // Laying out again puts them back.
                FrameworkInternals.Invalidate(text, true, true);
                return moved && FrameworkInternals.Characters(text)[0].DrawRectangle.Width != 3;
            });

            AddAssert("disposal is visible", () => !FrameworkInternals.IsDisposed(text));

            bool ran = false;
            AddStep("schedule on a drawable", () => FrameworkInternals.Schedule(text, () => ran = true));
            AddUntilStep("it ran on the next update", () => ran);
        }
    }
}
