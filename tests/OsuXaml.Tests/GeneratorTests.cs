using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using OsuXaml.Generator;

namespace OsuXaml.Tests
{
    /// <summary>
    /// Runs the source generator in memory against real osu!framework references and checks
    /// both the generated code and the build errors reported for broken XAML.
    /// </summary>
    [TestFixture]
    public class GeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
        public BindableBool Enabled { get; } = new BindableBool();
        public BindableList<Row> Rows { get; } = new BindableList<Row>();
        public void Save() { }
        public void Remove(Row row) { }
    }

    public class Row
    {
        public string Name { get; set; }
        public BindableFloat Progress { get; } = new BindableFloat();
    }

    public partial class View : CompositeDrawable
    {
        private static float Opacity(bool enabled) => enabled ? 1 : 0.5f;
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        [Test]
        public void ValidXamlGeneratesCompilingCode()
        {
            var (diagnostics, compileErrors, source) = run(header + @"
  <FillFlowContainer RelativeSizeAxes=""Both"" Direction=""Vertical"" Spacing=""0,4"" Padding=""8,4"">
    <SpriteText Text=""{Bind Title}"" Font=""Bold,20"" Colour=""#ff8800"" />
    <SpriteText>Static text</SpriteText>
    <BasicCheckbox Current=""{Bind Enabled}"" Alpha=""{Bind Enabled, Mode=OneWay, Converter=Opacity, Animate=150, Easing=OutQuad}"" />
    <BasicButton Text=""Save"" Size=""80,30"" Action=""{Bind Save}"" />
    <FillFlowContainer ItemsSource=""{Bind Rows}"" RelativeSizeAxes=""X"" AutoSizeAxes=""Y"">
      <FillFlowContainer.ItemTemplate>
        <Container RelativeSizeAxes=""X"" Height=""20"">
          <SpriteText Text=""{Bind Name}"" />
          <SpriteText Text=""{Bind Progress, StringFormat='{0:P0}'}"" Anchor=""CentreRight"" Origin=""CentreRight"" />
          <BasicButton Text=""x"" Action=""{Bind Remove, Source=Root}"" />
        </Container>
      </FillFlowContainer.ItemTemplate>
    </FillFlowContainer>
  </FillFlowContainer>
</CompositeDrawable>");

            Assert.That(diagnostics, Is.Empty);
            Assert.That(compileErrors, Is.Empty, source);
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e"));
            Assert.That(source, Does.Contain("dataContext.Remove(item"));
            Assert.That(source, Does.Contain("Easing.OutQuad"));
        }

        [Test]
        public void UnknownPropertyIsReportedOnItsLine()
        {
            var diagnostic = single(header + @"
  <Box Colour=""White"" Wibble=""3"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0004"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Wibble"));
            Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line, Is.EqualTo(2));
        }

        [Test]
        public void UnknownBindingPathIsReported()
        {
            var diagnostic = single(header + @"
  <SpriteText Text=""{Bind Titel}"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0006"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Titel"));
        }

        [Test]
        public void BindingTypeMismatchNeedsConverter()
        {
            var diagnostic = single(header + @"
  <Box Alpha=""{Bind Title}"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Converter"));
        }

        [Test]
        public void TwoWayBindingRequiresMatchingBindable()
        {
            var diagnostic = single(header + @"
  <BasicCheckbox Current=""{Bind Title}"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
        }

        [Test]
        public void InvalidEnumListsValidValues()
        {
            var diagnostic = single(header + @"
  <Box Anchor=""Middle"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0005"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Centre"));
        }

        [Test]
        public void UnknownElementIsReported()
        {
            var diagnostic = single(header + @"
  <StackPanel />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0003"));
        }

        private static Diagnostic single(string xaml)
        {
            var (diagnostics, _, _) = run(xaml);
            Assert.That(diagnostics, Has.Length.EqualTo(1), string.Join("\n", diagnostics));
            return diagnostics[0];
        }

        private static (ImmutableArray<Diagnostic> Diagnostics, Diagnostic[] CompileErrors, string Source) run(string xaml)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                             .Split(Path.PathSeparator)
                             .Concat(new[] { typeof(osu.Framework.Game).Assembly.Location, typeof(XamlBinding).Assembly.Location })
                             .Distinct()
                             .Select(p => MetadataReference.CreateFromFile(p));

            var compilation = CSharpCompilation.Create("Demo",
                new[] { CSharpSyntaxTree.ParseText(code_behind) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new XamlGenerator().AsSourceGenerator() },
                new[] { (AdditionalText)new InMemoryText("View.xaml", xaml) });

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

            var result = driver.GetRunResult();
            string source = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
            var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

            return (diagnostics, errors, source);
        }

        private sealed class InMemoryText : AdditionalText
        {
            private readonly string text;

            public InMemoryText(string path, string text)
            {
                Path = path;
                this.text = text;
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
        }
    }
}
