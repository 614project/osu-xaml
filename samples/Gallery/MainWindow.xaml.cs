using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;

namespace Gallery
{
    public partial class MainWindow : CompositeDrawable
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);
    }
}
