using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class SettingsDialog : Dialog
    {
        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel) => InitializeComponent(viewModel);

        // Setting DialogResult closes the dialog and completes ShowDialog's task with it, as in WPF.
        private void OnSave() => DialogResult = true;
    }
}
