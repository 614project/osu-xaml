using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>What the generator adds so a view can be rebuilt by hot reload.</summary>
    [TestFixture]
    public class HotReloadGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using OsuXaml.Controls;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
    }

    public partial class Page1 : UserControl
    {
        private void Loaded() { }
    }

    public partial class Plain : osu.Framework.Graphics.Containers.CompositeDrawable
    {
    }
}";

        [Test]
        public void ViewsRegisterTheirChildrenAndRebuildThroughTheInterface()
        {
            string source = GeneratorRunner.Compiles(code_behind, @"<UserControl xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.Page1"" x:DataType=""local:Vm"" OnLoadComplete=""Loaded"">
  <StackPanel>
    <TextBlock Text=""{Bind Title}"" />
  </StackPanel>
</UserControl>");

            Assert.That(source, Does.Contain("partial class Page1 : global::OsuXaml.IXamlHotReloadable"));
            Assert.That(source, Does.Contain("global::OsuXaml.XamlHotReload.Register(this, dataContext, e0);"));
            Assert.That(source, Does.Contain("InitializeComponent((global::Demo.Vm)dataContext);"));
            Assert.That(source, Does.Contain("partial void OnXamlReloaded();"));
            Assert.That(source, Does.Contain("if (!global::OsuXaml.XamlHotReload.IsRebuilding) this.OnLoadComplete +="), "root event handlers are added once");
        }

        [Test]
        public void ViewsWithoutDataTypeRebuildWithoutOne()
        {
            string source = GeneratorRunner.Compiles(code_behind, @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""Demo.Plain"">
  <Box />
  <Box />
</CompositeDrawable>");

            Assert.That(source, Does.Contain("global::OsuXaml.XamlHotReload.Register(this, null, e0, e1);"));
            Assert.That(source, Does.Contain("            InitializeComponent();"));
        }
    }
}
