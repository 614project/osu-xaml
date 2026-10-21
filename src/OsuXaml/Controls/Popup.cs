using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
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
    /// The layer also shows tooltips (<see cref="ToolTipService"/>) and opens context menus on right-click (<see cref="ContextMenuService"/>).
    /// Modal dialogs (<see cref="Dialog"/>, <see cref="MessageBox"/>) open above the popups in a <see cref="ModalHost"/> each; while one
    /// is open nothing below it takes input, and the layer shows no tooltips or context menus for the app underneath.
    /// A dialog is modal for the whole game, as WPF's are for the whole app: while it is open the layers of the game's other windows
    /// dim and take no input either (see <see cref="BlockingModal"/>), and the newest dialog in the game is the one that takes input.
    /// </summary>
    public partial class PopupLayer : CompositeDrawable
    {
        private static readonly ConditionalWeakTable<Game, PopupLayer> game_layers = new ConditionalWeakTable<Game, PopupLayer>();

        // Loaded layers, newest last, for dialogs shown without an owner.
        private static readonly List<WeakReference<PopupLayer>> loaded_layers = new List<WeakReference<PopupLayer>>();

        // Open modal dialogs of every game and window, oldest first. The newest one in a game takes the input of the whole game.
        private static readonly List<WeakReference<ModalHost>> open_modals = new List<WeakReference<ModalHost>>();

        private readonly Container<PopupRoot> roots;
        private readonly Container contextMenus;
        private readonly Container<ModalHost> modals;
        private readonly ToolTipHost toolTips;
        private readonly List<Popup> open = new List<Popup>();
        private readonly List<ModalHost> openModals = new List<ModalHost>();

        private bool wasBlocked;

        public PopupLayer()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new DismissArea(this),
                // Context menus attached with ContextMenuService aren't part of the app's tree, so they join it here while open.
                contextMenus = new Container { RelativeSizeAxes = Axes.Both },
                roots = new Container<PopupRoot> { RelativeSizeAxes = Axes.Both },
                modals = new Container<ModalHost> { RelativeSizeAxes = Axes.Both },
                // Above this layer's own dialogs, which wait under it while a newer dialog in another window takes the input.
                new ModalCover(this),
                toolTips = new ToolTipHost(this),
            };
        }

        /// <summary>Open popups, oldest first.</summary>
        public IReadOnlyList<Popup> OpenPopups => open;

        /// <summary>Open modal dialogs, bottom first; the last one takes the input. Closing dialogs leave the list at once.</summary>
        public IReadOnlyList<ModalHost> OpenModals => openModals;

        /// <summary>
        /// The dialog in another window of the game that has the input while it is open, if any. Meanwhile this layer closes its
        /// popups and shows no tooltips or context menus, and a window's layer dims the window and keeps clicks, hover, the wheel
        /// and keys from reaching it. A dialog inside what owns this layer (its own window, or anywhere for the game's own layer)
        /// doesn't block it.
        /// </summary>
        public ModalHost BlockingModal
        {
            get
            {
                var top = TopModal(this);
                return top == null || ModalHost.IsWithin(this, top) || ModalHost.IsWithin(top, Parent) ? null : top;
            }
        }

        /// <summary>
        /// The dialog that takes input in the game <paramref name="drawable"/> is in: the newest one open in any of its windows.
        /// Dialogs of a window that was taken out of the game don't count. Call from the update thread.
        /// </summary>
        internal static ModalHost TopModal(Drawable drawable)
        {
            lock (open_modals)
            {
                if (open_modals.Count == 0)
                    return null;

                var game = gameOf(drawable);
                if (game == null)
                    return null;

                for (int i = open_modals.Count - 1; i >= 0; i--)
                {
                    // The window may have been disposed with the dialog still open; such hosts never close.
                    if (!open_modals[i].TryGetTarget(out var host) || host.WasDisposed || host.IsClosing)
                    {
                        open_modals.RemoveAt(i);
                        continue;
                    }

                    if (gameOf(host) == game)
                        return host;
                }
            }

            return null;
        }

        private static Game gameOf(Drawable drawable)
        {
            for (var d = drawable; d != null; d = d.Parent)
            {
                if (d is Game game)
                    return game;
            }

            return null;
        }

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

        /// <summary>
        /// The layer a dialog owned by <paramref name="owner"/> opens in: the owner's own layer when it has one (a <see cref="Window"/>),
        /// the layer above it otherwise, and without an owner the most recently loaded layer of a running game. Call from the update thread.
        /// </summary>
        public static PopupLayer ForOwner(Drawable owner)
        {
            switch (owner)
            {
                case null:
                    return latest();

                case PopupLayer layer:
                    return layer;

                case IPopupLayerProvider provider:
                    return provider.PopupLayer;

                default:
                    return Find(owner);
            }
        }

        private static PopupLayer latest()
        {
            lock (loaded_layers)
            {
                for (int i = loaded_layers.Count - 1; i >= 0; i--)
                {
                    if (!loaded_layers[i].TryGetTarget(out var layer) || layer.IsDisposed)
                    {
                        loaded_layers.RemoveAt(i);
                        continue;
                    }

                    // A dialog's own layer would close with the dialog; a layer outside the game (a removed window) shows nothing.
                    if (layer.isTopLevelInGame())
                        return layer;
                }
            }

            return null;
        }

        private bool isTopLevelInGame()
        {
            for (var p = Parent; p != null; p = p.Parent)
            {
                if (p is ModalHost)
                    return false;

                if (p is Game)
                    return true;
            }

            return false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            lock (loaded_layers)
                loaded_layers.Add(new WeakReference<PopupLayer>(this));
        }

        protected override void Update()
        {
            base.Update();

            // A dialog opened in another window closes this one's drop-downs and menus, as switching windows does.
            bool blocked = BlockingModal != null;

            if (blocked && !wasBlocked)
                DismissAll();

            wasBlocked = blocked;

            updateCursor();
        }

        /// <summary>Shows a modal dialog above everything in this layer, closing the popups that don't stay open.</summary>
        internal void OpenModal(ModalHost host)
        {
            DismissAll();
            toolTips.SuppressHovered();

            openModals.Add(host);
            modals.Add(host);

            lock (open_modals)
                open_modals.Add(new WeakReference<ModalHost>(host));
        }

        internal void ScheduleOpenModal(ModalHost host) => Schedule(() => OpenModal(host));

        /// <summary>
        /// Takes a closing dialog out of the input order at once, and out of the layer once its closing animation has played.
        /// The removal runs on this layer's scheduler, not the host's: a drawable must not dispose itself while it updates.
        /// </summary>
        internal void CloseModal(ModalHost host, double removeAfter)
        {
            openModals.Remove(host);

            lock (open_modals)
                open_modals.RemoveAll(r => !r.TryGetTarget(out var h) || h == host);

            Scheduler.AddDelayed(() =>
            {
                if (host.Parent == modals)
                    modals.Remove(host, true);
            }, removeAfter);
        }

        /// <summary>
        /// Makes sure a layer exists for <paramref name="element"/> once it is in the tree, so tooltips and context menus
        /// attached to it work in apps without a <see cref="Window"/>.
        /// </summary>
        internal static void EnsureFor(Drawable element)
        {
            if (element.IsLoaded)
                Find(element);
            else
                element.OnLoadComplete += d => Find(d);
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

        /// <summary>Closes the newest popup that doesn't stay open, as Escape does. Returns whether there was one.</summary>
        internal bool DismissTop()
        {
            var top = open.LastOrDefault(p => !p.StaysOpen);
            if (top == null)
                return false;

            top.IsOpen.Value = false;
            return true;
        }

        /// <summary>
        /// The deepest of <paramref name="candidates"/> that is visible under <paramref name="screenSpacePos"/> and belongs to this layer.
        /// Over an open popup only that popup's content counts, so elements underneath don't react through it.
        /// </summary>
        internal Drawable FindTarget(IEnumerable<Drawable> candidates, Vector2 screenSpacePos)
        {
            // A modal dialog covers everything this layer serves; elements inside the dialog belong to the dialog's own layer.
            // A dialog in another window does too.
            if (openModals.Count > 0 || BlockingModal != null)
                return null;

            PopupRoot over = popupAt(screenSpacePos);

            Drawable best = null;
            int bestDepth = -1;

            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.IsLoaded || !candidate.IsPresent || !candidate.ScreenSpaceDrawQuad.Contains(screenSpacePos))
                    continue;

                if (reachable(candidate, screenSpacePos, over, out int depth) && depth > bestDepth)
                {
                    best = candidate;
                    bestDepth = depth;
                }
            }

            return best;
        }

        /// <summary>Whether nothing hides <paramref name="drawable"/> at the position (presence, masking) and its nearest layer is this one.</summary>
        private bool reachable(Drawable drawable, Vector2 screenSpacePos, PopupRoot over, out int depth)
        {
            depth = 0;
            bool insideOver = over == null;

            for (var p = drawable.Parent; p != null; p = p.Parent)
            {
                depth++;

                if (!p.IsPresent || (p.Masking && !p.ScreenSpaceDrawQuad.Contains(screenSpacePos)))
                    return false;

                if (p == over)
                    insideOver = true;

                switch (p)
                {
                    case PopupLayer layer:
                        return layer == this && insideOver;

                    case IPopupLayerProvider provider:
                        return provider.PopupLayer == this && insideOver;

                    case Game game:
                        return game_layers.TryGetValue(game, out var gameLayer) && gameLayer == this && insideOver;
                }
            }

            return false;
        }

        /// <summary>The newest open popup under the position, if any.</summary>
        private PopupRoot popupAt(Vector2 screenSpacePos)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var root = open[i].Root;

                if (root.Parent == roots && root.IsPresent && root.ScreenSpaceDrawQuad.Contains(screenSpacePos))
                    return root;
            }

            return null;
        }

        /// <summary>The cursor this layer last put on the pointer, or null while it leaves the pointer alone.</summary>
        internal Cursor CurrentCursor { get; private set; }

        [Resolved(canBeNull: true)]
        private GameHost host { get; set; }

        private void updateCursor()
        {
            var input = GetContainingInputManager();
            Cursor cursor = null;

            // Only the outermost layer sets the cursor; it sees through the layers of dialogs and windows inside it.
            if (input != null && ownsPointer())
            {
                var mouse = input.CurrentState.Mouse;

                if (CursorService.OverrideCursor != null)
                    cursor = CursorService.OverrideCursor;
                // Like WPF's mouse capture: a drag keeps the cursor of what is dragged, even when the pointer leaves it.
                else if (input.DraggedDrawable is Drawable dragged && serves(dragged))
                    cursor = cursorAround(dragged) ?? Cursors.Arrow;
                else if (mouse.IsPositionValid && ScreenSpaceDrawQuad.Contains(mouse.Position))
                    cursor = cursorAt(input, mouse.Position) ?? Cursors.Arrow;
            }

            CurrentCursor = cursor;

            if (host == null)
                return;

            if (cursor != null)
                SystemCursor.Request(host, this, cursor);
            else
                SystemCursor.Release(host, this);
        }

        private bool ownsPointer()
        {
            for (var p = Parent; p != null; p = p.Parent)
            {
                switch (p)
                {
                    case PopupLayer _:
                    case IPopupLayerProvider provider when provider.PopupLayer != this:
                    case Game game when game_layers.TryGetValue(game, out var gameLayer) && gameLayer != this:
                        return false;
                }
            }

            return true;
        }

        /// <summary>Whether <paramref name="drawable"/> is under this layer: in it, or in the content it covers.</summary>
        private bool serves(Drawable drawable)
        {
            for (var p = drawable.Parent; p != null; p = p.Parent)
            {
                if (p == this || (p is IPopupLayerProvider provider && provider.PopupLayer == this)
                              || (p is Game game && game_layers.TryGetValue(game, out var gameLayer) && gameLayer == this))
                    return true;
            }

            return false;
        }

        /// <summary>The cursor of the element or its nearest ancestor that has one.</summary>
        private static Cursor cursorAround(Drawable drawable)
        {
            for (var p = drawable; p != null; p = p.Parent)
            {
                if (CursorService.Resolve(p) is Cursor cursor)
                    return cursor;
            }

            return null;
        }

        /// <summary>The cursor of the deepest element under the position that has one and that nothing covers.</summary>
        private Cursor cursorAt(osu.Framework.Input.InputManager input, Vector2 screenSpacePos)
        {
            Cursor best = null;
            int bestDepth = -1;

            void consider(Drawable element, Cursor cursor)
            {
                if (uncovered(element, screenSpacePos, out int depth) && depth > bestDepth)
                {
                    best = cursor;
                    bestDepth = depth;
                }
            }

            // Controls with a cursor of their own take input, so hovering finds them, and finds them from anything inside them too.
            foreach (var hovered in input.HoveredDrawables)
            {
                for (var p = hovered; p != null; p = p.Parent)
                {
                    if (CursorService.Resolve(p) is Cursor cursor)
                    {
                        consider(p, cursor);
                        break;
                    }
                }
            }

            // A cursor set in XAML is often on text or a border, which take no input, so those are hit-tested like tooltips.
            foreach (var element in CursorService.Targets)
            {
                if (CursorService.GetCursor(element) is Cursor cursor && element.IsLoaded && element.IsPresent && element.ReceivePositionalInputAt(screenSpacePos))
                    consider(element, cursor);
            }

            return best;
        }

        /// <summary>
        /// Whether nothing hides <paramref name="drawable"/> at the position, through every layer up to the root: no parent is hidden or
        /// masks it out, no open popup lies over it unless it is in that popup, and no modal dialog is open unless it is in the top one.
        /// <paramref name="depth"/> counts its ancestors, so the deepest of several elements wins.
        /// </summary>
        private bool uncovered(Drawable drawable, Vector2 screenSpacePos, out int depth)
        {
            depth = 0;
            bool underThis = false;

            // The innermost layer the path came up through, and the path's last two steps below p.
            PopupLayer passed = null;
            Drawable child = drawable, grandchild = null;

            for (var p = drawable.Parent; p != null; grandchild = child, child = p, p = p.Parent)
            {
                depth++;

                if (!p.IsPresent || (p.Masking && !p.ScreenSpaceDrawQuad.Contains(screenSpacePos)))
                    return false;

                PopupLayer below = null;

                switch (p)
                {
                    case PopupLayer layer:
                        // The path came through one of the layer's popups or dialogs, which sits two levels down.
                        if (!layer.letsThrough(grandchild, screenSpacePos))
                            return false;

                        passed = layer;
                        underThis |= layer == this;
                        continue;

                    case IPopupLayerProvider provider when provider.PopupLayer != passed:
                        below = provider.PopupLayer;
                        break;

                    case Game game when game_layers.TryGetValue(game, out var gameLayer) && gameLayer != passed:
                        below = gameLayer;
                        break;
                }

                // Everything else under a provider is content its layer covers.
                if (below != null)
                {
                    if (below.openModals.Count > 0 || below.popupAt(screenSpacePos) != null)
                        return false;

                    underThis |= below == this;
                }
            }

            return underThis;
        }

        /// <summary>Whether <paramref name="top"/> (a popup root or dialog host in this layer) is on top at the position.</summary>
        private bool letsThrough(Drawable top, Vector2 screenSpacePos)
        {
            // Only the top dialog takes input; a closing one has already left the list.
            if (openModals.Count > 0)
                return openModals[^1] == top;

            return top is PopupRoot root && popupAt(screenSpacePos) == root;
        }

        /// <summary>Opens the context menu of the deepest element under the position that has one.</summary>
        internal bool TryOpenContextMenu(Vector2 screenSpacePos)
        {
            var target = FindTarget(ContextMenuService.Targets, screenSpacePos);
            var menu = target == null ? null : ContextMenuService.GetContextMenu(target);

            if (menu == null)
                return false;

            menu.OpenFor(this, target);
            return true;
        }

        /// <summary>Puts a context menu that isn't in the drawable tree into this layer so its popup can open.</summary>
        internal void HostContextMenu(ContextMenu menu)
        {
            if (menu.Parent == null)
                contextMenus.Add(menu);
        }

        /// <summary>Takes a closed context menu back out of the layer, without disposing it, so menus of removed elements don't pile up.</summary>
        internal void ReleaseContextMenu(ContextMenu menu)
        {
            if (IsDisposed)
                return;

            Schedule(() =>
            {
                if (menu.Parent == contextMenus && !menu.IsOpen.Value)
                    contextMenus.Remove(menu, false);
            });
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            // The layer sits above the app, so it sees presses first; a press and release can fall within one frame.
            toolTips.SuppressHovered();
            return e.Button == MouseButton.Right && TryOpenContextMenu(e.ScreenSpaceMousePosition);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            return e.Key == Key.Escape && DismissTop();
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

                // Right-clicking elsewhere while a menu is open opens the context menu there, as desktop apps do.
                if (e.Button == MouseButton.Right)
                    layer.TryOpenContextMenu(e.ScreenSpaceMousePosition);

                return true;
            }
        }

        /// <summary>
        /// Covers a window while a dialog in another window of the game has the input: it dims the window as the dialog's backdrop
        /// dims the dialog's own, and takes its clicks, hover and wheel turns so nothing in it reacts. Clicking it keeps focus in the
        /// dialog. A window the dialog's backdrop already dims (one below a dialog in the game's own layer) isn't dimmed twice. Only a
        /// window's outermost layer covers; the layers of the window's own dialogs are under it.
        /// </summary>
        internal partial class ModalCover : CompositeDrawable
        {
            private readonly PopupLayer layer;
            private readonly Box dim;
            private bool dimmed;

            public ModalCover(PopupLayer layer)
            {
                this.layer = layer;
                RelativeSizeAxes = Axes.Both;
                InternalChild = dim = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black, Alpha = 0 };
            }

            /// <summary>The dialog this layer is covered for, if any.</summary>
            private ModalHost covering => layer.isTopLevelInGame() ? layer.BlockingModal : null;

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => covering != null && base.ReceivePositionalInputAt(screenSpacePos);

            protected override void Update()
            {
                base.Update();

                bool shouldDim = covering is ModalHost modal && !ModalHost.IsWithin(layer, modal.Region);

                if (shouldDim == dimmed)
                    return;

                dimmed = shouldDim;

                if (dimmed)
                    dim.FadeTo(ModalHost.Dim, ModalHost.OpenDuration, Easing.OutQuint);
                else
                    dim.FadeOut(ModalHost.CloseDuration, Easing.OutQuint);
            }

            protected override bool OnMouseDown(MouseDownEvent e) => true;

            protected override bool OnClick(ClickEvent e)
            {
                covering?.FocusDialog();
                return true;
            }

            protected override bool OnDoubleClick(DoubleClickEvent e) => true;

            protected override bool OnDragStart(DragStartEvent e) => true;

            protected override bool OnScroll(ScrollEvent e) => true;

            protected override bool OnHover(HoverEvent e) => true;

            protected override bool OnMouseMove(MouseMoveEvent e) => true;
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
                // Looked up on every open: the popup (or its owner) may have moved to another window since.
                layer = PopupLayer.Find(this);

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
            // Closed content is in no tree and goes now (the layer may be disposed along with this popup, so its
            // scheduler would never run). Open content lives in the layer; hand it to the layer's update thread to remove.
            if (Root.Parent == null)
                Root.Dispose();
            else if (layer != null && layer.IsLoaded)
                layer.ScheduleRelease(Root);

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
                    // Flip below the target when there is no room above.
                    if (position.Y < 0 && bottomRight.Y + size.Y <= bounds.Y) position.Y = bottomRight.Y;
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
                    // Open up or to the left of the pointer when there is no room, instead of sliding under it.
                    if (position.X + size.X > bounds.X && position.X - size.X >= 0) position.X -= size.X;
                    if (position.Y + size.Y > bounds.Y && position.Y - size.Y >= 0) position.Y -= size.Y;
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
        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Right)
                Owner.Layer?.TryOpenContextMenu(e.ScreenSpaceMousePosition);

            return true;
        }

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnScroll(ScrollEvent e) => true;
    }
}
