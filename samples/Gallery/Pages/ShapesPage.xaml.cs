using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class ShapesPage : UserControl
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);
    }
}
