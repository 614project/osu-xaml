using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class MainWindow : Window
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);
    }
}
