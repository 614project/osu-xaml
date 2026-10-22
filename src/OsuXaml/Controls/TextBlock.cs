using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Layout;
using osu.Framework.Platform;
using osu.Framework.Text;
using osuTK;
using osuTK.Graphics;

namespace OsuXaml.Controls
{
    /// <summary>
    /// How heavy text is, as WPF's FontWeights name it. A family that lacks a weight shows the nearest one it has (see
    /// <see cref="FontFamilies"/>); osu!framework's Roboto has Regular and Bold.
    /// </summary>
    public enum FontWeight
    {
        Regular,
        Bold,
        Thin,
        ExtraLight,
        Light,
        SemiLight,
        Medium,
        SemiBold,
        ExtraBold,
        Black,

        /// <summary>The same as <see cref="Regular"/>, as WPF also calls it.</summary>
        Normal = Regular,
    }

    public enum TextWrapping
    {
        NoWrap,

        /// <summary>Breaks lines at the width the layout offers.</summary>
        Wrap,

        /// <summary>The same as <see cref="Wrap"/> here.</summary>
        WrapWithOverflow,
    }

    /// <summary>Where each line sits across the TextBlock's width, like WPF's TextAlignment.</summary>
    public enum TextAlignment
    {
        Left,
        Right,
        Center,

        /// <summary>Wrapped lines are spread to the full width by widening their spaces; the last line of a paragraph stays left-aligned.</summary>
        Justify,
    }

    public enum TextTrimming
    {
        None,

        /// <summary>Cuts text that doesn't fit and ends it with an ellipsis.</summary>
        CharacterEllipsis,

        /// <summary>The same as <see cref="CharacterEllipsis"/> here.</summary>
        WordEllipsis,
    }

    /// <summary>
    /// Text sized to its content. With <see cref="TextWrapping"/> or <see cref="TextTrimming"/> it wraps or trims at the
    /// width its panel offers (or its own Width), like WPF; a wrapping TextBlock in a horizontal StackPanel has unlimited width and stays on one line.
    /// Line breaks in the text start new lines, and <see cref="Inlines"/> (<see cref="Run"/>, <see cref="Bold"/>, <see cref="LineBreak"/>...) style parts of it.
    /// </summary>
    public partial class TextBlock : SpriteText, ILayoutElement, IHasDefaultCursor, IInheritsTextProperties
    {
        private static readonly char[] never_fixed_width_characters = { '.', ',', ':', ' ', '\u00A0', '\u202F' };

        // Inlines need a style around them to say what text they spell; the style itself isn't used.
        private static readonly TextStyle spelling_style = new TextStyle(FontUsage.Default, null, TextDecorations.None);

        private TextWrapping textWrapping;
        private TextTrimming textTrimming;
        private TextAlignment textAlignment;
        private TextDecorations textDecorations;

        // The width the layout last offered, which aligned lines spread across.
        private float availableWidth = float.PositiveInfinity;

        // The link under the mouse, and whether any text is a link at all (only then does the TextBlock take mouse input).
        private HyperlinkInline hoveredLink;
        private bool hasLinks;

        // Drawables among the text, and the layer next to the TextBlock that shows them.
        private readonly List<InlineUIContainer> uiContainers = new List<InlineUIContainer>();
        private InlineUILayer uiLayer;

        [Resolved(canBeNull: true)]
        private GameHost host { get; set; }

        // The size last reported to the layout; NaN until the first measure, so sizes set in XAML always apply.
        private Vector2 measured = new Vector2(float.NaN);

        // A width set in XAML or code (not the layout handing back the measured width), which also limits wrapping.
        private float? explicitWidth;

        private InlineCollection inlines;

        // The text the inlines spell, which Text is set to. The inlines are shown while Text still says that.
        private string inlineText;

        private TextBlockTextBuilder builder;

        // Which text properties were set on the TextBlock itself; the others follow the TextElement properties around it.
        private bool ownForeground;
        private bool ownFamily;
        private bool ownSize;
        private bool ownWeight;

        // The weight asked for, which the family may not have (the font then has the nearest one).
        private FontWeight? wantedWeight;

        // What the TextBlock looked like before it took inherited values, to go back to when they stop, and what it was given then:
        // a colour or font that differs from what was given was set directly meanwhile, and is the one to go back to.
        private ColourInfo? colourBeforeInherited;
        private ColourInfo inheritedColour;
        private FontUsage? fontBeforeInherited;
        private FontUsage inheritedFont;

        private InheritedTextWatcher inheritance;

        /// <summary>
        /// The control whose <see cref="ContentControl.Foreground"/> colours this text, for the text a control makes for its content.
        /// While the control sets a Foreground of its own, <see cref="TextElement"/>.Foreground from further out doesn't reach the text.
        /// </summary>
        internal ContentControl ForegroundOwner;

        public TextBlock()
        {
            Font = FrameworkFont.Regular.With(size: 16);
            Colour = Theme.Text;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            inheritance = new InheritedTextWatcher(this, applyInherited) { IncludesOwner = true };
            FollowInherited();
        }

        void IInheritsTextProperties.InheritedTextChanged() => FollowInherited();

        /// <summary>Takes the inherited text properties again, e.g. after <see cref="ForegroundOwner"/> changed its Foreground.</summary>
        internal void FollowInherited()
        {
            if (inheritance == null)
                return;

            inheritance.ForegroundBoundary = ForegroundOwner?.HasOwnForeground == true ? ForegroundOwner : null;
            inheritance.Attach();
        }

        private void applyInherited(InheritedText inherited)
        {
            // Only what an element around sets is touched, so a colour or font given directly stays when nothing is inherited.
            if (colourBeforeInherited != null && !Colour.Equals(inheritedColour))
                colourBeforeInherited = Colour;

            if (!ownForeground && inherited.Foreground is Colour4 foreground)
            {
                colourBeforeInherited ??= Colour;
                Colour = inheritedColour = foreground;
            }
            else if (colourBeforeInherited is ColourInfo before)
            {
                colourBeforeInherited = null;

                if (!ownForeground)
                    Colour = before;
            }

            if (fontBeforeInherited != null && !Font.Equals(inheritedFont))
                fontBeforeInherited = Font;

            string family = ownFamily ? null : FontFamilies.Resolve(inherited.FontFamily);
            float? size = ownSize ? null : inherited.FontSize;
            var weight = ownWeight ? null : inherited.FontWeight;

            if (family == null && size == null && weight == null)
            {
                if (fontBeforeInherited is FontUsage local)
                {
                    fontBeforeInherited = null;
                    setFont(local, null, null, null);
                }

                return;
            }

            fontBeforeInherited ??= Font;
            setFont(fontBeforeInherited.Value, family, size, weight);
        }

        // The font from inherited values, the ones set on the TextBlock, and for the rest those of `local`.
        private void setFont(FontUsage local, string family, float? size, FontWeight? weight)
        {
            family ??= ownFamily ? Font.Family : local.Family;
            float em = size ?? (ownSize ? FontSize : local.Size / FontFamilies.SizeScale(local.Family));
            var w = weight ?? (ownWeight ? FontWeight : FontFamilies.Parse(local.Weight));

            changeFont(Font.With(family: family, size: em * FontFamilies.SizeScale(family), weight: FontFamilies.WeightName(family, w)));
        }

        // A font change the TextBlock makes itself, which while it inherits isn't a font set directly.
        private void changeFont(FontUsage font)
        {
            Font = font;

            if (fontBeforeInherited != null)
                inheritedFont = font;
        }

        /// <summary>
        /// Sets the font as a whole and keeps it, whatever the elements around set: for text a control lays out itself.
        /// </summary>
        public void SetOwnFont(FontUsage font)
        {
            ownFamily = ownSize = ownWeight = true;
            wantedWeight = null;
            fontBeforeInherited = null;
            Font = font;
        }

        /// <summary>Wraps long text onto more lines, between words where it can.</summary>
        public TextWrapping TextWrapping
        {
            get => textWrapping;
            set
            {
                textWrapping = value;
                applyTextMode();
            }
        }

        /// <summary>Ends each line that doesn't fit with an ellipsis. Ignored while the text wraps.</summary>
        public TextTrimming TextTrimming
        {
            get => textTrimming;
            set
            {
                textTrimming = value;
                applyTextMode();
            }
        }

        /// <summary>
        /// Where lines sit across the width: the TextBlock's own Width, a stretched width, or else the width its panel offers
        /// (in a horizontal StackPanel, which offers unlimited width, the widest line).
        /// </summary>
        public TextAlignment TextAlignment
        {
            get => textAlignment;
            set
            {
                if (textAlignment == value)
                    return;

                textAlignment = value;
                FrameworkInternals.Invalidate(this, true, true);
            }
        }

        /// <summary>Whether the last layout cut text off for <see cref="TextTrimming"/>.</summary>
        public new bool IsTruncated => FrameworkInternals.Characters(this).Count > 0 && builder?.IsTruncated == true;

        /// <summary>
        /// Styled pieces of text, like WPF's: in XAML, <c>&lt;TextBlock&gt;Plain &lt;Bold&gt;bold&lt;/Bold&gt;&lt;LineBreak /&gt;next line&lt;/TextBlock&gt;</c>.
        /// Changing them sets <see cref="SpriteText.Text"/> to the text they spell; setting Text to something else shows that instead.
        /// </summary>
        public InlineCollection Inlines => inlines ??= new InlineCollection(onInlinesChanged);

        public void Add(Inline inline) => Inlines.Add(inline);

        /// <summary>Adds link text: <c>&lt;Hyperlink NavigateUri="..."&gt;docs&lt;/Hyperlink&gt;</c> written among the text.</summary>
        public void Add(Hyperlink link) => Inlines.Add(link);

        /// <summary>Adds a drawable among the text, as WPF wraps one in an <see cref="InlineUIContainer"/>.</summary>
        public void Add(Drawable drawable) => Inlines.Add(drawable);

        /// <summary>Adds a <see cref="Run"/> of <paramref name="text"/>.</summary>
        public void AddText(string text) => Inlines.Add(new Run(text));

        /// <summary>The glyphs' layout, brought up to date; null while there's no text or it hasn't loaded.</summary>
        internal TextBlockTextBuilder LaidOut
        {
            get
            {
                FrameworkInternals.Characters(this);
                return IsLoaded && !string.IsNullOrEmpty(FrameworkInternals.DisplayedText(this)) ? builder : null;
            }
        }

        private bool constrainsWidth => textWrapping != TextWrapping.NoWrap || textTrimming != TextTrimming.None;

        private void applyTextMode()
        {
            if (!constrainsWidth)
                MaxWidth = float.PositiveInfinity;

            FrameworkInternals.Invalidate(this, true, true);
            Layout.InvalidateMeasure(this);
        }

        private void onInlinesChanged()
        {
            var runs = new List<TextRun>();

            foreach (var inline in inlines)
                inline.AppendRuns(runs, spelling_style);

            string text = string.Empty;

            foreach (var run in runs)
                text += run.Text;

            inlineText = text;

            // A change that keeps the text the same (a run turning bold) still needs the glyphs laid out again.
            if (Text.ToString() == text)
            {
                FrameworkInternals.Invalidate(this, true, false);
                Layout.InvalidateMeasure(this);
            }
            else
                Text = text;
        }

        /// <summary>The text to lay out, split into runs of one style.</summary>
        internal void GetRuns(List<TextRun> runs)
        {
            var style = new TextStyle(Font, null, textDecorations);
            string text = FrameworkInternals.DisplayedText(this);

            if (inlines?.Count > 0 && text == inlineText)
            {
                foreach (var inline in inlines)
                    inline.AppendRuns(runs, style);
            }
            else
                runs.Add(new TextRun(TextRun.NormaliseLineBreaks(text), style));

            hasLinks = false;
            uiContainers.Clear();

            foreach (var run in runs)
            {
                if (run.Style.Link != null)
                    hasLinks = true;

                if (run.Style.Container != null)
                    uiContainers.Add(run.Style.Container);
            }

            if (hoveredLink != null && !hasLinks)
                setHoveredLink(null);
        }

        /// <summary>True: every change to the text or its style resizes the text, which its slot passes on.</summary>
        public bool ReportsMeasureChanges => true;

        public Vector2 Measure(Vector2 availableSize)
        {
            // Wrapping and trimming need the width on offer; MaxWidth keeps the text sizing itself up to that width.
            if (constrainsWidth)
                MaxWidth = float.IsFinite(availableSize.X) ? Math.Max(1, availableSize.X) : float.PositiveInfinity;

            // Aligned lines spread across the width on offer, so the text takes all of it.
            if (availableSize.X != availableWidth)
            {
                availableWidth = availableSize.X;

                if (textAlignment != TextAlignment.Left)
                    FrameworkInternals.Invalidate(this, true, true);
            }

            return measured = new Vector2(
                RelativeSizeAxes.HasFlag(Axes.X) ? 0 : DrawWidth,
                RelativeSizeAxes.HasFlag(Axes.Y) ? 0 : DrawHeight);
        }

        // osu!framework's text builders break lines wherever a character crosses the edge and know one font;
        // this one breaks between words, starts lines at line breaks, and gives every run its own font.
        protected override TextBuilder CreateTextBuilder(ITexturedGlyphLookupStore store)
        {
            float maxWidth = !constrainsWidth ? float.PositiveInfinity
                : RelativeSizeAxes.HasFlag(Axes.X) ? Math.Min(MaxWidth, DrawWidth) - Padding.Right
                : explicitWidth is float width ? Math.Min(MaxWidth, width) - Padding.Right
                : MaxWidth;

            bool wrap = textWrapping != TextWrapping.NoWrap;

            float alignWidth = textAlignment == TextAlignment.Left ? 0
                : RelativeSizeAxes.HasFlag(Axes.X) ? DrawWidth - Padding.Right
                : explicitWidth is float own ? own - Padding.Right
                : float.IsFinite(availableWidth) ? Math.Min(MaxWidth, availableWidth) - Padding.Right
                : 0;

            // The base builder's character list is the one this text draws from, so the replacement writes into it.
            var characters = base.CreateTextBuilder(store).Characters;

            return builder = new TextBlockTextBuilder(this, store, Font, maxWidth, textAlignment, alignWidth, wrap, !wrap && textTrimming != TextTrimming.None, EllipsisString,
                UseFullGlyphHeight, new Vector2(Padding.Left, Padding.Top), Spacing, characters, FixedWidthExcludeCharacters ?? never_fixed_width_characters,
                FallbackCharacter, FixedWidthReferenceCharacter);
        }

        protected override DrawNode CreateDrawNode() => new TextBlockDrawNode(this);

        // A slot hands layout elements the size they measured as a fixed size. Text measures itself, and a fixed
        // width or height would stop it following its text, so that hand-back is ignored.
        public override float Width
        {
            get => base.Width;
            set
            {
                if (value != measured.X)
                {
                    base.Width = value;
                    explicitWidth = value;
                }
            }
        }

        public override float Height
        {
            get => base.Height;
            set
            {
                if (value != measured.Y)
                    base.Height = value;
            }
        }

        /// <summary>
        /// The size of the text. For osu!framework's fonts it is the height of the font's line box; a family registered with
        /// <see cref="FontFamilies"/> takes it as the em size, as WPF does.
        /// </summary>
        public float FontSize
        {
            get => Font.Size / FontFamilies.SizeScale(Font.Family);
            set
            {
                ownSize = true;
                changeFont(Font.With(size: value * FontFamilies.SizeScale(Font.Family)));
            }
        }

        public FontWeight FontWeight
        {
            get => wantedWeight ?? FontFamilies.Parse(Font.Weight);
            set
            {
                ownWeight = true;
                wantedWeight = value;
                changeFont(Font.With(weight: FontFamilies.WeightName(Font.Family, value)));
            }
        }

        public FontStyle FontStyle
        {
            get => Font.Italics ? FontStyle.Italic : FontStyle.Normal;
            set => changeFont(Font.With(italics: value == FontStyle.Italic));
        }

        /// <summary>
        /// The font family: <c>Roboto</c> (the default), <c>RobotoCondensed</c>, or one the game added to its fonts.
        /// Of a WPF-style list (<c>"Segoe UI, Roboto"</c>) the first is used; text a family lacks falls back to another font with the same weight.
        /// </summary>
        public string FontFamily
        {
            get => Font.Family;
            set
            {
                ownFamily = true;

                float size = FontSize;
                var weight = FontWeight;
                string family = FontFamilies.Resolve(ParseFontFamily(value)) ?? FrameworkFont.Regular.Family;

                changeFont(Font.With(family: family, size: size * FontFamilies.SizeScale(family), weight: FontFamilies.WeightName(family, weight)));
            }
        }

        #region Links and drawables among the text

        // Only text with links takes mouse input, so plain text never stands in the way of what's under it.
        public override bool HandlePositionalInput => hasLinks;

        // A link among the text shows the hand, as a Hyperlink on its own does; the rest of the text shows its parents' cursor.
        Cursor IHasDefaultCursor.DefaultCursor => hoveredLink?.Link.IsEnabled == true ? Cursors.Hand : null;

        /// <summary>The style of the glyph at a point in the TextBlock's own space, or null where there's none.</summary>
        internal TextStyle StyleAt(Vector2 position)
        {
            var layout = LaidOut;

            if (layout == null)
                return null;

            var characters = FrameworkInternals.Characters(this);

            foreach (var line in layout.Lines)
            {
                if (position.Y < line.Top || position.Y >= line.Top + line.Height)
                    continue;

                for (int i = line.Start; i < line.End && i < characters.Count; i++)
                {
                    var glyph = characters[i];
                    float left = glyph.DrawRectangle.Left - glyph.XOffset;

                    if (position.X >= left && position.X < left + glyph.XAdvance)
                        return layout.GlyphStyles[i];
                }
            }

            return null;
        }

        private HyperlinkInline linkAt(Vector2 screenSpacePosition) => StyleAt(ToLocalSpace(screenSpacePosition))?.Link;

        private void setHoveredLink(HyperlinkInline link)
        {
            if (link == hoveredLink)
                return;

            if (hoveredLink != null)
                hoveredLink.Hovered = false;

            hoveredLink = link;

            if (link != null)
                link.Hovered = true;

            Invalidate(Invalidation.DrawNode);
        }

        // Hover isn't taken, so a button or list row around the text still sees the mouse.
        protected override bool OnHover(HoverEvent e)
        {
            setHoveredLink(linkAt(e.ScreenSpaceMousePosition));
            return false;
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            setHoveredLink(linkAt(e.ScreenSpaceMousePosition));
            return false;
        }

        protected override void OnHoverLost(HoverLostEvent e) => setHoveredLink(null);

        protected override bool OnClick(ClickEvent e) => linkAt(e.ScreenSpaceMousePosition)?.Link.Activate(host) == true;

        protected override void Update()
        {
            base.Update();

            if (uiContainers.Count > 0 || uiLayer != null)
                syncUILayer();
        }

        // The drawables among the text live in a layer next to the TextBlock (which, being a sprite, can't hold children),
        // in the same parent, following the TextBlock's place and size.
        private void syncUILayer()
        {
            var layout = LaidOut;

            uiLayer ??= new InlineUILayer(this);

            if (uiLayer.Parent != Parent && !uiLayer.Disposed)
            {
                (uiLayer.Parent as Container<Drawable>)?.Remove(uiLayer, false);

                if (Parent is Container<Drawable> container)
                    container.Add(uiLayer);
            }

            // A drawable's place in the line is its size, so a drawable that resized lays the text out again.
            if (uiLayer.Sync(layout == null ? Array.Empty<InlineUIContainer>() : uiContainers, layout, FrameworkInternals.Characters(this)))
                FrameworkInternals.Invalidate(this, true, false);
        }

        internal bool Disposed => IsDisposed;

        protected override void Dispose(bool isDisposing)
        {
            inheritance?.Detach();
            uiLayer?.Expire();
            base.Dispose(isDisposing);
        }

        #endregion

        /// <summary>The first family of a WPF-style list, or null for none.</summary>
        internal static string ParseFontFamily(string value)
        {
            if (value == null)
                return null;

            int comma = value.IndexOf(',');
            string family = (comma < 0 ? value : value.Substring(0, comma)).Trim().Trim('"', '\'');
            return family.Length > 0 ? family : null;
        }

        public TextDecorations TextDecorations
        {
            get => textDecorations;
            set
            {
                if (textDecorations == value)
                    return;

                textDecorations = value;
                FrameworkInternals.Invalidate(this, true, false);
            }
        }

        public Colour4 Foreground
        {
            get => Colour;
            set
            {
                ownForeground = true;
                Colour = value;
            }
        }

        /// <summary>
        /// Whether <see cref="Foreground"/> was set on this TextBlock rather than taken from the elements around it
        /// (<see cref="TextElement"/>). Setting it to false hands the colour back to them.
        /// </summary>
        public bool HasOwnForeground
        {
            get => ownForeground;
            set
            {
                ownForeground = value;
                if (!value) FollowInherited();
            }
        }

        /// <summary>Draws each glyph in its run's colour, then the underlines and strikethroughs.</summary>
        private class TextBlockDrawNode : TexturedShaderDrawNode
        {
            protected new TextBlock Source => (TextBlock)base.Source;

            private readonly List<Part> parts = new List<Part>();
            private readonly List<Part> lines = new List<Part>();

            private bool shadow;
            private ColourInfo shadowColour;
            private Vector2 shadowOffset;

            public TextBlockDrawNode(TextBlock source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                parts.Clear();
                lines.Clear();

                shadow = Source.Shadow;

                if (shadow)
                {
                    shadowColour = Source.ShadowColour;
                    shadowOffset = Source.ToScreenSpace(Source.ShadowOffset * Source.Font.Size) - Source.ToScreenSpace(Vector2.Zero);
                }

                var characters = FrameworkInternals.Characters(Source);
                var layout = Source.builder;

                if (characters.Count == 0 || layout == null)
                    return;

                // A run's colour replaces the TextBlock's own, under whatever its parents apply.
                var parentColour = Source.Parent?.DrawColourInfo.Colour ?? ColourInfo.SingleColour(Colour4.White);
                var colours = new Dictionary<TextStyle, ColourInfo>();

                ColourInfo colourOf(TextStyle style)
                {
                    if ((style?.Link?.CurrentForeground ?? style?.Foreground) is not Colour4 foreground || !parentColour.HasSingleColour)
                        return DrawColourInfo.Colour;

                    if (colours.TryGetValue(style, out var colour))
                        return colour;

                    colour = parentColour;
                    colour.ApplyChild(ColourInfo.SingleColour(foreground).MultiplyAlpha(Source.Alpha));
                    return colours[style] = colour;
                }

                Vector2 inflation = DrawInfo.MatrixInverse.ExtractScale().Xy;

                for (int i = 0; i < characters.Count; i++)
                {
                    var character = characters[i];

                    if (character.Texture == null)
                        continue;

                    parts.Add(new Part
                    {
                        Quad = Source.ToScreenSpace(character.DrawRectangle.Inflate(inflation)),
                        Inflation = new Vector2(
                            character.DrawRectangle.Width == 0 ? 0 : inflation.X / character.DrawRectangle.Width,
                            character.DrawRectangle.Height == 0 ? 0 : inflation.Y / character.DrawRectangle.Height),
                        Texture = character.Texture,
                        Colour = colourOf(i < layout.GlyphStyles.Count ? layout.GlyphStyles[i] : null),
                    });
                }

                foreach (var line in layout.Lines)
                {
                    int end = Math.Min(line.End, characters.Count);

                    // Spaces left hanging at the end of a line aren't underlined.
                    while (end > line.Start && char.IsWhiteSpace(characters[end - 1].Character))
                        end--;

                    for (int start = line.Start; start < end;)
                    {
                        var style = layout.GlyphStyles[start];
                        int stop = start + 1;

                        while (stop < end && layout.GlyphStyles[stop] == style)
                            stop++;

                        var decorations = style.Container != null ? TextDecorations.None
                            : style.Link?.Underlined == true ? style.Decorations | TextDecorations.Underline
                            : style.Decorations;

                        if (decorations != TextDecorations.None)
                        {
                            var first = characters[start];
                            var last = characters[stop - 1];
                            float left = first.DrawRectangle.Left - first.XOffset;
                            float right = last.DrawRectangle.Left - last.XOffset + last.XAdvance;
                            float size = style.Font.Size;
                            float thickness = Math.Max(1, size * 0.06f);

                            if (decorations.HasFlag(TextDecorations.Underline))
                                addLine(line.Baseline + size * 0.08f);
                            if (decorations.HasFlag(TextDecorations.Strikethrough))
                                addLine(line.Baseline - size * 0.27f - thickness / 2);

                            void addLine(float top) => lines.Add(new Part
                            {
                                Quad = Source.ToScreenSpace(new RectangleF(left, top, right - left, thickness)),
                                Colour = colourOf(style),
                            });
                        }

                        start = stop;
                    }
                }
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                BindTextureShader(renderer);

                var averageColour = (Color4)DrawColourInfo.Colour.AverageColour;

                // As SpriteText does: a fainter shadow under darker text, so it doesn't look muddy.
                float shadowAlpha = MathF.Pow(Math.Max(Math.Max(averageColour.R, averageColour.G), averageColour.B), 2);
                var finalShadowColour = DrawColourInfo.Colour;
                finalShadowColour.ApplyChild(shadowColour.MultiplyAlpha(shadowAlpha));

                foreach (var part in parts)
                {
                    if (shadow)
                    {
                        var quad = part.Quad;
                        renderer.DrawQuad(part.Texture, new Quad(quad.TopLeft + shadowOffset, quad.TopRight + shadowOffset, quad.BottomLeft + shadowOffset, quad.BottomRight + shadowOffset),
                            finalShadowColour, inflationPercentage: part.Inflation);
                    }

                    renderer.DrawQuad(part.Texture, part.Quad, part.Colour, inflationPercentage: part.Inflation);
                }

                foreach (var line in lines)
                    renderer.DrawQuad(renderer.WhitePixel, line.Quad, line.Colour);

                UnbindTextureShader(renderer);
            }

            private struct Part
            {
                public Quad Quad;
                public Vector2 Inflation;
                public Texture Texture;
                public ColourInfo Colour;
            }
        }
    }

    /// <summary>A line laid out by <see cref="TextBlockTextBuilder"/>: its glyphs, from <see cref="Start"/> up to <see cref="End"/>, and where their baseline is.</summary>
    internal readonly struct TextLine
    {
        public readonly int Start;
        public readonly int End;
        public readonly float Top;
        public readonly float Height;
        public readonly float Baseline;

        public TextLine(int start, int end, float top, float height, float baseline)
        {
            Start = start;
            End = end;
            Top = top;
            Height = height;
            Baseline = baseline;
        }
    }

    /// <summary>
    /// Lays out a TextBlock's runs. Line breaks start new lines. Wrapping breaks between words: when a word crosses the edge,
    /// the part of it already on the line moves down with it, and spaces may hang past the edge; a word longer than a whole line
    /// is broken where it crosses. Trimming ends each line that doesn't fit with an ellipsis. Each line is as tall as the largest font on it.
    /// </summary>
    /// <remarks>
    /// SpriteText resets the builder and then adds its text; the builder lays everything out while resetting and then takes no more characters.
    /// </remarks>
    internal sealed class TextBlockTextBuilder : TextBuilder
    {
        /// <summary>The style of each glyph in <see cref="TextBuilder.Characters"/>.</summary>
        public readonly List<TextStyle> GlyphStyles = new List<TextStyle>();

        public readonly List<TextLine> Lines = new List<TextLine>();

        public bool IsTruncated { get; private set; }

        private readonly TextBlock owner;
        private readonly StyledGlyphStore store;
        private readonly FontUsage font;
        private readonly TextAlignment alignment;
        private readonly float alignWidth;
        private readonly bool wrap;
        private readonly bool trim;
        private readonly string ellipsis;
        private readonly bool useFontSizeAsHeight;
        private readonly Vector2 startOffset;
        private readonly Vector2 spacing;
        private readonly char[] neverFixedWidthCharacters;
        private readonly char fallbackCharacter;

        private readonly List<TextRun> runs = new List<TextRun>();

        // The first glyph of each line, and the size of the line break that ends it (0 when it wrapped or is the last).
        private readonly List<int> lineStarts = new List<int>();
        private readonly List<float> breakSizes = new List<float>();

        private bool laidOut;
        private bool lineFull;
        private bool addingEllipsis;

        // The character being added, which decides whether a line wraps there.
        private char arriving;
        private TextStyle arrivingStyle;

        public TextBlockTextBuilder(TextBlock owner, ITexturedGlyphLookupStore store, FontUsage font, float maxWidth, TextAlignment alignment, float alignWidth, bool wrap, bool trim,
                                    string ellipsis, bool useFontSizeAsHeight, Vector2 startOffset, Vector2 spacing, List<TextBuilderGlyph> characters, char[] neverFixedWidthCharacters,
                                    char fallbackCharacter, char fixedWidthReferenceCharacter)
            : this(owner, new StyledGlyphStore(store, layoutFont(font)), layoutFont(font), maxWidth, alignment, alignWidth, wrap, trim, ellipsis, useFontSizeAsHeight, startOffset,
                spacing, characters, neverFixedWidthCharacters, fallbackCharacter, fixedWidthReferenceCharacter)
        {
        }

        private TextBlockTextBuilder(TextBlock owner, StyledGlyphStore store, FontUsage font, float maxWidth, TextAlignment alignment, float alignWidth, bool wrap, bool trim, string ellipsis,
                                     bool useFontSizeAsHeight, Vector2 startOffset, Vector2 spacing, List<TextBuilderGlyph> characters, char[] neverFixedWidthCharacters,
                                     char fallbackCharacter, char fixedWidthReferenceCharacter)
            : base(store, font, maxWidth, useFontSizeAsHeight, startOffset, spacing, characters, neverFixedWidthCharacters, fallbackCharacter, fixedWidthReferenceCharacter)
        {
            this.owner = owner;
            this.store = store;
            this.font = font;
            this.alignment = alignment;
            this.alignWidth = alignWidth;
            this.wrap = wrap;
            this.trim = trim;
            this.ellipsis = ellipsis;
            this.useFontSizeAsHeight = useFontSizeAsHeight;
            this.startOffset = startOffset;
            this.spacing = spacing;
            this.neverFixedWidthCharacters = neverFixedWidthCharacters;
            this.fallbackCharacter = fallbackCharacter;
        }

        // Glyphs are scaled from this font's size to their run's, so it can't be 0.
        private static FontUsage layoutFont(FontUsage font) => font.Size > 0 ? font : font.With(size: 1);

        protected override bool CanAddCharacters => (!laidOut && !lineFull) || addingEllipsis;

        protected override bool HasAvailableSpace(float length) => addingEllipsis || base.HasAvailableSpace(length);

        public override void Reset()
        {
            base.Reset();

            GlyphStyles.Clear();
            Lines.Clear();
            lineStarts.Clear();
            breakSizes.Clear();
            IsTruncated = false;
            laidOut = false;
            lineFull = false;

            lineStarts.Add(0);
            breakSizes.Add(0);

            runs.Clear();
            owner.GetRuns(runs);

            foreach (var run in runs)
            {
                foreach (char c in run.Text)
                {
                    if (c == '\n')
                    {
                        breakSizes[^1] = Math.Max(breakSizes[^1], run.Style.Font.Size);
                        newLine();
                        lineFull = false;
                    }
                    else if (!lineFull)
                        add(c, run.Style);
                }
            }

            placeLines();
            alignLines();
            laidOut = true;
        }

        private void add(char character, TextStyle style)
        {
            arriving = character;
            arrivingStyle = style;
            store.Style = style;

            AddCharacter(character);

            // Wrapping and trimming keep the styles in step with the glyphs they move; this records the glyph just added, if there was one.
            if (Characters.Count == GlyphStyles.Count + 1)
                GlyphStyles.Add(style);
        }

        private void removeLast()
        {
            RemoveLastCharacter();
            GlyphStyles.RemoveAt(GlyphStyles.Count - 1);
        }

        private void newLine()
        {
            AddNewLine();
            lineStarts.Add(Characters.Count);
            breakSizes.Add(0);
        }

        protected override void OnWidthExceeded()
        {
            if (addingEllipsis)
                return;

            if (trim)
                trimLine();
            else if (wrap)
                wrapLine();
        }

        private void wrapLine()
        {
            // Spaces hang past the edge, and a character wider than the whole line stays on it.
            int count = Characters.Count;
            int lineStart = lineStarts[^1];

            if (char.IsWhiteSpace(arriving) || count == lineStart)
                return;

            int lastSpace = -1;

            for (int i = count - 1; i >= lineStart; i--)
            {
                if (char.IsWhiteSpace(Characters[i].Character))
                {
                    lastSpace = i;
                    break;
                }
            }

            // A word that starts here, or one longer than the line, simply continues on the next line.
            if (lastSpace == count - 1 || lastSpace < 0)
            {
                newLine();
                return;
            }

            var moved = new (char Character, TextStyle Style)[count - 1 - lastSpace];

            for (int i = moved.Length - 1; i >= 0; i--)
            {
                moved[i] = (Characters[^1].Character, GlyphStyles[^1]);
                removeLast();
            }

            char character = arriving;
            var style = arrivingStyle;

            newLine();

            foreach (var (c, s) in moved)
                add(c, s);

            arriving = character;
            arrivingStyle = style;
            store.Style = style;
        }

        private void trimLine()
        {
            int lineStart = lineStarts[^1];
            var style = Characters.Count > lineStart ? GlyphStyles[^1] : arrivingStyle;

            addingEllipsis = true;

            try
            {
                if (string.IsNullOrEmpty(ellipsis))
                    return;

                // Measure the ellipsis in the list of glyphs, then take it back out.
                store.Style = style;
                int measureStart = Characters.Count;
                var measure = new TextBuilder(store, font, float.MaxValue, useFontSizeAsHeight, Vector2.Zero, spacing, Characters, neverFixedWidthCharacters, fallbackCharacter);
                measure.AddText(ellipsis);
                float ellipsisWidth = measure.Bounds.X;
                Characters.RemoveRange(measureStart, Characters.Count - measureStart);

                // Make room for it, keeping at least the first glyph of the line, and don't leave a space before it.
                while (Characters.Count > lineStart + 1 && (char.IsWhiteSpace(Characters[^1].Character) || !base.HasAvailableSpace(spacing.X + ellipsisWidth)))
                    removeLast();

                foreach (char c in ellipsis)
                    add(c, style);
            }
            finally
            {
                addingEllipsis = false;
                IsTruncated = true;
                lineFull = true;
            }
        }

        // TextBuilder makes every line as tall as the TextBlock's font. Each line is moved to sit under the one before it
        // at the height of the largest font on it, then the bounds are set to where the lines end.
        private void placeLines()
        {
            float top = startOffset.Y;

            for (int i = 0; i < lineStarts.Count; i++)
            {
                int start = lineStarts[i];
                int end = i + 1 < lineStarts.Count ? lineStarts[i + 1] : Characters.Count;

                float height = breakSizes[i];

                for (int g = start; g < end; g++)
                    height = Math.Max(height, GlyphStyles[g].Font.Size);

                // Only the line after a closing line break can be empty; it takes the TextBlock's own size.
                if (height == 0)
                    height = owner.Font.Size;

                float lineTop = useFontSizeAsHeight || end == start ? top : Characters[start].LinePosition;
                float offset = lineTop - (end > start ? Characters[start].LinePosition : lineTop);

                float baseline = float.NaN;
                float spaceBaseline = float.NaN;

                for (int g = start; g < end; g++)
                {
                    var glyph = Characters[g];

                    if (offset != 0)
                    {
                        FrameworkInternals.SetDrawRectangle(ref glyph, glyph.DrawRectangle.Offset(0, offset));
                        Characters[g] = glyph;
                    }

                    // Glyphs on a line share a baseline, except spaces, which TextBuilder doesn't align.
                    float glyphBaseline = glyph.DrawRectangle.Top - glyph.YOffset + glyph.Baseline;

                    if (char.IsWhiteSpace(glyph.Character))
                        spaceBaseline = float.IsNaN(spaceBaseline) ? glyphBaseline : Math.Max(spaceBaseline, glyphBaseline);
                    else
                        baseline = float.IsNaN(baseline) ? glyphBaseline : Math.Max(baseline, glyphBaseline);
                }

                if (float.IsNaN(baseline))
                    baseline = float.IsNaN(spaceBaseline) ? lineTop + height * 0.8f : spaceBaseline;

                // A drawable among the text stands on the baseline and can push it, and the line, further down than the fonts would.
                bool hasElement = false;
                float descent = 0;

                for (int g = start; g < end; g++)
                {
                    if (GlyphStyles[g].Container != null)
                        hasElement = true;
                    else if (!char.IsWhiteSpace(Characters[g].Character))
                        descent = Math.Max(descent, GlyphStyles[g].Font.Size - Characters[g].Baseline);
                }

                if (hasElement)
                    height = Math.Max(height, baseline - lineTop + descent);

                Lines.Add(new TextLine(start, end, lineTop, height, baseline));
                top = lineTop + height + spacing.Y;
            }

            if (useFontSizeAsHeight && Lines.Count > 0)
                FrameworkInternals.SetBounds(this, new Vector2(Bounds.X, Lines[^1].Top + Lines[^1].Height));
        }

        // Moves each line across the width: the align width, or the widest line when there's none (or it's narrower).
        private void alignLines()
        {
            if (alignment == TextAlignment.Left || Lines.Count == 0)
                return;

            var widths = new float[Lines.Count];
            float widest = 0;

            for (int i = 0; i < Lines.Count; i++)
                widest = Math.Max(widest, widths[i] = lineWidth(Lines[i]));

            float target = Math.Max(widest, alignWidth - startOffset.X);

            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                float extra = target - widths[i];

                if (extra <= 0 || line.End == line.Start)
                    continue;

                if (alignment == TextAlignment.Justify)
                {
                    // Only lines that wrapped are spread; a line a break or the text ends stays where it is, as in WPF.
                    if (i == Lines.Count - 1 || breakSizes[i] > 0)
                        continue;

                    int last = line.End - 1;

                    while (last > line.Start && char.IsWhiteSpace(Characters[last].Character))
                        last--;

                    int gaps = 0;

                    for (int g = line.Start; g < last; g++)
                    {
                        if (char.IsWhiteSpace(Characters[g].Character))
                            gaps++;
                    }

                    if (gaps == 0)
                        continue;

                    float perGap = extra / gaps;
                    float shift = 0;

                    for (int g = line.Start; g <= last; g++)
                    {
                        if (shift != 0)
                            move(g, shift);

                        if (char.IsWhiteSpace(Characters[g].Character))
                            shift += perGap;
                    }
                }
                else
                {
                    float shift = alignment == TextAlignment.Center ? extra / 2 : extra;

                    for (int g = line.Start; g < line.End; g++)
                        move(g, shift);
                }
            }

            FrameworkInternals.SetBounds(this, new Vector2(Math.Max(Bounds.X, startOffset.X + target), Bounds.Y));

            void move(int index, float x)
            {
                var glyph = Characters[index];
                FrameworkInternals.SetDrawRectangle(ref glyph, glyph.DrawRectangle.Offset(x, 0));
                Characters[index] = glyph;
            }
        }

        // From the line's start to the end of its last glyph that isn't a space.
        private float lineWidth(TextLine line)
        {
            for (int g = line.End - 1; g >= line.Start; g--)
            {
                var glyph = Characters[g];

                if (!char.IsWhiteSpace(glyph.Character))
                    return glyph.DrawRectangle.Left - glyph.XOffset + glyph.XAdvance - startOffset.X;
            }

            return 0;
        }
    }

    /// <summary>
    /// Looks glyphs up in the font of the run being laid out, scaled from the builder's font size to the run's.
    /// </summary>
    internal sealed class StyledGlyphStore : ITexturedGlyphLookupStore
    {
        private readonly ITexturedGlyphLookupStore store;
        private readonly FontUsage font;
        private readonly Dictionary<(ITexturedCharacterGlyph, float), ITexturedCharacterGlyph> scaled = new Dictionary<(ITexturedCharacterGlyph, float), ITexturedCharacterGlyph>();

        /// <summary>The style of the run being laid out.</summary>
        public TextStyle Style;

        public StyledGlyphStore(ITexturedGlyphLookupStore store, FontUsage font)
        {
            this.store = store;
            this.font = font;
        }

        public ITexturedCharacterGlyph Get(string fontName, char character)
        {
            if (Style?.Container is InlineUIContainer container)
                return new ElementGlyph(container.LaidOutSize / font.Size);

            var runFont = Style?.Font ?? font;

            // TextBuilder asks for its own font, then the same weight in any family, then any font at all.
            string name = fontName == font.FontName ? runFont.FontName
                : fontName == font.FontNameNoFamily ? runFont.FontNameNoFamily
                : fontName;

            var glyph = store.Get(name, character);
            float scale = runFont.Size / font.Size;

            if (glyph == null || scale == 1)
                return glyph;

            if (!scaled.TryGetValue((glyph, scale), out var result))
                scaled[(glyph, scale)] = result = new ScaledGlyph(glyph, scale);

            return result;
        }

        public Task<ITexturedCharacterGlyph> GetAsync(string fontName, char character) => Task.FromResult(Get(fontName, character));

        /// <summary>The room a drawable among the text takes: its size, standing on the baseline. Sizes are in units of the builder's font size.</summary>
        private sealed class ElementGlyph : ITexturedCharacterGlyph
        {
            private readonly Vector2 size;

            public ElementGlyph(Vector2 size)
            {
                this.size = size;
            }

            public Texture Texture => null;
            public float XOffset => 0;
            public float YOffset => 0;
            public float XAdvance => size.X;
            public float Baseline => size.Y;
            public char Character => InlineUIContainer.Placeholder;
            public float Width => size.X;
            public float Height => size.Y;

            public float GetKerning<T>(T lastGlyph)
                where T : ICharacterGlyph
                => 0;
        }

        private sealed class ScaledGlyph : ITexturedCharacterGlyph
        {
            private readonly ITexturedCharacterGlyph glyph;
            private readonly float scale;

            public ScaledGlyph(ITexturedCharacterGlyph glyph, float scale)
            {
                this.glyph = glyph;
                this.scale = scale;
            }

            public Texture Texture => glyph.Texture;
            public float XOffset => glyph.XOffset * scale;
            public float YOffset => glyph.YOffset * scale;
            public float XAdvance => glyph.XAdvance * scale;
            public float Baseline => glyph.Baseline * scale;
            public char Character => glyph.Character;
            public float Width => glyph.Width * scale;
            public float Height => glyph.Height * scale;

            public float GetKerning<T>(T lastGlyph)
                where T : ICharacterGlyph
                => glyph.GetKerning(lastGlyph) * scale;
        }
    }

    /// <summary>
    /// Shows the drawables of a TextBlock's <see cref="InlineUIContainer"/>s where their places are in its text. It sits next to the
    /// TextBlock in the same parent and copies the TextBlock's place and size each frame, so its space is the TextBlock's own.
    /// </summary>
    internal partial class InlineUILayer : Container
    {
        private readonly TextBlock owner;

        public InlineUILayer(TextBlock owner)
        {
            this.owner = owner;
        }

        internal bool Disposed => IsDisposed;

        /// <summary>Shows exactly these containers' drawables, at their glyphs. Returns whether any drawable's size changed since it was laid out.</summary>
        public bool Sync(IReadOnlyList<InlineUIContainer> containers, TextBlockTextBuilder layout, List<TextBuilderGlyph> characters)
        {
            foreach (var child in Children.ToArray())
            {
                if (!containers.Any(c => ReferenceEquals(c.Child, child)))
                    Remove(child, false);
            }

            bool resized = false;

            foreach (var container in containers)
            {
                var child = container.Child;

                if (child == null)
                    continue;

                if (child.Parent != this)
                {
                    (child.Parent as Container<Drawable>)?.Remove(child, false);
                    child.Anchor = child.Origin = Anchor.TopLeft;
                    Add(child);
                }

                if (child.LayoutSize != container.LaidOutSize)
                {
                    container.LaidOutSize = child.LayoutSize;
                    resized = true;
                }
            }

            if (layout != null)
            {
                for (int i = 0; i < layout.GlyphStyles.Count && i < characters.Count; i++)
                {
                    if (layout.GlyphStyles[i].Container?.Child is Drawable child && child.Parent == this)
                        child.Position = characters[i].DrawRectangle.TopLeft;
                }
            }

            return resized;
        }

        protected override void Update()
        {
            base.Update();

            if (owner.Disposed)
            {
                Expire();
                return;
            }

            Anchor = owner.Anchor;
            Origin = owner.Origin;
            Position = owner.Position;
            Margin = owner.Margin;
            Scale = owner.Scale;
            Rotation = owner.Rotation;
            Size = owner.DrawSize;

            // Hidden with the TextBlock, and when it has left this parent.
            Alpha = owner.Parent == Parent && owner.IsPresent ? owner.Alpha : 0;
        }
    }
}
