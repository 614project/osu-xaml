using System;
using System.Text;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A text label for another control, like WPF's Label. Clicking it focuses <see cref="Target"/>, and an underscore in text
    /// content marks an access key: <c>Content="_Name"</c> shows "Name", and Alt+N focuses the target. Write "__" for a literal underscore.
    /// </summary>
    public partial class Label : ContentControl
    {
        private Key? accessKey;

        public Label()
        {
            Padding = new MarginPadding { Vertical = 5 };
            VerticalContentAlignment = VerticalAlignment.Center;
        }

        /// <summary>The control that receives focus when the label is clicked or its access key is pressed.</summary>
        public Drawable Target { get; set; }

        /// <summary>The key that, with Alt, focuses <see cref="Target"/>; taken from the underscore in the text.</summary>
        public Key? AccessKey => accessKey;

        /// <summary>Moves focus to <see cref="Target"/> if it can take it. Returns whether it did.</summary>
        public bool FocusTarget()
        {
            if (Target == null || !Target.IsLoaded || !Target.AcceptsFocus)
                return false;

            var focusManager = GetContainingFocusManager();
            if (focusManager == null)
                return false;

            focusManager.ChangeFocus(Target);
            return true;
        }

        protected override Drawable CreateTextContent(LocalisableString text)
        {
            string shown = ParseAccessText(text.ToString(), out char? key);
            accessKey = key == null ? null : keyFor(key.Value);
            return new TextBlock { Text = shown, Colour = Foreground };
        }

        // The click moves focus itself; the framework must not take it away again afterwards.
        public override bool ChangeFocusOnClick => false;

        protected override bool OnClick(ClickEvent e) => FocusTarget();

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (accessKey != null && e.AltPressed && !e.ControlPressed && e.Key == accessKey && FocusTarget())
                return true;

            return base.OnKeyDown(e);
        }

        /// <summary>Removes the access key marker from <paramref name="text"/>: the character after a single underscore becomes the key.</summary>
        public static string ParseAccessText(string text, out char? key)
        {
            key = null;

            if (string.IsNullOrEmpty(text) || text.IndexOf('_') < 0)
                return text;

            var result = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '_' && i + 1 < text.Length)
                {
                    i++;

                    if (text[i] != '_' && key == null)
                        key = text[i];
                }

                result.Append(text[i]);
            }

            return result.ToString();
        }

        private static Key? keyFor(char c)
        {
            c = char.ToUpperInvariant(c);

            if (c >= 'A' && c <= 'Z')
                return Key.A + (c - 'A');

            if (c >= '0' && c <= '9')
                return Key.Number0 + (c - '0');

            return null;
        }
    }
}
