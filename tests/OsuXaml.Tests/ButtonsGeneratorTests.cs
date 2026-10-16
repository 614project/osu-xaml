using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>
    /// XAML for toggles, tabs, headered containers and bars, written the way a WPF developer would, checked through the generator.
    /// </summary>
    [TestFixture]
    public class ButtonsGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public BindableBool IsBold { get; } = new BindableBool();
        public BindableBool Small { get; } = new BindableBool(true);
        public BindableBool Large { get; } = new BindableBool();
        public BindableBool ShowDetails { get; } = new BindableBool();
        public BindableInt Tab { get; } = new BindableInt();
        public BindableList<Page> Pages { get; } = new BindableList<Page>();
        public Bindable<Page> CurrentPage { get; } = new Bindable<Page>();
        public Bindable<Section> Section { get; } = new Bindable<Section>();
        public Bindable<string> Status { get; } = new Bindable<string>(""Ready"");
        public void Save() { }
        public void ZoomIn() { }
        public void Open(string uri) { }
    }

    public class Page
    {
        public string Title { get; set; }
        public Bindable<string> Body { get; } = new Bindable<string>();
    }

    public class Section
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }

    public partial class View : CompositeDrawable
    {
        private void OnChecked() { }
        private void OnExpanded() { }
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, header + "\n" + content + "\n</CompositeDrawable>");

        [Test]
        public void TogglesBindTwoWayAndRaiseEvents()
        {
            string source = compiles(@"
  <StackPanel Spacing=""8"">
    <ToggleButton Content=""Bold"" IsChecked=""{Binding IsBold}"" Checked=""OnChecked"" Unchecked=""OnChecked"" CheckedBackground=""#a6e3a1"" />
    <ToggleButton IsChecked=""True""><TextBlock Text=""Rich"" /></ToggleButton>
    <StackPanel Orientation=""Horizontal"">
      <RadioButton Content=""Small"" IsChecked=""{Bind Small}"" />
      <RadioButton Content=""Large"" IsChecked=""{Bind Large}"" Checked=""{Bind Save}"" />
      <RadioButton GroupName=""Size"" IsEnabled=""False"">Huge</RadioButton>
    </StackPanel>
    <RepeatButton Content=""+"" Delay=""300"" Interval=""40"" Click=""{Bind ZoomIn}"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e1.IsChecked, dataContext.IsBold)"));
            Assert.That(source, Does.Contain(".IsChecked.Value = true"));
            Assert.That(source, Does.Contain(".Checked += () => this.OnChecked()"));
            Assert.That(source, Does.Contain("GroupName = \"Size\""));
            Assert.That(source, Does.Contain("Content = \"Huge\""));
            Assert.That(source, Does.Contain("Interval = 40"));
        }

        [Test]
        public void TabItemsWithHeadersContentAndPlacement()
        {
            string source = compiles(@"
  <TabControl SelectedIndex=""{Bind Tab}"" TabStripPlacement=""Left"" Padding=""16"">
    <TabItem Header=""General"">
      <StackPanel><TextBlock Text=""General settings"" /></StackPanel>
    </TabItem>
    <TabItem IsSelected=""True"">
      <TabItem.Header>
        <StackPanel Orientation=""Horizontal"" Spacing=""6"">
          <TextBlock Text=""*"" />
          <TextBlock Text=""Advanced"" />
        </StackPanel>
      </TabItem.Header>
      <TextBlock Text=""Advanced settings"" />
    </TabItem>
    <TabItem Header=""Text page"" IsEnabled=""False"">Just text</TabItem>
  </TabControl>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.TabControl()"));
            Assert.That(source, Does.Contain("TabStripPlacement = global::OsuXaml.Controls.Dock.Left"));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e0.SelectedIndex, dataContext.Tab)"));
            Assert.That(source, Does.Contain(".Header = e"), "property element header");
            Assert.That(source, Does.Contain(".Child = e"), "child element becomes the page");
            Assert.That(source, Does.Contain("Content = \"Just text\""));
            Assert.That(source, Does.Contain("IsSelected = true"));
        }

        [Test]
        public void TabsFromItemsSourceWithHeaderAndContentTemplates()
        {
            string source = compiles(@"
  <StackPanel>
    <TabControl ItemsSource=""{Bind Pages}"" SelectedItem=""{Bind CurrentPage}"">
      <TabControl.ItemTemplate>
        <DataTemplate>
          <TextBlock Text=""{Bind Title}"" />
        </DataTemplate>
      </TabControl.ItemTemplate>
      <TabControl.ContentTemplate>
        <DataTemplate>
          <StackPanel>
            <TextBlock Text=""{Bind Title}"" FontSize=""20"" />
            <TextBox Text=""{Bind Body}"" />
          </StackPanel>
        </DataTemplate>
      </TabControl.ContentTemplate>
    </TabControl>
    <TabControl ItemsSource=""{Bind Pages}"" DisplayMemberPath=""Title"" TabStripPlacement=""Bottom"" />
  </StackPanel>");

            Assert.That(source, Does.Contain(".ContentTemplate = t"));
            Assert.That(source, Does.Contain("new global::OsuXaml.DataTemplate<global::Demo.Page>("), "both templates take the item type from ItemsSource");
            Assert.That(source, Does.Contain("XamlBinding.TwoWayObject(this.xamlBindings, e1.SelectedItem, dataContext.CurrentPage)"));
            Assert.That(source, Does.Not.Contain("DataTemplate<object>"));
        }

        [Test]
        public void ExpanderAndGroupBoxHeaders()
        {
            string source = compiles(@"
  <StackPanel>
    <Expander Header=""Details"" IsExpanded=""{Binding ShowDetails}"" Expanded=""OnExpanded"" Collapsed=""{Bind Save}"">
      <StackPanel Spacing=""4"">
        <TextBlock Text=""Line 1"" />
        <TextBlock Text=""Line 2"" />
      </StackPanel>
    </Expander>
    <Expander IsExpanded=""True"" Background=""#181825"" Padding=""8"">
      <Expander.Header>
        <StackPanel Orientation=""Horizontal""><TextBlock Text=""Rich header"" /></StackPanel>
      </Expander.Header>
      <TextBlock Text=""Body"" />
    </Expander>
    <Expander Header=""{Bind Section}"">
      <Expander.HeaderTemplate>
        <DataTemplate>
          <TextBlock Text=""{Bind Name}"" />
        </DataTemplate>
      </Expander.HeaderTemplate>
    </Expander>
    <GroupBox Header=""Account"" BorderBrush=""#45475a"" CornerRadius=""4"">
      <StackPanel>
        <CheckBox Content=""Remember me"" />
      </StackPanel>
    </GroupBox>
  </StackPanel>");

            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e1.IsExpanded, dataContext.ShowDetails)"));
            Assert.That(source, Does.Contain(".Expanded += () => this.OnExpanded()"));
            Assert.That(source, Does.Contain("Header = \"Details\""));
            Assert.That(source, Does.Contain("new global::OsuXaml.DataTemplate<global::Demo.Section>("), "HeaderTemplate follows the type bound to Header");
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.GroupBox()"));
        }

        [Test]
        public void LabelTargetsAndHyperlinks()
        {
            string source = compiles(@"
  <Grid ColumnDefinitions=""Auto,*"" RowDefinitions=""Auto,Auto"">
    <Label Content=""_Name:"" Target=""{Binding ElementName=nameBox}"" />
    <TextBox x:Name=""nameBox"" Grid.Column=""1"" />
    <Label Grid.Row=""1"">Plain label</Label>
    <StackPanel Grid.Row=""1"" Grid.Column=""1"" Orientation=""Horizontal"">
      <TextBlock Text=""See "" />
      <Hyperlink NavigateUri=""https://github.com/ppy/osu-framework"">osu!framework</Hyperlink>
      <Hyperlink Text=""docs"" RequestNavigate=""{Bind Open}"" Click=""{Bind Save}"" />
    </StackPanel>
  </Grid>");

            Assert.That(source, Does.Contain("Content = \"_Name:\""));
            Assert.That(source, Does.Contain(".Target = this.nameBox"));
            Assert.That(source, Does.Contain("Text = \"osu!framework\""));
            Assert.That(source, Does.Contain("NavigateUri = \"https://github.com/ppy/osu-framework\""));
            Assert.That(source, Does.Contain(".RequestNavigate += (a0) => dataContext.Open(a0)"));
        }

        [Test]
        public void StatusBarAndToolBars()
        {
            string source = compiles(@"
  <DockPanel>
    <ToolBarTray DockPanel.Dock=""Top"">
      <ToolBar Header=""File"">
        <Button Content=""Save"" Click=""{Bind Save}"" />
        <Separator />
        <ToggleButton Content=""B"" IsChecked=""{Bind IsBold}"" />
        <RepeatButton Content=""+"" Click=""{Bind ZoomIn}"" />
      </ToolBar>
      <ToolBar Band=""1"" BandIndex=""0"" ShowGrip=""False"">
        <Button Content=""Help"" />
      </ToolBar>
    </ToolBarTray>
    <StatusBar DockPanel.Dock=""Bottom"">
      <StatusBarItem Content=""{Bind Status}"" />
      <Separator />
      <TextBlock Text=""Ln 1"" />
      <StatusBarItem DockPanel.Dock=""Right"">
        <ProgressBar Width=""100"" Value=""40"" />
      </StatusBarItem>
      <StatusBarItem />
    </StatusBar>
    <TextBlock Text=""Body"" />
  </DockPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.ToolBarTray()"));
            Assert.That(source, Does.Contain("Header = \"File\""));
            Assert.That(source, Does.Contain("Band = 1"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.StatusBar()"));
            Assert.That(source, Does.Contain("DockPanel.SetDock(e"));
            Assert.That(source, Does.Contain(".Content = x"), "status text binds one-way");
        }
    }
}
