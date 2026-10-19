using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>DataGrid AutoGenerateColumns, which the generator compiles from the item type instead of reflecting at run time.</summary>
    [TestFixture]
    public class AutoGenerateColumnsGeneratorTests
    {
        private const string code_behind = @"
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public BindableList<Person> People { get; } = new BindableList<Person>();
        public BindableList<object> Anything { get; } = new BindableList<object>();
        public IEnumerable Untyped { get; } = new ArrayList();
        public BindableBool AutoColumns { get; } = new BindableBool(true);
    }

    public class Entity
    {
        public int Id { get; set; }
    }

    public class Person : Entity
    {
        public Bindable<string> Name { get; } = new Bindable<string>();
        public readonly BindableInt Age = new BindableInt();
        public BindableBool Active { get; } = new BindableBool();
        public Bindable<bool?> Verified { get; } = new Bindable<bool?>();

        [Display(Name = ""E-mail"", Order = 0)]
        public string Email { get; set; }

        [DisplayName(""Nick"")]
        [ReadOnly(true)]
        public Bindable<string> Nickname { get; } = new Bindable<string>();

        [Browsable(false)]
        public string Secret { get; set; }

        [Display(AutoGenerateField = false)]
        public string Hidden { get; set; }

        public BindableList<string> Tags { get; } = new BindableList<string>();
        public System.Action Changed;
        public static int Count;
        internal string Internal;
        public string this[int i] => null;
    }

    public partial class View : CompositeDrawable
    {
        private void OnGenerating(OsuXaml.Controls.DataGridAutoGeneratingColumnEventArgs e) { }
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string page(string content) => header + "\n" + content + "\n</CompositeDrawable>";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, page(content));

        private static Microsoft.CodeAnalysis.Diagnostic error(string content) => GeneratorRunner.SingleError(code_behind, page(content));

        [Test]
        public void AGridWithoutColumnsGeneratesOnePerMember()
        {
            string source = compiles(@"<DataGrid ItemsSource=""{Bind People}"" AutoGeneratingColumn=""OnGenerating"" />");

            Assert.That(source, Does.Contain("e0.AutoGenerateColumns = true;"));
            Assert.That(source, Does.Contain(".AutoGeneratingColumn += (a0) => this.OnGenerating(a0)"));

            // Display(Order = 0) puts Email first, then base class members, then declaration order.
            string[] order = { "\"Email\"", "\"Id\"", "\"Name\"", "\"Age\"", "\"Active\"", "\"Verified\"", "\"Nickname\"" };
            int last = -1;

            foreach (string name in order)
            {
                int at = source.IndexOf("DataGridAutoColumn(" + name, System.StringComparison.Ordinal);
                Assert.That(at, Is.GreaterThan(last), name + " in order");
                last = at;
            }

            Assert.That(source, Does.Contain("DataGridAutoColumn(\"Name\", typeof(string), () => new global::OsuXaml.Controls.DataGridTextColumn { Header = \"Name\", Binding = new global::OsuXaml.ItemBinding<global::Demo.Person, string>(\"Name\""));
            Assert.That(source, Does.Contain(".Name.Value)"));
            Assert.That(source, Does.Contain("typeof(int), () => new global::OsuXaml.Controls.DataGridTextColumn { Header = \"Age\""));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.DataGridCheckBoxColumn { Header = \"Active\""));
            Assert.That(source, Does.Contain("Header = \"Verified\", Binding = new global::OsuXaml.ItemBinding<global::Demo.Person, bool?>(\"Verified\""));
            Assert.That(source, Does.Contain("IsThreeState = true"));

            // Attributes name, protect and hide members; plain members can't be written back.
            Assert.That(source, Does.Contain("Header = \"E-mail\""));
            Assert.That(source, Does.Contain("Header = \"Nick\""));
            Assert.That(source, Does.Match("Header = \"Nick\".*IsReadOnly = true"));
            Assert.That(source, Does.Match("Header = \"Id\".*IsReadOnly = true"));
            Assert.That(source, Does.Not.Match("Header = \"Name\"[^\\n]*IsReadOnly"));

            foreach (string skipped in new[] { "Secret", "Hidden", "Tags", "Changed", "Count", "Internal", "this[]" })
                Assert.That(source, Does.Not.Contain("DataGridAutoColumn(\"" + skipped + "\""), skipped);
        }

        [Test]
        public void DeclaredColumnsTurnItOffUnlessAsked()
        {
            string declared = compiles(@"
  <DataGrid ItemsSource=""{Bind People}"">
    <DataGrid.Columns>
      <DataGridTextColumn Header=""Who"" Binding=""{Binding Name}"" />
    </DataGrid.Columns>
  </DataGrid>");

            Assert.That(declared, Does.Not.Contain("SetAutoGeneratedColumns"));

            string both = compiles(@"
  <DataGrid ItemsSource=""{Bind People}"" AutoGenerateColumns=""True"">
    <DataGrid.Columns>
      <DataGridTextColumn Header=""Who"" Binding=""{Binding Name}"" />
    </DataGrid.Columns>
  </DataGrid>");

            Assert.That(both, Does.Contain("AutoGenerateColumns = true"));
            Assert.That(both, Does.Contain("SetAutoGeneratedColumns"));
            Assert.That(both.IndexOf("Header = \"Who\"", System.StringComparison.Ordinal), Is.LessThan(both.IndexOf("SetAutoGeneratedColumns", System.StringComparison.Ordinal)));

            string off = compiles(@"<DataGrid ItemsSource=""{Bind People}"" AutoGenerateColumns=""False"" />");
            Assert.That(off, Does.Contain("AutoGenerateColumns = false"));
            Assert.That(off, Does.Not.Contain("SetAutoGeneratedColumns"));

            // A binding decides at run time, so the columns are compiled in case it turns them on.
            string bound = compiles(@"<DataGrid ItemsSource=""{Bind People}"" AutoGenerateColumns=""{Bind AutoColumns}"" />");
            Assert.That(bound, Does.Contain("SetAutoGeneratedColumns"));
            Assert.That(bound, Does.Not.Contain("AutoGenerateColumns = true;"));
        }

        [Test]
        public void ItemTypeMustBeKnownAtBuildTime()
        {
            // Without AutoGenerateColumns written out, an untyped grid just has no columns, as before.
            string quiet = compiles(@"<DataGrid ItemsSource=""{Bind Untyped}"" />");
            Assert.That(quiet, Does.Not.Contain("SetAutoGeneratedColumns"));

            var untyped = error(@"<DataGrid ItemsSource=""{Bind Untyped}"" AutoGenerateColumns=""True"" />");
            Assert.That(untyped.Id, Is.EqualTo("OX0009"));
            Assert.That(untyped.GetMessage(), Does.Contain("typed list"));

            var objects = error(@"<DataGrid ItemsSource=""{Bind Anything}"" AutoGenerateColumns=""True"" />");
            Assert.That(objects.Id, Is.EqualTo("OX0009"));
        }
    }
}
