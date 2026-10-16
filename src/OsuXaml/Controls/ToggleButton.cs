using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A button that stays pressed, like WPF's ToggleButton: each click flips <see cref="IsChecked"/>, which binds two-way.
    /// Unchecked it is a quiet surface-coloured button; checked it fills with <see cref="CheckedBackground"/>.
    /// <see cref="Checked"/> and <see cref="Unchecked"/> are raised whenever the value changes, also from a binding.
    /// </summary>
    public partial class ToggleButton : Button
    {
        private readonly Box checkedFill;
        private Colour4 checkedBackground = Theme.Accent;
        private Colour4 checkedForeground = Theme.AccentText;
        private TextBlock ownText;

        public ToggleButton()
        {
            Background = Theme.Surface;
            Foreground = Theme.Text;

            // The fill goes between the background and the content, so a custom Background still shows while unchecked.
            Chrome.ChangeChildDepth(BackgroundBox, 2);
            Chrome.Add(checkedFill = new Box { RelativeSizeAxes = Axes.Both, Colour = checkedBackground, Alpha = 0, Depth = 1 });

            IsChecked.BindValueChanged(e =>
            {
                (e.NewValue ? Checked : Unchecked)?.Invoke();
                updateCheckedState(IsLoaded);
            });
        }

        /// <summary>Whether the button is pressed in. Binds two-way.</summary>
        public Bindable<bool> IsChecked { get; } = new Bindable<bool>();

        /// <summary>Raised when <see cref="IsChecked"/> becomes true.</summary>
        public event Action Checked;

        /// <summary>Raised when <see cref="IsChecked"/> becomes false.</summary>
        public event Action Unchecked;

        /// <summary>The fill shown while checked.</summary>
        public Colour4 CheckedBackground
        {
            get => checkedBackground;
            set
            {
                checkedBackground = value;
                checkedFill.Colour = value;
            }
        }

        /// <summary>The colour of text content while checked.</summary>
        public Colour4 CheckedForeground
        {
            get => checkedForeground;
            set
            {
                checkedForeground = value;
                updateCheckedState(false);
            }
        }

        /// <summary>Flips <see cref="IsChecked"/>. Called on click before <see cref="Button.Click"/> is raised.</summary>
        protected virtual void OnToggle()
        {
            if (!IsChecked.Disabled)
                IsChecked.Value = !IsChecked.Value;
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!IsEnabled)
                return false;

            OnToggle();
            return base.OnClick(e);
        }

        protected override Drawable CreateTextContent(LocalisableString text)
        {
            var drawable = base.CreateTextContent(text);
            ownText = drawable as TextBlock;
            return drawable;
        }

        protected override void OnContentPresented(Drawable drawable)
        {
            base.OnContentPresented(drawable);
            updateCheckedState(false);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateCheckedState(false);
        }

        private void updateCheckedState(bool animate)
        {
            bool isChecked = IsChecked.Value;
            double duration = animate ? 150 : 0;

            checkedFill.FadeTo(isChecked ? 1 : 0, duration, Easing.OutQuint);

            // Only text this button created follows the state; drawable content keeps its own colours.
            if (ownText != null && ReferenceEquals(PresentedContent, ownText))
                ownText.FadeColour(isChecked ? checkedForeground : Foreground, duration, Easing.OutQuint);
        }
    }
}
