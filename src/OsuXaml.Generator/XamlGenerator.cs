using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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

            // ResourceDictionary files are shared by every page, like App.xaml resources in WPF.
            var dictionaries = files.Where(f => f.Text != null && isResourceDictionary(f.Text)).Collect();

            context.RegisterSourceOutput(files.Combine(dictionaries).Combine(context.CompilationProvider), (spc, pair) =>
            {
                var (((path, text), shared), compilation) = pair;
                if (text == null)
                    return;

                var result = new XamlCompiler(compilation, path, shared).Compile(text);

                foreach (var diagnostic in result.Diagnostics)
                    spc.ReportDiagnostic(diagnostic);

                if (result.Source != null)
                    spc.AddSource(hintName(path, result.ClassName), SourceText.From(result.Source, Encoding.UTF8));
            });
        }

        private static bool isResourceDictionary(string text)
        {
            try
            {
                var root = XDocument.Parse(text).Root;
                return root?.Name.LocalName == "ResourceDictionary"
                       && !root.Attributes().Any(a => a.Name.LocalName == "Class" && a.Name.NamespaceName != string.Empty);
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static string hintName(string path, string className)
            => (className ?? Path.GetFileNameWithoutExtension(path)).Replace('.', '_') + ".xaml.g.cs";
    }
}
