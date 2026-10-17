using osu.Framework.Allocation;
using OsuXaml.Controls;

namespace Gallery
{
    public partial class DialogsPage : UserControl
    {
        private GalleryViewModel viewModel;

        [BackgroundDependencyLoader]
        private void load(GalleryViewModel viewModel)
        {
            this.viewModel = viewModel;
            InitializeComponent(viewModel);
        }

        // MessageBox.Show can't block the update thread, so it returns a task to await, unlike WPF.
        private async void OnInformation()
        {
            var result = await MessageBox.Show(this, "The file was saved to your Documents folder.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            viewModel.Status.Value = result.ToString();
        }

        private async void OnQuestion()
        {
            var result = await MessageBox.Show(this, "Save changes to Untitled before closing?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            viewModel.Status.Value = result.ToString();
        }

        private async void OnWarning()
        {
            var result = await MessageBox.Show(this, "Delete 3 files? This can't be undone.", "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            viewModel.Status.Value = result.ToString();
        }

        private async void OnError()
        {
            var result = await MessageBox.Show(this, "The network is unreachable.\nCheck your connection and try again.", "Connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
            viewModel.Status.Value = result.ToString();
        }

        private async void OnSettings()
        {
            bool? saved = await new SettingsDialog().ShowDialog(this);
            viewModel.Status.Value = saved == true ? "Settings saved" : "Settings cancelled";
        }
    }
}
