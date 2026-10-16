using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Threading;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A button that raises <see cref="Button.Click"/> as soon as it is pressed and then again every
    /// <see cref="Interval"/> milliseconds while it is held, after an initial <see cref="Delay"/>, like WPF's RepeatButton.
    /// Moving the pointer off the button pauses the repeats until it comes back.
    /// </summary>
    public partial class RepeatButton : Button
    {
        private ScheduledDelegate repeat;
        private InputState pressState;

        /// <summary>Milliseconds between the press and the first repeat.</summary>
        public int Delay { get; set; } = 400;

        /// <summary>Milliseconds between repeats.</summary>
        public int Interval { get; set; } = 50;

        /// <summary>Whether the button is held down right now.</summary>
        public bool IsPressed => pressState != null;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButton.Left || !IsEnabled)
                return false;

            pressState = e.CurrentState;
            raiseClick();

            repeat?.Cancel();
            repeat = Scheduler.AddDelayed(() =>
            {
                repeat = Scheduler.AddDelayed(() =>
                {
                    if (pointerIsOver())
                        raiseClick();
                }, System.Math.Max(1, Interval), true);
            }, System.Math.Max(0, Delay));

            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left)
                stop();

            base.OnMouseUp(e);
        }

        // The press already raised Click; the release must not raise it again.
        protected override bool OnClick(ClickEvent e) => IsEnabled;

        // Holding the button inside a scroll view must not start a drag-scroll.
        protected override bool OnDragStart(DragStartEvent e) => IsPressed;

        private void raiseClick()
        {
            if (!IsEnabled)
            {
                stop();
                return;
            }

            base.OnClick(new ClickEvent(pressState, MouseButton.Left));
        }

        // Hover doesn't change while a button is held (the press captures the pointer), so test the position itself.
        private bool pointerIsOver()
        {
            var mouse = GetContainingInputManager()?.CurrentState.Mouse;
            return mouse != null && ReceivePositionalInputAt(mouse.Position);
        }

        private void stop()
        {
            repeat?.Cancel();
            repeat = null;
            pressState = null;
        }
    }
}
