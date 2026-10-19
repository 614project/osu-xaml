using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OsuXaml.Generator;

namespace OsuXaml.Schema
{
    /// <summary>
    /// Writes the XAML completion schemas for one project after it builds (see OsuXaml.Schema.targets). The source generator
    /// can't write files, so this reads the built assembly and its references instead.
    /// </summary>
    /// <remarks>
    /// Takes one argument, a file of <c>key=value</c> lines: <c>out</c> (the folder to write to), <c>project</c> (the built
    /// assembly), <c>reference</c> (each assembly it compiled against) and <c>xaml</c> (each XAML file).
    /// </remarks>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length != 1 || !File.Exists(args[0]))
            {
                Console.Error.WriteLine("usage: OsuXaml.Schema <arguments file>");
                return 2;
            }

            var options = File.ReadAllLines(args[0])
                              .Select(l => l.Split(new[] { '=' }, 2))
                              .Where(p => p.Length == 2 && p[1].Length > 0)
                              .ToLookup(p => p[0].Trim(), p => p[1].Trim());

            string output = options["out"].Single();
            string projectPath = Path.GetFullPath(options["project"].Single());

            var references = options["reference"].Select(Path.GetFullPath)
                                                 .Where(p => !string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .Select(p => MetadataReference.CreateFromFile(p))
                                                 .ToList();

            var project = MetadataReference.CreateFromFile(projectPath);

            // Internal types of the project itself can be used in its XAML, so they are imported too.
            var compilation = CSharpCompilation.Create("OsuXaml.Schema", references: references.Append(project),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Internal));

            if (!(compilation.GetAssemblyOrModuleSymbol(project) is IAssemblySymbol projectAssembly))
            {
                Console.Error.WriteLine($"OsuXaml.Schema: {projectPath} couldn't be read");
                return 1;
            }

            var uris = options["xaml"].Where(File.Exists).SelectMany(f => XamlSchema.ClrNamespaceUris(File.ReadAllText(f)));
            var files = XamlSchema.Generate(compilation, projectAssembly, uris);

            Directory.CreateDirectory(output);

            // Schemas of clr-namespace URIs no XAML file uses any more are removed.
            var written = new HashSet<string>(files.Select(f => f.FileName), StringComparer.OrdinalIgnoreCase);

            foreach (string stale in Directory.GetFiles(output, "clr-namespace.*.xsd").Where(f => !written.Contains(Path.GetFileName(f))))
                File.Delete(stale);

            // Unchanged files are left alone so editors don't reload them on every build.
            foreach (var (name, content) in files)
            {
                string path = Path.Combine(output, name);

                if (!File.Exists(path) || File.ReadAllText(path) != content)
                    File.WriteAllText(path, content);
            }

            return 0;
        }
    }
}
