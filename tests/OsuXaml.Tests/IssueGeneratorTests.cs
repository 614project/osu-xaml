using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Regression tests for reported generator issues, one region per issue.</summary>
    [TestFixture]
    public class IssueGeneratorTests
    {
        private const string code_behind = @"
using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
        public BindableList<TodoItem> Items { get; } = new BindableList<TodoItem>();
        public void OnItemChanged(TodoItem item) { }
        public void Save() { }
    }

    public class TodoItem
    {
        public Bindable<string> Name { get; } = new Bindable<string>();
        public BindableDouble Progress { get; } = new BindableDouble();
    }

    public partial class View : CompositeDrawable
    {
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string page(string content) => header + "\n" + content + "\n</CompositeDrawable>";

        private static string compiles(string content, string codeBehind = code_behind) => GeneratorRunner.Compiles(codeBehind, page(content));

        private static Microsoft.CodeAnalysis.Diagnostic error(string content, string codeBehind = code_behind) => GeneratorRunner.SingleError(codeBehind, page(content));

        #region #17: literal resources

        [Test]
        public void EmptyElementResourcesAreElementsAndObjectTargetsKeepTheResourceType()
        {
            string source = compiles(@"
  <StackPanel>
    <StackPanel.Resources>
      <Button x:Key=""EmptyButton"" />
      <x:Int32 x:Key=""Answer"">42</x:Int32>
      <Colour4 x:Key=""Accent"">#89b4fa</Colour4>
      <x:String x:Key=""None"" />
    </StackPanel.Resources>
    <Border Child=""{StaticResource EmptyButton}"" />
    <Button Content=""{StaticResource EmptyButton}"" />
    <Button Content=""{StaticResource Answer}"" />
    <Button Content=""{StaticResource Accent}"" />
    <TextBlock Text=""{StaticResource None}"" />
  </StackPanel>");

            Assert.That(source, Does.Match(@"var (e\d+) = new global::OsuXaml\.Controls\.Button\(\);\s+e\d+\.Child = \1;"), "an empty element resource is created");
            Assert.That(source, Does.Match(@"var (e\d+) = new global::OsuXaml\.Controls\.Button\(\);\s+e\d+\.Content = \1;"), "also for an object property");
            Assert.That(source, Does.Contain(".Content = 42;"), "an x:Int32 stays a number");
            Assert.That(source, Does.Contain(".Content = global::osu.Framework.Graphics.Colour4.FromHex(\"89b4fa\");"), "a colour stays a colour");
            Assert.That(source, Does.Contain(".Text = \"\";"), "an empty x:String is still the empty string");
        }

        #endregion

        #region #18: handler and converter methods

        private const string methods_code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
        public BindableList<TodoItem> Items { get; } = new BindableList<TodoItem>();
        public void OnItemChanged(TodoItem item) { }
        public void Add(int count) { }
        public void Add() { }
        public static void Reset() { }
    }

    public class TodoItem
    {
        public Bindable<string> Name { get; } = new Bindable<string>();
    }

    public static class Conv
    {
        public static string Upper(string s) => s.ToUpperInvariant();
        public static string Upper(object o) => o?.ToString();
    }

    public class InstanceConv
    {
        public string Shout(string s) => s + ""!"";
    }

    public partial class ViewBase : CompositeDrawable
    {
        protected void OnBaseClick() { }
        protected string Trimmed(string s) => s.Trim();
    }

    public partial class View : ViewBase
    {
        private void Save(int count) { }
        private void Save() { }
        private static void OnStaticClick() { }
        private Drawable MakeTip(string text) => new OsuXaml.Controls.TextBlock { Text = text };
    }
}";

        [Test]
        public void TemplateItemMethodIsNotPassedTheEventsArgument()
        {
            string source = compiles(@"
  <ItemsControl ItemsSource=""{Bind Items}"">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <Slider ValueChanged=""{Bind OnItemChanged, Source=Root}"" />
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>", methods_code_behind);

            // Slider.ValueChanged passes a double, which isn't a TodoItem: the method gets the template's item instead.
            Assert.That(source, Does.Match(@"ValueChanged \+= \(a0\) => \w+\.OnItemChanged\(item\w*\)"));
        }

        [Test]
        public void HandlersPickTheFittingOverloadAndCallStaticAndInheritedMethods()
        {
            string source = compiles(@"
  <StackPanel>
    <Button Content=""Save"" Click=""Save"" />
    <Button Content=""Static"" Click=""OnStaticClick"" />
    <Button Content=""Inherited"" Click=""OnBaseClick"" />
    <Button Content=""Add"" Click=""{Bind Add}"" />
    <Button Content=""Reset"" Click=""{Bind Reset}"" />
  </StackPanel>", methods_code_behind);

            Assert.That(source, Does.Contain("() => this.Save()"));
            Assert.That(source, Does.Contain("() => global::Demo.View.OnStaticClick()"));
            Assert.That(source, Does.Contain("() => this.OnBaseClick()"));
            Assert.That(source, Does.Contain("() => dataContext.Add()"));
            Assert.That(source, Does.Contain("() => global::Demo.Vm.Reset()"));
        }

        [Test]
        public void ConvertersMayBeInheritedOverloadedAndMustBeStaticOnAnotherType()
        {
            string source = compiles(@"
  <StackPanel>
    <TextBlock Text=""{Bind Title, Converter=Trimmed}"" />
    <TextBlock Text=""{Bind Title, Converter=local:Conv.Upper}"" />
  </StackPanel>", methods_code_behind);

            Assert.That(source, Does.Contain("Trimmed("));
            Assert.That(source, Does.Contain("global::Demo.Conv.Upper("));

            var diagnostic = error(@"<TextBlock Text=""{Bind Title, Converter=local:InstanceConv.Shout}"" />", methods_code_behind);
            Assert.That(diagnostic.Id, Is.EqualTo("OX0011"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("static"));
        }

        [Test]
        public void AttachedBindingsPickTheOverloadForTheConvertedValueAndRefuseMethods()
        {
            string source = compiles(@"<Button Content=""Tip"" ToolTip=""{Bind Title, Converter=MakeTip}"" />", methods_code_behind);
            Assert.That(source, Does.Match(@"SetToolTip\(e\d+, MakeTip\(\w+\)\)"), "the Drawable overload, not the tooltip's text: " + source);

            var diagnostic = error(@"<Button Content=""Row"" Grid.Row=""{Bind Add}"" />", methods_code_behind);
            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
        }

        #endregion

        #region #22: markup messages and names

        [Test]
        public void MarkupErrorsListEverythingThatIsSupported()
        {
            var extension = error(@"<TextBlock Text=""{Nope Title}"" />");
            Assert.That(extension.GetMessage(), Does.Contain("{DynamicResource}").And.Contain("{x:Type}"));

            var option = error(@"<TextBlock Text=""{Bind Title, Nope=1}"" />");
            Assert.That(option.GetMessage(), Does.Contain("UpdateSourceTrigger"));
        }

        [Test]
        public void StyleSettersGiveWayToContentAndQualifiedProperties()
        {
            string source = compiles(@"
  <StackPanel>
    <StackPanel.Resources>
      <Style x:Key=""Styled"" TargetType=""Button"">
        <Setter Property=""Content"" Value=""{Bind Title}"" />
        <Setter Property=""Background"" Value=""#ff0000"" />
        <Setter Property=""Grid.Row"" Value=""1"" />
      </Style>
    </StackPanel.Resources>
    <Button x:Name=""text"" Style=""{StaticResource Styled}"">Save</Button>
    <Button x:Name=""child"" Style=""{StaticResource Styled}""><TextBlock Text=""Icon"" /></Button>
    <Button x:Name=""qualified"" Style=""{StaticResource Styled}"" Button.Background=""#00ff00"" Content=""Plain"">
      <Grid.Row>2</Grid.Row>
    </Button>
    <Button x:Name=""styled"" Style=""{StaticResource Styled}"" />
  </StackPanel>");

            string qualified = System.Text.RegularExpressions.Regex.Match(source, @"this\.qualified = (e\d+);").Groups[1].Value;

            Assert.That(System.Text.RegularExpressions.Regex.Matches(source, @"dataContext\.Title").Count, Is.EqualTo(1), "only the button without content takes the Content setter");
            Assert.That(source, Does.Match(@"this\.styled = (e\d+);\s+global::OsuXaml\.XamlBinding\.OneWay\(this\.xamlBindings, dataContext\.Title, \(x, _\) => \1\.Content = x\);"));
            Assert.That(source, Does.Not.Contain($"{qualified}.Background = global::osu.Framework.Graphics.Colour4.FromHex(\"ff0000\")"), "Button.Background is Background");
            Assert.That(source, Does.Not.Contain($"SetRow({qualified}, 1)"), "a <Grid.Row> property element is Grid.Row");
            Assert.That(source, Does.Contain($"SetRow({qualified}, 2)"));
        }

        [Test]
        public void ADuplicateNameIsReported()
        {
            var diagnostic = error(@"
  <StackPanel>
    <TextBlock x:Name=""title"" />
    <TextBlock x:Name=""title"" />
  </StackPanel>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0014"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("'title'").And.Contain("line"));
        }

        [Test]
        public void ANamedResourceUsedTwiceIsReported()
        {
            var diagnostic = error(@"
  <StackPanel>
    <StackPanel.Resources>
      <TextBlock x:Key=""Shared"" x:Name=""shared"" Text=""hi"" />
    </StackPanel.Resources>
    <Border Child=""{StaticResource Shared}"" />
    <Border Child=""{StaticResource Shared}"" />
  </StackPanel>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0014"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("more than once"));
        }

        [Test]
        public void ANamedResourceUsedOnceAndAnItemsPanelCanBeNamed()
        {
            string source = compiles(@"
  <StackPanel>
    <StackPanel.Resources>
      <TextBlock x:Key=""Shared"" x:Name=""shared"" Text=""hi"" />
    </StackPanel.Resources>
    <Border Child=""{StaticResource Shared}"" />
    <ItemsControl ItemsSource=""{Bind Items}"">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
          <WrapPanel x:Name=""panel"" />
        </ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
    </ItemsControl>
    <Label Content=""Items"" Target=""{Binding ElementName=panel}"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("private global::OsuXaml.Controls.WrapPanel panel;"));
            Assert.That(source, Does.Contain("this.panel"));
        }

        #endregion
    }
}
