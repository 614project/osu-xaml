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
    /// Runs the XAML source generator in memory against real osu!framework and OsuXaml references, then compiles
    /// the result, so tests see both the generator's diagnostics and any C# errors in the generated code.
    /// </summary>
    public static class GeneratorRunner
    {
        public sealed class Result
        {
            public ImmutableArray<Diagnostic> Diagnostics;
            public Diagnostic[] CompileErrors;
            public string Source;

            public override string ToString() => string.Join("\n", Diagnostics.Concat(CompileErrors)) + "\n" + Source;
        }

        /// <param name="codeBehind">C# for the view model and the partial x:Class.</param>
        /// <param name="xaml">The page, compiled as View.xaml.</param>
        /// <param name="extraFiles">More XAML files (path, text), e.g. shared ResourceDictionary files.</param>
        public static Result Run(string codeBehind, string xaml, params (string Path, string Text)[] extraFiles)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                             .Split(Path.PathSeparator)
                             .Concat(new[] { typeof(osu.Framework.Game).Assembly.Location, typeof(XamlBinding).Assembly.Location })
                             .Distinct()
                             .Select(p => MetadataReference.CreateFromFile(p));

            var compilation = CSharpCompilation.Create("Demo",
                new[] { CSharpSyntaxTree.ParseText(codeBehind) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var texts = new[] { (AdditionalText)new InMemoryText("View.xaml", xaml) }
                        .Concat(extraFiles.Select(f => (AdditionalText)new InMemoryText(f.Path, f.Text)));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new XamlGenerator().AsSourceGenerator() }, texts);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

            return new Result
            {
                Diagnostics = diagnostics,
                CompileErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(),
                Source = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString())),
            };
        }

        /// <summary>Asserts the XAML compiles cleanly and returns the generated C#.</summary>
        public static string Compiles(string codeBehind, string xaml, params (string Path, string Text)[] extraFiles)
        {
            var result = Run(codeBehind, xaml, extraFiles);
            Assert.That(result.Diagnostics, Is.Empty, result.ToString());
            Assert.That(result.CompileErrors, Is.Empty, result.ToString());
            return result.Source;
        }

        /// <summary>Asserts the XAML produces exactly one generator diagnostic and returns it.</summary>
        public static Diagnostic SingleError(string codeBehind, string xaml, params (string Path, string Text)[] extraFiles)
        {
            var result = Run(codeBehind, xaml, extraFiles);
            Assert.That(result.Diagnostics, Has.Length.EqualTo(1), result.ToString());
            return result.Diagnostics[0];
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
