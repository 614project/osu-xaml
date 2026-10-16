using osu.Framework.Graphics;
using osu.Framework.Localisation;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A titled, bordered box around related controls, like WPF's GroupBox. The <c>Header</c> sits just above the border;
    /// <c>BorderBrush</c>, <c>BorderThickness</c>, <c>CornerRadius</c>, <c>Background</c> and <c>Padding</c> apply to the box as on a Border.
    /// </summary>
    public partial class GroupBox : HeaderedContentControl
    {
        public GroupBox()
            : base(headerAboveBorder: true)
        {
            BorderBrush = Theme.Border;
            BorderThickness = 1;
            CornerRadius = 8;
            HeaderPadding = new MarginPadding { Left = 4, Right = 4, Bottom = 6 };
            Padding = new MarginPadding(12);
        }

        protected override Drawable CreateHeaderTextContent(LocalisableString text)
            => new TextBlock { Text = text, Colour = Theme.SubtleText, FontSize = 14, FontWeight = FontWeight.Bold };
    }
}
