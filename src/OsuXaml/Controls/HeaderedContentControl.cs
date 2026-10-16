using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A <see cref="ContentControl"/> with a <see cref="Header"/> row above its content, like WPF's HeaderedContentControl;
    /// the base of <see cref="Expander"/> and <see cref="GroupBox"/>. The header is presented like content: text becomes a
    /// TextBlock, a drawable is shown as is, and other objects go through <see cref="HeaderTemplate"/>.
    /// Background and border surround both the header and the content, unless a subclass puts the header above the border.
    /// </summary>
    public partial class HeaderedContentControl : ContentControl
    {
        private readonly HeaderPresenter headerPresenter;
        private readonly bool headerAboveBorder;
        private MarginPadding padding;
        private MarginPadding headerPadding;
        private float headerHeight;

        public HeaderedContentControl()
            : this(false)
        {
        }

        /// <param name="headerAboveBorder">Puts the header row above the border and background instead of inside them.</param>
        protected HeaderedContentControl(bool headerAboveBorder)
        {
            this.headerAboveBorder = headerAboveBorder;

            HeaderArea = new Container
            {
                Child = headerPresenter = new HeaderPresenter(this)
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
            };

            if (headerAboveBorder)
            {
                // The chrome starts below the header row; its height is set every frame.
                Chrome.RelativeSizeAxes = Axes.X;
                Chrome.Anchor = Chrome.Origin = Anchor.TopLeft;
                AddInternal(HeaderArea);
            }
            else
                Chrome.Add(HeaderArea);
        }

        /// <summary>What the header row shows. Text, a drawable (through a property element), or an object shown through <see cref="HeaderTemplate"/>.</summary>
        public object Header
        {
            get => headerPresenter.Content;
            set => headerPresenter.Content = value;
        }

        /// <summary>Builds the drawable for a non-drawable <see cref="Header"/>.</summary>
        public DataTemplate HeaderTemplate
        {
            get => headerPresenter.ContentTemplate;
            set => headerPresenter.ContentTemplate = value;
        }

        /// <summary>The drawable currently shown for <see cref="Header"/>, once loaded.</summary>
        public Drawable PresentedHeader => headerPresenter.PresentedContent;

        /// <summary>Space around the content, below the header row.</summary>
        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                padding = value;
                applyPadding();
            }
        }

        /// <summary>The header row. Subclasses add their own parts (a chevron, a click target) to it.</summary>
        protected Container HeaderArea { get; }

        /// <summary>Space around the header inside the header row.</summary>
        protected MarginPadding HeaderPadding
        {
            get => headerPadding;
            set
            {
                headerPadding = value;
                headerPresenter.X = value.Left;
                headerPresenter.Y = (value.Top - value.Bottom) / 2;
            }
        }

        /// <summary>The smallest height of the header row.</summary>
        protected float MinHeaderHeight { get; set; }

        /// <summary>How much of the content part is shown, from 0 (only the header) to 1. Expander animates it.</summary>
        protected virtual float ContentFraction => 1;

        /// <summary>Creates the drawable for a text header.</summary>
        protected virtual Drawable CreateHeaderTextContent(LocalisableString text) => new TextBlock { Text = text, Colour = Foreground };

        /// <summary>The size of the header row for a given width, including <see cref="HeaderPadding"/>.</summary>
        protected Vector2 MeasureHeader(float availableWidth)
        {
            var pad = new Vector2(headerPadding.TotalHorizontal, headerPadding.TotalVertical);
            var desired = headerPresenter.Measure(new Vector2(Math.Max(0, availableWidth - pad.X), float.PositiveInfinity)) + pad;
            return new Vector2(desired.X, Math.Max(MinHeaderHeight, desired.Y));
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var full = base.Measure(availableSize);

            if (Template != null)
                return full;

            float fraction = Math.Clamp(ContentFraction, 0, 1);
            float border = headerAboveBorder ? 0 : BorderThickness * 2;
            var header = MeasureHeader(availableSize.X - border) + new Vector2(border);

            // Inside the border, the base size counts the header row through the padding it reserved; take that out to get the part below it.
            float below = headerAboveBorder ? full.Y : Math.Max(0, full.Y - border - headerHeight);

            // Collapsing shrinks only the part below the header; a border inside keeps closing around what is left.
            float height = header.Y + below * fraction;
            float width = fraction > 0 ? Math.Max(full.X, header.X) : header.X;

            return new Vector2(width, height);
        }

        protected override void UpdateAfterChildren()
        {
            layoutHeader();

            base.UpdateAfterChildren();

            if (Template != null)
                return;

            // Sizing to content above may have changed this control's size.
            layoutHeader();

            if (PresentedContent?.Parent is Slot slot && slot.Parent is Container area)
            {
                float fraction = ContentFraction;
                slot.Alpha = fraction > 0 ? 1 : 0;

                // A chrome below the header was just resized; a partly collapsed one must clip its content rather than squash it.
                if (fraction > 0 && (headerAboveBorder || fraction < 1))
                {
                    var size = area.ChildSize;
                    float desired = fraction < 1 ? slot.Measure(new Vector2(size.X, float.PositiveInfinity)).Y : 0;
                    slot.Arrange(Vector2.Zero, new Vector2(size.X, Math.Max(size.Y, desired)));
                }
            }
        }

        private void layoutHeader()
        {
            float border = headerAboveBorder ? 0 : BorderThickness;
            float height = Template != null ? 0 : MeasureHeader(DrawWidth - border * 2).Y;

            HeaderArea.Alpha = Template != null ? 0 : 1;
            HeaderArea.Position = new Vector2(border);
            HeaderArea.Size = new Vector2(Math.Max(0, DrawWidth - border * 2), height);

            if (headerAboveBorder)
            {
                Chrome.Y = height;
                Chrome.Height = Math.Max(0, DrawHeight - height);
            }

            if (height != headerHeight)
            {
                headerHeight = height;
                applyPadding();
            }
        }

        private void applyPadding()
        {
            float reserved = headerAboveBorder ? 0 : headerHeight;
            base.Padding = new MarginPadding { Left = padding.Left, Top = padding.Top + reserved, Right = padding.Right, Bottom = padding.Bottom };
        }

        /// <summary>Presents the header with the owner's text style.</summary>
        private partial class HeaderPresenter : ContentPresenter
        {
            private readonly HeaderedContentControl owner;

            public HeaderPresenter(HeaderedContentControl owner)
            {
                this.owner = owner;
            }

            protected override Drawable CreateTextContent(LocalisableString text) => owner.CreateHeaderTextContent(text);
        }
    }
}
