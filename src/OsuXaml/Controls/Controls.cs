using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;

namespace OsuXaml.Controls
{
    public enum FontWeight
    {
        Regular,
        Bold,
    }

    /// <summary>A line of text sized to its content.</summary>
    public partial class TextBlock : SpriteText
    {
        public TextBlock()
        {
            Font = FrameworkFont.Regular.With(size: 16);
            Colour = Theme.Text;
        }

        public float FontSize
        {
            get => Font.Size;
            set => Font = Font.With(size: value);
        }

        public FontWeight FontWeight
        {
            get => Font.Weight == "Bold" ? FontWeight.Bold : FontWeight.Regular;
            set => Font = Font.With(weight: value.ToString());
        }

        public Colour4 Foreground
        {
            get => Colour;
            set => Colour = value;
        }
    }

    /// <summary>
    /// A button sized to its content. Text content becomes a bold TextBlock; any element can be the content instead.
    /// Hover and press feedback run as osu!framework transforms.
    /// </summary>
    public partial class Button : ContentControl
    {
        private readonly Box hover;
        private bool isEnabled = true;

        /// <summary>Raised when the button is clicked.</summary>
        public event Action Click;

        /// <summary>The same as <see cref="Click"/>, as a property for bindings: <c>Action="{Bind Save}"</c>.</summary>
        public Action Action { get; set; }

        public Button()
        {
            Background = Theme.Accent;
            Foreground = Theme.AccentText;
            CornerRadius = 6;
            Padding = new MarginPadding { Horizontal = 16, Vertical = 8 };
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;

            Chrome.Add(hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 });
        }

        protected override Drawable CreateTextContent(LocalisableString text) => new TextBlock { Text = text, Colour = Foreground, FontWeight = FontWeight.Bold };

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);
            }
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (isEnabled)
                hover.FadeTo(0.15f, 100, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(250, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (isEnabled)
                Chrome.ScaleTo(0.95f, 80, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            Chrome.ScaleTo(1, 400, Easing.OutElastic);
            base.OnMouseUp(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return false;

            hover.FadeTo(0.35f).FadeTo(0.15f, 300, Easing.OutQuint);
            Click?.Invoke();
            Action?.Invoke();
            return true;
        }
    }

    /// <summary>A checkbox with a label. <c>IsChecked</c> binds two-way.</summary>
    public partial class CheckBox : BasicCheckbox
    {
        public CheckBox()
        {
            CheckedColor = Theme.Accent;
            UncheckedColor = Theme.Surface;
        }

        public Bindable<bool> IsChecked => Current;

        public LocalisableString Content
        {
            get => LabelText;
            set => LabelText = value;
        }
    }

    /// <summary>A single-line text box. <c>Text</c> is a bindable so it binds two-way like WPF.</summary>
    public partial class TextBox : BasicTextBox
    {
        public TextBox()
        {
            Height = 36;
            BackgroundUnfocused = Theme.Surface;
            BackgroundFocused = Theme.Border;
            BackgroundCommit = Theme.Accent;
            CornerRadius = 6;
            Masking = true;
        }

        public new Bindable<string> Text => Current;

        private float laidOutWidth = -1;

        protected override void Update()
        {
            base.Update();

            // osu!framework's TextBox lays text out against the width it had when the text was set. Inside a panel the
            // width arrives later, which leaves the text scrolled out of view until focus, so re-apply it on resize.
            if (DrawWidth != laidOutWidth && !HasFocus)
            {
                laidOutWidth = DrawWidth;

                string value = base.Text;
                base.Text = string.Empty;
                base.Text = value;
            }
        }
    }

    /// <summary>A horizontal slider over a double range.</summary>
    public partial class Slider : BasicSliderBar<double>
    {
        public Slider()
        {
            Height = 20;
            BackgroundColour = Theme.Surface;
            SelectionColour = Theme.Accent;
            CornerRadius = 4;
            Masking = true;
            CurrentNumber.MaxValue = 100;
        }

        public Bindable<double> Value => Current;

        public double Minimum
        {
            get => CurrentNumber.MinValue;
            set => CurrentNumber.MinValue = value;
        }

        public double Maximum
        {
            get => CurrentNumber.MaxValue;
            set => CurrentNumber.MaxValue = value;
        }
    }

    /// <summary>A bar filled to Value between Minimum and Maximum; changes animate.</summary>
    public partial class ProgressBar : CompositeDrawable
    {
        private readonly Box track;
        private readonly Box fill;
        private double value;

        public ProgressBar()
        {
            Height = 8;
            Masking = true;
            CornerRadius = 4;

            InternalChildren = new Drawable[]
            {
                track = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Surface },
                fill = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = Theme.Accent },
            };
        }

        public double Minimum { get; set; }

        public double Maximum { get; set; } = 100;

        public double Value
        {
            get => value;
            set
            {
                this.value = value;
                float fraction = Maximum > Minimum ? (float)Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
                fill.ResizeWidthTo(fraction, IsLoaded ? 300 : 0, Easing.OutQuint);
            }
        }

        public Colour4 Foreground
        {
            get => fill.Colour;
            set => fill.Colour = value;
        }

        public Colour4 Background
        {
            get => track.Colour;
            set => track.Colour = value;
        }
    }

    /// <summary>Scrolls one child vertically. A panel child keeps the viewport width and grows to its content height.</summary>
    public partial class ScrollViewer : CompositeDrawable, ILayoutElement
    {
        private readonly BasicScrollContainer scroll;
        private Drawable child;

        public ScrollViewer()
        {
            InternalChild = scroll = new BasicScrollContainer { RelativeSizeAxes = Axes.Both };
        }

        public Drawable Child
        {
            get => child;
            set
            {
                if (child != null)
                    scroll.Remove(child, true);

                child = value;

                if (child == null)
                    return;

                if (!Layout.AutoSizeAxes(child).HasFlag(Axes.X))
                {
                    child.RelativeSizeAxes |= Axes.X;
                    child.Width = 1;
                }

                scroll.Add(child);
            }
        }

        public Vector2 Measure(Vector2 availableSize)
            => child == null ? Vector2.Zero : Layout.Measure(child, new Vector2(availableSize.X, float.PositiveInfinity), default, false, false);

        /// <summary>Scrolls until <paramref name="descendant"/> is visible.</summary>
        public void ScrollIntoView(Drawable descendant, bool animated = true) => scroll.ScrollIntoView(descendant, animated);

        public void ScrollToStart(bool animated = true) => scroll.ScrollToStart(animated);

        public void ScrollToEnd(bool animated = true) => scroll.ScrollToEnd(animated);

        /// <summary>The current scroll offset from the top.</summary>
        public double VerticalOffset => scroll.Current;
    }

    /// <summary>A thin horizontal rule.</summary>
    public partial class Separator : Box
    {
        public Separator()
        {
            RelativeSizeAxes = Axes.X;
            Height = 1;
            Colour = Theme.Border;
            Margin = new MarginPadding { Vertical = 6 };
        }
    }
}
