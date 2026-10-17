using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class PeoplePage : Page
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);

        private void OnBack()
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }
    }
}
