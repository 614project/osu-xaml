using osu.Framework.Graphics;
using osu.Framework.Localisation;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A titled, bordered box around related controls, like WPF's GroupBox. The <c>Header</c> sits at the top inside the border;
    /// <c>BorderBrush</c>, <c>BorderThickness</c>, <c>CornerRadius</c>, <c>Background</c> and <c>Padding</c> work as on a Border.
    /// </summary>
    public partial class GroupBox : HeaderedContentControl
    {
        public GroupBox()
        {
            BorderBrush = Theme.Border;
            BorderThickness = 1;
            CornerRadius = 8;
            HeaderPadding = new MarginPadding { Left = 12, Right = 12, Top = 10, Bottom = 2 };
            Padding = new MarginPadding { Left = 12, Right = 12, Top = 6, Bottom = 12 };
            Foreground = Theme.Text;
        }

        protected override Drawable CreateHeaderTextContent(LocalisableString text)
            => new TextBlock { Text = text, Colour = Theme.SubtleText, FontSize = 14, FontWeight = FontWeight.Bold };
    }
}
