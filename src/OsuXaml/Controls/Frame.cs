using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Shows one page at a time with back/forward history, like WPF's Frame. <see cref="Navigate"/> takes a <see cref="Page"/>,
    /// any drawable, or another object shown through <see cref="ContentTemplate"/> (or as text); initial content can be a
    /// child element (<c>&lt;Frame&gt;&lt;local:HomePage /&gt;&lt;/Frame&gt;</c>) or <c>Content="{Bind CurrentItem}"</c>.
    /// <see cref="GoBack"/> and <see cref="GoForward"/> walk the history; <see cref="CanGoBack"/> and <see cref="CanGoForward"/>
    /// are bindables, so a Back button can bind <c>IsEnabled</c> to them. Content in the history is kept alive, as WPF does
    /// for object navigation: going back shows the same page instance with its state. Content dropped from the history
    /// (the forward stack when navigating somewhere new, or <see cref="RemoveBackEntry"/>) is disposed. Pages slide and fade
    /// between each other, and the mouse's back/forward buttons and Alt+Left/Alt+Right navigate too.
    /// </summary>
    public partial class Frame : CompositeDrawable, ILayoutElement
    {
        private const double transition_duration = 250;
        private const float slide_distance = 40;

        private readonly Container<EntryPresenter> pageArea;

        // Most recent last; BackStack and ForwardStack list them most recent first, as WPF does.
        private readonly List<object> backStack = new List<object>();
        private readonly List<object> forwardStack = new List<object>();

        // One presenter per piece of content, so content that appears in the history more than once is shown by one drawable.
        private readonly Dictionary<object, EntryPresenter> presenters = new Dictionary<object, EntryPresenter>(ReferenceEqualityComparer.Instance);

        private object content;
        private EntryPresenter current;
        private DataTemplate contentTemplate;
        private bool sizeToContentX;
        private bool sizeToContentY;
        private bool pushing;

        public Frame()
        {
            NavigationService = new NavigationService(this);

            InternalChild = pageArea = new Container<EntryPresenter>
            {
                RelativeSizeAxes = Axes.Both,
                // Pages slide in from the side; nothing should show outside the frame while they do.
                Masking = true,
            };

            CanGoBack.BindValueChanged(_ => coerceCanNavigate());
            CanGoForward.BindValueChanged(_ => coerceCanNavigate());
        }

        /// <summary>Navigates the frame; pages hosted by it get this as <see cref="Page.NavigationService"/>.</summary>
        public NavigationService NavigationService { get; }

        /// <summary>Whether there is an entry to go back to. Bind a Back button's IsEnabled to it.</summary>
        public Bindable<bool> CanGoBack { get; } = new Bindable<bool>();

        /// <summary>Whether there is an entry to go forward to.</summary>
        public Bindable<bool> CanGoForward { get; } = new Bindable<bool>();

        /// <summary>The content shown now. Setting it navigates to the new content, as in WPF.</summary>
        public object Content
        {
            get => content;
            set => Navigate(value);
        }

        /// <summary>The initial content as a XAML child element: <c>&lt;Frame&gt;&lt;local:HomePage /&gt;&lt;/Frame&gt;</c>.</summary>
        public Drawable Child
        {
            get => content as Drawable;
            set => Navigate(value);
        }

        /// <summary>Builds the drawable for content that isn't a drawable (a view model), as ContentControl does.</summary>
        public DataTemplate ContentTemplate
        {
            get => contentTemplate;
            set
            {
                contentTemplate = value;

                foreach (var presenter in presenters.Values)
                    presenter.ContentTemplate = value;
            }
        }

        /// <summary>The content of the entries behind the current one, most recent first.</summary>
        public IReadOnlyList<object> BackStack => Enumerable.Reverse(backStack).ToArray();

        /// <summary>The content of the entries ahead of the current one, nearest first.</summary>
        public IReadOnlyList<object> ForwardStack => Enumerable.Reverse(forwardStack).ToArray();

        /// <summary>The drawable showing the current content (the page itself, or what the template built), once loaded.</summary>
        public Drawable PresentedContent => current?.PresentedContent;

        /// <summary>Raised before each navigation; set <c>Cancel</c> to stay on the current content.</summary>
        public event Action<NavigatingCancelEventArgs> Navigating;

        /// <summary>Raised after each navigation, once the new content is in place.</summary>
        public event Action<NavigationEventArgs> Navigated;

        /// <summary>
        /// Shows <paramref name="newContent"/> and puts the current content on the back stack. The forward stack is dropped and its
        /// content disposed (unless it is still in the history or is the new content). Returns false when nothing changed: the
        /// content is already shown, or a <see cref="Navigating"/> handler cancelled. Call from the update thread.
        /// </summary>
        public bool Navigate(object newContent)
        {
            if (ReferenceEquals(newContent, content) || !raiseNavigating(newContent, NavigationMode.New))
                return false;

            if (content != null)
                backStack.Add(content);

            var dropped = forwardStack.ToArray();
            forwardStack.Clear();

            show(newContent, NavigationMode.New);

            foreach (object entry in dropped)
                releaseIfUnused(entry);

            return true;
        }

        /// <summary>Goes to the previous entry. Does nothing when <see cref="CanGoBack"/> is false.</summary>
        public void GoBack()
        {
            if (backStack.Count == 0 || !raiseNavigating(backStack[^1], NavigationMode.Back))
                return;

            object target = backStack[^1];
            backStack.RemoveAt(backStack.Count - 1);

            if (content != null)
                forwardStack.Add(content);

            show(target, NavigationMode.Back);
        }

        /// <summary>Goes to the next entry after going back. Does nothing when <see cref="CanGoForward"/> is false.</summary>
        public void GoForward()
        {
            if (forwardStack.Count == 0 || !raiseNavigating(forwardStack[^1], NavigationMode.Forward))
                return;

            object target = forwardStack[^1];
            forwardStack.RemoveAt(forwardStack.Count - 1);

            if (content != null)
                backStack.Add(content);

            show(target, NavigationMode.Forward);
        }

        /// <summary>Removes the most recent back entry and returns its content (disposed unless it is still in use), or null.</summary>
        public object RemoveBackEntry()
        {
            if (backStack.Count == 0)
                return null;

            object entry = backStack[^1];
            backStack.RemoveAt(backStack.Count - 1);
            syncCanNavigate();
            releaseIfUnused(entry);
            return entry;
        }

        private bool raiseNavigating(object target, NavigationMode mode)
        {
            if (Navigating == null)
                return true;

            var args = new NavigatingCancelEventArgs(this, target, mode);
            Navigating.Invoke(args);
            return !args.Cancel;
        }

        private void show(object newContent, NavigationMode mode)
        {
            var outgoing = current;

            content = newContent;
            current = newContent == null ? null : presenterFor(newContent);
            syncCanNavigate();

            bool animate = IsLoaded && outgoing != current;
            int direction = mode == NavigationMode.Back ? -1 : 1;

            if (outgoing != null && outgoing != current)
                hide(outgoing, direction, animate);

            if (current != null)
            {
                current.Active = true;
                current.ClearTransforms();

                if (current.Parent == null)
                    pageArea.Add(current);

                // The newest page draws above one that is still sliding out.
                pageArea.ChangeChildDepth(current, outgoing == null ? 0 : outgoing.Depth - 1);

                if (animate)
                {
                    current.MoveToX(direction * slide_distance).MoveToX(0, transition_duration, Easing.OutQuint);
                    current.FadeInFromZero(transition_duration * 0.8, Easing.OutQuint);
                }
                else
                {
                    current.X = 0;
                    current.Alpha = 1;
                }
            }

            Navigated?.Invoke(new NavigationEventArgs(this, newContent, mode));
        }

        private void hide(EntryPresenter presenter, int direction, bool animate)
        {
            presenter.Active = false;

            // Keys typed after navigating shouldn't reach a page that is leaving.
            var input = GetContainingInputManager();
            if (input?.FocusedDrawable != null && isWithin(input.FocusedDrawable, presenter))
                GetContainingFocusManager()?.ChangeFocus(null);

            if (!animate)
            {
                retire(presenter);
                return;
            }

            presenter.ClearTransforms();
            presenter.MoveToX(-direction * slide_distance, transition_duration, Easing.OutQuint);
            presenter.FadeOut(transition_duration * 0.6, Easing.OutQuint);

            // Runs on the frame's scheduler: a drawable must not remove itself while it updates. A page that came back and
            // left again before this runs is retired by its latest exit instead.
            int exit = ++presenter.Exits;
            Scheduler.AddDelayed(() =>
            {
                if (presenter.Exits == exit)
                    retire(presenter);
            }, transition_duration);
        }

        /// <summary>Takes a page that has finished leaving out of the tree, keeping it while the history still holds it.</summary>
        private void retire(EntryPresenter presenter)
        {
            if (presenter.Active || presenter.Disposed)
                return;

            bool inUse = presenter.Content != null && presenters.TryGetValue(presenter.Content, out var known) && known == presenter;

            if (presenter.Parent == pageArea)
                pageArea.Remove(presenter, !inUse);
            else if (!inUse)
                presenter.Dispose();
        }

        private EntryPresenter presenterFor(object entry)
        {
            if (presenters.TryGetValue(entry, out var presenter))
                return presenter;

            presenter = new EntryPresenter
            {
                RelativeSizeAxes = Axes.Both,
                ContentTemplate = contentTemplate,
                Content = entry,
            };

            if (entry is Page page)
                page.NavigationService = NavigationService;

            presenters.Add(entry, presenter);
            return presenter;
        }

        /// <summary>Disposes content that is no longer current and no longer in the history.</summary>
        private void releaseIfUnused(object entry)
        {
            if (entry == null || ReferenceEquals(entry, content) || backStack.Contains(entry, ReferenceEqualityComparer.Instance)
                || forwardStack.Contains(entry, ReferenceEqualityComparer.Instance))
                return;

            if (!presenters.Remove(entry, out var presenter))
                return;

            if (entry is Page page && page.NavigationService == NavigationService)
                page.NavigationService = null;

            // A page still sliding out is disposed when its animation ends; one already out of the tree goes now.
            if (presenter.Parent == null)
                presenter.Dispose();
            else if (!presenter.Active && presenter.Alpha == 0)
                retire(presenter);
        }

        private void syncCanNavigate()
        {
            CanGoBack.Value = backStack.Count > 0;
            CanGoForward.Value = forwardStack.Count > 0;
        }

        /// <summary>
        /// CanGoBack and CanGoForward are read-only in spirit: a value written through a two-way binding is put right. Binding
        /// copies the source's value before the link exists, so the correction is also repeated once the binding is in place.
        /// </summary>
        private void coerceCanNavigate()
        {
            if (pushing || (CanGoBack.Value == backStack.Count > 0 && CanGoForward.Value == forwardStack.Count > 0))
                return;

            syncCanNavigate();
            Scheduler.AddOnce(pushCanNavigate);
        }

        /// <summary>Writes the true values out again so every bindable bound to them agrees, including ones bound after the bad write.</summary>
        private void pushCanNavigate()
        {
            pushing = true;

            try
            {
                // Setting an equal value doesn't reach bound bindables, so the value flips and flips back.
                foreach (var (bindable, value) in new[] { (CanGoBack, backStack.Count > 0), (CanGoForward, forwardStack.Count > 0) })
                {
                    bindable.Value = !value;
                    bindable.Value = value;
                }
            }
            finally
            {
                pushing = false;
            }
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

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            switch (e.Button)
            {
                // The mouse's back and forward side buttons.
                case MouseButton.Button1 when CanGoBack.Value:
                    GoBack();
                    return true;

                case MouseButton.Button2 when CanGoForward.Value:
                    GoForward();
                    return true;
            }

            return false;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!e.AltPressed || e.ControlPressed || e.ShiftPressed || e.SuperPressed || e.Repeat)
                return false;

            // Only the frame the keyboard focus is in (or any frame, when nothing has focus) answers; inner frames go first.
            var focused = GetContainingInputManager()?.FocusedDrawable;
            if (focused != null && focused != this && !isWithin(focused, this))
                return false;

            switch (e.Key)
            {
                case Key.Left when CanGoBack.Value:
                    GoBack();
                    return true;

                case Key.Right when CanGoForward.Value:
                    GoForward();
                    return true;
            }

            return false;
        }

        public Vector2 Measure(Vector2 availableSize) => current?.Measure(availableSize) ?? Vector2.Zero;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && !AutoSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && !AutoSizeAxes.HasFlag(Axes.Y) && Height == 0;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (sizeToContentX || sizeToContentY)
            {
                var desired = Measure(new Vector2(float.PositiveInfinity));
                if (sizeToContentX) Width = desired.X;
                if (sizeToContentY) Height = desired.Y;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            // Pages kept for the history are outside the tree, so they aren't disposed with the frame's children.
            foreach (var presenter in presenters.Values)
            {
                if (presenter.Parent == null)
                    presenter.Dispose();
            }

            presenters.Clear();
            base.Dispose(isDisposing);
        }

        /// <summary>Shows one entry's content; only the current entry takes input.</summary>
        private partial class EntryPresenter : ContentPresenter
        {
            public bool Active = true;
            public int Exits;

            public bool Disposed => IsDisposed;

            public override bool PropagatePositionalInputSubTree => Active && base.PropagatePositionalInputSubTree;

            public override bool PropagateNonPositionalInputSubTree => Active && base.PropagateNonPositionalInputSubTree;
        }
    }
}
