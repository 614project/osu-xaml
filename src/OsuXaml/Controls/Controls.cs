using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Text;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuXaml.Controls
{
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
    /// It draws a thin rounded track, filled in the accent colour up to a round thumb that grows while hovered or dragged;
    /// grabbing the thumb drags it from where it was grabbed, clicking the track jumps there.
    /// </summary>
    public partial class Slider : BasicSliderBar<double>
    {
        // The cross size of the row the thumb moves in; tick rows add to it.
        private const float thumb_row = 20;
        private const float tick_row = 7;
        private const float tick_length = 4;
        private const float track_thickness = 4;
        private const float thumb_size = 18;
        private const int max_ticks = 200;

        private readonly Container body;
        private readonly Container track;
        private readonly Container travel;
        private readonly Container ticks;
        private readonly SliderThumb thumb;

        private Orientation orientation = Orientation.Horizontal;
        private double tickFrequency = 1;
        private TickPlacement tickPlacement;
        private bool ticksValid;
        private bool syncing;

        // The cross size the slider gave itself; it follows orientation and tick changes until a size is set.
        private float defaultThickness = thumb_row;

        private bool pressed;
        private bool pressedOnThumb;
        private float valueAtPress;
        private float pointerAtPress;

        public Slider()
        {
            Height = thumb_row;
            BackgroundColour = Theme.Surface;
            SelectionColour = Theme.Accent;
            FocusColour = Theme.Overlay;
            Masking = false;
            RangePadding = thumb_size / 2;
            CurrentNumber.MinValue = 0;
            CurrentNumber.MaxValue = 100;

            // The base class adds the track and fill boxes as direct children; they move into the thin rounded track.
            Remove(Box, false);
            Remove(SelectionBox, false);

            Add(body = new Container
            {
                Children = new Drawable[]
                {
                    track = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        CornerRadius = track_thickness / 2,
                        Children = new Drawable[] { Box, SelectionBox },
                    },
                    travel = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            ticks = new Container { RelativeSizeAxes = Axes.Both },
                            thumb = new SliderThumb(thumb_size),
                        },
                    },
                },
            });

            applyOrientation();

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

            Value.DisabledChanged += disabled =>
            {
                CurrentNumber.Disabled = disabled;
                this.FadeTo(disabled ? 0.5f : 1, 150, Easing.OutQuint);
            };

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
                if (value == Orientation.Vertical && Width == 0 && Height == defaultThickness)
                {
                    Height = 0;
                    Width = defaultThickness;
                }
                else if (value == Orientation.Horizontal && Height == 0 && Width == defaultThickness)
                {
                    Width = 0;
                    Height = defaultThickness;
                }

                applyOrientation();
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

        /// <summary>Where tick marks are drawn. Ticks get rows of their own beside the thumb, so the slider grows to make room unless it has a size.</summary>
        public TickPlacement TickPlacement
        {
            get => tickPlacement;
            set
            {
                tickPlacement = value;
                ticksValid = false;
                applyThickness();
                applyOrientation();
            }
        }

        /// <summary>Makes dragging, clicking and keys land on multiples of <see cref="TickFrequency"/> from <see cref="Minimum"/>.</summary>
        public bool IsSnapToTickEnabled { get; set; }

        /// <summary>How far the arrow keys move the value (1 by default).</summary>
        public double SmallChange { get; set; } = 1;

        /// <summary>How far Page Up and Page Down move the value (10 by default).</summary>
        public double LargeChange { get; set; } = 10;

        private bool vertical => orientation == Orientation.Vertical;

        private bool ticksBefore => tickPlacement == TickPlacement.TopLeft || tickPlacement == TickPlacement.Both;

        private bool ticksAfter => tickPlacement == TickPlacement.BottomRight || tickPlacement == TickPlacement.Both;

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

        /// <summary>Grows or shrinks the default cross size for the tick rows, while the slider has no size of its own.</summary>
        private void applyThickness()
        {
            float thickness = thumb_row + (ticksBefore ? tick_row : 0) + (ticksAfter ? tick_row : 0);

            if (vertical && Width == defaultThickness)
                Width = thickness;
            else if (!vertical && Height == defaultThickness)
                Height = thickness;

            defaultThickness = thickness;
        }

        private void applyOrientation()
        {
            // The thumb row sits between the tick rows; with a size larger than needed, the whole lot is centred.
            float shift = ((ticksBefore ? tick_row : 0) - (ticksAfter ? tick_row : 0)) / 2;

            thumb.ClearTransforms();

            if (vertical)
            {
                body.RelativeSizeAxes = Axes.Y;
                body.Size = new Vector2(thumb_row, 1);
                body.Anchor = body.Origin = Anchor.TopCentre;
                body.Position = new Vector2(shift, 0);

                track.RelativeSizeAxes = Axes.Y;
                track.Size = new Vector2(track_thickness, 1);

                SelectionBox.RelativeSizeAxes = Axes.X;
                SelectionBox.Anchor = SelectionBox.Origin = Anchor.BottomCentre;
                SelectionBox.Size = new Vector2(1, 0);

                travel.Padding = new MarginPadding { Vertical = RangePadding };

                thumb.Anchor = Anchor.BottomCentre;
                thumb.RelativePositionAxes = Axes.Y;
                thumb.Position = new Vector2(0, IsLoaded ? -NormalizedValue : 0);
            }
            else
            {
                body.RelativeSizeAxes = Axes.X;
                body.Size = new Vector2(1, thumb_row);
                body.Anchor = body.Origin = Anchor.CentreLeft;
                body.Position = new Vector2(0, shift);

                track.RelativeSizeAxes = Axes.X;
                track.Size = new Vector2(1, track_thickness);

                SelectionBox.RelativeSizeAxes = Axes.Y;
                SelectionBox.Anchor = SelectionBox.Origin = Anchor.CentreLeft;
                SelectionBox.Size = new Vector2(0, 1);

                travel.Padding = new MarginPadding { Horizontal = RangePadding };

                thumb.Anchor = Anchor.CentreLeft;
                thumb.RelativePositionAxes = Axes.X;
                thumb.Position = new Vector2(IsLoaded ? NormalizedValue : 0, 0);
            }

            SelectionBox.Scale = Vector2.One;
        }

        protected override void UpdateValue(float value)
        {
            // The thumb follows the pointer exactly while dragged; keys, clicks and bindings glide it there.
            double duration = IsDragged ? 0 : 200;

            if (vertical)
                thumb.MoveToY(-value, duration, Easing.OutQuint);
            else
                thumb.MoveToX(value, duration, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            if (!ticksValid)
                updateTicks();

            // The fill ends under the thumb's centre, wherever its animation has got to.
            if (vertical)
                SelectionBox.Height = RangePadding - thumb.Y * travel.ChildSize.Y;
            else
                SelectionBox.Width = RangePadding + thumb.X * travel.ChildSize.X;

            thumb.SetState(IsDragged || pressed ? SliderThumb.State.Pressed : IsHovered ? SliderThumb.State.Hovered : SliderThumb.State.Idle);
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

                if (ticksBefore)
                    ticks.Add(createTick(fraction, before: true));
                if (ticksAfter)
                    ticks.Add(createTick(fraction, before: false));
            }
        }

        /// <summary>A short line just outside the thumb row, so the thumb never covers it.</summary>
        private Drawable createTick(float fraction, bool before)
        {
            const float gap = 1;

            return new Box
            {
                Anchor = vertical ? (before ? Anchor.BottomLeft : Anchor.BottomRight) : (before ? Anchor.TopLeft : Anchor.BottomLeft),
                Origin = vertical ? (before ? Anchor.CentreRight : Anchor.CentreLeft) : (before ? Anchor.BottomCentre : Anchor.TopCentre),
                RelativePositionAxes = vertical ? Axes.Y : Axes.X,
                Size = vertical ? new Vector2(tick_length, 1) : new Vector2(1, tick_length),
                X = vertical ? (before ? -gap : gap) : fraction,
                Y = vertical ? -fraction : (before ? -gap : gap),
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

        // The base class outlines a focused slider, which needs masking; the thumb row must not be clipped, so no outline.
        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            BorderThickness = 0;
        }

        private bool isOnThumb(Vector2 screenSpacePosition)
        {
            var local = thumb.ToLocalSpace(screenSpacePosition);
            return Vector2.Distance(local, thumb.DrawSize / 2) <= thumb_size / 2 + 2;
        }

        // Grabbing the thumb drags it relative to where it was grabbed instead of jumping to the pointer.
        protected override bool ShouldHandleAsRelativeDrag(MouseDownEvent e) => isOnThumb(e.ScreenSpaceMousePosition);

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Left && !CurrentNumber.Disabled)
            {
                pressed = true;
                pressedOnThumb = isOnThumb(e.ScreenSpaceMousePosition);
                valueAtPress = NormalizedValue;
                pointerAtPress = ToLocalSpace(e.ScreenSpaceMousePosition).Y;
            }

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left)
                pressed = false;

            base.OnMouseUp(e);
        }

        // SliderBar only knows horizontal input, so a vertical slider handles the pointer itself.
        protected override bool OnClick(ClickEvent e)
        {
            if (!vertical)
                return base.OnClick(e);

            if (!pressedOnThumb)
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
            float fraction;

            if (usable <= 0)
                fraction = 0;
            else if (pressedOnThumb)
                fraction = valueAtPress + (pointerAtPress - y) / usable;
            else
                fraction = 1 - (y - RangePadding) / usable;

            changeValueByUser(Minimum + Math.Clamp(fraction, 0, 1) * (Maximum - Minimum));
        }

        /// <summary>The round handle: a ring with an accent dot that grows while hovered and grows further while dragged.</summary>
        private partial class SliderThumb : CompositeDrawable
        {
            public enum State
            {
                Idle,
                Hovered,
                Pressed,
            }

            private readonly Circle ring;
            private readonly Circle dot;
            private State state = State.Idle;

            public SliderThumb(float size)
            {
                Size = new Vector2(size);
                Origin = Anchor.Centre;

                InternalChildren = new Drawable[]
                {
                    ring = new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Theme.Border,
                        EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                        {
                            Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                            Colour = Colour4.Black.Opacity(0.25f),
                            Radius = 4,
                            Offset = new Vector2(0, 1),
                        },
                    },
                    dot = new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Theme.Accent,
                        Scale = new Vector2(0.55f),
                    },
                };
            }

            public void SetState(State newState)
            {
                if (state == newState)
                    return;

                state = newState;

                float scale = newState == State.Pressed ? 0.8f : newState == State.Hovered ? 0.7f : 0.55f;
                dot.ScaleTo(scale, 150, Easing.OutQuint);
                ring.FadeColour(newState == State.Idle ? Theme.Border : Theme.Overlay, 150, Easing.OutQuint);
            }
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
