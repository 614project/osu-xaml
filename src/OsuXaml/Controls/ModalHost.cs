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
    /// inside the dialog open above the dialog. Clicking the backdrop does nothing. Hosts stack; only the top one takes input.
    /// <see cref="Dialog.ShowDialog(Drawable)"/> and <see cref="MessageBox.Show(Drawable, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult)"/> create them.
    /// </summary>
    public partial class ModalHost : CompositeDrawable, IPopupLayerProvider
    {
        private const double open_duration = 250;
        private const double close_duration = 150;
        private const float dim = 0.5f;

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

        public override bool PropagatePositionalInputSubTree => !IsClosing && base.PropagatePositionalInputSubTree;

        public override bool PropagateNonPositionalInputSubTree => !IsClosing && base.PropagateNonPositionalInputSubTree;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            previousFocus = GetContainingInputManager()?.FocusedDrawable;

            backdrop.FadeTo(dim, open_duration, Easing.OutQuint);
            Dialog.FadeInFromZero(open_duration * 0.6, Easing.OutQuint);
            Dialog.ScaleTo(0.94f).ScaleTo(1, open_duration, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            // Focus moves into the dialog so keys stop reaching whatever had it below. A drawable can only take focus once it is
            // alive, which is a frame after it was added, so this retries until it works.
            if (!focused && !IsClosing && Dialog.IsAlive)
            {
                var manager = GetContainingFocusManager();
                focused = manager == null || !Dialog.AcceptsFocus || manager.ChangeFocus(Dialog);
            }
        }

        /// <summary>Animates the dialog out and removes it, giving focus back to what had it before the dialog opened.</summary>
        internal void Close()
        {
            if (IsClosing)
                return;

            IsClosing = true;
            layer.CloseModal(this, close_duration);

            var input = GetContainingInputManager();
            var focus = input?.FocusedDrawable;

            if (focus == null || isWithin(focus, this))
            {
                if (previousFocus != null && previousFocus.IsLoaded && previousFocus.AcceptsFocus && isInTree(previousFocus))
                    GetContainingFocusManager()?.ChangeFocus(previousFocus);
                else if (focus != null)
                    GetContainingFocusManager()?.ChangeFocus(null);
            }

            PopupLayer.DismissAll();

            backdrop.FadeOut(close_duration, Easing.OutQuint);
            Dialog.ScaleTo(0.96f, close_duration, Easing.OutQuint).FadeOut(close_duration, Easing.OutQuint);
        }

        internal void ScheduleClose() => Schedule(Close);

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

        private static bool isWithin(Drawable drawable, Drawable ancestor)
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
            if (Dialog.AcceptsFocus)
                GetContainingFocusManager()?.ChangeFocus(Dialog);

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
