using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Templates, resources, styles and the newer binding forms, checked through the generator.</summary>
    [TestFixture]
    public class TemplateGeneratorTests
    {
        private const string code_behind = @"
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
        public BindableBool Enabled { get; } = new BindableBool();
        public BindableList<Person> People { get; } = new BindableList<Person>();
        public Bindable<Person> Selected { get; } = new Bindable<Person>();
        public BindableInt SelectedIndex { get; } = new BindableInt();
        public BindableList<Node> Roots { get; } = new BindableList<Node>();
        public string[] Colours { get; } = { ""Red"", ""Green"" };
        public void Save() { }
    }

    public class Person
    {
        public string Name { get; set; }
        public BindableInt Age { get; } = new BindableInt();
    }

    public class Node
    {
        public string Label { get; set; }
        public BindableList<Node> Children { get; } = new BindableList<Node>();
    }

    public static class Palette
    {
        public static readonly Colour4 Pink = Colour4.Pink;
        public const float Gap = 12;
    }

    public partial class Bag : Drawable
    {
        public List<string> Tags { get; } = new List<string>();
        public List<string> Words { get; set; } = new List<string>();
    }

    public class Col
    {
        public string Header { get; set; }
        public OsuXaml.ItemBinding Binding { get; set; }
    }

    public partial class Table : OsuXaml.Controls.ItemsControl
    {
        public List<Col> Columns { get; } = new List<Col>();
    }

    public partial class View : CompositeDrawable
    {
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string page(string content) => header + "\n" + content + "\n</CompositeDrawable>";

        private static string compiles(string content, params (string, string)[] extra) => GeneratorRunner.Compiles(code_behind, page(content), extra);

        private static Microsoft.CodeAnalysis.Diagnostic error(string content) => GeneratorRunner.SingleError(code_behind, page(content));

        [Test]
        public void ItemTemplateInfersItemTypeFromItemsSource()
        {
            string source = compiles(@"
  <ListBox ItemsSource=""{Bind People}"">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <StackPanel Orientation=""Horizontal"" Spacing=""8"">
          <TextBlock Text=""{Bind Name}"" />
          <TextBlock Text=""{Bind Age, StringFormat='({0})'}"" />
          <Button Content=""Save"" Click=""{Bind Save, Source=Root}"" />
        </StackPanel>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>");

            Assert.That(source, Does.Contain("new global::OsuXaml.DataTemplate<global::Demo.Person>("));
            Assert.That(source, Does.Contain("XamlBinding.ListCopy(this.xamlBindings, dataContext.People)"));
        }

        [Test]
        public void DisplayMemberPathBecomesATextTemplate()
        {
            string source = compiles(@"<ListBox ItemsSource=""{Bind People}"" DisplayMemberPath=""Name"" />");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.TextBlock()"));
            Assert.That(source, Does.Contain(".DisplayMemberPath = t"));
        }

        [Test]
        public void SelectionBindsTwoWayToTypedBindables()
        {
            string source = compiles(@"<ListBox ItemsSource=""{Bind People}"" SelectedItem=""{Bind Selected}"" SelectedIndex=""{Bind SelectedIndex}"" />");

            Assert.That(source, Does.Contain("XamlBinding.TwoWayObject(this.xamlBindings, e0.SelectedItem, dataContext.Selected)"));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e0.SelectedIndex, dataContext.SelectedIndex)"));
        }

        [Test]
        public void LiteralAndDirectiveItems()
        {
            compiles(@"
  <StackPanel>
    <ListBox ItemsSource=""Red,Green,Blue"" />
    <ListBox ItemsSource=""{Bind Colours}"" />
    <ListBox>
      <x:String>One</x:String>
      <ListBoxItem Content=""Two"" />
      <ListBoxItem><TextBlock Text=""Three"" /></ListBoxItem>
    </ListBox>
  </StackPanel>");
        }

        [Test]
        public void ResourcesStylesAndStaticResources()
        {
            string source = compiles(@"
  <Grid>
    <Grid.Resources>
      <Colour4 x:Key=""Warm"">#fab387</Colour4>
      <x:Double x:Key=""Big"">28</x:Double>
      <Style TargetType=""Button"">
        <Setter Property=""CornerRadius"" Value=""12"" />
        <Setter Property=""Padding"" Value=""20,10"" />
      </Style>
      <Style x:Key=""Danger"" TargetType=""Button"" BasedOn=""{StaticResource Base}"">
        <Setter Property=""Background"" Value=""{StaticResource Warm}"" />
      </Style>
      <Style x:Key=""Base"" TargetType=""ContentControl"">
        <Setter Property=""HorizontalAlignment"" Value=""Center"" />
        <Setter Property=""Grid.Row"" Value=""1"" />
      </Style>
    </Grid.Resources>
    <TextBlock FontSize=""{StaticResource Big}"" Foreground=""{StaticResource Warm}"" Text=""Title"" />
    <Button Content=""Implicit"" Padding=""4"" />
    <Button Content=""Keyed"" Style=""{StaticResource Danger}"" />
  </Grid>");

            Assert.That(source, Does.Contain("CornerRadius = 12f"), "implicit style applies");
            Assert.That(source, Does.Contain("Padding = new global::osu.Framework.Graphics.MarginPadding(4f)"), "local value wins");
            Assert.That(source, Does.Not.Contain("Horizontal = 20f"), "overridden setter is skipped");
            Assert.That(source, Does.Contain("Colour4.FromHex(\"fab387\")"));
            Assert.That(source, Does.Contain("FontSize = 28f"));
            Assert.That(source, Does.Contain("Layout.SetHorizontalAlignment(e"), "BasedOn setters apply");
            Assert.That(source, Does.Contain("Grid.SetRow(e"));
        }

        [Test]
        public void DataTemplateResourcesByKeyAndByType()
        {
            string source = compiles(@"
  <StackPanel>
    <StackPanel.Resources>
      <DataTemplate x:Key=""Card"" x:DataType=""local:Person"">
        <Border Padding=""8""><TextBlock Text=""{Bind Name}"" /></Border>
      </DataTemplate>
      <DataTemplate DataType=""{x:Type local:Person}"">
        <TextBlock Text=""{Bind Age}"" />
      </DataTemplate>
    </StackPanel.Resources>
    <ListBox ItemsSource=""{Bind People}"" ItemTemplate=""{StaticResource Card}"" />
    <ItemsControl ItemsSource=""{Bind People}"" />
    <WrapPanel ItemsSource=""{Bind People}"" ItemTemplate=""{StaticResource Card}"" />
  </StackPanel>");

            Assert.That(source, Does.Contain(".ItemTemplate = t"), "keyed and implicit templates are assigned");
            Assert.That(source, Does.Contain("XamlBinding.Items<global::Demo.Person>"), "panels take a template resource too");
        }

        [Test]
        public void ControlTemplateWithTemplateBindingAndContentPresenter()
        {
            string source = compiles(@"
  <Button Content=""Templated"" Background=""#a6e3a1"">
    <Button.Template>
      <ControlTemplate TargetType=""Button"">
        <Border Background=""{TemplateBinding Background}"" CornerRadius=""16"" Padding=""12,6"">
          <ContentPresenter />
        </Border>
      </ControlTemplate>
    </Button.Template>
  </Button>");

            Assert.That(source, Does.Contain("new global::OsuXaml.ControlTemplate<global::OsuXaml.Controls.Button>("));
            Assert.That(source, Does.Contain(".AttachPresenter(e"));
            Assert.That(source, Does.Contain(".Background = item"));
        }

        [Test]
        public void ElementNameBindingsRunAfterEveryElementExists()
        {
            string source = compiles(@"
  <StackPanel>
    <TextBlock Text=""{Bind Value, ElementName=volume, StringFormat='{0:0}%'}"" />
    <Slider x:Name=""volume"" />
  </StackPanel>");

            int deferredAt = source.IndexOf("Bindings to named elements", System.StringComparison.Ordinal);
            Assert.That(deferredAt, Is.GreaterThan(source.IndexOf("this.volume = ", System.StringComparison.Ordinal)));
            Assert.That(source.Substring(deferredAt), Does.Contain("this.volume.Value"));
        }

        [Test]
        public void StaticNullAndBuiltInConverters()
        {
            string source = compiles(@"
  <StackPanel Spacing=""{x:Static local:Palette.Gap}"">
    <TextBlock Foreground=""{x:Static local:Palette.Pink}"" Text=""x"" Visibility=""{Bind Enabled, Converter=BoolToVisibility}"" />
    <Button Content=""{x:Null}"" IsEnabled=""{Bind Enabled, Converter=Not}"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("global::Demo.Palette.Gap"));
            Assert.That(source, Does.Contain("global::OsuXaml.Converters.BoolToVisibility(x)"));
            Assert.That(source, Does.Contain("global::OsuXaml.Converters.Not(x)"));
            Assert.That(source, Does.Contain("Content = null"));
        }

        [Test]
        public void HierarchicalTemplatesAndPropertyElements()
        {
            string source = compiles(@"
  <StackPanel>
    <ItemsControl ItemsSource=""{Bind Roots}"">
      <ItemsControl.ItemTemplate>
        <HierarchicalDataTemplate ItemsSource=""{Bind Children}"">
          <TextBlock Text=""{Bind Label}"" />
        </HierarchicalDataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <Button>
      <Button.Content>
        <StackPanel Orientation=""Horizontal""><TextBlock Text=""Rich"" /></StackPanel>
      </Button.Content>
    </Button>
    <local:Bag>
      <local:Bag.Tags>
        <x:String>a</x:String>
        <x:String>b</x:String>
      </local:Bag.Tags>
    </local:Bag>
  </StackPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.HierarchicalDataTemplate<global::Demo.Node>("));
            Assert.That(source, Does.Contain("XamlBinding.ListCopy(scope"));
            Assert.That(source, Does.Contain(".Tags.Add(e"));
        }

        [Test]
        public void SharedResourceDictionaryFilesApplyToEveryPage()
        {
            const string theme = @"<ResourceDictionary xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"">
  <Colour4 x:Key=""Brand"">#cba6f7</Colour4>
  <Style TargetType=""TextBlock"">
    <Setter Property=""FontSize"" Value=""19"" />
  </Style>
</ResourceDictionary>";

            string source = compiles(@"<TextBlock Text=""Hi"" Foreground=""{StaticResource Brand}"" />", ("Theme.xaml", theme));

            Assert.That(source, Does.Contain("FontSize = 19f"));
            Assert.That(source, Does.Contain("FromHex(\"cba6f7\")"));
        }

        [Test]
        public void ItemBindingsCompileToTypedAccessors()
        {
            string source = compiles(@"
  <local:Table ItemsSource=""{Bind People}"">
    <local:Table.Columns>
      <local:Col Header=""Name"" Binding=""{Binding Name}"" />
      <local:Col Header=""Age"" Binding=""Age"" />
    </local:Table.Columns>
  </local:Table>");

            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Person, string>(\"Name\""));
            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Person, int>(\"Age\""));
            Assert.That(source, Does.Contain(".Age.Value)"));
        }

        [Test]
        public void ItemBindingWithoutItemTypeIsReported()
        {
            var diagnostic = error(@"<local:Table><local:Table.Columns><local:Col Binding=""{Bind Name}"" /></local:Table.Columns></local:Table>");
            Assert.That(diagnostic.Id, Is.EqualTo("OX0009"));
        }

        [Test]
        public void PropertyElementsAddToCollectionsAndUnwrapItemsPanelTemplate()
        {
            string source = compiles(@"
  <StackPanel>
    <local:Bag>
      <local:Bag.Words>
        <x:String>one</x:String>
      </local:Bag.Words>
    </local:Bag>
    <ListBox>
      <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
          <WrapPanel Orientation=""Horizontal"" />
        </ItemsPanelTemplate>
      </ListBox.ItemsPanel>
    </ListBox>
  </StackPanel>");

            Assert.That(source, Does.Contain(".Words.Add(e"));
            Assert.That(source, Does.Contain(".ItemsPanel = e"));
        }

        [Test]
        public void MissingResourceIsReported()
        {
            var diagnostic = error(@"<TextBlock Foreground=""{StaticResource Nope}"" />");
            Assert.That(diagnostic.Id, Is.EqualTo("OX0012"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Nope"));
        }

        [Test]
        public void TemplatePathErrorsPointAtTheTemplate()
        {
            var diagnostic = error(@"
  <ListBox ItemsSource=""{Bind People}"">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <TextBlock Text=""{Bind Nmae}"" />
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0006"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Person"));
            Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line, Is.EqualTo(6));
        }

        [Test]
        public void StyleForAnotherTypeIsReported()
        {
            var diagnostic = error(@"
  <StackPanel>
    <StackPanel.Resources>
      <Style x:Key=""B"" TargetType=""Button""><Setter Property=""CornerRadius"" Value=""3"" /></Style>
    </StackPanel.Resources>
    <TextBlock Style=""{StaticResource B}"" />
  </StackPanel>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0010"));
        }

        [Test]
        public void UnknownElementNameIsReported()
        {
            var diagnostic = error(@"<TextBlock Text=""{Bind Value, ElementName=nothing}"" />");
            Assert.That(diagnostic.GetMessage(), Does.Contain("nothing"));
        }

        [Test]
        public void UnknownBindingOptionIsReported()
        {
            var diagnostic = error(@"<TextBlock Text=""{Binding Title, ConverterParameter=3}"" />");
            Assert.That(diagnostic.Id, Is.EqualTo("OX0010"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ConverterParameter"));
        }
    }
}
