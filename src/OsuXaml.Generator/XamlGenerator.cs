using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace OsuXaml.Generator
{
    /// <summary>
    /// Compiles every <c>*.xaml</c> AdditionalFile into a partial class with an <c>InitializeComponent</c> method
    /// that builds the osu!framework drawable tree and wires bindings with plain, strongly typed C#.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class XamlGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var files = context.AdditionalTextsProvider
                               .Where(f => f.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                               .Select((f, ct) => (f.Path, Text: f.GetText(ct)?.ToString()));

            context.RegisterSourceOutput(files.Combine(context.CompilationProvider), (spc, pair) =>
            {
                var ((path, text), compilation) = pair;
                if (text == null)
                    return;

                var result = new XamlCompiler(compilation, path).Compile(text);

                foreach (var diagnostic in result.Diagnostics)
                    spc.ReportDiagnostic(diagnostic);

                if (result.Source != null)
                    spc.AddSource(hintName(path, result.ClassName), SourceText.From(result.Source, Encoding.UTF8));
            });
        }

        private static string hintName(string path, string className)
            => (className ?? Path.GetFileNameWithoutExtension(path)).Replace('.', '_') + ".xaml.g.cs";
    }
}
