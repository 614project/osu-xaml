using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class LayoutPage : UserControl
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);
    }
}
