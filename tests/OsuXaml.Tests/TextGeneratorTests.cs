using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Text in XAML: inlines in a TextBlock, WPF's whitespace rules, and multi-line TextBoxes.</summary>
    [TestFixture]
    public class TextGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Name { get; } = new Bindable<string>(""Ann"");
        public Bindable<string> Notes { get; } = new Bindable<string>("""");
    }

    public partial class View : CompositeDrawable
    {
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string page(string content) => header + "\n" + content + "\n</CompositeDrawable>";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, page(content));

        /// <summary>Asserts each piece appears in the source after the one before it.</summary>
        private static void inOrder(string source, params string[] pieces)
        {
            int at = 0;

            foreach (string piece in pieces)
            {
                int found = source.IndexOf(piece, at, System.StringComparison.Ordinal);
                Assert.That(found, Is.GreaterThanOrEqualTo(0), $"\"{piece}\" after position {at} in:\n{source}");
                at = found + piece.Length;
            }
        }

        [Test]
        public void TextAndInlinesKeepTheirOrderWithWpfWhitespace()
        {
            string source = compiles(@"
  <TextBlock>
    Hello,
    <Bold>bold</Bold> <Italic>italic</Italic>
    <LineBreak />
    second   line
  </TextBlock>");

            // Whitespace collapses to one space, is dropped inside the tags and around LineBreak, and stays between inlines.
            inOrder(source,
                "e0.AddText(\"Hello, \");",
                "new global::OsuXaml.Controls.Bold();", "e1.AddText(\"bold\");", "e0.Add(e1);",
                "e0.AddText(\" \");",
                "new global::OsuXaml.Controls.Italic();", "e2.AddText(\"italic\");", "e0.Add(e2);",
                "new global::OsuXaml.Controls.LineBreak();", "e0.Add(e3);",
                "e0.AddText(\"second line\");");
            Assert.That(source, Does.Not.Contain("AddText(\" \\n"));
        }

        [Test]
        public void TextOnlyContentSetsText()
        {
            string source = compiles(@"
  <StackPanel>
    <TextBlock>  just
       text  </TextBlock>
    <TextBlock xml:space=""preserve"">  kept   as is </TextBlock>
  </StackPanel>");

            Assert.That(source, Does.Contain("e1.Text = \"just text\";"));
            Assert.That(source, Does.Contain("e2.Text = \"  kept   as is \";"));
            Assert.That(source, Does.Not.Contain("AddText"));
        }

        [Test]
        public void RunsSpansAndTheInlinesProperty()
        {
            string source = compiles(@"
  <StackPanel>
    <TextBlock>Hi <Run Text=""{Binding Name}"" FontWeight=""Bold"" />!</TextBlock>
    <TextBlock><Span Foreground=""Red"">red <Underline>under</Underline></Span></TextBlock>
    <TextBlock>
      <TextBlock.Inlines>
        <Run>one</Run>
        <LineBreak />
        <Run FontSize=""24"" FontStyle=""Italic"" TextDecorations=""Underline, Strikethrough"">two</Run>
      </TextBlock.Inlines>
    </TextBlock>
  </StackPanel>");

            inOrder(source, "e1.AddText(\"Hi \");", "XamlBinding.OneWay(this.xamlBindings, dataContext.Name, (x, _) => e2.Text = x);",
                "e2.FontWeight = global::OsuXaml.Controls.FontWeight.Bold;", "e1.Add(e2);", "e1.AddText(\"!\");");
            inOrder(source, "e4.Foreground = global::osu.Framework.Graphics.Colour4.Red;", "e4.AddText(\"red \");",
                "e5.AddText(\"under\");", "e4.Add(e5);", "e3.Add(e4);");
            inOrder(source, "e7.Text = \"one\";", "e6.Inlines.Add(e7);", "e6.Inlines.Add(e8);",
                "e9.FontSize = 24f;", "e9.FontStyle = global::OsuXaml.Controls.FontStyle.Italic;",
                "e9.TextDecorations = global::OsuXaml.Controls.TextDecorations.Underline | global::OsuXaml.Controls.TextDecorations.Strikethrough;",
                "e9.Text = \"two\";", "e6.Inlines.Add(e9);");
        }

        [Test]
        public void TextInTheInlinesPropertyElement()
        {
            string source = compiles(@"
  <TextBlock>
    <TextBlock.Inlines>
      Hello
      <Bold>there</Bold>
      <Run>again</Run>
    </TextBlock.Inlines>
  </TextBlock>");

            inOrder(source, "e0.Inlines.Add(\"Hello \");", "e0.Inlines.Add(e1);", "e0.Inlines.Add(\" \");", "e0.Inlines.Add(e2);");
        }

        [Test]
        public void MultilineTextBox()
        {
            string source = compiles(@"
  <TextBox Text=""{Binding Notes}"" AcceptsReturn=""True"" TextWrapping=""Wrap"" MinLines=""3"" MaxLines=""8"" />");

            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e0.Text, dataContext.Notes)"));
            Assert.That(source, Does.Contain("e0.AcceptsReturn = true;"));
            Assert.That(source, Does.Contain("e0.TextWrapping = global::OsuXaml.Controls.TextWrapping.Wrap;"));
            Assert.That(source, Does.Contain("e0.MinLines = 3;"));
            Assert.That(source, Does.Contain("e0.MaxLines = 8;"));
        }
    }
}
