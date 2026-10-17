using System;
using System.Collections.Generic;
using CancelEventArgs = System.ComponentModel.CancelEventArgs;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

[assembly: OsuXaml.XamlGlobalProperties(typeof(OsuXaml.Controls.DialogService))]

namespace OsuXaml.Controls
{
    /// <summary>
    /// A modal dialog with custom content, in the spirit of WPF's <c>Window.ShowDialog()</c>. Make one in code or as a XAML root
    /// (<c>&lt;Dialog x:Class="App.SettingsDialog" Title="Settings"&gt;...&lt;/Dialog&gt;</c>) and show it with
    /// <c>bool? ok = await new SettingsDialog().ShowDialog(this)</c>. The update thread can't block, so <see cref="ShowDialog(Drawable)"/>
    /// returns a task that completes when the dialog closes. Setting <see cref="DialogResult"/> closes the dialog with that result;
    /// <see cref="Close"/>, the title bar's close button and Escape close it with false (unless a result was set).
    /// Enter clicks the button marked <c>IsDefault="True"</c>; Escape clicks the one marked <c>IsCancel="True"</c>, and clicking an
    /// IsCancel button closes the dialog with false, as in WPF. The dialog sizes to its content (within the window) unless it has
    /// a size, opens centred over a dimmed window and can be moved by its title bar.
    /// </summary>
    public partial class Dialog : ContentControl
    {
        private const float title_bar_height = 44;
        private const float window_margin = 24;

        private readonly TitleBar titleBar;
        private readonly TextBlock titleText;
        private readonly CloseButton closeButton;

        private string title;
        private bool showCloseButton = true;
        private MarginPadding padding;
        private bool? dialogResult;
        private ModalHost host;
        private TaskCompletionSource<bool?> completion;
        private bool closed;

        // Text boxes in the dialog whose Enter commits are watched, so Enter in a text field clicks the default button as in WPF.
        private readonly HashSet<TextBox> watchedTextBoxes = new HashSet<TextBox>();

        public Dialog()
        {
            Background = Theme.Base;
            BorderBrush = Theme.Border;
            BorderThickness = 1;
            CornerRadius = 10;

            Chrome.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Colour4.Black.Opacity(0.4f),
                Radius = 24,
                Offset = new Vector2(0, 6),
            };

            Chrome.Add(titleBar = new TitleBar(this)
            {
                RelativeSizeAxes = Axes.X,
                Height = title_bar_height,
                Children = new Drawable[]
                {
                    titleText = new TextBlock
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 20,
                        FontWeight = FontWeight.Bold,
                    },
                    closeButton = new CloseButton(this)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -8,
                    },
                },
            });

            Padding = new MarginPadding { Horizontal = 20, Top = 4, Bottom = 20 };
        }

        /// <summary>The caption shown in the title bar.</summary>
        public string Title
        {
            get => title;
            set
            {
                title = value;
                titleText.Text = value ?? string.Empty;
                applyTitleBar();
            }
        }

        /// <summary>Whether the title bar has a close button (true by default). Without a title or close button there is no title bar.</summary>
        public bool ShowCloseButton
        {
            get => showCloseButton;
            set
            {
                showCloseButton = value;
                closeButton.Alpha = value ? 1 : 0;
                applyTitleBar();
            }
        }

        /// <summary>Space around the content, below the title bar.</summary>
        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                padding = value;
                applyTitleBar();
            }
        }

        /// <summary>
        /// The result <see cref="ShowDialog(Drawable)"/> completes with. Setting it while the dialog is open closes the dialog,
        /// as in WPF: <c>DialogResult = true</c> in an OK button's handler.
        /// </summary>
        public bool? DialogResult
        {
            get => dialogResult;
            set
            {
                dialogResult = value;

                if (IsOpen)
                    Close();
            }
        }

        /// <summary>The element whose window the dialog opens over when <see cref="ShowDialog()"/> is called without one.</summary>
        public Drawable Owner { get; set; }

        public float MinWidth { get; set; }

        /// <summary>The widest the dialog sizes itself; it never grows past the window either way.</summary>
        public float MaxWidth { get; set; } = float.PositiveInfinity;

        public float MinHeight { get; set; }

        public float MaxHeight { get; set; } = float.PositiveInfinity;

        /// <summary>Whether the dialog is showing and hasn't started closing.</summary>
        public bool IsOpen => host != null && !closed;

        /// <summary>Raised before the dialog closes; set <c>Cancel</c> to keep it open.</summary>
        public event Action<CancelEventArgs> Closing;

        /// <summary>
        /// Raised once the dialog has closed, before the task from <see cref="ShowDialog(Drawable)"/> completes. When the window
        /// is removed while the dialog is open, it is raised as the dialog is disposed, which may be off the update thread.
        /// </summary>
        public event Action Closed;

        /// <summary>The modal host showing this dialog, once shown.</summary>
        public ModalHost Host => host;

        public override bool AcceptsFocus => true;

        /// <summary>Shows the dialog over the window of <see cref="Owner"/>, or over the game's window without one.</summary>
        public Task<bool?> ShowDialog() => ShowDialog(Owner);

        /// <summary>
        /// Shows the dialog modally over the window <paramref name="owner"/> is in (any element of it) and returns a task that
        /// completes with <see cref="DialogResult"/> when the dialog closes. A dialog can be shown once.
        /// </summary>
        public Task<bool?> ShowDialog(Drawable owner)
        {
            if (host != null || closed)
                throw new InvalidOperationException("A dialog can only be shown once. Create a new one to show it again.");

            if (Parent != null)
                throw new InvalidOperationException("A dialog that is already in the drawable tree can't be shown as a dialog.");

            var layer = PopupLayer.ForOwner(owner ?? Owner)
                        ?? throw new InvalidOperationException("There is no window to show the dialog in. Pass an element of the window as the owner.");

            Owner ??= owner;
            dialogResult = null;
            completion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            host = new ModalHost(layer, this);

            // Adding to the tree has to happen on the update thread.
            if (ThreadSafety.IsUpdateThread)
                layer.OpenModal(host);
            else
                layer.ScheduleOpenModal(host);

            return completion.Task;
        }

        /// <summary>Closes the dialog; the task completes with <see cref="DialogResult"/>, or false when none was set.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;

            var args = new CancelEventArgs();
            Closing?.Invoke(args);

            if (args.Cancel)
            {
                // As in WPF, a cancelled close forgets the result that triggered it.
                dialogResult = null;
                return;
            }

            closed = true;
            dialogResult ??= false;

            if (ThreadSafety.IsUpdateThread)
                host.Close();
            else
                host.ScheduleClose();

            Closed?.Invoke();
            completion.TrySetResult(dialogResult);
        }

        /// <summary>
        /// Called for the close button and for Escape when no IsCancel button handles it. Closes with false; message boxes pick
        /// their cancel result instead.
        /// </summary>
        protected virtual void OnCancel() => DialogResult = false;

        /// <summary>Called when an IsCancel button inside the dialog is clicked.</summary>
        internal void CancelButtonClicked() => OnCancel();

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!IsOpen)
                return base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.Enter:
                case Key.KeypadEnter:
                    if (e.Repeat)
                        return true;

                    DialogService.FindButton(this, cancel: false)?.TriggerClick();
                    return true;

                case Key.Escape:
                    if (e.Repeat)
                        return true;

                    // An open drop-down or menu inside the dialog closes first, even when the dialog itself has focus.
                    if (host.PopupLayer.DismissTop())
                        return true;

                    var cancelButton = DialogService.FindButton(this, cancel: true);

                    if (cancelButton != null)
                        cancelButton.TriggerClick();
                    else
                        OnCancel();

                    return true;
            }

            return base.OnKeyDown(e);
        }

        protected override void Update()
        {
            base.Update();

            // osu!framework text boxes take Enter themselves (they commit), so the dialog never sees it as a key press.
            // A plain TextBox or PasswordBox that gets focus is watched instead; ones inside other controls (an editable
            // ComboBox, a DataGrid cell editor) keep Enter for their control.
            if (IsOpen && GetContainingInputManager()?.FocusedDrawable is TextBox textBox && !(textBox is DataGridTextEditor)
                && !watchedTextBoxes.Contains(textBox) && DialogService.FindDialog(textBox) == this)
            {
                watchedTextBoxes.Add(textBox);
                textBox.OnCommit += onTextBoxCommit;
            }
        }

        private void onTextBoxCommit(osu.Framework.Graphics.UserInterface.TextBox sender, bool newText)
        {
            var keys = GetContainingInputManager()?.CurrentState.Keyboard.Keys;

            // Commits also happen when focus leaves a box that commits on focus loss; only Enter picks the default button.
            if (!IsOpen || keys == null || !(keys.IsPressed(Key.Enter) || keys.IsPressed(Key.KeypadEnter)))
                return;

            Schedule(() =>
            {
                if (IsOpen)
                    DialogService.FindButton(this, cancel: false)?.TriggerClick();
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            foreach (var textBox in watchedTextBoxes)
                textBox.OnCommit -= onTextBoxCommit;

            watchedTextBoxes.Clear();

            // The window went away with the dialog still open (WPF closes owned windows with their owner). Whoever awaits
            // ShowDialog still gets an answer: the result set so far, else false. This can run on osu!framework's disposal thread.
            if (host != null && !closed)
            {
                closed = true;
                dialogResult ??= false;
                Closed?.Invoke();
                completion.TrySetResult(dialogResult);
            }

            base.Dispose(isDisposing);
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var limit = maxSize();
            var desired = base.Measure(Vector2.ComponentMin(availableSize, limit));

            // The title and close button need room too.
            if (titleBar.Alpha > 0)
                desired.X = Math.Max(desired.X, titleText.DrawWidth + 20 + (showCloseButton ? 52 : 20));

            desired = Vector2.ComponentMax(desired, new Vector2(MinWidth, MinHeight));
            return Vector2.ComponentMin(desired, limit);
        }

        /// <summary>The largest size the dialog takes: its Max sizes, within the window less a margin.</summary>
        private Vector2 maxSize()
        {
            var size = new Vector2(MaxWidth, MaxHeight);

            if (Parent != null)
                size = Vector2.ComponentMin(size, Vector2.ComponentMax(Vector2.Zero, Parent.ChildSize - new Vector2(2 * window_margin)));

            return size;
        }

        private void applyTitleBar()
        {
            bool visible = !string.IsNullOrEmpty(title) || showCloseButton;
            titleBar.Alpha = visible ? 1 : 0;

            base.Padding = visible ? Layout.Add(padding, new MarginPadding { Top = title_bar_height }) : padding;
        }

        /// <summary>Keeps a dragged dialog's title bar inside the window.</summary>
        private void moveBy(Vector2 delta)
        {
            if (Parent == null)
                return;

            var bounds = Parent.ChildSize;
            var position = Position + delta;
            float spareX = (bounds.X + DrawWidth) / 2 - 60;

            position.X = Math.Clamp(position.X, -spareX, spareX);
            position.Y = Math.Clamp(position.Y, -(bounds.Y - DrawHeight) / 2, (bounds.Y + DrawHeight) / 2 - title_bar_height);
            Position = position;
        }

        /// <summary>The strip with the caption; dragging it moves the dialog.</summary>
        private partial class TitleBar : Container
        {
            private readonly Dialog dialog;

            public TitleBar(Dialog dialog)
            {
                this.dialog = dialog;
            }

            protected override bool OnDragStart(DragStartEvent e) => e.Button == MouseButton.Left;

            protected override void OnDrag(DragEvent e) => dialog.moveBy(e.Delta);
        }

        private partial class CloseButton : CompositeDrawable
        {
            private readonly Dialog dialog;
            private readonly Box highlight;
            private readonly SpriteIcon icon;

            public CloseButton(Dialog dialog)
            {
                this.dialog = dialog;

                Size = new Vector2(28);
                Masking = true;
                CornerRadius = 6;

                InternalChildren = new Drawable[]
                {
                    highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(12),
                        Icon = FontAwesome.Solid.Times,
                        Colour = Theme.SubtleText,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                highlight.FadeTo(0.1f, 100, Easing.OutQuint);
                icon.FadeColour(Theme.Text, 100, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                highlight.FadeOut(250, Easing.OutQuint);
                icon.FadeColour(Theme.SubtleText, 250, Easing.OutQuint);
            }

            protected override bool OnClick(ClickEvent e)
            {
                if (Alpha > 0)
                    dialog.OnCancel();

                return true;
            }
        }
    }

    /// <summary>
    /// <c>IsDefault</c> and <c>IsCancel</c> for buttons in a <see cref="Dialog"/>, like WPF's Button properties: every XAML element
    /// accepts <c>IsDefault="True"</c> (Enter clicks it) and <c>IsCancel="True"</c> (Escape clicks it, and clicking it closes the
    /// dialog with false).
    /// </summary>
    public static class DialogService
    {
        private sealed class Entry
        {
            public bool IsDefault;
            public bool IsCancel;
            public bool Hooked;
        }

        private static readonly ConditionalWeakTable<Drawable, Entry> entries = new ConditionalWeakTable<Drawable, Entry>();

        public static void SetIsDefault(Drawable element, bool value) => entry(element).IsDefault = value;

        public static bool GetIsDefault(Drawable element) => entries.TryGetValue(element, out var e) && e.IsDefault;

        public static void SetIsCancel(Drawable element, bool value)
        {
            var e = entry(element);
            e.IsCancel = value;

            if (value && !e.Hooked && element is Button button)
            {
                e.Hooked = true;
                button.Click += () =>
                {
                    if (GetIsCancel(button))
                        findDialog(button)?.CancelButtonClicked();
                };
            }
        }

        public static bool GetIsCancel(Drawable element) => entries.TryGetValue(element, out var e) && e.IsCancel;

        /// <summary>The first visible, enabled IsDefault (or IsCancel) element inside <paramref name="dialog"/>.</summary>
        internal static Drawable FindButton(Dialog dialog, bool cancel)
        {
            foreach (var pair in (IEnumerable<KeyValuePair<Drawable, Entry>>)entries)
            {
                var element = pair.Key;

                if ((cancel ? pair.Value.IsCancel : pair.Value.IsDefault) && usable(element) && findDialog(element) == dialog)
                    return element;
            }

            return null;
        }

        private static bool usable(Drawable element)
        {
            if (!element.IsLoaded || element is Button { IsEnabled: false })
                return false;

            for (Drawable d = element; d != null; d = d.Parent)
            {
                if (!d.IsPresent)
                    return false;
            }

            return true;
        }

        /// <summary>The nearest dialog <paramref name="element"/> is in, if any.</summary>
        internal static Dialog FindDialog(Drawable element) => findDialog(element);

        private static Dialog findDialog(Drawable element)
        {
            for (var p = element.Parent; p != null; p = p.Parent)
            {
                if (p is Dialog dialog)
                    return dialog;
            }

            return null;
        }

        private static Entry entry(Drawable element) => entries.GetValue(element, _ => new Entry());
    }
}
