using System;
using osu.Framework.Bindables;
using osu.Framework.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A single-line box that masks what is typed, like WPF's PasswordBox. <see cref="Password"/> binds two-way;
    /// <see cref="PasswordChar"/> picks the mask character and <see cref="RevealPassword"/> shows the password as it is.
    /// Built on osu!framework's password input type, so the text can't be copied out, word navigation is off and the
    /// platform's IME stays out of the way.
    /// </summary>
    public partial class PasswordBox : TextBox
    {
        private char passwordChar = '•';
        private bool revealPassword;

        public PasswordBox()
        {
            InputProperties = new TextInputProperties(TextInputType.Password, false);
        }

        /// <summary>The entered password. Binds two-way: <c>Password="{Bind Password}"</c>.</summary>
        public Bindable<string> Password => Current;

        /// <summary>The character shown for each typed character (a bullet by default).</summary>
        public char PasswordChar
        {
            get => passwordChar;
            set
            {
                if (passwordChar == value)
                    return;

                passwordChar = value;
                RedrawCharacters();
            }
        }

        /// <summary>
        /// Shows the password as plain text while true (Avalonia's <c>RevealPassword</c>), for a "show password" check box or a
        /// reveal button. Copying stays off either way.
        /// </summary>
        public bool RevealPassword
        {
            get => revealPassword;
            set
            {
                if (revealPassword == value)
                    return;

                revealPassword = value;
                RedrawCharacters();
            }
        }

        protected override bool MasksText => !revealPassword;

        /// <summary>Raised when the password changes.</summary>
        public event Action PasswordChanged;

        protected override char MaskCharacter => passwordChar;

        protected override void OnTextChanged(string text)
        {
            base.OnTextChanged(text);
            PasswordChanged?.Invoke();
        }
    }
}
