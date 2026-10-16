using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;

namespace OsuXaml.Controls
{
    /// <summary>
    /// osu!framework's colour picker (a saturation/value square, a hue slider and a hex box) under the name WPF toolkits use.
    /// <see cref="SelectedColor"/> binds two-way: <c>SelectedColor="{Bind Tint}"</c> with a <c>Bindable&lt;Colour4&gt;</c>,
    /// or set it to a literal such as <c>#89b4fa</c>. It is 300 wide by default and as tall as its parts.
    /// </summary>
    public partial class ColorPicker : BasicColourPicker
    {
        public ColorPicker()
        {
            Current.ValueChanged += e => SelectedColorChanged?.Invoke(e.NewValue);
        }

        /// <summary>The picked colour.</summary>
        public Bindable<Colour4> SelectedColor => Current;

        /// <summary>Raised when <see cref="SelectedColor"/> changes, with the new colour.</summary>
        public event Action<Colour4> SelectedColorChanged;

        protected override HSVColourPicker CreateHSVColourPicker() => new ThemedHSVColourPicker();

        protected override HexColourPicker CreateHexColourPicker() => new ThemedHexColourPicker();

        private partial class ThemedHSVColourPicker : BasicHSVColourPicker
        {
            public ThemedHSVColourPicker()
            {
                Background.Colour = Theme.Mantle;
            }
        }

        private partial class ThemedHexColourPicker : BasicHexColourPicker
        {
            public ThemedHexColourPicker()
            {
                Background.Colour = Theme.Mantle;
            }
        }
    }
}
