using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class ListsPage : UserControl
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);
    }
}
