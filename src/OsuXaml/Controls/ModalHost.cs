using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Implemented by a dialog that brings its own look to the <see cref="ModalHost"/> showing it: the backdrop's colour and the
    /// opening and closing animations. Without it the backdrop is half-transparent black and the dialog fades and scales in.
    /// </summary>
    public interface IModalAppearance
    {
        /// <summary>
        /// The backdrop's colour once the dialog has opened, transparency included. The game's other windows, which the dialog
        /// also blocks, dim in this colour too.
        /// </summary>
        Colour4 BackdropColour { get; }

        /// <summary>How long the backdrop takes to fade in, in milliseconds.</summary>
        double OpenDuration { get; }

        /// <summary>How long the backdrop takes to fade out, and the host waits before it removes the dialog.</summary>
        double CloseDuration { get; }

        /// <summary>Plays the dialog's opening animation, on the dialog itself.</summary>
        void AnimateOpen();

        /// <summary>Plays the dialog's closing animation, which should take no longer than <see cref="CloseDuration"/>.</summary>
        void AnimateClose();
    }

    /// <summary>
    /// One modal dialog in a <see cref="PopupLayer"/>: a dimmed backdrop over the whole layer that takes every click, hover,
    /// wheel turn and key press, the dialog centred above it, and a popup layer of its own so drop-downs, menus and tooltips
    /// inside the dialog open above the dialog. Clicking the backdrop does nothing. Hosts stack; only the top one takes input, and the
    /// top one is the newest in the whole game: the layers of other windows are covered meanwhile (<see cref="PopupLayer.BlockingModal"/>).
    /// <see cref="Dialog.ShowDialog(Drawable)"/> and <see cref="MessageBox.Show(Drawable, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult)"/> create them.
    /// </summary>
    public partial class ModalHost : CompositeDrawable, IPopupLayerProvider
    {
        internal const double OpenDuration = 250;
        internal const double CloseDuration = 150;
        internal const float Dim = 0.5f;

        private readonly Box backdrop;
        private readonly PopupLayer layer;

        private Drawable previousFocus;
        private bool focused;

        internal ModalHost(PopupLayer layer, Drawable dialog)
        {
            this.layer = layer;
            Dialog = dialog;

            RelativeSizeAxes = Axes.Both;

            dialog.Anchor = Anchor.Centre;
            dialog.Origin = Anchor.Centre;

            InternalChildren = new Drawable[]
            {
                backdrop = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black, Alpha = 0 },
                dialog,
                PopupLayer = new PopupLayer(),
            };
        }

        /// <summary>The dialog this host shows.</summary>
        public Drawable Dialog { get; }

        /// <summary>Where popups opened from inside the dialog go.</summary>
        public PopupLayer PopupLayer { get; }

        /// <summary>Whether the dialog has closed and is animating out; a closing host takes no input.</summary>
        public bool IsClosing { get; private set; }

        /// <summary>The backdrop's colour once open, transparency included: the dialog's own, or half-transparent black.</summary>
        internal Colour4 BackdropColour => Dialog is IModalAppearance appearance ? appearance.BackdropColour : Colour4.Black.Opacity(Dim);

        /// <summary>How long the backdrop takes to fade in.</summary>
        internal double BackdropOpenDuration => Dialog is IModalAppearance appearance ? appearance.OpenDuration : OpenDuration;

        /// <summary>How long the backdrop takes to fade out.</summary>
        internal double BackdropCloseDuration => Dialog is IModalAppearance appearance ? appearance.CloseDuration : CloseDuration;

        /// <summary>Whether the host is gone without closing: its window was disposed with the dialog still open.</summary>
        internal bool WasDisposed => IsDisposed;

        public override bool PropagatePositionalInputSubTree => !IsClosing && base.PropagatePositionalInputSubTree;

        public override bool PropagateNonPositionalInputSubTree => !IsClosing && base.PropagateNonPositionalInputSubTree;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            previousFocus = GetContainingInputManager()?.FocusedDrawable;

            if (Dialog is IModalAppearance appearance)
            {
                backdrop.Colour = appearance.BackdropColour.Opacity(1f);
                backdrop.FadeTo(appearance.BackdropColour.A, appearance.OpenDuration, Easing.OutQuint);
                appearance.AnimateOpen();
                return;
            }

            backdrop.FadeTo(Dim, OpenDuration, Easing.OutQuint);
            Dialog.FadeInFromZero(OpenDuration * 0.6, Easing.OutQuint);
            Dialog.ScaleTo(0.94f).ScaleTo(1, OpenDuration, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            if (IsClosing || !Dialog.IsAlive)
                return;

            // Focus moves into the dialog so keys stop reaching whatever had it below. A drawable can only take focus once it is
            // alive, which is a frame after it was added, so this retries until it works.
            if (!focused)
            {
                var manager = GetContainingFocusManager();
                focused = manager == null || !Dialog.AcceptsFocus || manager.ChangeFocus(Dialog);
                return;
            }

            // A focused drawable gets keys before anything else, wherever it is, so focus mustn't stay below the top dialog
            // (for example when a dialog above this one closes and gives focus back to what had it before both opened).
            if (isTopModal && Dialog.AcceptsFocus)
            {
                var focus = GetContainingInputManager()?.FocusedDrawable;

                if (focus != null && !IsWithin(focus, this))
                    GetContainingFocusManager()?.ChangeFocus(Dialog);
            }
        }

        /// <summary>Animates the dialog out and removes it, giving focus back to what had it before the dialog opened.</summary>
        internal void Close()
        {
            if (IsClosing)
                return;

            IsClosing = true;

            var appearance = Dialog as IModalAppearance;
            double duration = appearance?.CloseDuration ?? CloseDuration;
            layer.CloseModal(this, duration);

            var input = GetContainingInputManager();
            var focus = input?.FocusedDrawable;

            if (focus == null || IsWithin(focus, this))
            {
                if (previousFocus != null && previousFocus.IsLoaded && previousFocus.AcceptsFocus && isInTree(previousFocus))
                    GetContainingFocusManager()?.ChangeFocus(previousFocus);
                else if (focus != null)
                    GetContainingFocusManager()?.ChangeFocus(null);
            }

            PopupLayer.DismissAll();

            backdrop.FadeOut(duration, Easing.OutQuint);

            if (appearance != null)
                appearance.AnimateClose();
            else
                Dialog.ScaleTo(0.96f, CloseDuration, Easing.OutQuint).FadeOut(CloseDuration, Easing.OutQuint);
        }

        internal void ScheduleClose() => Schedule(Close);

        /// <summary>
        /// Whether this is the dialog that takes input: the newest open in the game, in any window. Outside a game, the newest in its
        /// layer with none open above it from inside.
        /// </summary>
        private bool isTopModal => PopupLayer.TopModal(this) is ModalHost top
            ? top == this
            : layer.OpenModals.Count > 0 && layer.OpenModals[^1] == this && PopupLayer.OpenModals.Count == 0;

        /// <summary>
        /// What the backdrop dims: the area of the outermost popup layer the dialog is in, which is its window (or the whole game,
        /// for the game's own layer). Dialogs opened from inside a dialog dim the same window.
        /// </summary>
        internal Drawable Region
        {
            get
            {
                Drawable outer = layer;

                for (var d = layer.Parent; d != null; d = d.Parent)
                {
                    if (d is PopupLayer p)
                        outer = p;
                }

                return outer.Parent;
            }
        }

        /// <summary>Moves focus into the dialog, as clicking its backdrop does.</summary>
        internal void FocusDialog()
        {
            if (Dialog.AcceptsFocus)
                GetContainingFocusManager()?.ChangeFocus(Dialog);
        }

        private bool isInTree(Drawable drawable)
        {
            var top = drawable;
            while (top.Parent != null)
                top = top.Parent;

            var ownTop = (Drawable)this;
            while (ownTop.Parent != null)
                ownTop = ownTop.Parent;

            return top == ownTop;
        }

        /// <summary>Whether <paramref name="drawable"/> is <paramref name="ancestor"/> or inside it.</summary>
        internal static bool IsWithin(Drawable drawable, Drawable ancestor)
        {
            for (var d = drawable; d != null; d = d.Parent)
            {
                if (d == ancestor)
                    return true;
            }

            return false;
        }

        // The backdrop: everything the dialog and its popups don't handle stops here, so nothing below reacts.
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        protected override bool OnClick(ClickEvent e)
        {
            // Clicking the backdrop (or an empty part of the dialog) keeps focus in the dialog instead of clearing it.
            FocusDialog();
            return true;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e) => true;

        protected override bool OnDragStart(DragStartEvent e) => true;

        protected override bool OnScroll(ScrollEvent e) => true;

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnMouseMove(MouseMoveEvent e) => true;

        protected override bool OnKeyDown(KeyDownEvent e) => true;
    }
}
