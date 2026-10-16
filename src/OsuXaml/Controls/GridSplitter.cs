using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>Whether a <see cref="GridSplitter"/> resizes columns or rows.</summary>
    public enum GridResizeDirection
    {
        /// <summary>
        /// Columns when the splitter has a horizontal alignment other than Stretch, rows when it has a vertical one,
        /// otherwise columns when it is taller than it is wide.
        /// </summary>
        Auto,
        Columns,
        Rows,
    }

    /// <summary>Which two columns (or rows) a <see cref="GridSplitter"/> resizes.</summary>
    public enum GridResizeBehavior
    {
        /// <summary>
        /// From the alignment: Left/Top resizes the previous and current track, Right/Bottom the current and next,
        /// Center/Stretch the previous and next (the splitter sits in a track of its own).
        /// </summary>
        BasedOnAlignment,
        CurrentAndNext,
        PreviousAndCurrent,
        PreviousAndNext,
    }

    /// <summary>
    /// A bar that resizes the grid columns (or rows) on either side of it while it is dragged, like WPF's GridSplitter.
    /// The usual layout puts it in a column of its own: <c>&lt;GridSplitter Grid.Column="1" Width="5" /&gt;</c> in
    /// <c>ColumnDefinitions="*,Auto,2*"</c> resizes columns 0 and 2; <c>Height="5"</c> makes a bar that resizes rows.
    /// Two star tracks stay proportional (every star track takes its current size as its weight); otherwise the
    /// non-star track gets a pixel size and the star track takes up the difference. Min/Max limits of the tracks hold.
    /// Arrow keys move a focused (clicked) or hovered splitter by <see cref="KeyboardIncrement"/>, and Escape cancels a drag.
    /// </summary>
    /// <remarks>
    /// Unlike WPF, whose GridSplitter defaults to HorizontalAlignment Right, the default alignment is Stretch,
    /// so a splitter in a column of its own resizes its neighbours without further settings.
    /// </remarks>
    public partial class GridSplitter : CompositeDrawable
    {
        private const float default_thickness = 5;

        private readonly Box background;
        private readonly Box preview;
        private Colour4 backgroundColour = Theme.Border;
        private GridResizeDirection resizeDirection;
        private bool sizeIsDefault = true;
        private bool isEnabled = true;

        // The resize in progress: the grid, the two tracks and their sizes when it began.
        private IReadOnlyList<DefinitionBase> definitions;
        private bool resizingColumns;
        private DefinitionBase first;
        private DefinitionBase second;
        private float firstStart;
        private float secondStart;
        private GridLength[] originalLengths;
        private float pendingOffset;

        public GridSplitter()
        {
            base.Width = default_thickness;

            InternalChildren = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = backgroundColour },
                preview = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Accent, Alpha = 0 },
            };
        }

        /// <summary>Raised when a drag (or keyboard resize) starts.</summary>
        public event Action DragStarted;

        /// <summary>Raised while resizing, with the offset in pixels from where the resize started.</summary>
        public event Action<float> DragDelta;

        /// <summary>Raised when a resize ends or is cancelled.</summary>
        public event Action DragCompleted;

        /// <summary>Columns, rows, or <see cref="GridResizeDirection.Auto"/> (the default) to decide from alignment and shape.</summary>
        public GridResizeDirection ResizeDirection
        {
            get => resizeDirection;
            set
            {
                resizeDirection = value;

                // The default thickness goes on the axis the splitter resizes, unless a size was set.
                if (value == GridResizeDirection.Rows && sizeIsDefault)
                {
                    base.Width = 0;
                    base.Height = default_thickness;
                }
                else if (value == GridResizeDirection.Columns && sizeIsDefault)
                {
                    base.Height = 0;
                    base.Width = default_thickness;
                }
            }
        }

        public GridResizeBehavior ResizeBehavior { get; set; }

        public Colour4 Background
        {
            get => backgroundColour;
            set
            {
                backgroundColour = value;
                updateColour(false);
            }
        }

        /// <summary>Drags move in steps of this many pixels (1 by default).</summary>
        public float DragIncrement { get; set; } = 1;

        /// <summary>
        /// When true, dragging moves a preview of the splitter and the tracks resize only when the drag ends
        /// (Escape cancels it). Useful when the content is expensive to lay out.
        /// </summary>
        public bool ShowsPreview { get; set; }

        /// <summary>How far one arrow key press moves the splitter (10 pixels by default).</summary>
        public float KeyboardIncrement { get; set; } = 10;

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);
            }
        }

        /// <summary>Whether a resize is in progress.</summary>
        public bool IsDragging => first != null;

        public override float Width
        {
            get => base.Width;
            set
            {
                // A slot stretching the splitter sets a relative size, which isn't a size the user asked for.
                if (!RelativeSizeAxes.HasFlag(Axes.X))
                    sizeIsDefault = false;

                base.Width = value;
            }
        }

        public override float Height
        {
            get => base.Height;
            set
            {
                // Height="5" without a width asks for a horizontal bar, so the default width goes.
                if (sizeIsDefault && value > 0 && !RelativeSizeAxes.HasFlag(Axes.Y))
                {
                    sizeIsDefault = false;

                    if (!RelativeSizeAxes.HasFlag(Axes.X))
                        base.Width = 0;
                }

                base.Height = value;
            }
        }

        private Grid grid => (Parent as Slot)?.Parent as Grid;

        /// <summary>The direction this splitter resizes in, with <see cref="GridResizeDirection.Auto"/> resolved like WPF.</summary>
        public GridResizeDirection EffectiveResizeDirection
        {
            get
            {
                if (resizeDirection != GridResizeDirection.Auto)
                    return resizeDirection;

                if (Layout.GetHorizontalAlignment(this) != HorizontalAlignment.Stretch)
                    return GridResizeDirection.Columns;

                if (Layout.GetVerticalAlignment(this) != VerticalAlignment.Stretch)
                    return GridResizeDirection.Rows;

                return DrawWidth <= DrawHeight ? GridResizeDirection.Columns : GridResizeDirection.Rows;
            }
        }

        private GridResizeBehavior effectiveBehavior(bool columns)
        {
            if (ResizeBehavior != GridResizeBehavior.BasedOnAlignment)
                return ResizeBehavior;

            if (columns)
            {
                switch (Layout.GetHorizontalAlignment(this))
                {
                    case HorizontalAlignment.Left: return GridResizeBehavior.PreviousAndCurrent;
                    case HorizontalAlignment.Right: return GridResizeBehavior.CurrentAndNext;
                    default: return GridResizeBehavior.PreviousAndNext;
                }
            }

            switch (Layout.GetVerticalAlignment(this))
            {
                case VerticalAlignment.Top: return GridResizeBehavior.PreviousAndCurrent;
                case VerticalAlignment.Bottom: return GridResizeBehavior.CurrentAndNext;
                default: return GridResizeBehavior.PreviousAndNext;
            }
        }

        /// <summary>
        /// Starts resizing the two tracks next to the splitter, as a drag does. Returns false when there is nothing to
        /// resize: the splitter isn't in a <see cref="Grid"/>, is disabled, or the tracks it would resize don't exist.
        /// </summary>
        public bool BeginResize()
        {
            var owner = grid;

            if (owner == null || !isEnabled || IsDragging)
                return false;

            bool columns = EffectiveResizeDirection == GridResizeDirection.Columns;
            var defs = columns ? owner.EffectiveColumns : owner.EffectiveRows;
            int index = Math.Clamp(columns ? Grid.GetColumn(this) : Grid.GetRow(this), 0, defs.Count - 1);

            var (a, b) = effectiveBehavior(columns) switch
            {
                GridResizeBehavior.CurrentAndNext => (index, index + 1),
                GridResizeBehavior.PreviousAndCurrent => (index - 1, index),
                _ => (index - 1, index + 1),
            };

            if (a < 0 || b >= defs.Count)
                return false;

            definitions = defs;
            resizingColumns = columns;
            first = defs[a];
            second = defs[b];
            firstStart = first.ActualLength;
            secondStart = second.ActualLength;
            originalLengths = defs.Select(d => d.Length).ToArray();

            updateColour(true);
            DragStarted?.Invoke();
            return true;
        }

        /// <summary>
        /// Moves the splitter <paramref name="offset"/> pixels (right or down when positive) from where the resize began,
        /// within both tracks' limits. The grid applies the new lengths on its next layout pass.
        /// </summary>
        public void ResizeTo(float offset)
        {
            if (!IsDragging)
                return;

            if (DragIncrement > 0)
                offset = MathF.Round(offset / DragIncrement) * DragIncrement;

            // Growing the first track shrinks the second, so each limit of one is a limit on the offset.
            float lowest = Math.Max(Math.Max(first.MinLength, 0) - firstStart, secondStart - second.MaxLength);
            float highest = Math.Min(first.MaxLength - firstStart, secondStart - Math.Max(second.MinLength, 0));

            if (lowest > highest)
                return;

            offset = Math.Clamp(offset, lowest, highest);

            if (ShowsPreview)
            {
                pendingOffset = offset;
                preview.Position = resizingColumns ? new Vector2(offset, 0) : new Vector2(0, offset);
                preview.Alpha = 0.6f;
            }
            else
                apply(firstStart + offset, secondStart - offset);

            DragDelta?.Invoke(offset);
        }

        /// <summary>Finishes the resize, keeping the new lengths.</summary>
        public void EndResize()
        {
            if (!IsDragging)
                return;

            if (ShowsPreview && pendingOffset != 0)
                apply(firstStart + pendingOffset, secondStart - pendingOffset);

            clearResize();
            DragCompleted?.Invoke();
        }

        /// <summary>Ends the resize and puts every track back to the length it had before it started.</summary>
        public void CancelResize()
        {
            if (!IsDragging)
                return;

            for (int i = 0; i < definitions.Count && i < originalLengths.Length; i++)
                definitions[i].Length = originalLengths[i];

            clearResize();
            DragCompleted?.Invoke();
        }

        private void clearResize()
        {
            definitions = null;
            first = second = null;
            originalLengths = null;
            pendingOffset = 0;
            preview.FadeOut(100, Easing.OutQuint);
            updateColour(true);
        }

        private void apply(float firstLength, float secondLength)
        {
            if (first.Length.IsStar && second.Length.IsStar)
            {
                // Like WPF, star tracks take their current pixel sizes as weights, so only the two neighbours change size.
                foreach (var d in definitions)
                {
                    if (d.Length.IsStar && d != first && d != second)
                        d.Length = new GridLength(d.ActualLength, GridUnitType.Star);
                }

                first.Length = new GridLength(firstLength, GridUnitType.Star);
                second.Length = new GridLength(secondLength, GridUnitType.Star);
            }
            else if (!first.Length.IsStar)
                first.Length = new GridLength(firstLength);
            else
                second.Length = new GridLength(secondLength);
        }

        private void updateColour(bool animate)
        {
            var target = IsDragging ? Theme.Accent : (IsHovered || HasFocus) && isEnabled ? Theme.Overlay : backgroundColour;
            background.FadeColour(target, animate ? 100 : 0, Easing.OutQuint);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateColour(true);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => updateColour(true);

        // Clicking or dragging the splitter focuses it, so the arrow keys keep working after it moves away from the pointer.
        public override bool AcceptsFocus => isEnabled;

        protected override void OnFocus(FocusEvent e) => updateColour(true);

        protected override void OnFocusLost(FocusLostEvent e) => updateColour(true);

        // Handling the click is what moves focus here (osu!framework focuses the drawable that took the click).
        protected override bool OnClick(ClickEvent e) => isEnabled;

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (!BeginResize())
                return false;

            // A drag blocks the click, so it focuses the splitter itself.
            GetContainingFocusManager()?.ChangeFocus(this);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            var owner = grid;
            if (owner == null || !IsDragging)
                return;

            // The grid stays put while its tracks resize, so the drag is measured in its space.
            var delta = owner.ToLocalSpace(e.ScreenSpaceMousePosition) - owner.ToLocalSpace(e.ScreenSpaceMouseDownPosition);
            ResizeTo(resizingColumns ? delta.X : delta.Y);
        }

        protected override void OnDragEnd(DragEndEvent e) => EndResize();

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape && IsDragging)
            {
                CancelResize();
                return true;
            }

            if ((!IsHovered && !HasFocus) || IsDragging)
                return false;

            bool columns = EffectiveResizeDirection == GridResizeDirection.Columns;
            float step;

            switch (e.Key)
            {
                case Key.Left when columns:
                case Key.Up when !columns:
                    step = -KeyboardIncrement;
                    break;

                case Key.Right when columns:
                case Key.Down when !columns:
                    step = KeyboardIncrement;
                    break;

                default:
                    return false;
            }

            if (!BeginResize())
                return false;

            ResizeTo(step);
            EndResize();
            return true;
        }
    }
}
