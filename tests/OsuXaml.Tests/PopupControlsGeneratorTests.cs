using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>ComboBox, Menu, ContextMenu and ToolTip written the way a WPF developer would, checked through the generator.</summary>
    [TestFixture]
    public class PopupControlsGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public BindableList<Fruit> Fruits { get; } = new BindableList<Fruit>();
        public Bindable<Fruit> SelectedFruit { get; } = new Bindable<Fruit>();
        public BindableInt SelectedIndex { get; } = new BindableInt();
        public BindableInt SelectedId { get; } = new BindableInt();
        public BindableBool IsOpen { get; } = new BindableBool();
        public Bindable<string> Query { get; } = new Bindable<string>(string.Empty);
        public Bindable<string> Status { get; } = new Bindable<string>(""ready"");
        public BindableBool WordWrap { get; } = new BindableBool();
        public BindableBool CanSave { get; } = new BindableBool(true);
        public BindableList<Command> Commands { get; } = new BindableList<Command>();
        public void Open() { }
        public void Save() { }
        public void Delete(Fruit fruit) { }
    }

    public class Fruit
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public BindableInt Stock { get; } = new BindableInt();
    }

    public class Command
    {
        public string Title { get; set; }
        public BindableList<Command> Children { get; } = new BindableList<Command>();
    }

    public partial class View : CompositeDrawable
    {
        private void OnExit() { }
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, header + "\n" + content + "\n</CompositeDrawable>");

        private static Microsoft.CodeAnalysis.Diagnostic error(string content) => GeneratorRunner.SingleError(code_behind, header + "\n" + content + "\n</CompositeDrawable>");

        [Test]
        public void ComboBoxWithItemsSourceDisplayMemberPathAndTwoWaySelection()
        {
            string source = compiles(@"
  <StackPanel Spacing=""8"">
    <ComboBox ItemsSource=""{Binding Fruits}"" DisplayMemberPath=""Name"" SelectedItem=""{Binding SelectedFruit}""
              SelectedIndex=""{Binding SelectedIndex}"" IsDropDownOpen=""{Binding IsOpen}"" MaxDropDownHeight=""200""
              PlaceholderText=""Pick a fruit"" Width=""220"" />
    <ComboBox ItemsSource=""{Bind Fruits}"" SelectedValuePath=""Id"" SelectedValue=""{Bind SelectedId}"">
      <ComboBox.ItemTemplate>
        <DataTemplate>
          <StackPanel Orientation=""Horizontal"" Spacing=""6"">
            <TextBlock Text=""{Bind Name}"" />
            <TextBlock Text=""{Bind Stock, StringFormat='({0})'}"" Foreground=""#a6adc8"" />
          </StackPanel>
        </DataTemplate>
      </ComboBox.ItemTemplate>
    </ComboBox>
  </StackPanel>");

            Assert.That(source, Does.Contain("XamlBinding.TwoWayObject(this.xamlBindings, e1.SelectedItem, dataContext.SelectedFruit)"));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e1.IsDropDownOpen, dataContext.IsOpen)"));
            Assert.That(source, Does.Contain("MaxDropDownHeight = 200f"));
            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Fruit, int>(\"Id\""));
            Assert.That(source, Does.Contain("XamlBinding.TwoWayObject(this.xamlBindings, e3.SelectedValue, dataContext.SelectedId)"));
            Assert.That(source, Does.Contain("new global::OsuXaml.DataTemplate<global::Demo.Fruit>("));
        }

        [Test]
        public void ComboBoxWithItemChildrenAndEditableText()
        {
            string source = compiles(@"
  <StackPanel>
    <ComboBox SelectedIndex=""1"">
      <ComboBoxItem Content=""Small"" />
      <ComboBoxItem Content=""Medium"" />
      <ComboBoxItem>
        <StackPanel Orientation=""Horizontal""><TextBlock Text=""Large"" /></StackPanel>
      </ComboBoxItem>
    </ComboBox>
    <ComboBox IsEditable=""True"" Text=""{Bind Query}"">
      <x:String>Apple</x:String>
      <x:String>Banana</x:String>
    </ComboBox>
  </StackPanel>");

            Assert.That(source, Does.Contain("SelectedIndex.Value = 1"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.ComboBoxItem()"));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e"));
            Assert.That(source, Does.Contain(".Text, dataContext.Query)"));
            Assert.That(source, Does.Contain(".Add(\"Apple\")").Or.Contain("= \"Apple\""));
        }

        [Test]
        public void MenuBarWithSubmenusSeparatorsChecksAndHandlers()
        {
            string source = compiles(@"
  <DockPanel RelativeSizeAxes=""Both"">
    <Menu DockPanel.Dock=""Top"">
      <MenuItem Header=""_File"">
        <MenuItem Header=""_Open..."" InputGestureText=""Ctrl+O"" Click=""{Binding Open}"" />
        <MenuItem Header=""_Save"" InputGestureText=""Ctrl+S"" Command=""{Bind Save}"" IsEnabled=""{Bind CanSave}"" />
        <MenuItem Header=""Recent files"">
          <MenuItem Header=""notes.txt"" />
          <MenuItem Header=""todo.md"" />
        </MenuItem>
        <Separator />
        <MenuItem Header=""E_xit"" Click=""OnExit"" />
      </MenuItem>
      <MenuItem Header=""_View"">
        <MenuItem Header=""_Word wrap"" IsCheckable=""True"" IsChecked=""{Binding WordWrap}"" />
        <MenuItem Header=""Status bar"" IsCheckable=""True"" IsChecked=""True"" StaysOpenOnClick=""True"" />
        <MenuItem>
          <MenuItem.Header>
            <StackPanel Orientation=""Horizontal"" Spacing=""6""><TextBlock Text=""Rich"" /><TextBlock Text=""header"" /></StackPanel>
          </MenuItem.Header>
          <MenuItem.Icon>
            <Box Size=""12"" Colour=""#f38ba8"" />
          </MenuItem.Icon>
        </MenuItem>
      </MenuItem>
    </Menu>
    <Border />
  </DockPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Menu()"));
            Assert.That(source, Does.Contain("Header = \"_File\""));
            Assert.That(source, Does.Contain("InputGestureText = \"Ctrl+O\""));
            Assert.That(source, Does.Contain(".Click += () => dataContext.Open()"));
            Assert.That(source, Does.Contain(".Command = () => dataContext.Save()"));
            Assert.That(source, Does.Contain(".Click += () => this.OnExit()"));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e"));
            Assert.That(source, Does.Contain(".IsChecked, dataContext.WordWrap)"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Separator()"));
            Assert.That(source, Does.Contain(".Icon = e"));
        }

        [Test]
        public void DataBoundMenuUsesHierarchicalTemplate()
        {
            string source = compiles(@"
  <Menu ItemsSource=""{Bind Commands}"">
    <Menu.ItemTemplate>
      <HierarchicalDataTemplate ItemsSource=""{Bind Children}"">
        <TextBlock Text=""{Bind Title}"" />
      </HierarchicalDataTemplate>
    </Menu.ItemTemplate>
  </Menu>");

            Assert.That(source, Does.Contain("new global::OsuXaml.HierarchicalDataTemplate<global::Demo.Command>("));
        }

        [Test]
        public void ContextMenuAttachesToAnyElement()
        {
            string source = compiles(@"
  <StackPanel>
    <Border Background=""#313244"" Padding=""12"">
      <ContextMenuService.ContextMenu>
        <ContextMenu>
          <MenuItem Header=""_Copy"" InputGestureText=""Ctrl+C"" />
          <MenuItem Header=""More"">
            <MenuItem Header=""Nested"" Click=""{Bind Save}"" />
          </MenuItem>
          <Separator />
          <MenuItem Header=""_Delete"" Click=""{Bind Open}"" />
        </ContextMenu>
      </ContextMenuService.ContextMenu>
      <TextBlock Text=""Right-click me"" />
    </Border>
    <TextBlock Text=""Also me"">
      <ContextMenuService.ContextMenu>
        <ContextMenu Placement=""Bottom"" Opened=""{Bind Open}"">
          <MenuItem Header=""Only item"" />
        </ContextMenu>
      </ContextMenuService.ContextMenu>
    </TextBlock>
  </StackPanel>");

            Assert.That(source, Does.Contain("global::OsuXaml.Controls.ContextMenuService.SetContextMenu(e"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.ContextMenu()"));
            Assert.That(source, Does.Contain("Placement = global::OsuXaml.Controls.PlacementMode.Bottom"));
        }

        [Test]
        public void ContextMenuInsideAnItemTemplatePassesTheItem()
        {
            string source = compiles(@"
  <ListBox ItemsSource=""{Bind Fruits}"">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <Border Padding=""4"">
          <ContextMenuService.ContextMenu>
            <ContextMenu>
              <MenuItem Header=""Delete"" Click=""{Bind Delete, Source=Root}"" />
            </ContextMenu>
          </ContextMenuService.ContextMenu>
          <TextBlock Text=""{Bind Name}"" ToolTip=""{Bind Name}"" />
        </Border>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>");

            Assert.That(source, Does.Contain("dataContext.Delete(item"));
            Assert.That(source, Does.Contain("ToolTipService.SetToolTip(e"));
        }

        [Test]
        public void ToolTipsAsTextBindingsAndRichContent()
        {
            string source = compiles(@"
  <StackPanel>
    <Button Content=""Save"" ToolTip=""Save the document"" />
    <TextBlock Text=""Status"" ToolTip=""{Bind Status}"" ToolTipService.InitialShowDelay=""1000"" ToolTipService.Placement=""Bottom"" />
    <Border ToolTip=""{Bind SelectedId, StringFormat='Item {0}'}"" />
    <Button Content=""Rich"">
      <ToolTipService.ToolTip>
        <ToolTip>
          <StackPanel Spacing=""4"">
            <TextBlock Text=""Rich tooltip"" FontWeight=""Bold"" />
            <TextBlock Text=""{Bind Status}"" FontSize=""13"" />
          </StackPanel>
        </ToolTip>
      </ToolTipService.ToolTip>
    </Button>
    <Border>
      <ToolTipService.ToolTip>
        <TextBlock Text=""Plain element"" />
      </ToolTipService.ToolTip>
    </Border>
  </StackPanel>");

            Assert.That(source, Does.Contain("ToolTipService.SetToolTip(e1, \"Save the document\")"));
            Assert.That(source, Does.Contain("XamlBinding.OneWay(this.xamlBindings, dataContext.Status, (x, _) => global::OsuXaml.Controls.ToolTipService.SetToolTip(e2, x))"));
            Assert.That(source, Does.Contain("ToolTipService.SetInitialShowDelay(e2, 1000)"));
            Assert.That(source, Does.Contain("ToolTipService.SetPlacement(e2, global::OsuXaml.Controls.PlacementMode.Bottom)"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.ToolTip()"));
        }

        [Test]
        public void ContextMenuMustBeAContextMenu()
        {
            var diagnostic = error(@"<Border><ContextMenuService.ContextMenu><TextBlock Text=""no"" /></ContextMenuService.ContextMenu></Border>");
            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ContextMenu"));
        }
    }
}
