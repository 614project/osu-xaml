using osu.Framework;
using osu.Framework.Allocation;

namespace Gallery
{
    public partial class GalleryGame : Game
    {
        [Cached]
        private readonly GalleryViewModel viewModel = new GalleryViewModel();

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new MainWindow());
        }
    }
}
