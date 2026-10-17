using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Dialog as a XAML root with code-behind that awaits ShowDialog, the way a WPF developer writes a settings window.</summary>
    [TestFixture]
    public class DialogGeneratorTests
    {
        private const string code_behind = @"
using System.ComponentModel;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using OsuXaml.Controls;

namespace App
{
    public class SettingsViewModel
    {
        public Bindable<string> UserName { get; } = new Bindable<string>(string.Empty);
        public BindableBool Notifications { get; } = new BindableBool();
        public BindableBool CanSave { get; } = new BindableBool(true);
    }

    public partial class SettingsDialog : Dialog
    {
        public SettingsDialog(SettingsViewModel viewModel) => InitializeComponent(viewModel);

        private void OnSave() => DialogResult = true;

        private void OnClosing(CancelEventArgs e) => e.Cancel = false;
    }

    // Code-behind elsewhere in the app opens it the WPF way, without blocking the update thread.
    public partial class MainView : UserControl
    {
        public async Task<bool> EditSettings(SettingsViewModel viewModel)
        {
            bool? ok = await new SettingsDialog(viewModel) { Owner = this }.ShowDialog();
            bool? again = await new SettingsDialog(viewModel).ShowDialog(this);
            var answer = await MessageBox.Show(this, ""Discard changes?"", ""Settings"", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            MessageBox.Show(this, ""Saved"", ""Settings"", MessageBoxButton.OK, MessageBoxImage.Information, result => { });
            return ok == true && again != false && answer == MessageBoxResult.Yes;
        }
    }
}";

        private const string settings_xaml = @"
<Dialog xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:App""
        x:Class=""App.SettingsDialog"" x:DataType=""local:SettingsViewModel""
        Title=""Settings"" MinWidth=""360"" MaxWidth=""600"" Closing=""OnClosing"">
  <StackPanel Spacing=""12"">
    <TextBlock Text=""Name"" />
    <TextBox Text=""{Bind UserName}"" />
    <CheckBox Content=""Enable notifications"" IsChecked=""{Bind Notifications}"" />
    <StackPanel Orientation=""Horizontal"" Spacing=""8"" HorizontalAlignment=""Right"">
      <Button Content=""Save"" IsDefault=""True"" Click=""OnSave"" IsEnabled=""{Bind CanSave}"" />
      <Button Content=""Cancel"" IsCancel=""True"" />
    </StackPanel>
  </StackPanel>
</Dialog>";

        [Test]
        public void DialogRootWithTitleContentAndDefaultAndCancelButtons()
        {
            string source = GeneratorRunner.Compiles(code_behind, settings_xaml);

            Assert.That(source, Does.Contain("this.Title = \"Settings\""));
            Assert.That(source, Does.Contain("this.MinWidth = 360f"));
            Assert.That(source, Does.Contain("DialogService.SetIsDefault("));
            Assert.That(source, Does.Contain("DialogService.SetIsCancel("));
            Assert.That(source, Does.Contain("OnClosing"));
        }

        [Test]
        public void DialogAsAnElementWithAPropertyElementTitle()
        {
            GeneratorRunner.Compiles(code_behind, @"
<UserControl xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""App.MainView"">
  <Dialog ShowCloseButton=""False"" Padding=""24"">
    <Dialog.Title>About</Dialog.Title>
    <TextBlock Text=""OsuXaml"" />
  </Dialog>
</UserControl>", ("SettingsDialog.xaml", settings_xaml));
        }

        [Test]
        public void IsDefaultTakesABoolean()
        {
            var error = GeneratorRunner.SingleError(code_behind, @"
<Dialog xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""App.SettingsDialog"" Title=""Settings"">
  <Button Content=""OK"" IsDefault=""Sometimes"" />
</Dialog>");

            Assert.That(error.Id, Is.EqualTo("OX0005"));
        }
    }
}
