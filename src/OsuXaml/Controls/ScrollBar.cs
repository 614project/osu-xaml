using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>What kind of user action moved a <see cref="ScrollBar"/>, like WPF's ScrollEventType.</summary>
    public enum ScrollEventType
    {
        /// <summary>The user let go after paging, stepping or dragging.</summary>
        EndScroll,

        /// <summary>Moved to <see cref="ScrollBar.Minimum"/>.</summary>
        First,

        /// <summary>Paged back by clicking the track before the thumb.</summary>
        LargeDecrement,

        /// <summary>Paged forward by clicking the track after the thumb.</summary>
        LargeIncrement,

        /// <summary>Moved to <see cref="ScrollBar.Maximum"/>.</summary>
        Last,

        /// <summary>Stepped back with the arrow button or the mouse wheel.</summary>
        SmallDecrement,

        /// <summary>Stepped forward with the arrow button or the mouse wheel.</summary>
        SmallIncrement,

        /// <summary>The thumb was dropped at a new position.</summary>
        ThumbPosition,

        /// <summary>The thumb is being dragged.</summary>
        ThumbTrack,
    }

    /// <summary>Raised by <see cref="ScrollBar.Scroll"/> after a user action changed (or ended changing) the value.</summary>
    public class ScrollEventArgs
    {
        public ScrollEventArgs(ScrollEventType scrollEventType, double newValue)
        {
            ScrollEventType = scrollEventType;
            NewValue = newValue;
        }

        public ScrollEventType ScrollEventType { get; }

        public double NewValue { get; }
    }

    /// <summary>
    /// A standalone scroll bar, like WPF's ScrollBar. <see cref="Value"/> binds two-way and stays within <see cref="Minimum"/>
    /// and <see cref="Maximum"/>; <see cref="ViewportSize"/> (how much of the content is visible, in value units) sets the thumb's
    /// length. Dragging the <see cref="Thumb"/> scrolls, clicking the track pages by <see cref="LargeChange"/> (repeating while
    /// held), the arrow buttons and the mouse wheel step by <see cref="SmallChange"/>. Vertical by default.
    /// <see cref="ScrollViewer"/> draws its own bars; use this one to scroll something yourself.
    /// </summary>
    public partial class ScrollBar : CompositeDrawable, ILayoutElement
    {
        private const float default_thickness = 14;
        private const float min_thumb_length = 20;
        private const float thumb_inset = 3;
        private const double repeat_delay = 400;
        private const double repeat_interval = 50;

        private readonly Container track;
        private readonly Box trackFill;
        private readonly Thumb thumb;
        private readonly ArrowButton decreaseButton;
        private readonly ArrowButton increaseButton;

        private Orientation orientation = Orientation.Vertical;
        private double minimum;
        private double maximum = 100;
        private double viewportSize;
        private bool showArrows = true;
        private bool isEnabled = true;

        // WPF keeps the value it was asked for and re-coerces it when the range changes, so attribute order doesn't matter.
        private double requestedValue;
        private double reportedValue;
        private bool coercing;

        private ScheduledDelegate paging;
        private double valueAtThumbPress;

        public ScrollBar()
        {
            Width = default_thickness;

            InternalChildren = new Drawable[]
            {
                track = new TrackArea(this)
                {
                    Masking = true,
                    CornerRadius = default_thickness / 2,
                    Child = trackFill = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Mantle },
                },
                thumb = new Thumb { CornerRadius = (default_thickness - 2 * thumb_inset) / 2 },
                decreaseButton = new ArrowButton(this, -1),
                increaseButton = new ArrowButton(this, 1),
            };

            thumb.DragStarted += _ => valueAtThumbPress = Value.Value;
            thumb.DragDelta += onThumbDelta;
            thumb.DragCompleted += e =>
            {
                if (e.Canceled)
                {
                    setValueByUser(valueAtThumbPress, ScrollEventType.ThumbPosition);
                    return;
                }

                raiseScroll(ScrollEventType.ThumbPosition);
                raiseScroll(ScrollEventType.EndScroll);
            };

            Value.ValueChanged += e =>
            {
                // Coercion sets the value again from inside this handler; the outer call finishes the change.
                if (coercing)
                    return;

                requestedValue = e.NewValue;
                coerceValue();
            };

            applyOrientation();
        }

        /// <summary>The scroll position. Binds two-way: <c>Value="{Bind Offset}"</c>.</summary>
        public Bindable<double> Value { get; } = new Bindable<double>();

        /// <summary>Raised when <see cref="Value"/> changes, with the new value.</summary>
        public event Action<double> ValueChanged;

        /// <summary>Raised after the user scrolls, with what they did.</summary>
        public event Action<ScrollEventArgs> Scroll;

        public double Minimum
        {
            get => minimum;
            set
            {
                minimum = value;
                coerceValue();
            }
        }

        public double Maximum
        {
            get => maximum;
            set
            {
                maximum = value;
                coerceValue();
            }
        }

        /// <summary>How much of the scrolled content is visible, in the same units as the value. Sets the thumb length; 0 gives a small fixed thumb.</summary>
        public double ViewportSize
        {
            get => viewportSize;
            set
            {
                viewportSize = value;
                layoutParts();
            }
        }

        /// <summary>How far the arrow buttons and one wheel notch move the value (1 by default).</summary>
        public double SmallChange { get; set; } = 1;

        /// <summary>How far a click on the track moves the value (10 by default).</summary>
        public double LargeChange { get; set; } = 10;

        /// <summary>Vertical (the default) or horizontal.</summary>
        public Orientation Orientation
        {
            get => orientation;
            set
            {
                if (orientation == value)
                    return;

                orientation = value;

                // Move the default thickness to the new cross axis, unless a size was set.
                if (value == Orientation.Horizontal && Height == 0 && Width == default_thickness)
                {
                    Width = 0;
                    Height = default_thickness;
                }
                else if (value == Orientation.Vertical && Width == 0 && Height == default_thickness)
                {
                    Height = 0;
                    Width = default_thickness;
                }

                applyOrientation();
                Layout.InvalidateMeasure(this);
            }
        }

        /// <summary>Whether the step buttons show at the ends (true by default).</summary>
        public bool ShowArrows
        {
            get => showArrows;
            set
            {
                if (!Layout.SetAffectsMeasure(this, ref showArrows, value))
                    return;

                decreaseButton.Alpha = increaseButton.Alpha = value ? 1 : 0;
                layoutParts();
            }
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                thumb.IsEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);

                if (!value)
                    stopPaging();
            }
        }

        /// <summary>The track colour.</summary>
        public Colour4 Background
        {
            get => trackFill.Colour;
            set => trackFill.Colour = value;
        }

        /// <summary>The thumb colour.</summary>
        public Colour4 Foreground
        {
            get => thumb.Background;
            set => thumb.Background = value;
        }

        /// <summary>The draggable part, for styling.</summary>
        public Thumb Thumb => thumb;

        public override bool PropagatePositionalInputSubTree => isEnabled && base.PropagatePositionalInputSubTree;

        /// <summary>Moves back by <see cref="SmallChange"/>, as the up/left arrow does.</summary>
        public void LineDecrease() => setValueByUser(Value.Value - SmallChange, ScrollEventType.SmallDecrement);

        public void LineIncrease() => setValueByUser(Value.Value + SmallChange, ScrollEventType.SmallIncrement);

        /// <summary>Moves back by <see cref="LargeChange"/>, as a click on the track before the thumb does.</summary>
        public void PageDecrease() => setValueByUser(Value.Value - LargeChange, ScrollEventType.LargeDecrement);

        public void PageIncrease() => setValueByUser(Value.Value + LargeChange, ScrollEventType.LargeIncrement);

        public void ScrollToMinimum() => setValueByUser(minimum, ScrollEventType.First);

        public void ScrollToMaximum() => setValueByUser(maximum, ScrollEventType.Last);

        private bool vertical => orientation == Orientation.Vertical;

        private double range => maximum - minimum;

        private float length => vertical ? DrawHeight : DrawWidth;

        private float thickness => vertical ? DrawWidth : DrawHeight;

        private float arrowLength => showArrows ? Math.Min(thickness, length / 2) : 0;

        private float trackLength => Math.Max(0, length - 2 * arrowLength);

        private float thumbLength
        {
            get
            {
                float available = trackLength;

                if (viewportSize > 0 && range > 0)
                    return Math.Clamp(available * (float)(viewportSize / (range + viewportSize)), Math.Min(min_thumb_length, available), available);

                return Math.Min(min_thumb_length, available);
            }
        }

        /// <summary>How much the value changes per pixel the thumb moves along the track.</summary>
        private double valuePerPixel
        {
            get
            {
                float travel = trackLength - thumbLength;
                return travel > 0 ? range / travel : 0;
            }
        }

        public bool ReportsMeasureChanges => true;

        public Vector2 Measure(Vector2 availableSize)
        {
            float along = 2 * (showArrows ? default_thickness : 0) + min_thumb_length;
            return vertical ? new Vector2(default_thickness, along) : new Vector2(along, default_thickness);
        }

        private double coerce(double value) => range <= 0 ? minimum : Math.Clamp(value, minimum, maximum);

        private void coerceValue()
        {
            double coerced = coerce(requestedValue);

            if (Value.Value != coerced && !Value.Disabled)
            {
                coercing = true;
                Value.Value = coerced;
                coercing = false;
            }

            layoutParts();

            if (Value.Value != reportedValue)
            {
                reportedValue = Value.Value;
                ValueChanged?.Invoke(reportedValue);
            }
        }

        private void setValueByUser(double value, ScrollEventType type)
        {
            if (Value.Disabled || !isEnabled)
                return;

            Value.Value = coerce(value);
            raiseScroll(type);
        }

        private void raiseScroll(ScrollEventType type) => Scroll?.Invoke(new ScrollEventArgs(type, Value.Value));

        private void onThumbDelta(DragDeltaEventArgs e)
        {
            double change = (vertical ? e.VerticalChange : e.HorizontalChange) * valuePerPixel;

            // The thumb moves at once, so the next delta (measured in the thumb's own space) is the movement since this one.
            if (change != 0)
                setValueByUser(Value.Value + change, ScrollEventType.ThumbTrack);
        }

        private void applyOrientation()
        {
            decreaseButton.Icon = vertical ? FontAwesome.Solid.CaretUp : FontAwesome.Solid.CaretLeft;
            increaseButton.Icon = vertical ? FontAwesome.Solid.CaretDown : FontAwesome.Solid.CaretRight;
            layoutParts();
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            layoutParts();
        }

        /// <summary>Places the arrows, track and thumb; called on every change so the thumb is where the value says straight away.</summary>
        private void layoutParts()
        {
            float arrows = arrowLength;
            float cross = thickness;
            float trackSize = trackLength;
            float thumbSize = thumbLength;
            double fraction = range > 0 ? (Value.Value - minimum) / range : 0;
            float thumbStart = arrows + (float)fraction * Math.Max(0, trackSize - thumbSize);

            // Nothing to scroll: no thumb, like WPF.
            thumb.Alpha = range > 0 && thumbSize > 0 ? 1 : 0;

            if (vertical)
            {
                decreaseButton.Size = increaseButton.Size = new Vector2(cross, arrows);
                decreaseButton.Position = Vector2.Zero;
                increaseButton.Position = new Vector2(0, arrows + trackSize);
                track.Position = new Vector2(0, arrows);
                track.Size = new Vector2(cross, trackSize);
                thumb.Position = new Vector2(thumb_inset, thumbStart);
                thumb.Size = new Vector2(Math.Max(0, cross - 2 * thumb_inset), thumbSize);
            }
            else
            {
                decreaseButton.Size = increaseButton.Size = new Vector2(arrows, cross);
                decreaseButton.Position = Vector2.Zero;
                increaseButton.Position = new Vector2(arrows + trackSize, 0);
                track.Position = new Vector2(arrows, 0);
                track.Size = new Vector2(trackSize, cross);
                thumb.Position = new Vector2(thumbStart, thumb_inset);
                thumb.Size = new Vector2(thumbSize, Math.Max(0, cross - 2 * thumb_inset));
            }

            track.CornerRadius = cross / 2;
            thumb.CornerRadius = Math.Max(0, cross - 2 * thumb_inset) / 2;
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            if (!isEnabled || range <= 0)
                return false;

            // Wheel down (a negative delta) scrolls towards the maximum, three steps a notch like WPF's ScrollViewer.
            float notches = Math.Abs(e.ScrollDelta.Y) >= Math.Abs(e.ScrollDelta.X) ? e.ScrollDelta.Y : -e.ScrollDelta.X;
            double target = Value.Value - notches * SmallChange * 3;
            setValueByUser(target, notches > 0 ? ScrollEventType.SmallDecrement : ScrollEventType.SmallIncrement);
            return true;
        }

        /// <summary>Pages towards the pointer now and, while the track stays pressed, again until the thumb reaches the pointer.</summary>
        private void startPaging(Func<Vector2> pointer)
        {
            stopPaging();

            if (!pageTowards(pointer()))
                return;

            paging = Scheduler.AddDelayed(() =>
            {
                paging = Scheduler.AddDelayed(() =>
                {
                    if (!pageTowards(pointer()))
                        stopPaging();
                }, repeat_interval, true);
            }, repeat_delay);
        }

        /// <returns>Whether the pointer was beyond the thumb, so the value moved.</returns>
        private bool pageTowards(Vector2 screenSpacePointer)
        {
            var local = ToLocalSpace(screenSpacePointer);
            float position = vertical ? local.Y : local.X;
            float thumbStart = vertical ? thumb.Y : thumb.X;

            if (position < thumbStart)
                PageDecrease();
            else if (position > thumbStart + thumbLength)
                PageIncrease();
            else
                return false;

            return true;
        }

        private void stopPaging()
        {
            if (paging == null)
                return;

            paging.Cancel();
            paging = null;
            raiseScroll(ScrollEventType.EndScroll);
        }

        /// <summary>The groove behind the thumb; pressing it pages.</summary>
        private partial class TrackArea : Container
        {
            private readonly ScrollBar owner;

            public TrackArea(ScrollBar owner)
            {
                this.owner = owner;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button != MouseButton.Left || owner.range <= 0)
                    return false;

                owner.startPaging(() => GetContainingInputManager()?.CurrentState.Mouse.Position ?? e.ScreenSpaceMousePosition);
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                if (e.Button == MouseButton.Left)
                    owner.stopPaging();

                base.OnMouseUp(e);
            }

            // Holding the track inside a scroll view must not start a drag-scroll there.
            protected override bool OnDragStart(DragStartEvent e) => true;
        }

        /// <summary>A step button at one end; it steps when pressed and keeps stepping while held over.</summary>
        private partial class ArrowButton : CompositeDrawable
        {
            private readonly ScrollBar owner;
            private readonly int direction;
            private readonly Box highlight;
            private readonly SpriteIcon icon;
            private ScheduledDelegate repeat;
            private bool hovered;

            public ArrowButton(ScrollBar owner, int direction)
            {
                this.owner = owner;
                this.direction = direction;

                Masking = true;
                CornerRadius = 3;

                InternalChildren = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(8),
                        Colour = Theme.SubtleText,
                    },
                };
            }

            public IconUsage Icon
            {
                set => icon.Icon = value;
            }

            protected override bool OnHover(HoverEvent e)
            {
                hovered = true;
                updateState();
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hovered = false;
                updateState();
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button != MouseButton.Left)
                    return false;

                step();

                repeat?.Cancel();
                repeat = Scheduler.AddDelayed(() =>
                {
                    repeat = Scheduler.AddDelayed(() =>
                    {
                        if (pointerIsOver())
                            step();
                    }, repeat_interval, true);
                }, repeat_delay);

                highlight.FadeTo(0.2f, 60, Easing.OutQuint);
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                if (e.Button == MouseButton.Left && repeat != null)
                {
                    repeat.Cancel();
                    repeat = null;
                    owner.raiseScroll(ScrollEventType.EndScroll);
                }

                updateState();
                base.OnMouseUp(e);
            }

            protected override bool OnDragStart(DragStartEvent e) => true;

            private void step()
            {
                if (direction < 0)
                    owner.LineDecrease();
                else
                    owner.LineIncrease();
            }

            // Hover doesn't change while a button is held, so test the position itself.
            private bool pointerIsOver()
            {
                var mouse = GetContainingInputManager()?.CurrentState.Mouse;
                return mouse != null && ReceivePositionalInputAt(mouse.Position);
            }

            private void updateState()
            {
                highlight.FadeTo(hovered ? 0.08f : 0, 150, Easing.OutQuint);
                icon.FadeColour(hovered ? Theme.Text : Theme.SubtleText, 150, Easing.OutQuint);
            }
        }
    }
}
