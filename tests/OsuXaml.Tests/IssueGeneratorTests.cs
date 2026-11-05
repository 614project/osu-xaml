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
    }
}
