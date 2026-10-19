using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using OsuXaml.Generator;

namespace OsuXaml.Tests
{
    /// <summary>The completion schemas (XSD) written for XML editors: they compile, accept every sample, and know the controls.</summary>
    [TestFixture]
    public class SchemaTests
    {
        private static readonly XNamespace xs = "http://www.w3.org/2001/XMLSchema";

        private static string repository
        {
            get
            {
                for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "OsuXaml.slnx")))
                        return dir.FullName;
                }

                throw new InvalidOperationException("The repository root wasn't found.");
            }
        }

        private static IEnumerable<TestCaseData> projects()
        {
            yield return new TestCaseData(typeof(TodoApp.TodoGame).Assembly.Location, "samples/TodoApp").SetName("TodoApp");
            yield return new TestCaseData(typeof(Gallery.GalleryGame).Assembly.Location, "samples/Gallery").SetName("Gallery");
            yield return new TestCaseData(typeof(SchemaTests).Assembly.Location, "tests/OsuXaml.Tests/Views").SetName("TestViews");
        }

        [TestCaseSource(nameof(projects))]
        public void EverySampleIsValid(string assemblyPath, string xamlDirectory)
        {
            var xaml = Directory.GetFiles(Path.Combine(repository, xamlDirectory), "*.xaml", SearchOption.AllDirectories)
                                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                                .ToList();

            Assert.That(xaml, Is.Not.Empty);

            var schemas = compile(write(generate(assemblyPath, xaml.Select(File.ReadAllText))));

            foreach (string file in xaml)
                Assert.That(validate(schemas, File.ReadAllText(file)), Is.Empty, file);
        }

        [Test]
        public void ControlsListTheirMembersAttachedPropertiesAndEnumValues()
        {
            var files = generate(typeof(Gallery.GalleryGame).Assembly.Location, new[] { @"<Window xmlns=""osu-xaml"" xmlns:local=""clr-namespace:Gallery"" />" });
            var main = XDocument.Parse(files.Single(f => f.FileName == XamlSchema.MainFile).Content);

            var button = global(main, "Button");
            var buttonAttributes = attributesOf(main, button);

            Assert.That(buttonAttributes, Does.Contain("Click"));
            Assert.That(buttonAttributes, Does.Contain("Content"));
            Assert.That(buttonAttributes, Does.Contain("Width"), "inherited from Drawable");
            Assert.That(buttonAttributes, Does.Contain("Grid.Row"), "attached properties");
            Assert.That(buttonAttributes, Does.Contain("HorizontalAlignment"), "global properties");

            var grid = global(main, "Grid");
            Assert.That(grid.Descendants(xs + "element").Select(e => (string)e.Attribute("name")), Does.Contain("Grid.RowDefinitions").And.Contain("Grid.Resources"));

            var alignment = main.Root!.Elements(xs + "simpleType").Single(t => ((string)t.Attribute("name")).EndsWith(".HorizontalAlignment", StringComparison.Ordinal));
            Assert.That(alignment.Descendants(xs + "enumeration").Select(e => (string)e.Attribute("value")), Does.Contain("Left").And.Contain("Stretch"));

            Assert.That(main.Root.Elements(xs + "element").Select(e => (string)e.Attribute("name")), Does.Contain("Style").And.Contain("DataTemplate").And.Contain("CompositeDrawable"));

            var gallery = XDocument.Parse(files.Single(f => f.FileName == XamlSchema.ClrFileName("clr-namespace:Gallery")).Content);
            Assert.That(gallery.Root!.Elements(xs + "element").Select(e => (string)e.Attribute("name")), Does.Contain("WelcomePage"));

            var catalog = XDocument.Parse(files.Single(f => f.FileName == XamlSchema.CatalogFile).Content);
            Assert.That(catalog.Root!.Elements().Select(e => (string)e.Attribute("name")), Is.EquivalentTo(new[] { "osu-xaml", "osu-xaml:x", "clr-namespace:Gallery" }));
        }

        [Test]
        public void WrongNamesAndValuesAreFlaggedButMarkupIsNot()
        {
            const string page = @"<Window xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Gallery"" x:Class=""Gallery.MainWindow"">
  <StackPanel>
    <Button Content=""Ok"" IsEnabled=""{Bind CanSave}"" HorizontalAlignment=""Middle"" />
    <CheckBox IsChecked=""maybe"" />
    <Buton />
    <local:WelcomePage HorizontalAlignment=""Center"" />
  </StackPanel>
</Window>";

            var schemas = compile(write(generate(typeof(Gallery.GalleryGame).Assembly.Location, new[] { page })));
            var errors = validate(schemas, page);

            Assert.That(errors, Has.Count.EqualTo(3), string.Join("\n", errors));
            Assert.That(errors, Has.Some.Contains("HorizontalAlignment"));
            Assert.That(errors, Has.Some.Contains("IsChecked"));
            Assert.That(errors, Has.Some.Contains("Buton"));
        }

        [Test]
        public void SchemaLocationAttributesAreIgnoredByTheGenerator()
        {
            GeneratorRunner.Compiles(@"
namespace Demo
{
    public partial class View : osu.Framework.Graphics.Containers.CompositeDrawable
    {
    }
}", @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    xsi:schemaLocation=""osu-xaml obj/osu-xaml/osu-xaml.xsd"" x:Class=""Demo.View"">
  <CompositeDrawable.Resources>
    <Colour4 x:Key=""Warm"" xsi:type=""Colour4"">#fab387</Colour4>
  </CompositeDrawable.Resources>
  <Box Colour=""{StaticResource Warm}"" xsi:nil=""false"" />
</CompositeDrawable>");
        }

        private static List<(string FileName, string Content)> generate(string assemblyPath, IEnumerable<string> xaml)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                             .Split(Path.PathSeparator)
                             .Concat(new[] { typeof(osu.Framework.Game).Assembly.Location, typeof(XamlBinding).Assembly.Location, assemblyPath })
                             .Distinct()
                             .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                             .ToList();

            var project = references.Last(r => ((PortableExecutableReference)r).FilePath == assemblyPath);

            var compilation = CSharpCompilation.Create("Schema", references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Internal));

            return XamlSchema.Generate(compilation, (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(project), xaml.SelectMany(XamlSchema.ClrNamespaceUris));
        }

        private static string write(List<(string FileName, string Content)> files)
        {
            string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "schemas", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            foreach (var (name, content) in files)
                File.WriteAllText(Path.Combine(dir, name), content);

            return dir;
        }

        private static XmlSchemaSet compile(string dir)
        {
            var problems = new List<string>();
            var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            set.ValidationEventHandler += (_, e) => problems.Add($"{e.Severity}: {e.Message} ({e.Exception?.SourceUri}:{e.Exception?.LineNumber})");

            try
            {
                foreach (string file in Directory.GetFiles(dir, "*.xsd"))
                    set.Add(null, new Uri(file).AbsoluteUri);

                set.Compile();
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            Assert.That(problems, Is.Empty);
            return set;
        }

        private static List<string> validate(XmlSchemaSet schemas, string xaml)
        {
            var errors = new List<string>();
            var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
            settings.ValidationEventHandler += (_, e) =>
            {
                if (e.Severity == XmlSeverityType.Error)
                    errors.Add($"{e.Exception.LineNumber}: {e.Message}");
            };

            using (var reader = XmlReader.Create(new StringReader(xaml), settings))
            {
                while (reader.Read())
                {
                }
            }

            return errors;
        }

        private static XElement global(XDocument schema, string name) => schema.Root!.Elements(xs + "element").Single(e => (string)e.Attribute("name") == name);

        /// <summary>Attribute names of an element: its own, and those of the attribute groups it refers to.</summary>
        private static List<string> attributesOf(XDocument schema, XElement element)
        {
            var groups = schema.Root!.Elements(xs + "attributeGroup").ToDictionary(g => (string)g.Attribute("name"));
            var names = new List<string>();

            void collect(XElement parent)
            {
                foreach (var child in parent.Elements())
                {
                    if (child.Name == xs + "attribute" && child.Attribute("name") != null)
                        names.Add((string)child.Attribute("name"));
                    else if (child.Name == xs + "attributeGroup" && child.Attribute("ref") is XAttribute reference && groups.TryGetValue(reference.Value, out var group))
                        collect(group);
                    else if (child.Name == xs + "complexType")
                        collect(child);
                }
            }

            collect(element);
            return names;
        }
    }
}
