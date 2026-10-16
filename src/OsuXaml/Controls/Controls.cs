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
using osuTK.Graphics;
using osuTK.Input;

namespace OsuXaml.Controls
{
    public enum FontWeight
    {
        Regular,
        Bold,
    }

    public enum TextWrapping
    {
        NoWrap,

        /// <summary>Breaks lines at the width the layout offers.</summary>
        Wrap,

        /// <summary>The same as <see cref="Wrap"/> here.</summary>
        WrapWithOverflow,
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
    /// </summary>
    public partial class TextBlock : SpriteText, ILayoutElement
    {
        private TextWrapping textWrapping;
        private TextTrimming textTrimming;

        // The size last reported to the layout; NaN until the first measure, so sizes set in XAML always apply.
        private Vector2 measured = new Vector2(float.NaN);

        public TextBlock()
        {
            Font = FrameworkFont.Regular.With(size: 16);
            Colour = Theme.Text;
        }

        /// <summary>Wraps long text onto more lines. Line breaks inside the text are not supported.</summary>
        public TextWrapping TextWrapping
        {
            get => textWrapping;
            set
            {
                textWrapping = value;
                applyTextMode();
            }
        }

        /// <summary>Ends text that doesn't fit with an ellipsis. Ignored while the text wraps.</summary>
        public TextTrimming TextTrimming
        {
            get => textTrimming;
            set
            {
                textTrimming = value;
                applyTextMode();
            }
        }

        private bool constrainsWidth => textWrapping != TextWrapping.NoWrap || textTrimming != TextTrimming.None;

        private void applyTextMode()
        {
            // osu!framework's SpriteText can either wrap or truncate, so wrapping wins when both are set.
            bool wrap = textWrapping != TextWrapping.NoWrap;
            Truncate = !wrap && textTrimming != TextTrimming.None;
            AllowMultiline = wrap;

            if (!constrainsWidth)
                MaxWidth = float.PositiveInfinity;
        }

        public Vector2 Measure(Vector2 availableSize)
        {
            // Wrapping and trimming need the width on offer; MaxWidth keeps the text sizing itself up to that width.
            if (constrainsWidth)
                MaxWidth = float.IsFinite(availableSize.X) ? Math.Max(1, availableSize.X) : float.PositiveInfinity;

            return measured = new Vector2(
                RelativeSizeAxes.HasFlag(Axes.X) ? 0 : DrawWidth,
                RelativeSizeAxes.HasFlag(Axes.Y) ? 0 : DrawHeight);
        }

        // A slot hands layout elements the size they measured as a fixed size. Text measures itself, and a fixed
        // width or height would stop it following its text, so that hand-back is ignored.
        public override float Width
        {
            get => base.Width;
            set
            {
                if (value != measured.X)
                    base.Width = value;
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

    /// <summary>
    /// A single-line text box. <c>Text</c> is a bindable so it binds two-way like WPF. <c>PlaceholderText</c> shows a hint
    /// while it is empty, <c>MaxLength</c> limits the length and <c>IsReadOnly</c> stops editing.
    /// </summary>
    public partial class TextBox : BasicTextBox
    {
        private bool refreshing;

        public TextBox()
        {
            Height = 36;
            BackgroundUnfocused = Theme.Surface;
            BackgroundFocused = Theme.Border;
            BackgroundCommit = Theme.Accent;
            CornerRadius = 6;
            Masking = true;

            Current.ValueChanged += e =>
            {
                if (!refreshing)
                    OnTextChanged(e.NewValue);
            };
        }

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

        protected override Color4 SelectionColour => Theme.Accent;

        protected override SpriteText CreatePlaceholder()
        {
            var placeholder = base.CreatePlaceholder();
            placeholder.Colour = Theme.Overlay;
            return placeholder;
        }

        private float laidOutWidth = -1;

        protected override void Update()
        {
            base.Update();

            // osu!framework's TextBox lays text out against the width it had when the text was set. Inside a panel the
            // width arrives later, which leaves the text scrolled out of view until focus, so re-apply it on resize.
            if (DrawWidth != laidOutWidth && !HasFocus)
            {
                laidOutWidth = DrawWidth;
                RefreshText();
            }
        }

        /// <summary>Lays the current text out again, e.g. after the box resized or the characters should look different.</summary>
        protected void RefreshText()
        {
            string value = base.Text;

            refreshing = true;
            base.Text = string.Empty;
            base.Text = value;
            refreshing = false;
        }
    }

    /// <summary>Where a <see cref="Slider"/> draws tick marks.</summary>
    public enum TickPlacement
    {
        None,

        /// <summary>Along the top of a horizontal slider, the left of a vertical one.</summary>
        TopLeft,

        /// <summary>Along the bottom of a horizontal slider, the right of a vertical one.</summary>
        BottomRight,

        Both,
    }

    /// <summary>
    /// A slider over a double range, like WPF's Slider. <see cref="Value"/> binds two-way and is kept within
    /// <see cref="Minimum"/> and <see cref="Maximum"/> (the slider's own range, whatever range the bound source has).
    /// <see cref="Orientation"/> makes it vertical; <see cref="IsSnapToTickEnabled"/> snaps dragging and keys to
    /// <see cref="TickFrequency"/>. Arrow keys move by <see cref="SmallChange"/>, Page Up/Down by <see cref="LargeChange"/>,
    /// Home/End to the ends, while the slider is focused or hovered.
    /// </summary>
    public partial class Slider : BasicSliderBar<double>
    {
        private const float thickness = 20;
        private const int max_ticks = 200;

        private readonly Container ticks;
        private Orientation orientation = Orientation.Horizontal;
        private double tickFrequency = 1;
        private TickPlacement tickPlacement;
        private bool ticksValid;
        private bool syncing;

        public Slider()
        {
            Height = thickness;
            BackgroundColour = Theme.Surface;
            SelectionColour = Theme.Accent;
            FocusColour = Theme.Overlay;
            CornerRadius = 4;
            Masking = true;
            CurrentNumber.MinValue = 0;
            CurrentNumber.MaxValue = 100;

            Add(ticks = new Container { RelativeSizeAxes = Axes.Both });

            // Value is separate from the slider's range-constrained number, so binding it to a view model never copies the
            // source's range onto the slider (or the slider's range onto the source).
            Value.ValueChanged += e =>
            {
                if (!syncing)
                {
                    setNumber(e.NewValue);

                    // Out-of-range values are coerced, like WPF, and the coerced value flows back to the source.
                    if (CurrentNumber.Value != e.NewValue && !Value.Disabled)
                    {
                        Value.Value = CurrentNumber.Value;
                        return;
                    }
                }

                ValueChanged?.Invoke(e.NewValue);
            };

            Value.DisabledChanged += disabled => CurrentNumber.Disabled = disabled;

            // Changes that don't come from Value come from the user (dragging, clicking, keys) and snap to ticks.
            CurrentNumber.ValueChanged += e =>
            {
                if (syncing)
                    return;

                double snapped = snap(e.NewValue);

                if (snapped != e.NewValue)
                {
                    CurrentNumber.Value = snapped;
                    if (CurrentNumber.Value != e.NewValue)
                        return;
                }

                if (Value.Disabled)
                    return;

                syncing = true;
                Value.Value = e.NewValue;
                syncing = false;
            };
        }

        /// <summary>The slider's value. Binds two-way: <c>Value="{Bind Volume}"</c>.</summary>
        public Bindable<double> Value { get; } = new Bindable<double>();

        /// <summary>Raised when <see cref="Value"/> changes, with the new value.</summary>
        public event Action<double> ValueChanged;

        public double Minimum
        {
            get => CurrentNumber.MinValue;
            set
            {
                CurrentNumber.MinValue = value;
                ticksValid = false;
            }
        }

        public double Maximum
        {
            get => CurrentNumber.MaxValue;
            set
            {
                CurrentNumber.MaxValue = value;
                ticksValid = false;
            }
        }

        /// <summary>Horizontal (the default) or vertical, with the minimum at the bottom.</summary>
        public Orientation Orientation
        {
            get => orientation;
            set
            {
                if (orientation == value)
                    return;

                orientation = value;

                // Move the default thickness to the new cross axis, unless a size was set.
                if (value == Orientation.Vertical && Width == 0 && Height == thickness)
                {
                    Height = 0;
                    Width = thickness;
                }
                else if (value == Orientation.Horizontal && Height == 0 && Width == thickness)
                {
                    Width = 0;
                    Height = thickness;
                }

                SelectionBox.ClearTransforms();
                SelectionBox.Anchor = SelectionBox.Origin = value == Orientation.Vertical ? Anchor.BottomLeft : Anchor.TopLeft;
                SelectionBox.Scale = Vector2.One;
                ticksValid = false;

                if (IsLoaded)
                    UpdateValue(NormalizedValue);
            }
        }

        /// <summary>The distance between ticks; also the step used while <see cref="IsSnapToTickEnabled"/>.</summary>
        public double TickFrequency
        {
            get => tickFrequency;
            set
            {
                tickFrequency = value;
                ticksValid = false;
            }
        }

        public TickPlacement TickPlacement
        {
            get => tickPlacement;
            set
            {
                tickPlacement = value;
                ticksValid = false;
            }
        }

        /// <summary>Makes dragging, clicking and keys land on multiples of <see cref="TickFrequency"/> from <see cref="Minimum"/>.</summary>
        public bool IsSnapToTickEnabled { get; set; }

        /// <summary>How far the arrow keys move the value (1 by default).</summary>
        public double SmallChange { get; set; } = 1;

        /// <summary>How far Page Up and Page Down move the value (10 by default).</summary>
        public double LargeChange { get; set; } = 10;

        private bool vertical => orientation == Orientation.Vertical;

        private double snap(double value)
        {
            if (!IsSnapToTickEnabled || tickFrequency <= 0)
                return value;

            double snapped = Minimum + Math.Round((value - Minimum) / tickFrequency) * tickFrequency;
            return Math.Clamp(snapped, Minimum, Maximum);
        }

        private void setNumber(double value)
        {
            // The number follows Value even while disabled; disabling only stops user input.
            bool disabled = CurrentNumber.Disabled;

            syncing = true;
            CurrentNumber.Disabled = false;
            CurrentNumber.Value = value;
            CurrentNumber.Disabled = disabled;
            syncing = false;
        }

        protected override void UpdateValue(float value)
        {
            SelectionBox.ScaleTo(vertical ? new Vector2(1, value) : new Vector2(value, 1), 300, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            if (!ticksValid)
                updateTicks();
        }

        private void updateTicks()
        {
            ticksValid = true;
            ticks.Clear();

            double range = Maximum - Minimum;
            if (tickPlacement == TickPlacement.None || tickFrequency <= 0 || range <= 0 || range / tickFrequency > max_ticks)
                return;

            for (double v = Minimum + tickFrequency; v < Maximum - tickFrequency * 1e-6; v += tickFrequency)
            {
                float fraction = (float)((v - Minimum) / range);

                if (tickPlacement != TickPlacement.BottomRight)
                    ticks.Add(createTick(fraction, start: true));
                if (tickPlacement != TickPlacement.TopLeft)
                    ticks.Add(createTick(fraction, start: false));
            }
        }

        private Drawable createTick(float fraction, bool start)
        {
            return new Box
            {
                Anchor = vertical ? (start ? Anchor.BottomLeft : Anchor.BottomRight) : (start ? Anchor.TopLeft : Anchor.BottomLeft),
                Origin = vertical ? (start ? Anchor.CentreLeft : Anchor.CentreRight) : (start ? Anchor.TopCentre : Anchor.BottomCentre),
                RelativePositionAxes = vertical ? Axes.Y : Axes.X,
                RelativeSizeAxes = vertical ? Axes.X : Axes.Y,
                Size = vertical ? new Vector2(0.35f, 1) : new Vector2(1, 0.35f),
                X = vertical ? 0 : fraction,
                Y = vertical ? -fraction : 0,
                Colour = Theme.Text,
                Alpha = 0.35f,
            };
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (CurrentNumber.Disabled || (!IsHovered && !HasFocus))
                return false;

            double step = IsSnapToTickEnabled && tickFrequency > 0 ? tickFrequency : SmallChange;
            double value = CurrentNumber.Value;

            switch (e.Key)
            {
                case Key.Left:
                case Key.Down:
                    value -= step;
                    break;

                case Key.Right:
                case Key.Up:
                    value += step;
                    break;

                case Key.PageDown:
                    value -= LargeChange;
                    break;

                case Key.PageUp:
                    value += LargeChange;
                    break;

                case Key.Home:
                    value = Minimum;
                    break;

                case Key.End:
                    value = Maximum;
                    break;

                default:
                    return false;
            }

            changeValueByUser(value);
            return true;
        }

        /// <summary>Sets the value as user input does: clamped to the range and snapped to ticks when snapping is on.</summary>
        private void changeValueByUser(double value) => CurrentNumber.Value = Math.Clamp(value, Minimum, Maximum);

        // SliderBar only knows horizontal input, so a vertical slider handles the pointer itself.
        protected override bool OnClick(ClickEvent e)
        {
            if (!vertical)
                return base.OnClick(e);

            setFromPointer(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (!vertical)
                return base.OnDragStart(e);

            var delta = e.MouseDownPosition - e.MousePosition;

            // A sideways drag belongs to whatever scrolls sideways around the slider.
            if (CurrentNumber.Disabled || Math.Abs(delta.X) > Math.Abs(delta.Y))
                return false;

            GetContainingFocusManager()?.ChangeFocus(this);
            setFromPointer(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            if (vertical)
                setFromPointer(e.ScreenSpaceMousePosition);
            else
                base.OnDrag(e);
        }

        private void setFromPointer(Vector2 screenSpacePosition)
        {
            if (CurrentNumber.Disabled)
                return;

            float usable = DrawHeight - 2 * RangePadding;
            float y = ToLocalSpace(screenSpacePosition).Y;
            float fraction = usable > 0 ? 1 - (y - RangePadding) / usable : 0;

            changeValueByUser(Minimum + Math.Clamp(fraction, 0, 1) * (Maximum - Minimum));
        }
    }

    /// <summary>
    /// A bar filled to Value between Minimum and Maximum; changes animate. <see cref="IsIndeterminate"/> shows a segment
    /// sweeping along the track instead, for work of unknown length. <see cref="Orientation"/> makes it vertical, filling upwards.
    /// </summary>
    public partial class ProgressBar : CompositeDrawable
    {
        private const float thickness = 8;
        private const float segment_length = 0.3f;

        private readonly Box track;
        private readonly Box fill;
        private readonly Box segment;

        private double value;
        private double minimum;
        private double maximum = 100;
        private bool isIndeterminate;
        private Orientation orientation = Orientation.Horizontal;

        public ProgressBar()
        {
            Height = thickness;
            Masking = true;
            CornerRadius = 4;

            InternalChildren = new Drawable[]
            {
                track = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Surface },
                fill = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = Theme.Accent },
                segment = new Box { RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.Both, Width = segment_length, Colour = Theme.Accent, Alpha = 0 },
            };
        }

        public double Minimum
        {
            get => minimum;
            set
            {
                minimum = value;
                updateFill(false);
            }
        }

        public double Maximum
        {
            get => maximum;
            set
            {
                maximum = value;
                updateFill(false);
            }
        }

        public double Value
        {
            get => value;
            set
            {
                this.value = value;
                updateFill(IsLoaded);
            }
        }

        /// <summary>The filled fraction of the track, from 0 to 1.</summary>
        public float Fraction => maximum > minimum ? (float)Math.Clamp((value - minimum) / (maximum - minimum), 0, 1) : 0;

        /// <summary>Shows a looping sweep instead of the value.</summary>
        public bool IsIndeterminate
        {
            get => isIndeterminate;
            set
            {
                if (isIndeterminate == value)
                    return;

                isIndeterminate = value;

                if (IsLoaded)
                    updateIndeterminate();
            }
        }

        /// <summary>Horizontal (the default) or vertical, filling from the bottom.</summary>
        public Orientation Orientation
        {
            get => orientation;
            set
            {
                if (orientation == value)
                    return;

                orientation = value;

                if (value == Orientation.Vertical && Width == 0 && Height == thickness)
                {
                    Height = 0;
                    Width = thickness;
                }
                else if (value == Orientation.Horizontal && Height == 0 && Width == thickness)
                {
                    Width = 0;
                    Height = thickness;
                }

                fill.ClearTransforms();
                fill.Anchor = fill.Origin = value == Orientation.Vertical ? Anchor.BottomLeft : Anchor.TopLeft;
                fill.Size = Vector2.One;
                updateFill(false);

                if (IsLoaded)
                    updateIndeterminate();
            }
        }

        public Colour4 Foreground
        {
            get => fill.Colour;
            set => fill.Colour = segment.Colour = value;
        }

        public Colour4 Background
        {
            get => track.Colour;
            set => track.Colour = value;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateIndeterminate();
        }

        private void updateFill(bool animate)
        {
            double duration = animate ? 300 : 0;

            if (orientation == Orientation.Vertical)
                fill.ResizeHeightTo(Fraction, duration, Easing.OutQuint);
            else
                fill.ResizeWidthTo(Fraction, duration, Easing.OutQuint);
        }

        private void updateIndeterminate()
        {
            segment.ClearTransforms();

            if (!isIndeterminate)
            {
                segment.FadeOut(150, Easing.OutQuint);
                fill.FadeIn(150, Easing.OutQuint);
                return;
            }

            fill.FadeOut(150, Easing.OutQuint);
            segment.FadeIn(150, Easing.OutQuint);

            if (orientation == Orientation.Vertical)
            {
                segment.Size = new Vector2(1, segment_length);
                segment.X = 0;
                segment.MoveToY(1).MoveToY(-segment_length, 1400, Easing.InOutSine).Loop();
            }
            else
            {
                segment.Size = new Vector2(segment_length, 1);
                segment.Y = 0;
                segment.MoveToX(-segment_length).MoveToX(1, 1400, Easing.InOutSine).Loop();
            }
        }
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
