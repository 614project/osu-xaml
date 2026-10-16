using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>The buttons a <see cref="MessageBox"/> shows, with WPF's names and values.</summary>
    public enum MessageBoxButton
    {
        OK = 0,
        OKCancel = 1,
        YesNoCancel = 3,
        YesNo = 4,
    }

    /// <summary>The icon a <see cref="MessageBox"/> shows, with WPF's names (and aliases) and values.</summary>
    public enum MessageBoxImage
    {
        None = 0,
        Hand = 16,
        Stop = 16,
        Error = 16,
        Question = 32,
        Exclamation = 48,
        Warning = 48,
        Asterisk = 64,
        Information = 64,
    }

    /// <summary>Which button closed a <see cref="MessageBox"/>, with WPF's names and values.</summary>
    public enum MessageBoxResult
    {
        None = 0,
        OK = 1,
        Cancel = 2,
        Yes = 6,
        No = 7,
    }

    /// <summary>
    /// Shows a modal message, the way WPF code does: <c>var answer = await MessageBox.Show(this, "Delete 3 files?", "Confirm",
    /// MessageBoxButton.YesNo, MessageBoxImage.Warning)</c>. The update thread can't wait, so Show returns a task (or calls back)
    /// with the button that closed the box. <c>owner</c> is any element of the window the box covers; without one the box opens
    /// over the game's window. Enter picks the default button, Escape (and the close button) picks Cancel, No for a Yes/No box,
    /// or OK for an OK box. Clicking outside does nothing. Boxes can stack, and Show may be called from any thread.
    /// </summary>
    public static class MessageBox
    {
        /// <summary>
        /// Shows a message over the window <paramref name="owner"/> is in (any element of it; null means the game's window) and
        /// returns a task that completes with the button that closed the box.
        /// </summary>
        public static Task<MessageBoxResult> Show(Drawable owner, string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK,
                                                  MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            var dialog = new MessageBoxDialog(messageBoxText, caption, button, icon, defaultResult);
            dialog.ShowDialog(owner);
            return dialog.Completion;
        }

        /// <summary>Shows a message over the game's window.</summary>
        public static Task<MessageBoxResult> Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK,
                                                  MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
            => Show(null, messageBoxText, caption, button, icon, defaultResult);

        /// <summary>
        /// Shows a message and calls <paramref name="closed"/> with the button that closed it, on the thread that closed it
        /// (the update thread when the user answers).
        /// </summary>
        public static void Show(Drawable owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, Action<MessageBoxResult> closed)
            => Show(owner, messageBoxText, caption, button, icon, MessageBoxResult.None, closed);

        /// <summary>Shows a message with a default button and calls <paramref name="closed"/> with the button that closed it.</summary>
        public static void Show(Drawable owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult,
                                Action<MessageBoxResult> closed)
        {
            var dialog = new MessageBoxDialog(messageBoxText, caption, button, icon, defaultResult);
            dialog.Closed += () => closed?.Invoke(dialog.Result);
            dialog.ShowDialog(owner);
        }
    }

    /// <summary>
    /// The dialog <see cref="MessageBox"/> shows: the icon and the message (wrapped at a readable width, one paragraph per line
    /// break) above a footer of right-aligned buttons, the default one accented.
    /// </summary>
    public sealed partial class MessageBoxDialog : Dialog
    {
        private const float max_text_width = 440;

        // Continuations run after Close has finished, not inside it.
        private readonly TaskCompletionSource<MessageBoxResult> completion = new TaskCompletionSource<MessageBoxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<MessageText> paragraphs = new List<MessageText>();
        private readonly MessageBoxResult cancelResult;

        public MessageBoxDialog(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            Text = messageBoxText ?? string.Empty;
            Button = button;
            Image = icon;

            Title = caption;
            Padding = new MarginPadding();

            var results = resultsFor(button);
            cancelResult = button switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                _ => MessageBoxResult.Cancel,
            };

            if (!results.Contains(defaultResult))
                defaultResult = results[0];

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Layout.SetHorizontalAlignment(buttons, HorizontalAlignment.Right);

            foreach (var result in results)
                buttons.Add(createButton(result, result == defaultResult));

            var footer = new Border { Background = Theme.Mantle, Padding = new MarginPadding { Horizontal = 20, Vertical = 12 }, Child = buttons };
            DockPanel.SetDock(footer, Dock.Bottom);

            var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new MarginPadding { Left = 20, Right = 24, Top = 4, Bottom = 20 } };

            if (icon != MessageBoxImage.None)
            {
                var (iconUsage, colour) = iconFor(icon);
                var image = new SpriteIcon { Icon = iconUsage, Colour = colour, Size = new Vector2(30) };
                Layout.SetVerticalAlignment(image, VerticalAlignment.Top);
                body.Add(image);
            }

            var text = new StackPanel { Spacing = 6 };
            Layout.SetVerticalAlignment(text, VerticalAlignment.Center);

            // TextBlock wraps but doesn't break lines, so each line of the message is a paragraph of its own.
            foreach (string line in Text.Replace("\r\n", "\n").Split('\n'))
            {
                var paragraph = new MessageText { Text = line.Length > 0 ? line : " " };
                paragraphs.Add(paragraph);
                text.Add(paragraph);
            }

            body.Add(text);

            var layout = new DockPanel();
            layout.Add(footer);
            layout.Add(body);
            Content = layout;

            Closed += () => completion.TrySetResult(Result);
        }

        /// <summary>The message.</summary>
        public string Text { get; }

        /// <summary>Which buttons the box has.</summary>
        public MessageBoxButton Button { get; }

        /// <summary>The icon.</summary>
        public MessageBoxImage Image { get; }

        /// <summary>The button that closed the box; None while it is open.</summary>
        public MessageBoxResult Result { get; private set; }

        /// <summary>Completes with <see cref="Result"/> when the box closes.</summary>
        public Task<MessageBoxResult> Completion => completion.Task;

        /// <summary>Closes the box as if <paramref name="result"/>'s button was clicked.</summary>
        public void Choose(MessageBoxResult result)
        {
            if (!IsOpen)
                return;

            Result = result;
            DialogResult = result == MessageBoxResult.OK || result == MessageBoxResult.Yes;
        }

        protected override void OnCancel() => Choose(cancelResult);

        protected override void Update()
        {
            base.Update();

            // Long messages wrap at a comfortable reading width, narrower when the window is.
            float width = Parent == null ? max_text_width : Math.Clamp(Parent.ChildSize.X - 160, 160, max_text_width);

            foreach (var paragraph in paragraphs)
                paragraph.MaxLineWidth = width;
        }

        private Button createButton(MessageBoxResult result, bool isDefault)
        {
            var button = new Button { Content = result.ToString(), Width = 88 };

            if (isDefault)
                DialogService.SetIsDefault(button, true);
            else
            {
                button.Background = Theme.Surface;
                button.Foreground = Theme.Text;
            }

            button.Click += () => Choose(result);
            return button;
        }

        private static MessageBoxResult[] resultsFor(MessageBoxButton button) => button switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.OK },
        };

        private static (IconUsage, Colour4) iconFor(MessageBoxImage image) => image switch
        {
            MessageBoxImage.Error => (FontAwesome.Solid.TimesCircle, Theme.Danger),
            MessageBoxImage.Question => (FontAwesome.Solid.QuestionCircle, Theme.Accent),
            MessageBoxImage.Warning => (FontAwesome.Solid.ExclamationTriangle, Theme.Warning),
            _ => (FontAwesome.Solid.InfoCircle, Theme.Accent),
        };

        /// <summary>Message text that wraps at <see cref="MaxLineWidth"/> even where its panel offers unlimited width.</summary>
        private partial class MessageText : TextBlock, ILayoutElement
        {
            public float MaxLineWidth = max_text_width;

            public MessageText()
            {
                TextWrapping = TextWrapping.Wrap;
            }

            Vector2 ILayoutElement.Measure(Vector2 availableSize) => Measure(new Vector2(Math.Min(availableSize.X, MaxLineWidth), availableSize.Y));
        }
    }
}
