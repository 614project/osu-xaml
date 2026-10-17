using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Page and Frame written the way a WPF developer would: Page roots with code-behind, a Frame shell with a bound Back button.</summary>
    [TestFixture]
    public class NavigationGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using OsuXaml.Controls;

namespace App
{
    public class Vm
    {
        public Bindable<string> Greeting { get; } = new Bindable<string>(""Hello"");
        public Bindable<Step> CurrentStep { get; } = new Bindable<Step>();
        public BindableBool CanGoBack { get; } = new BindableBool();
    }

    public class Step
    {
        public string Name { get; set; }
    }

    public partial class HomePage : Page
    {
        private void OnDetails() => NavigationService.Navigate(new DetailsPage());
    }

    public partial class DetailsPage : Page
    {
        private void OnBack()
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }
    }

    public partial class MainWindow : Window
    {
        private void OnBack() => frame.GoBack();

        private void OnForward() => frame.GoForward();

        private void OnNavigated(NavigationEventArgs e) => Title = (e.Content as Page)?.Title;

        private void OnNavigating(NavigatingCancelEventArgs e) => e.Cancel = e.NavigationMode == NavigationMode.Forward && !frame.CanGoForward.Value;
    }
}";

        private const string home_page = @"
<Page xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""App.HomePage"" Title=""Home"" Background=""#1e1e2e"">
  <StackPanel Spacing=""8"">
    <TextBlock Text=""Welcome"" FontSize=""22"" />
    <Button Content=""Details"" Click=""OnDetails"" />
  </StackPanel>
</Page>";

        private const string details_page = @"
<Page xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:App"" x:Class=""App.DetailsPage"" x:DataType=""local:Vm"">
  <Page.Title>Details</Page.Title>
  <StackPanel Spacing=""8"">
    <TextBlock Text=""{Bind Greeting}"" />
    <Button Content=""Back"" Click=""OnBack"" />
  </StackPanel>
</Page>";

        [Test]
        public void PageRootsWithTitleAndCodeBehindNavigation()
        {
            string source = GeneratorRunner.Compiles(code_behind, home_page, ("DetailsPage.xaml", details_page), ("MainWindow.xaml", main_window));

            Assert.That(source, Does.Contain("partial class HomePage"));
            Assert.That(source, Does.Contain("this.Title = \"Home\""));
            Assert.That(source, Does.Contain("partial class DetailsPage"));
            Assert.That(source, Does.Contain("this.Title = \"Details\""));
        }

        private const string main_window = @"
<Window xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:App"" x:Class=""App.MainWindow"" x:DataType=""local:Vm"" Title=""Shell"">
  <DockPanel>
    <StackPanel DockPanel.Dock=""Top"" Orientation=""Horizontal"" Spacing=""8"" Margin=""8"">
      <Button Content=""Back"" Click=""OnBack"" IsEnabled=""{Bind CanGoBack, ElementName=frame}"" />
      <Button Content=""Forward"" Click=""OnForward"" IsEnabled=""{Bind CanGoForward, ElementName=frame}"" />
    </StackPanel>
    <Frame x:Name=""frame"" Navigated=""OnNavigated"" Navigating=""OnNavigating"" CanGoBack=""{Bind CanGoBack}"" NavigationUIVisibility=""Automatic"">
      <local:HomePage />
    </Frame>
  </DockPanel>
</Window>";

        [Test]
        public void FrameShellWithInitialPageAndBoundBackButton()
        {
            string source = GeneratorRunner.Compiles(code_behind, main_window, ("HomePage.xaml", home_page), ("DetailsPage.xaml", details_page));

            Assert.That(source, Does.Contain("private global::OsuXaml.Controls.Frame frame;"));
            Assert.That(source, Does.Contain(".Child = "));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay("));
        }

        [Test]
        public void FrameContentBoundToAViewModelWithAContentTemplate()
        {
            string source = GeneratorRunner.Compiles(code_behind, @"
<Window xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:App"" x:Class=""App.MainWindow"" x:DataType=""local:Vm"">
  <Frame x:Name=""frame"" Content=""{Bind CurrentStep}"">
    <Frame.ContentTemplate>
      <DataTemplate>
        <TextBlock Text=""{Bind Name}"" />
      </DataTemplate>
    </Frame.ContentTemplate>
  </Frame>
</Window>", ("HomePage.xaml", home_page), ("DetailsPage.xaml", details_page));

            Assert.That(source, Does.Contain("DataTemplate<global::App.Step>"));
        }

        [Test]
        public void NavigationServiceIsProvidedByTheFrame()
        {
            var error = GeneratorRunner.SingleError(code_behind, @"
<Page xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""App.HomePage"" NavigationService=""home"" />", ("DetailsPage.xaml", details_page));

            Assert.That(error.Id, Is.EqualTo("OX0004"));
        }
    }
}
