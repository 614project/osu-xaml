using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class WelcomePage : Page
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);

        private void OnPeople() => NavigationService.Navigate(new PeoplePage());
    }
}
