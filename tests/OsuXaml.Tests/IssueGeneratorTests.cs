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

        #region #22: markup messages

        [Test]
        public void MarkupErrorsListEverythingThatIsSupported()
        {
            var extension = error(@"<TextBlock Text=""{Nope Title}"" />");
            Assert.That(extension.GetMessage(), Does.Contain("{DynamicResource}").And.Contain("{x:Type}"));

            var option = error(@"<TextBlock Text=""{Bind Title, Nope=1}"" />");
            Assert.That(option.GetMessage(), Does.Contain("UpdateSourceTrigger"));
        }

        #endregion
    }
}
