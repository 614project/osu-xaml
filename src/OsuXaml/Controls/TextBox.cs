using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// What <see cref="TextBox"/> and <see cref="PasswordBox"/> share: osu!framework's BasicTextBox in the library's look, plus lines.
    /// With <see cref="AcceptsReturn"/>, <see cref="TextWrapping"/>, or text that contains line breaks, the box lays its text out in lines
    /// and edits it there: Up/Down, Home/End per line, Ctrl+Home/End, PageUp/PageDown, clicking and dragging across lines, and the mouse wheel.
    /// </summary>
    public abstract partial class TextBoxBase : BasicTextBox, ILayoutElement
    {
        // osu!framework's TextBox refuses control characters, so line breaks are kept inside it as U+2028 LINE SEPARATOR
        // and turned back into '\n' wherever the text leaves it.
        private const char line_separator = '\u2028';

        private const float default_height = 36;

        [Resolved]
        private Clipboard clipboard { get; set; }

        [Resolved]
        private TextInputSource textInput { get; set; }

        private bool acceptsReturn;
        private TextWrapping textWrapping;
        private int minLines = 1;
        private int maxLines = int.MaxValue;

        // The text in lines: scrolled as a whole, with the selection behind the text and the caret in front.
        private readonly Container lineContent;
        private readonly Container selectionBoxes;
        private readonly TextBlock lineText;
        private readonly Container lineCaret;

        private bool lineMode;
        private bool lineLayoutValid;

        // What the lines were last laid out from.
        private string laidOutRaw;
        private float laidOutWrapWidth = float.NaN;
        private int laidOutCompositionStart;
        private int laidOutCompositionLength;
        private float laidOutFontSize;

        // Where the caret can stand: for each index into the text, the line it's on and its x.
        private int[] stopLine = { 0 };
        private float[] stopX = { 0 };
        private readonly List<LineInfo> lines = new List<LineInfo>();
        private float contentWidth;
        private float contentHeight;

        private Vector2 scroll;
        private bool scrollPlaced;
        private bool scrollPending;
        private Vector2 scrolledViewSize;

        // The caret sits at the end of a wrapped line rather than the start of the next (they're the same index).
        private int lineEndCaret = -1;

        // The x that Up/Down aim for, kept while the caret only moves up and down.
        private float? verticalX;
        private int verticalCaret = -1;

        private double? lastDoubleClickTime;
        private (int Start, int End)? doubleClickWord;
        private bool tripleClicked;

        // A height set in XAML or code, rather than the default or the layout handing back the measured height.
        private bool heightSet;
        private float autoHeight = default_height;

        private string lastRaw;
        private float laidOutWidth = -1;
        private bool refreshing;

        protected TextBoxBase()
        {
            base.Height = default_height;

            // osu!framework sizes text to the box's height unless told otherwise; match the rest of the controls instead.
            FontSize = 16;
            BackgroundUnfocused = Theme.Surface;
            BackgroundFocused = Theme.Border;
            BackgroundCommit = Theme.Accent;
            CornerRadius = 6;
            Masking = true;

            Add(lineContent = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Depth = 0.5f,
                Alpha = 0,
                Children = new Drawable[]
                {
                    selectionBoxes = new Container { Depth = 1 },
                    lineText = new TextBlock { Colour = Colour4.White },
                    lineCaret = new Container
                    {
                        Depth = -1,
                        Width = CaretWidth,
                        Alpha = 0,
                        Masking = true,
                        CornerRadius = 1,
                        Child = new Box { RelativeSizeAxes = Axes.Both },
                    },
                },
            });
        }

        /// <summary>
        /// Enter starts a new line instead of committing, and pasted line breaks are kept, like WPF's AcceptsReturn.
        /// Without a Height of its own the box grows with its lines, between <see cref="MinLines"/> and <see cref="MaxLines"/>.
        /// </summary>
        public bool AcceptsReturn
        {
            get => acceptsReturn;
            set
            {
                if (acceptsReturn == value)
                    return;

                acceptsReturn = value;
                applyLineMode();
                applyOwnHeight();
                Layout.InvalidateMeasure(this);
            }
        }

        /// <summary>Wraps long lines at the box's width, between words where it can. The box then grows with its lines as with <see cref="AcceptsReturn"/>.</summary>
        public TextWrapping TextWrapping
        {
            get => textWrapping;
            set
            {
                if (textWrapping == value)
                    return;

                textWrapping = value;
                applyLineMode();
                applyOwnHeight();
                Layout.InvalidateMeasure(this);
            }
        }

        /// <summary>The fewest lines a growing box shows.</summary>
        public int MinLines
        {
            get => minLines;
            set
            {
                minLines = Math.Max(1, value);
                lineLayoutValid = false;
            }
        }

        /// <summary>The most lines a growing box shows before it scrolls.</summary>
        public int MaxLines
        {
            get => maxLines;
            set
            {
                maxLines = Math.Max(1, value);
                lineLayoutValid = false;
            }
        }

        /// <summary>The text, with '\n' between lines. Line breaks set as "\r\n" or "\r" become '\n'.</summary>
        public override string Text
        {
            get
            {
                string raw = base.Text;

                if (rawTextOnce)
                {
                    rawTextOnce = false;
                    return raw;
                }

                return fromRaw(raw);
            }
            set => base.Text = toRaw(value);
        }

        // osu!framework's TextBox re-adds its text while it loads, through Text, which would hand it the '\n's it refuses.
        private bool rawTextOnce;

        protected override void LoadComplete()
        {
            rawTextOnce = true;
            base.LoadComplete();
            rawTextOnce = false;
        }

        private static string toRaw(string text)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0))
                return text;

            return TextRun.NormaliseLineBreaks(text).Replace('\n', line_separator);
        }

        private static string fromRaw(string raw) => raw.IndexOf(line_separator) < 0 ? raw : raw.Replace(line_separator, '\n');

        protected override Color4 SelectionColour => Theme.Accent;

        protected override float LeftRightPadding => 10;

        protected override SpriteText CreatePlaceholder()
        {
            var placeholder = base.CreatePlaceholder();
            placeholder.Colour = Theme.Overlay;
            return placeholder;
        }

        protected override Caret CreateCaret() => new SingleLineCaret(this)
        {
            CaretWidth = CaretWidth,
            SelectionColour = SelectionColour,
        };

        protected override Drawable GetDrawableCharacter(char c) => new Character(this)
        {
            AutoSizeAxes = Axes.Both,
            Child = new SpriteText { Text = c.ToString(), Font = FrameworkFont.Condensed.With(size: FontSize) },
        };

        /// <summary>Whether the text is laid out in lines (rather than osu!framework's single row).</summary>
        protected bool IsMultiline => lineMode;

        /// <summary>True while <see cref="RefreshText"/> re-sets the text, which isn't a change to report.</summary>
        protected bool IsRefreshing => refreshing;

        /// <summary>Lays the current text out again, e.g. after the box resized or the characters should look different.</summary>
        protected void RefreshText()
        {
            string value = base.Text;

            refreshing = true;
            base.Text = string.Empty;
            base.Text = value;
            refreshing = false;
        }

        #region Size

        // A height set in XAML or code keeps the box at that height. A slot sizes its content through Height, both when it
        // hands back the measured height and when it stretches it (RelativeSizeAxes converts the height); neither counts.
        public override float Height
        {
            get => base.Height;
            set
            {
                if (!(Parent is Slot))
                    heightSet = true;

                base.Height = value;
            }
        }

        private bool growsWithLines => (acceptsReturn || textWrapping != TextWrapping.NoWrap) && !heightSet;

        // A panel takes a height the box has when it joins as fixed, so a growing box has none of its own until it's laid out
        // (outside a panel; inside one, the panel measures it).
        private void applyOwnHeight()
        {
            if (heightSet || RelativeSizeAxes.HasFlag(Axes.Y))
                return;

            base.Height = growsWithLines ? 0 : default_height;
        }

        public bool ReportsMeasureChanges => true;

        public Vector2 Measure(Vector2 availableSize)
        {
            // A stretched box still asks for a row's height, as a WPF TextBox asks for its content's.
            float height = growsWithLines ? autoHeight
                : RelativeSizeAxes.HasFlag(Axes.Y) ? default_height
                : Height;

            return new Vector2(0, height);
        }

        #endregion

        #region Line mode

        private float verticalPadding => Math.Max(4, (default_height - FontSize) / 2);

        private float lineGap => MathF.Round(FontSize * 0.25f);

        private void applyLineMode()
        {
            bool lines = acceptsReturn || textWrapping != TextWrapping.NoWrap || base.Text.IndexOf(line_separator) >= 0;

            if (lines == lineMode)
                return;

            lineMode = lines;
            lineLayoutValid = false;

            // The single row stays in step underneath, just hidden, so switching back needs nothing rebuilt.
            TextFlow.Alpha = lines ? 0 : 1;
            lineContent.Alpha = lines ? 1 : 0;

            var anchor = lines ? Anchor.TopLeft : Anchor.CentreLeft;
            TextContainer.Anchor = TextContainer.Origin = anchor;
            TextContainer.Y = lines ? verticalPadding : 0;
            Placeholder.Anchor = Placeholder.Origin = anchor;

            if (!lines)
                laidOutWidth = -1;

            FrameworkInternals.CursorAndLayout(this).Invalidate();
        }

        protected override void Update()
        {
            base.Update();

            string raw = base.Text;

            if (!ReferenceEquals(raw, lastRaw))
            {
                lastRaw = raw;
                applyLineMode();
            }

            // osu!framework's TextBox lays text out against the width it had when the text was set. Inside a panel the
            // width arrives later, which leaves the text scrolled out of view until focus, so re-apply it on resize.
            if (!lineMode && DrawWidth != laidOutWidth && !HasFocus)
            {
                laidOutWidth = DrawWidth;

                // An empty box has nothing to re-set, but its placeholder was scrolled against the old width just the same.
                if (string.IsNullOrEmpty(raw))
                    MoveCursorBy(0);
                else
                    RefreshText();
            }
        }

        protected override void UpdateAfterChildren()
        {
            bool cursorMoved = !FrameworkInternals.CursorAndLayout(this).IsValid;

            base.UpdateAfterChildren();

            if (!lineMode)
                return;

            bool textChanged = layoutLines();
            var view = viewSize;

            // A box that grows with its lines takes its new height when its panel arranges it, after this, so the scroll to
            // the caret waits a frame for it. Only a box being edited follows its caret: text set from outside leaves the view
            // where it was, which starts at the top, as in WPF (osu!framework puts the caret at the end).
            if (scrollPending)
            {
                scrollPending = false;

                if (HasFocus)
                    scrollToCaret();
                else
                    applyScroll();

                updateImeRectangle();
            }
            else if (view != scrolledViewSize)
                applyScroll();

            scrolledViewSize = view;

            if (!textChanged && !cursorMoved)
                return;

            int caret = selectionEnd;

            if (caret != verticalCaret)
                verticalX = null;
            if (caret != lineEndCaret)
                lineEndCaret = -1;

            layoutSelection();
            scrollPending = true;
        }

        /// <summary>Lays the text out in lines if it, the width, or the IME's unfinished text changed.</summary>
        /// <returns>Whether it did.</returns>
        private bool layoutLines()
        {
            if (!lineText.IsLoaded)
                return false;

            string raw = base.Text;
            float wrapWidth = textWrapping == TextWrapping.NoWrap ? float.PositiveInfinity : Math.Max(1, DrawWidth - LeftRightPadding * 2 - CaretWidth);
            int compositionLength = FrameworkInternals.ImeCompositionDrawables(this).Count;
            int compositionStart = compositionLength > 0 ? FrameworkInternals.ImeCompositionStart(this) : 0;

            if (lineLayoutValid && ReferenceEquals(raw, laidOutRaw) && wrapWidth == laidOutWrapWidth && FontSize == laidOutFontSize
                && compositionStart == laidOutCompositionStart && compositionLength == laidOutCompositionLength)
                return false;

            lineLayoutValid = true;
            laidOutRaw = raw;
            laidOutWrapWidth = wrapWidth;
            laidOutFontSize = FontSize;
            laidOutCompositionStart = compositionStart;
            laidOutCompositionLength = compositionLength;

            string display = InputProperties.Type.IsPassword() ? mask(raw) : raw.Replace(line_separator, '\n');

            lineText.Font = FrameworkFont.Condensed.With(size: FontSize);
            lineText.Spacing = new Vector2(0, lineGap);
            lineText.TextWrapping = textWrapping == TextWrapping.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
            lineText.MaxWidth = wrapWidth;

            // The IME's unfinished text is underlined and faded, as it is faded in a single row.
            lineText.Inlines.Clear();

            if (compositionLength > 0 && compositionStart + compositionLength <= display.Length)
            {
                lineText.Inlines.Add(new Run(display.Substring(0, compositionStart)));
                lineText.Inlines.Add(new Run(display.Substring(compositionStart, compositionLength))
                {
                    TextDecorations = TextDecorations.Underline,
                    Foreground = new Colour4(1f, 1f, 1f, 0.6f),
                });
                lineText.Inlines.Add(new Run(display.Substring(compositionStart + compositionLength)));
            }
            else
                lineText.Text = display;

            findStops(display);

            lineCaret.Height = FontSize;

            int shown = Math.Clamp(lines.Count, minLines, Math.Max(minLines, maxLines));
            Layout.SetAffectsMeasure(this, ref autoHeight, verticalPadding * 2 + shown * FontSize + (shown - 1) * lineGap);

            if (growsWithLines && !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y))
                base.Height = autoHeight;

            return true;
        }

        private string mask(string raw)
        {
            var chars = raw.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
                chars[i] = chars[i] == line_separator ? '\n' : MaskCharacter;

            return new string(chars);
        }

        /// <summary>Works out where the caret can stand, from the glyphs the lines were laid out with.</summary>
        private void findStops(string display)
        {
            int length = display.Length;

            stopLine = new int[length + 1];
            stopX = new float[length + 1];
            lines.Clear();

            var layout = lineText.LaidOut;

            if (layout == null || layout.Lines.Count == 0)
            {
                lines.Add(new LineInfo { Top = 0, Height = FontSize, First = 0, Last = length, EndX = 0 });
                contentWidth = 0;
                contentHeight = FontSize;
                return;
            }

            var glyphs = FrameworkInternals.Characters(lineText);
            var textLines = layout.Lines;

            float endOf(int line)
            {
                var l = textLines[line];
                if (l.End <= l.Start || l.End > glyphs.Count)
                    return 0;

                var g = glyphs[l.End - 1];
                return g.DrawRectangle.Left - g.XOffset + g.XAdvance;
            }

            int current = 0;
            int glyph = 0;

            for (int i = 0; i < length; i++)
            {
                if (display[i] == '\n')
                {
                    stopLine[i] = current;
                    stopX[i] = endOf(current);
                    current = Math.Min(current + 1, textLines.Count - 1);
                    continue;
                }

                // Every other character has a glyph; a line ends where the next one's glyphs start.
                while (current < textLines.Count - 1 && glyph >= textLines[current].End)
                    current++;

                stopLine[i] = current;

                if (glyph < glyphs.Count)
                {
                    var g = glyphs[glyph];
                    stopX[i] = g.DrawRectangle.Left - g.XOffset;
                }
                else
                    stopX[i] = endOf(current);

                glyph++;
            }

            stopLine[length] = current;
            stopX[length] = endOf(current);

            contentWidth = 0;

            for (int line = 0; line < textLines.Count; line++)
            {
                int first = -1, last = -1;

                for (int i = 0; i <= length; i++)
                {
                    if (stopLine[i] != line)
                        continue;

                    if (first < 0)
                        first = i;
                    last = i;
                }

                // A line with nowhere for the caret can't happen, but keep the lists in step if it does.
                if (first < 0)
                    first = last = lines.Count > 0 ? lines[^1].Last : 0;

                float end = endOf(line);
                contentWidth = Math.Max(contentWidth, end);

                lines.Add(new LineInfo
                {
                    Top = textLines[line].Top,
                    Height = textLines[line].Height,
                    First = first,
                    Last = last,
                    EndX = end,
                    Wrapped = line < textLines.Count - 1 && last < length && display[last] != '\n',
                });
            }

            contentHeight = lines[^1].Top + lines[^1].Height;
        }

        private int selectionStart => FrameworkInternals.SelectionStart(this);

        private int selectionEnd => FrameworkInternals.SelectionEnd(this);

        /// <summary>The line the caret at <paramref name="index"/> shows on, and its x there.</summary>
        private (int Line, float X) positionOf(int index)
        {
            index = Math.Clamp(index, 0, stopLine.Length - 1);
            int line = Math.Clamp(stopLine[index], 0, lines.Count - 1);

            if (index == lineEndCaret && line > 0 && lines[line - 1].Wrapped && lines[line].First == index)
                return (line - 1, lines[line - 1].EndX);

            return (line, stopX[index]);
        }

        private void layoutSelection()
        {
            int start = Math.Min(selectionStart, selectionEnd);
            int end = Math.Max(selectionStart, selectionEnd);
            float gap = lineGap;

            selectionBoxes.Clear();

            if (start != end)
            {
                foreach (var line in lines)
                {
                    if (end <= line.First || start > line.Last)
                        continue;

                    float left = stopX[Math.Max(start, line.First)];
                    float right = end > line.Last ? line.EndX : stopX[end];

                    // A selected line break shows as a little room after the line.
                    if (end > line.Last && !line.Wrapped && line.Last < stopLine.Length - 1)
                        right += FontSize * 0.3f;

                    selectionBoxes.Add(new Box
                    {
                        Colour = SelectionColour,
                        Alpha = 0.5f,
                        Position = new Vector2(left, line.Top - gap / 2),
                        Size = new Vector2(Math.Max(0, right - left), line.Height + gap),
                    });
                }
            }

            lineCaret.ClearTransforms();

            if (HasFocus && start == end)
            {
                var (caretLine, x) = positionOf(selectionEnd);

                lineCaret.Position = new Vector2(x - CaretWidth / 2, lines[caretLine].Top);
                lineCaret.FadeTo(0.7f).Loop(c => c.FadeTo(0.7f).FadeTo(0.4f, 500, Easing.InOutSine));
            }
            else
                lineCaret.Alpha = 0;
        }

        private Vector2 viewSize => new Vector2(Math.Max(0, DrawWidth - LeftRightPadding * 2), Math.Max(0, DrawHeight - verticalPadding * 2));

        private void scrollToCaret()
        {
            var (line, x) = positionOf(selectionEnd);
            var info = lines[line];
            var view = viewSize;

            if (x < scroll.X)
                scroll.X = x;
            else if (x + CaretWidth > scroll.X + view.X)
                scroll.X = x + CaretWidth - view.X;

            if (info.Top < scroll.Y)
                scroll.Y = info.Top;
            else if (info.Top + info.Height > scroll.Y + view.Y)
                scroll.Y = info.Top + info.Height - view.Y;

            applyScroll();
        }

        private void applyScroll()
        {
            var view = viewSize;

            scroll = new Vector2(
                Math.Clamp(scroll.X, 0, Math.Max(0, contentWidth + CaretWidth - view.X)),
                Math.Clamp(scroll.Y, 0, Math.Max(0, contentHeight - view.Y)));

            var position = new Vector2(LeftRightPadding - scroll.X, verticalPadding - scroll.Y);

            if (scrollPlaced)
                lineContent.MoveTo(position, 150, Easing.OutQuint);
            else
            {
                lineContent.Position = position;
                scrollPlaced = true;
            }
        }

        private void updateImeRectangle()
        {
            if (!HasFocus || textInput == null)
                return;

            var (line, x) = positionOf(selectionEnd);
            var rectangle = new RectangleF(x, lines[line].Top, CaretWidth, lines[line].Height);

            // The IME's window goes by its unfinished text when it's on the caret's line.
            int compositionLength = FrameworkInternals.ImeCompositionDrawables(this).Count;

            if (compositionLength > 0)
            {
                int start = FrameworkInternals.ImeCompositionStart(this);
                var (startLine, startX) = positionOf(start);

                if (startLine == line && startX < x)
                    rectangle = new RectangleF(startX, lines[line].Top, x - startX, lines[line].Height);
            }

            // From where the lines are headed rather than where their scroll animation has got to.
            var offset = new Vector2(LeftRightPadding - scroll.X, verticalPadding - scroll.Y);
            textInput.SetImeRectangle(ToScreenSpace(rectangle.Offset(offset)).AABBFloat);
        }

        #endregion

        #region Moving the caret

        /// <summary>Where the caret goes for a point on screen, and whether it's past the end of a wrapped line.</summary>
        private (int Index, bool LineEnd) hitTest(Vector2 screenSpacePosition)
        {
            var position = lineContent.ToLocalSpace(screenSpacePosition);
            float gap = lineGap;

            int line = lines.Count - 1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (position.Y < lines[i].Top + lines[i].Height + gap / 2)
                {
                    line = i;
                    break;
                }
            }

            return hitLine(line, position.X);
        }

        private (int Index, bool LineEnd) hitLine(int line, float x)
        {
            var info = lines[line];
            int best = info.First;

            for (int i = info.First + 1; i <= info.Last; i++)
            {
                if (Math.Abs(stopX[i] - x) < Math.Abs(stopX[best] - x))
                    best = i;
            }

            if (info.Wrapped && Math.Abs(info.EndX - x) < Math.Abs(stopX[best] - x))
                return (info.Last + 1, true);

            return (best, false);
        }

        private (int Index, bool LineEnd) endOfLine(int line)
        {
            var info = lines[line];
            return info.Wrapped ? (info.Last + 1, true) : (info.Last, false);
        }

        private void moveCaret(int index, bool lineEnd, bool select)
        {
            int amount = index - selectionEnd;

            if (select)
                ExpandSelectionBy(amount);
            else
                MoveCursorBy(amount);

            lineEndCaret = lineEnd ? index : -1;
            FrameworkInternals.CursorAndLayout(this).Invalidate();
        }

        private void moveLines(int count, bool select)
        {
            var (line, x) = positionOf(selectionEnd);
            float targetX = verticalX ?? x;
            int target = Math.Clamp(line + count, 0, lines.Count - 1);

            if (target != line)
            {
                var (index, lineEnd) = hitLine(target, targetX);
                moveCaret(index, lineEnd, select);
                verticalCaret = index;
            }

            verticalX = targetX;
        }

        private void setSelection(int start, int end)
        {
            FrameworkInternals.SelectionStart(this) = start;
            FrameworkInternals.SelectionEnd(this) = end;
            FrameworkInternals.CursorAndLayout(this).Invalidate();
        }

        /// <summary>The word (or run of other characters) around <paramref name="index"/>, within its line.</summary>
        private (int Start, int End) wordAt(int index)
        {
            string raw = base.Text;

            if (raw[index] == line_separator && index > 0 && raw[index - 1] != line_separator)
                index--;

            if (raw[index] == line_separator)
                return (index, index);

            bool letters = char.IsLetterOrDigit(raw[index]);
            int start = index, end = index;

            while (start > 0 && raw[start - 1] != line_separator && char.IsLetterOrDigit(raw[start - 1]) == letters)
                start--;
            while (end < raw.Length && raw[end] != line_separator && char.IsLetterOrDigit(raw[end]) == letters)
                end++;

            return (start, end);
        }

        #endregion

        #region Input

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (lineMode && HasFocus && !ReadOnly && !ImeCompositionActive)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                    case Key.KeypadEnter:
                        if (!acceptsReturn || e.AltPressed)
                            break;

                        InsertString(line_separator.ToString());
                        return true;

                    case Key.Up:
                    case Key.Down:
                        moveLines(e.Key == Key.Up ? -1 : 1, e.ShiftPressed);
                        return true;

                    case Key.PageUp:
                    case Key.PageDown:
                    {
                        int page = Math.Max(1, (int)(viewSize.Y / (FontSize + lineGap)) - 1);
                        moveLines(e.Key == Key.PageUp ? -page : page, e.ShiftPressed);
                        return true;
                    }

                    case Key.Home when e.ControlPressed:
                        moveCaret(0, false, e.ShiftPressed);
                        return true;

                    case Key.End when e.ControlPressed:
                        moveCaret(base.Text.Length, false, e.ShiftPressed);
                        return true;
                }
            }

            return base.OnKeyDown(e);
        }

        public override bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            if (lineMode && HasFocus && !ImeCompositionActive)
            {
                // Home and End work on the line the caret is on, rather than the whole text.
                int line = positionOf(selectionEnd).Line;
                bool hasSelection = selectionStart != selectionEnd;

                switch (e.Action)
                {
                    case PlatformAction.MoveBackwardLine:
                    case PlatformAction.SelectBackwardLine:
                        moveCaret(lines[line].First, false, e.Action == PlatformAction.SelectBackwardLine);
                        return true;

                    case PlatformAction.MoveForwardLine:
                    case PlatformAction.SelectForwardLine:
                    {
                        var (end, lineEnd) = endOfLine(line);
                        moveCaret(end, lineEnd, e.Action == PlatformAction.SelectForwardLine);
                        return true;
                    }

                    case PlatformAction.DeleteBackwardLine:
                        if (!hasSelection)
                            ExpandSelectionBy(lines[line].First - selectionEnd);
                        DeleteBy(0);
                        return true;

                    case PlatformAction.DeleteForwardLine:
                        if (!hasSelection)
                            ExpandSelectionBy(endOfLine(line).Index - selectionEnd);
                        DeleteBy(0);
                        return true;

                    case PlatformAction.Paste when acceptsReturn:
                        InsertString(toRaw(clipboard.GetText()));
                        return true;
                }
            }

            return base.OnPressed(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (!lineMode || ReadOnly)
                return base.OnMouseDown(e);

            FinalizeImeComposition(true);

            float tripleClickTime = GetContainingInputManager()?.GetButtonEventManagerFor(e.Button).DoubleClickTime ?? 250;

            // A third click selects the whole line, up to its line break.
            if (lastDoubleClickTime != null && Time.Current - lastDoubleClickTime < tripleClickTime)
            {
                lastDoubleClickTime = null;
                tripleClicked = true;

                string raw = base.Text;
                int caret = Math.Clamp(selectionEnd, 0, raw.Length);
                int start = caret > 0 ? raw.LastIndexOf(line_separator, caret - 1) + 1 : 0;
                int end = raw.IndexOf(line_separator, caret);

                setSelection(start, end < 0 ? raw.Length : end);
                return true;
            }

            var (index, lineEnd) = hitTest(e.ScreenSpaceMousePosition);
            setSelection(index, index);
            lineEndCaret = lineEnd ? index : -1;
            return false;
        }

        protected override void OnDrag(DragEvent e)
        {
            if (!lineMode)
            {
                base.OnDrag(e);
                return;
            }

            if (ReadOnly || tripleClicked)
                return;

            FinalizeImeComposition(true);

            var (index, lineEnd) = hitTest(e.ScreenSpaceMousePosition);

            if (doubleClickWord is (int wordStart, int wordEnd))
            {
                // After a double-click, dragging selects whole words.
                if (index > wordEnd)
                    setSelection(wordStart, wordAt(index - 1).End);
                else if (index < wordStart)
                    setSelection(wordEnd, wordAt(index).Start);
                else
                    setSelection(wordStart, wordEnd);

                return;
            }

            if (base.Text.Length == 0)
                return;

            setSelection(selectionStart, index);
            lineEndCaret = lineEnd ? index : -1;

            if (selectionStart != selectionEnd)
                GetContainingFocusManager()?.ChangeFocus(this);
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            if (!lineMode)
                return base.OnDoubleClick(e);

            lastDoubleClickTime = Time.Current;
            FinalizeImeComposition(true);

            string raw = base.Text;

            if (raw.Length == 0)
                return true;

            var word = AllowClipboardExport ? wordAt(Math.Min(raw.Length - 1, hitTest(e.ScreenSpaceMousePosition).Index)) : (0, raw.Length);

            setSelection(word.Item1, word.Item2);
            doubleClickWord = word;
            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            doubleClickWord = null;
            tripleClicked = false;
            base.OnMouseUp(e);
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            if (!lineMode)
                return base.OnScroll(e);

            float before = scroll.Y;

            scroll.Y -= e.ScrollDelta.Y * (FontSize + lineGap) * 3;
            applyScroll();

            // At either end the wheel goes on to scroll whatever is around the box.
            return scroll.Y != before;
        }

        #endregion

        private struct LineInfo
        {
            public float Top;
            public float Height;

            /// <summary>The first and last places the caret can stand on the line.</summary>
            public int First;

            public int Last;

            /// <summary>Where the line's text ends.</summary>
            public float EndX;

            /// <summary>Whether the line ends because it wrapped, rather than at a line break or the end of the text.</summary>
            public bool Wrapped;
        }

        /// <summary>A character in the single row, which hides away while the text is shown in lines.</summary>
        private partial class Character : FallingDownContainer
        {
            private readonly TextBoxBase textBox;

            public Character(TextBoxBase textBox)
            {
                this.textBox = textBox;
            }

            public override void Hide()
            {
                if (textBox.lineMode)
                    this.FadeOut();
                else
                    base.Hide();
            }
        }

        /// <summary>osu!framework's caret, which the lines replace with their own.</summary>
        private partial class SingleLineCaret : BasicCaret
        {
            private readonly TextBoxBase textBox;

            public SingleLineCaret(TextBoxBase textBox)
            {
                this.textBox = textBox;
            }

            public override bool IsPresent => !textBox.lineMode && base.IsPresent;
        }
    }

    /// <summary>
    /// A text box. <c>Text</c> is a bindable so it binds two-way like WPF. <c>PlaceholderText</c> shows a hint
    /// while it is empty, <c>MaxLength</c> limits the length and <c>IsReadOnly</c> stops editing.
    /// <c>AcceptsReturn</c> and <c>TextWrapping</c> make it multi-line.
    /// </summary>
    public partial class TextBox : TextBoxBase
    {
        public TextBox()
        {
            Current.ValueChanged += e =>
            {
                if (!IsRefreshing)
                    OnTextChanged(e.NewValue);
            };
        }

        /// <summary>The text, with '\n' between lines.</summary>
        public new Bindable<string> Text => Current;

        /// <summary>Raised when the text changes, with the new text.</summary>
        public event Action<string> TextChanged;

        /// <summary>The longest text the user can enter; 0 (the default) means no limit.</summary>
        public int MaxLength
        {
            get => LengthLimit ?? 0;
            set => LengthLimit = value > 0 ? value : null;
        }

        /// <summary>When true the text can be selected and copied but not changed.</summary>
        /// <remarks>Hides the container's collection flag of the same name, which XAML never needs.</remarks>
        public new bool IsReadOnly
        {
            get => ReadOnly;
            set => ReadOnly = value;
        }

        protected virtual void OnTextChanged(string text) => TextChanged?.Invoke(text);
    }
}
