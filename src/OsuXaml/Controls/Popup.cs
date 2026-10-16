using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>Where a <see cref="Popup"/> opens relative to its <see cref="Popup.PlacementTarget"/>.</summary>
    public enum PlacementMode
    {
        Bottom,
        Top,
        Right,
        Left,
        Mouse,
        Center,
    }

    /// <summary>A drawable that hosts the popup layer for everything inside it, such as <see cref="Window"/>.</summary>
    public interface IPopupLayerProvider
    {
        PopupLayer PopupLayer { get; }
    }

    /// <summary>
    /// A full-size layer above the app content where popups open, so drop-downs and menus are never clipped
    /// by a ScrollViewer or masked parent. <see cref="Window"/> has one; without a Window one is added to the Game.
    /// Clicking outside a popup that doesn't <see cref="Popup.StaysOpen"/> closes it, and so does Escape.
    /// </summary>
    public partial class PopupLayer : CompositeDrawable
    {
        private static readonly ConditionalWeakTable<Game, PopupLayer> game_layers = new ConditionalWeakTable<Game, PopupLayer>();

        private readonly Container<PopupRoot> roots;
        private readonly List<Popup> open = new List<Popup>();

        public PopupLayer()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new DismissArea(this),
                roots = new Container<PopupRoot> { RelativeSizeAxes = Axes.Both },
            };
        }

        /// <summary>Open popups, oldest first.</summary>
        public IReadOnlyList<Popup> OpenPopups => open;

        /// <summary>
        /// The layer popups opened from <paramref name="from"/> go to: the nearest <see cref="IPopupLayerProvider"/>
        /// (or popup layer) above it, otherwise a layer added on demand to the <see cref="Game"/>. Call from the update thread.
        /// </summary>
        public static PopupLayer Find(Drawable from)
        {
            for (var p = from.Parent; p != null; p = p.Parent)
            {
                switch (p)
                {
                    case PopupLayer layer:
                        return layer;

                    case IPopupLayerProvider provider:
                        return provider.PopupLayer;

                    case Game game:
                        if (!game_layers.TryGetValue(game, out var gameLayer))
                        {
                            gameLayer = new PopupLayer { Depth = float.MinValue };
                            game_layers.Add(game, gameLayer);
                            game.Add(gameLayer);
                        }

                        return gameLayer;
                }
            }

            return null;
        }

        internal void Open(Popup popup)
        {
            if (open.Contains(popup))
                return;

            open.Add(popup);

            if (popup.Root.Parent != roots)
                roots.Add(popup.Root);

            // Later popups (sub-menus) draw above earlier ones.
            roots.ChangeChildDepth(popup.Root, -open.Count);
        }

        internal void Close(Popup popup)
        {
            if (!open.Remove(popup))
                return;

            if (popup.Root.Parent == roots)
                roots.Remove(popup.Root, false);
        }

        /// <summary>Removes and disposes a popup's content on the update thread after its owner was disposed.</summary>
        internal void ScheduleRelease(PopupRoot root)
        {
            if (!IsDisposed)
                Schedule(() => release(root));
        }

        private void release(PopupRoot root)
        {
            open.Remove(root.Owner);

            if (root.Parent == roots)
                roots.Remove(root, true);
            else
                root.Dispose();
        }

        /// <summary>Closes every open popup that doesn't stay open.</summary>
        public void DismissAll()
        {
            foreach (var popup in open.Where(p => !p.StaysOpen).ToArray())
                popup.IsOpen.Value = false;
        }

        internal bool HasDismissable => open.Any(p => !p.StaysOpen);

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key != Key.Escape)
                return false;

            var top = open.LastOrDefault(p => !p.StaysOpen);
            if (top == null)
                return false;

            top.IsOpen.Value = false;
            return true;
        }

        /// <summary>Catches clicks outside every popup while a dismissable popup is open.</summary>
        private partial class DismissArea : Drawable
        {
            private readonly PopupLayer layer;

            public DismissArea(PopupLayer layer)
            {
                this.layer = layer;
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;
                Alpha = 0;
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => layer.HasDismissable && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                layer.DismissAll();
                return true;
            }
        }
    }

    /// <summary>
    /// Shows content above everything else while <see cref="IsOpen"/> is true, placed next to
    /// <see cref="PlacementTarget"/> (by default the popup's parent). It takes no space where it is declared.
    /// </summary>
    public partial class Popup : CompositeDrawable
    {
        internal readonly PopupRoot Root;

        private PopupLayer layer;
        private Vector2 mousePosition;

        public Popup()
        {
            Root = new PopupRoot(this);
            AlwaysPresent = true;
        }

        /// <summary>Whether the popup is showing. Binds two-way.</summary>
        public Bindable<bool> IsOpen { get; } = new Bindable<bool>();

        /// <summary>The popup's content.</summary>
        public Drawable Child
        {
            get => Root.PopupContent;
            set => Root.PopupContent = value;
        }

        /// <summary>The element the popup opens next to. Defaults to the popup's parent element.</summary>
        public Drawable PlacementTarget { get; set; }

        public PlacementMode Placement { get; set; } = PlacementMode.Bottom;

        public float HorizontalOffset { get; set; }

        public float VerticalOffset { get; set; }

        /// <summary>When false (the default), clicking outside or pressing Escape closes the popup.</summary>
        public bool StaysOpen { get; set; }

        /// <summary>Makes the content as wide as the placement target, as drop-down lists are.</summary>
        public bool MatchTargetWidth { get; set; }

        public event Action Opened;

        public event Action Closed;

        /// <summary>The layer this popup opened in, once it has opened.</summary>
        public PopupLayer Layer => layer;

        public void Open() => IsOpen.Value = true;

        public void Close() => IsOpen.Value = false;

        public void Toggle() => IsOpen.Value = !IsOpen.Value;

        /// <summary>The element placement is measured from.</summary>
        internal Drawable EffectiveTarget
        {
            get
            {
                if (PlacementTarget != null)
                    return PlacementTarget;

                // A popup declared in a panel sits in a zero-size slot; place it against the panel instead.
                var parent = (Drawable)Parent;
                return parent is Slot slot ? (Drawable)slot.Parent ?? slot : parent ?? this;
            }
        }

        internal Vector2 MousePosition => mousePosition;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            IsOpen.BindValueChanged(e => apply(e.NewValue), true);
        }

        private void apply(bool isOpen)
        {
            if (isOpen)
            {
                layer ??= PopupLayer.Find(this);

                if (layer == null)
                {
                    IsOpen.Value = false;
                    return;
                }

                mousePosition = GetContainingInputManager()?.CurrentState.Mouse.Position ?? Vector2.Zero;
                layer.Open(this);
                Root.Appear();
                Opened?.Invoke();
            }
            else if (layer != null && layer.OpenPopups.Contains(this))
            {
                layer.Close(this);
                Closed?.Invoke();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            // The content lives in another part of the tree; hand it back to the layer's update thread to remove.
            if (layer != null && layer.IsLoaded)
                layer.ScheduleRelease(Root);
            else if (Root.Parent == null)
                Root.Dispose();

            base.Dispose(isDisposing);
        }
    }

    /// <summary>The part of a <see cref="Popup"/> that lives in the <see cref="PopupLayer"/>.</summary>
    public partial class PopupRoot : Container
    {
        public readonly Popup Owner;

        private Drawable content;

        internal PopupRoot(Popup owner)
        {
            Owner = owner;
            AutoSizeAxes = Axes.Both;
        }

        public Drawable PopupContent
        {
            get => content;
            set
            {
                if (content != null)
                    Remove(content, true);

                content = value;

                if (content != null)
                    Add(content);
            }
        }

        internal void Appear()
        {
            place();
            this.FadeInFromZero(120, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();
            place();
        }

        private void place()
        {
            if (!(Parent is Drawable container))
                return;

            var target = Owner.EffectiveTarget;

            // A target or owner that left the tree (or was disposed) closes the popup.
            var top = topOf(this);

            if (target == null || topOf(target) != top || topOf(Owner) != top)
            {
                Schedule(() => Owner.IsOpen.Value = false);
                return;
            }

            var quad = target.ScreenSpaceDrawQuad;
            var topLeft = container.ToLocalSpace(quad.TopLeft);
            var bottomRight = container.ToLocalSpace(quad.BottomRight);

            if (Owner.MatchTargetWidth && content != null)
            {
                AutoSizeAxes = Axes.Y;
                Width = Math.Max(0, bottomRight.X - topLeft.X);

                if (!content.RelativeSizeAxes.HasFlag(Axes.X))
                {
                    content.RelativeSizeAxes |= Axes.X;
                    content.Width = 1;
                }
            }

            var size = DrawSize;
            var bounds = container.DrawSize;
            Vector2 position;

            switch (Owner.Placement)
            {
                case PlacementMode.Top:
                    position = new Vector2(topLeft.X, topLeft.Y - size.Y);
                    break;

                case PlacementMode.Right:
                    position = new Vector2(bottomRight.X, topLeft.Y);
                    if (position.X + size.X > bounds.X) position.X = topLeft.X - size.X;
                    break;

                case PlacementMode.Left:
                    position = new Vector2(topLeft.X - size.X, topLeft.Y);
                    if (position.X < 0) position.X = bottomRight.X;
                    break;

                case PlacementMode.Mouse:
                    position = container.ToLocalSpace(Owner.MousePosition);
                    break;

                case PlacementMode.Center:
                    position = (topLeft + bottomRight) / 2 - size / 2;
                    break;

                default:
                    position = new Vector2(topLeft.X, bottomRight.Y);
                    // Flip above the target when there is no room below.
                    if (position.Y + size.Y > bounds.Y && topLeft.Y - size.Y >= 0) position.Y = topLeft.Y - size.Y;
                    break;
            }

            position += new Vector2(Owner.HorizontalOffset, Owner.VerticalOffset);
            position.X = Math.Clamp(position.X, 0, Math.Max(0, bounds.X - size.X));
            position.Y = Math.Clamp(position.Y, 0, Math.Max(0, bounds.Y - size.Y));
            Position = position;
        }

        private static Drawable topOf(Drawable drawable)
        {
            while (drawable.Parent != null)
                drawable = drawable.Parent;

            return drawable;
        }

        // Clicks, hover and scrolling inside a popup stay inside it.
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnScroll(ScrollEvent e) => true;
    }
}
