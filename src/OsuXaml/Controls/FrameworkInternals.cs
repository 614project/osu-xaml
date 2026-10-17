using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Caching;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Text;
using osuTK;
using FrameworkTextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace OsuXaml.Controls
{
    /// <summary>
    /// The few private osu!framework members that multi-line text needs. [UnsafeAccessor] binds them when first called,
    /// without reflection; <c>FrameworkInternalsTests</c> calls each one, so a framework update that renames them fails there first.
    /// </summary>
    internal static class FrameworkInternals
    {
        // osu!framework's TextBox keeps its caret and selection private and only ever moves them along one row.
        // Laying text out in lines needs to read them, and to place them for Up/Down and clicks on other lines.
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "selectionStart")]
        public static extern ref int SelectionStart(FrameworkTextBox textBox);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "selectionEnd")]
        public static extern ref int SelectionEnd(FrameworkTextBox textBox);

        /// <summary>Invalidated after placing the selection, so the box updates its caret state and the IME window.</summary>
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "cursorAndLayout")]
        public static extern ref Cached CursorAndLayout(FrameworkTextBox textBox);

        // Where the IME's unfinished text is, so lines can underline it.
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "imeCompositionStart")]
        public static extern ref int ImeCompositionStart(FrameworkTextBox textBox);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "imeCompositionDrawables")]
        public static extern ref List<Drawable> ImeCompositionDrawables(FrameworkTextBox textBox);

        /// <summary>The glyphs a SpriteText draws, laid out first if they're out of date.</summary>
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_characters")]
        public static extern List<TextBuilderGlyph> Characters(SpriteText text);

        /// <summary>Lays the glyphs out again, for changes SpriteText can't see (a run's font inside a TextBlock).</summary>
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "invalidate")]
        public static extern void Invalidate(SpriteText text, bool characters, bool textBuilder);

        /// <summary>The text a SpriteText shows: its Text, translated once it has loaded.</summary>
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_displayedText")]
        public static extern string DisplayedText(SpriteText text);

        // TextBuilder makes every line as tall as its font. A TextBlock mixing font sizes moves each line to fit what's on it,
        // then reports the height that adds up to.
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_DrawRectangle")]
        public static extern void SetDrawRectangle(ref TextBuilderGlyph glyph, RectangleF value);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Bounds")]
        public static extern void SetBounds(TextBuilder builder, Vector2 value);
    }
}
