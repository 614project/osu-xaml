using System;
using osu.Framework.Bindables;
using osu.Framework.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A single-line box that masks what is typed, like WPF's PasswordBox. <see cref="Password"/> binds two-way;
    /// <see cref="PasswordChar"/> picks the mask character. Built on osu!framework's password input type, so the
    /// text can't be copied out, word navigation is off and the platform's IME stays out of the way.
    /// </summary>
    public partial class PasswordBox : TextBox
    {
        private char passwordChar = '•';

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

                if (IsLoaded)
                    RefreshText();
            }
        }

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
