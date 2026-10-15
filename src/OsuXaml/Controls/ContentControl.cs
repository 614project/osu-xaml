using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>Default colours for the controls; every one can be overridden per element.</summary>
    public static class Theme
    {
        public static Colour4 Text = Colour4.FromHex("cdd6f4");
        public static Colour4 SubtleText = Colour4.FromHex("a6adc8");
        public static Colour4 Surface = Colour4.FromHex("313244");
        public static Colour4 Border = Colour4.FromHex("45475a");
        public static Colour4 Accent = Colour4.FromHex("89b4fa");
        public static Colour4 AccentText = Colour4.FromHex("1e1e2e");
    }

    /// <summary>
    /// Base for controls with one child (Border, Button). The child sits in a <see cref="Slot"/>,
    /// so it gets the same alignment rules as panel children.
    /// </summary>
    public abstract partial class ContentControl : CompositeDrawable, ILayoutElement
    {
        /// <summary>Holds the chrome (background, border, corner masking); animations can scale it around its centre.</summary>
        protected readonly Container Chrome;

        protected readonly Box BackgroundBox;

        private readonly Container contentArea;
        private Slot slot;

        private bool sizeToContentX;
        private bool sizeToContentY;

        protected ContentControl()
        {
            InternalChild = Chrome = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                Children = new Drawable[]
                {
                    BackgroundBox = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0 },
                    contentArea = new Container { RelativeSizeAxes = Axes.Both },
                },
            };
        }

        public Drawable Child
        {
            get => slot?.Content;
            set
            {
                if (slot != null)
                    contentArea.Remove(slot, true);

                slot = value == null ? null : new Slot(value);

                if (slot != null)
                    contentArea.Add(slot);
            }
        }

        public Colour4 Background
        {
            get => BackgroundBox.Colour;
            set
            {
                BackgroundBox.Colour = value;
                BackgroundBox.Alpha = 1;
            }
        }

        public Colour4 BorderBrush
        {
            get => Chrome.BorderColour;
            set => Chrome.BorderColour = value;
        }

        public float BorderThickness
        {
            get => Chrome.BorderThickness;
            set
            {
                Chrome.BorderThickness = value;
                contentArea.Padding = Layout.Add(new MarginPadding(value), padding);
            }
        }

        public float CornerRadius
        {
            get => Chrome.CornerRadius;
            set => Chrome.CornerRadius = value;
        }

        private MarginPadding padding;

        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                padding = value;
                contentArea.Padding = Layout.Add(new MarginPadding(Chrome.BorderThickness), value);
            }
        }

        public virtual Vector2 Measure(Vector2 availableSize)
        {
            var chrome = new Vector2(contentArea.Padding.TotalHorizontal, contentArea.Padding.TotalVertical);
            var content = slot?.Measure(Vector2.ComponentMax(Vector2.Zero, availableSize - chrome)) ?? Vector2.Zero;
            return content + chrome;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && !AutoSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && !AutoSizeAxes.HasFlag(Axes.Y) && Height == 0;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (sizeToContentX || sizeToContentY)
            {
                var desired = Measure(new Vector2(float.PositiveInfinity));
                if (sizeToContentX) Width = desired.X;
                if (sizeToContentY) Height = desired.Y;
            }

            slot?.Arrange(Vector2.Zero, contentArea.ChildSize);
        }
    }

    /// <summary>Draws a background, border and rounded corners around one child.</summary>
    public partial class Border : ContentControl
    {
    }
}
