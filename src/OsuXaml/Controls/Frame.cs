using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>When a <see cref="Frame"/> shows its own back/forward bar, with WPF's names.</summary>
    public enum NavigationUIVisibility
    {
        /// <summary>While there is history to move through.</summary>
        Automatic,

        Visible,

        /// <summary>Never (the default here; WPF's default is Automatic). Shells usually bind their own Back button instead.</summary>
        Hidden,
    }

    /// <summary>
    /// Shows one page at a time with back/forward history, like WPF's Frame. <see cref="Navigate"/> takes a <see cref="Page"/>,
    /// any drawable, or another object shown through <see cref="ContentTemplate"/> (or as text); initial content can be a
    /// child element (<c>&lt;Frame&gt;&lt;local:HomePage /&gt;&lt;/Frame&gt;</c>) or <c>Content="{Bind CurrentItem}"</c>.
    /// <see cref="GoBack"/> and <see cref="GoForward"/> walk the history; <see cref="CanGoBack"/> and <see cref="CanGoForward"/>
    /// are bindables, so a Back button can bind <c>IsEnabled</c> to them. Content in the history is kept alive, as WPF does
    /// for object navigation: going back shows the same page instance with its state. Content dropped from the history
    /// (the forward stack when navigating somewhere new, or <see cref="RemoveBackEntry"/>) is disposed. Pages slide and fade
    /// between each other, and the mouse's back/forward buttons and Alt+Left/Alt+Right navigate too.
    /// <see cref="NavigationUIVisibility"/> adds a bar with back/forward buttons and the page's title.
    /// </summary>
    public partial class Frame : CompositeDrawable, ILayoutElement
    {
        private const double transition_duration = 250;
        private const float slide_distance = 40;

        private const float bar_height = 40;

        private readonly Container<EntryPresenter> pageArea;
        private readonly NavigationBar bar;
        private NavigationUIVisibility navigationUIVisibility = NavigationUIVisibility.Hidden;

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
        private bool updating;

        public Frame()
        {
            NavigationService = new NavigationService(this);

            InternalChildren = new Drawable[]
            {
                pageArea = new Container<EntryPresenter>
                {
                    RelativeSizeAxes = Axes.Both,
                    // Pages slide in from the side; nothing should show outside the frame while they do.
                    Masking = true,
                },
                bar = new NavigationBar(this) { RelativeSizeAxes = Axes.X, Height = bar_height },
            };

            CanGoBack.BindValueChanged(_ => coerceCanNavigate());
            CanGoForward.BindValueChanged(_ => coerceCanNavigate());
            applyNavigationUI();
        }

        /// <summary>Whether the frame shows its own bar with back/forward buttons and the current page's title.</summary>
        public NavigationUIVisibility NavigationUIVisibility
        {
            get => navigationUIVisibility;
            set
            {
                navigationUIVisibility = value;
                applyNavigationUI();
            }
        }

        private bool barVisible => navigationUIVisibility == NavigationUIVisibility.Visible
                                   || (navigationUIVisibility == NavigationUIVisibility.Automatic && (backStack.Count > 0 || forwardStack.Count > 0));

        private void applyNavigationUI()
        {
            bool visible = barVisible;

            bar.Alpha = visible ? 1 : 0;
            pageArea.Padding = new MarginPadding { Top = visible ? bar_height : 0 };
            bar.Refresh();
            Layout.InvalidateMeasure(this);
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
            Layout.InvalidateMeasure(this);

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
            // Between the two writes one value is already new and the other still old; that isn't a bad write to put right.
            bool wasUpdating = updating;
            updating = true;

            try
            {
                CanGoBack.Value = backStack.Count > 0;
                CanGoForward.Value = forwardStack.Count > 0;
            }
            finally
            {
                updating = wasUpdating;
            }

            applyNavigationUI();
        }

        /// <summary>
        /// CanGoBack and CanGoForward are read-only in spirit: a value written through a two-way binding is put right. Binding
        /// copies the source's value before the link exists, so the correction is also repeated once the binding is in place.
        /// </summary>
        private void coerceCanNavigate()
        {
            if (updating || (CanGoBack.Value == backStack.Count > 0 && CanGoForward.Value == forwardStack.Count > 0))
                return;

            syncCanNavigate();
            Scheduler.AddOnce(pushCanNavigate);
        }

        /// <summary>Writes the true values out again so every bindable bound to them agrees, including ones bound after the bad write.</summary>
        private void pushCanNavigate()
        {
            updating = true;

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
                updating = false;
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

        public bool ReportsMeasureChanges => true;

        public Vector2 Measure(Vector2 availableSize)
        {
            float top = barVisible ? bar_height : 0;
            var desired = current?.Measure(Vector2.ComponentMax(Vector2.Zero, availableSize - new Vector2(0, top))) ?? Vector2.Zero;
            return desired + new Vector2(0, top);
        }

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

        /// <summary>The bar <see cref="NavigationUIVisibility"/> shows: back and forward buttons and the current page's title.</summary>
        private partial class NavigationBar : CompositeDrawable
        {
            private readonly Frame frame;
            private readonly NavigationButton backButton;
            private readonly NavigationButton forwardButton;
            private readonly TextBlock title;

            public NavigationBar(Frame frame)
            {
                this.frame = frame;

                InternalChildren = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Mantle },
                    new Box { RelativeSizeAxes = Axes.X, Height = 1, Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Colour = Theme.Border },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(4, 0),
                        Padding = new MarginPadding { Left = 8 },
                        Children = new Drawable[]
                        {
                            backButton = new NavigationButton(FontAwesome.Solid.ArrowLeft, frame.GoBack),
                            forwardButton = new NavigationButton(FontAwesome.Solid.ArrowRight, frame.GoForward),
                            title = new TextBlock { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Margin = new MarginPadding { Left = 8 }, FontWeight = FontWeight.Bold },
                        },
                    },
                };
            }

            public override bool PropagatePositionalInputSubTree => Alpha > 0 && base.PropagatePositionalInputSubTree;

            public void Refresh()
            {
                backButton.IsEnabled = frame.backStack.Count > 0;
                forwardButton.IsEnabled = frame.forwardStack.Count > 0;
            }

            protected override void Update()
            {
                base.Update();

                // Page titles are plain properties, so the bar follows them here.
                string text = (frame.content as Page)?.Title ?? string.Empty;
                if (title.Text.ToString() != text)
                    title.Text = text;
            }

            // Clicks on the bar's background stay on the bar.
            protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;
        }

        private partial class NavigationButton : CompositeDrawable
        {
            private readonly Action action;
            private readonly Box highlight;
            private bool isEnabled;

            public NavigationButton(IconUsage icon, Action action)
            {
                this.action = action;

                Size = new Vector2(28);
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                Masking = true;
                CornerRadius = 6;

                InternalChildren = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(12),
                        Icon = icon,
                        Colour = Theme.Text,
                    },
                };
            }

            public bool IsEnabled
            {
                get => isEnabled;
                set
                {
                    isEnabled = value;
                    this.FadeTo(value ? 1 : 0.35f, 150, Easing.OutQuint);
                    if (!value) highlight.FadeOut(150, Easing.OutQuint);
                }
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (isEnabled)
                    highlight.FadeTo(0.1f, 100, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => highlight.FadeOut(250, Easing.OutQuint);

            protected override bool OnClick(ClickEvent e)
            {
                if (isEnabled)
                    action();
                return true;
            }
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
