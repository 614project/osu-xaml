using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Uri properties take an address as text, as WPF's and WinUI's (<c>NavigateUri="https://..."</c>).</summary>
    [TestFixture]
    public class UriLiteralGeneratorTests
    {
        private const string code_behind = @"
using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
    }

    public partial class Link : Drawable
    {
        public Uri Address { get; set; }
    }

    public partial class View : CompositeDrawable
    {
    }
}";

        private static string page(string content) => @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">" + "\n" + content + "\n</CompositeDrawable>";

        [Test]
        public void AbsoluteAndRelativeAddresses()
        {
            string source = GeneratorRunner.Compiles(code_behind, page(@"
  <Container>
    <local:Link Address=""https://learn.microsoft.com/windows/apps/"" />
    <local:Link Address=""docs/readme.md"" />
  </Container>"));

            Assert.That(source, Does.Contain(@"Address = new global::System.Uri(""https://learn.microsoft.com/windows/apps/"", global::System.UriKind.RelativeOrAbsolute)"));
            Assert.That(source, Does.Contain(@"Address = new global::System.Uri(""docs/readme.md"", global::System.UriKind.RelativeOrAbsolute)"));
        }
    }
}
