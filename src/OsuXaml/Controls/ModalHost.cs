using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;

namespace OsuXaml.Controls
{
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

        /// <summary>Whether the host is gone without closing: its window was disposed with the dialog still open.</summary>
        internal bool WasDisposed => IsDisposed;

        public override bool PropagatePositionalInputSubTree => !IsClosing && base.PropagatePositionalInputSubTree;

        public override bool PropagateNonPositionalInputSubTree => !IsClosing && base.PropagateNonPositionalInputSubTree;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            previousFocus = GetContainingInputManager()?.FocusedDrawable;

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
            layer.CloseModal(this, CloseDuration);

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

            backdrop.FadeOut(CloseDuration, Easing.OutQuint);
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
