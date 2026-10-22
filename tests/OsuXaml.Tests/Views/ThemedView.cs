using OsuXaml.Controls;

namespace OsuXaml.Tests.Views
{
    /// <summary>A view using {ThemeResource} on its root, on attached properties, in a style, on a brush and with a key no theme has.</summary>
    public partial class ThemedView : UserControl
    {
        public ThemedView()
        {
            InitializeComponent();
        }

        public StackPanel Panel => panel;

        public TextBlock Primary => primary;

        public TextBlock Secondary => secondary;

        public Border Card => card;

        public Rectangle Swatch => swatch;

        public TextBlock Missing => missing;
    }
}
