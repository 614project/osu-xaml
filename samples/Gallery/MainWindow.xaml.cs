using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class MainWindow : Window
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);

        private void OnAbout() => MessageBox.Show(this, "WPF-style controls on osu!framework, written in compiled XAML.", "About osu!xaml gallery",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
