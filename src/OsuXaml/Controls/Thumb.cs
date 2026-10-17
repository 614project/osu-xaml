using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>Where a <see cref="Thumb"/> drag started, relative to the thumb's top-left corner.</summary>
    public class DragStartedEventArgs
    {
        public DragStartedEventArgs(double horizontalOffset, double verticalOffset)
        {
            HorizontalOffset = horizontalOffset;
            VerticalOffset = verticalOffset;
        }

        public double HorizontalOffset { get; }

        public double VerticalOffset { get; }
    }

    /// <summary>
    /// How far the pointer is from where it grabbed a <see cref="Thumb"/>, in the thumb's own coordinates. A handler that moves
    /// the thumb by this much (<c>Canvas.SetLeft(thumb, left + e.HorizontalChange)</c>) gets the movement since the last event.
    /// </summary>
    public class DragDeltaEventArgs
    {
        public DragDeltaEventArgs(double horizontalChange, double verticalChange)
        {
            HorizontalChange = horizontalChange;
            VerticalChange = verticalChange;
        }

        public double HorizontalChange { get; }

        public double VerticalChange { get; }
    }

    /// <summary>The pointer's final distance from the grab point, and whether <see cref="Thumb.CancelDrag"/> ended the drag.</summary>
    public class DragCompletedEventArgs
    {
        public DragCompletedEventArgs(double horizontalChange, double verticalChange, bool canceled)
        {
            HorizontalChange = horizontalChange;
            VerticalChange = verticalChange;
            Canceled = canceled;
        }

        public double HorizontalChange { get; }

        public double VerticalChange { get; }

        public bool Canceled { get; }
    }

    /// <summary>
    /// A handle the user drags, like WPF's Thumb: pressing it raises <see cref="DragStarted"/>, moving the pointer raises
    /// <see cref="DragDelta"/> and releasing raises <see cref="DragCompleted"/>. The thumb doesn't move itself; handlers
    /// move it (or whatever it resizes), as in WPF. It brightens while hovered and is fully opaque while dragged.
    /// </summary>
    public partial class Thumb : CompositeDrawable
    {
        private const float idle_alpha = 0.6f;
        private const float hover_alpha = 0.85f;

        private readonly Container chrome;
        private readonly Box fill;

        private bool isEnabled = true;
        private bool hovered;
        private Vector2 origin;
        private Vector2 lastPointer;

        public Thumb()
        {
            InternalChild = chrome = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4,
                Child = fill = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Overlay, Alpha = idle_alpha },
            };
        }

        /// <summary>Raised when the thumb is pressed.</summary>
        public event Action<DragStartedEventArgs> DragStarted;

        /// <summary>Raised each time the pointer moves while the thumb is held.</summary>
        public event Action<DragDeltaEventArgs> DragDelta;

        /// <summary>Raised when the thumb is released, or the drag is cancelled.</summary>
        public event Action<DragCompletedEventArgs> DragCompleted;

        /// <summary>Whether the thumb is being dragged.</summary>
        public bool IsDragging { get; private set; }

        /// <summary>The fill colour; it is drawn translucent until hovered or dragged.</summary>
        public Colour4 Background
        {
            get => fill.Colour;
            set => fill.Colour = value;
        }

        public new float CornerRadius
        {
            get => chrome.CornerRadius;
            set => chrome.CornerRadius = value;
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;

                if (!value && IsDragging)
                    CancelDrag();

                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);
                updateState();
            }
        }

        /// <summary>Ends the current drag, raising <see cref="DragCompleted"/> with <c>Canceled</c> set.</summary>
        public void CancelDrag()
        {
            if (IsDragging)
                complete(lastPointer, true);
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
            if (e.Button != MouseButton.Left || !isEnabled)
                return false;

            IsDragging = true;
            origin = ToLocalSpace(e.ScreenSpaceMousePosition);
            lastPointer = e.ScreenSpaceMousePosition;
            updateState();

            DragStarted?.Invoke(new DragStartedEventArgs(origin.X, origin.Y));
            return true;
        }

        // osu!framework starts a drag only after the pointer has moved a little; until then the pointer is still over the
        // thumb, so plain moves keep it following from the first pixel.
        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            if (IsDragging && !IsDragged)
                delta(e.ScreenSpaceMousePosition);

            return base.OnMouseMove(e);
        }

        protected override bool OnDragStart(DragStartEvent e) => IsDragging;

        protected override void OnDrag(DragEvent e)
        {
            if (IsDragging)
                delta(e.ScreenSpaceMousePosition);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left && IsDragging)
                complete(e.ScreenSpaceMousePosition, false);

            base.OnMouseUp(e);
        }

        private void delta(Vector2 screenSpacePointer)
        {
            // The same pointer position can arrive as a move and as a drag; report it once.
            if (screenSpacePointer == lastPointer)
                return;

            lastPointer = screenSpacePointer;

            var change = ToLocalSpace(screenSpacePointer) - origin;
            DragDelta?.Invoke(new DragDeltaEventArgs(change.X, change.Y));
        }

        private void complete(Vector2 screenSpacePointer, bool canceled)
        {
            IsDragging = false;
            updateState();

            var change = ToLocalSpace(screenSpacePointer) - origin;
            DragCompleted?.Invoke(new DragCompletedEventArgs(change.X, change.Y, canceled));
        }

        private void updateState()
        {
            float alpha = IsDragging ? 1 : hovered && isEnabled ? hover_alpha : idle_alpha;
            fill.FadeTo(alpha, IsDragging ? 60 : 150, Easing.OutQuint);
        }
    }
}
