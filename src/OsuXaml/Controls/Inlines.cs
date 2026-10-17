using System;
using System.Collections;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;

namespace OsuXaml.Controls
{
    public enum FontStyle
    {
        Normal,
        Italic,
    }

    /// <summary>Lines drawn through text. Combine them with <c>|</c>.</summary>
    [Flags]
    public enum TextDecorations
    {
        None = 0,
        Underline = 1,
        Strikethrough = 2,
    }

    /// <summary>
    /// A piece of <see cref="TextBlock"/> content, like WPF's inlines: a <see cref="Run"/> of text, a <see cref="LineBreak"/>,
    /// or a <see cref="Span"/> that styles what it contains. Properties left unset take the value of the span or TextBlock around it.
    /// </summary>
    public abstract class Inline
    {
        private Colour4? foreground;
        private float? fontSize;
        private FontWeight? fontWeight;
        private FontStyle? fontStyle;
        private TextDecorations textDecorations;

        /// <summary>The collection this inline belongs to.</summary>
        internal InlineCollection Owner;

        public Colour4 Foreground
        {
            get => foreground ?? Colour4.White;
            set
            {
                foreground = value;
                Changed();
            }
        }

        public float FontSize
        {
            get => fontSize ?? 0;
            set
            {
                fontSize = value;
                Changed();
            }
        }

        public FontWeight FontWeight
        {
            get => fontWeight ?? FontWeight.Regular;
            set
            {
                fontWeight = value;
                Changed();
            }
        }

        public FontStyle FontStyle
        {
            get => fontStyle ?? FontStyle.Normal;
            set
            {
                fontStyle = value;
                Changed();
            }
        }

        /// <summary>Added to the decorations of the span around it.</summary>
        public TextDecorations TextDecorations
        {
            get => textDecorations;
            set
            {
                textDecorations = value;
                Changed();
            }
        }

        /// <summary>Tells the TextBlock showing this inline to lay its text out again.</summary>
        protected void Changed() => Owner?.OnChanged();

        /// <summary>The style of this inline's text, given the style around it.</summary>
        internal TextStyle StyleWithin(TextStyle outer)
        {
            if (foreground == null && fontSize == null && fontWeight == null && fontStyle == null && textDecorations == TextDecorations.None)
                return outer;

            var font = outer.Font;

            if (fontWeight is FontWeight weight)
                font = font.With(weight: weight.ToString());
            if (fontStyle is FontStyle style)
                font = font.With(italics: style == FontStyle.Italic);
            if (fontSize is float size)
                font = font.With(size: size);

            return new TextStyle(font, foreground ?? outer.Foreground, outer.Decorations | textDecorations);
        }

        /// <summary>Appends this inline's text, split into runs of one style.</summary>
        internal abstract void AppendRuns(List<TextRun> runs, TextStyle outer);

        /// <summary>Moves the inline out of any collection it is in, so it can join another.</summary>
        internal void Detach() => Owner?.Remove(this);
    }

    /// <summary>A run of text in one style. Line breaks in <see cref="Text"/> start new lines.</summary>
    public class Run : Inline
    {
        private string text = string.Empty;

        public Run()
        {
        }

        public Run(string text)
        {
            this.text = text ?? string.Empty;
        }

        public string Text
        {
            get => text;
            set
            {
                value ??= string.Empty;

                if (text == value)
                    return;

                text = value;
                Changed();
            }
        }

        internal override void AppendRuns(List<TextRun> runs, TextStyle outer)
        {
            if (text.Length > 0)
                runs.Add(new TextRun(TextRun.NormaliseLineBreaks(text), StyleWithin(outer)));
        }
    }

    /// <summary>Ends the line, like WPF's &lt;LineBreak /&gt;. Whitespace written next to it in XAML is dropped.</summary>
    [TrimSurroundingWhitespace]
    public class LineBreak : Inline
    {
        internal override void AppendRuns(List<TextRun> runs, TextStyle outer) => runs.Add(new TextRun("\n", StyleWithin(outer)));
    }

    /// <summary>Groups inlines and styles them together. Text written inside it in XAML becomes runs.</summary>
    public class Span : Inline
    {
        public Span()
        {
            Inlines = new InlineCollection(Changed);
        }

        public InlineCollection Inlines { get; }

        public void Add(Inline inline) => Inlines.Add(inline);

        public void AddText(string text) => Inlines.Add(new Run(text));

        internal override void AppendRuns(List<TextRun> runs, TextStyle outer)
        {
            var style = StyleWithin(outer);

            foreach (var inline in Inlines)
                inline.AppendRuns(runs, style);
        }
    }

    public class Bold : Span
    {
        public Bold()
        {
            FontWeight = FontWeight.Bold;
        }
    }

    public class Italic : Span
    {
        public Italic()
        {
            FontStyle = FontStyle.Italic;
        }
    }

    public class Underline : Span
    {
        public Underline()
        {
            TextDecorations = TextDecorations.Underline;
        }
    }

    /// <summary>The inlines of a <see cref="TextBlock"/> or <see cref="Span"/>. Adding an inline takes it out of the collection it was in.</summary>
    public sealed class InlineCollection : IEnumerable<Inline>
    {
        private readonly List<Inline> items = new List<Inline>();
        private readonly Action changed;

        internal InlineCollection(Action changed)
        {
            this.changed = changed;
        }

        public int Count => items.Count;

        public Inline this[int index] => items[index];

        public void Add(Inline inline) => Insert(items.Count, inline);

        /// <summary>Adds a <see cref="Run"/> of <paramref name="text"/>.</summary>
        public void Add(string text) => Add(new Run(text));

        public void Insert(int index, Inline inline)
        {
            ArgumentNullException.ThrowIfNull(inline);

            inline.Detach();
            items.Insert(Math.Clamp(index, 0, items.Count), inline);
            inline.Owner = this;
            changed();
        }

        public bool Remove(Inline inline)
        {
            if (!items.Remove(inline))
                return false;

            inline.Owner = null;
            changed();
            return true;
        }

        public void Clear()
        {
            if (items.Count == 0)
                return;

            foreach (var inline in items)
                inline.Owner = null;

            items.Clear();
            changed();
        }

        internal void OnChanged() => changed();

        public List<Inline>.Enumerator GetEnumerator() => items.GetEnumerator();

        IEnumerator<Inline> IEnumerable<Inline>.GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }

    /// <summary>How a stretch of TextBlock text looks. Instances are compared by reference while laying out.</summary>
    internal sealed class TextStyle
    {
        public readonly FontUsage Font;

        /// <summary>Null draws in the TextBlock's own colour.</summary>
        public readonly Colour4? Foreground;

        public readonly TextDecorations Decorations;

        public TextStyle(FontUsage font, Colour4? foreground, TextDecorations decorations)
        {
            Font = font;
            Foreground = foreground;
            Decorations = decorations;
        }
    }

    internal readonly struct TextRun
    {
        public readonly string Text;
        public readonly TextStyle Style;

        public TextRun(string text, TextStyle style)
        {
            Text = text;
            Style = style;
        }

        /// <summary>Turns "\r\n" and a lone "\r" into "\n".</summary>
        public static string NormaliseLineBreaks(string text) => text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
