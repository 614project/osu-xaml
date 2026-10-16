using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>Shared XAML scaffolding for the shapes, image, viewbox and grid tests.</summary>
    public static class ShapesTestData
    {
        public const string CODE_BEHIND = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Logo { get; } = new Bindable<string>(""Textures/logo"");
        public Bindable<Colour4> Accent { get; } = new Bindable<Colour4>(Colour4.Pink);
        public Bindable<float> Thickness { get; } = new Bindable<float>(2);
        public Bindable<float> Progress { get; } = new Bindable<float>(40);
        public Bindable<string> IconName { get; } = new Bindable<string>(""Solid.Check"");
        public OsuXaml.Controls.PointCollection Trend { get; } = OsuXaml.Controls.PointCollection.Parse(""0,10 10,0 20,5"");
    }

    public partial class View : CompositeDrawable
    {
        private void OnSplitterDone() { }
        private void OnImageFailed(string source) { }
    }
}";

        public static string Page(string content) => @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">
" + content + @"
</CompositeDrawable>";

        public static string Compiles(string content) => GeneratorRunner.Compiles(CODE_BEHIND, Page(content));

        public static Microsoft.CodeAnalysis.Diagnostic Error(string content) => GeneratorRunner.SingleError(CODE_BEHIND, Page(content));
    }

    /// <summary>Row and column definitions in both the compact string form and WPF's element form, through the generator.</summary>
    [TestFixture]
    public class GridDefinitionGeneratorTests
    {
        [Test]
        public void ElementFormAddsDefinitions()
        {
            string source = ShapesTestData.Compiles(@"
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height=""Auto"" />
      <RowDefinition Height=""*"" MinHeight=""40"" />
    </Grid.RowDefinitions>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width=""120"" />
      <ColumnDefinition Width=""2*"" MinWidth=""40"" MaxWidth=""400"" />
      <ColumnDefinition />
    </Grid.ColumnDefinitions>
    <TextBlock Text=""Title"" Grid.ColumnSpan=""3"" />
    <Border Grid.Row=""1"" Grid.Column=""1"" Background=""#313244"" />
  </Grid>");

            Assert.That(source, Does.Contain(".RowDefinitions.Add(e"));
            Assert.That(source, Does.Contain(".ColumnDefinitions.Add(e"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.RowDefinition()"));
            Assert.That(source, Does.Contain("Height = global::OsuXaml.Controls.GridLength.Parse(\"Auto\")"));
            Assert.That(source, Does.Contain("MinWidth = 40f"));
        }

        [Test]
        public void SingleDefinitionElementAlsoAdds()
        {
            string source = ShapesTestData.Compiles(@"
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width=""Auto"" />
    </Grid.ColumnDefinitions>
  </Grid>");

            Assert.That(source, Does.Contain(".ColumnDefinitions.Add(e"));
        }

        [Test]
        public void CompactFormStillParses()
        {
            string source = ShapesTestData.Compiles(@"<Grid RowDefinitions=""Auto,*"" ColumnDefinitions=""100,2*,*"" RowSpacing=""4"" />");

            Assert.That(source, Does.Contain("GridLengthCollection.Parse(\"Auto,*\")"));
            Assert.That(source, Does.Contain("GridLengthCollection.Parse(\"100,2*,*\")"));
        }

        [Test]
        public void DefinitionLengthsCompileToParse()
        {
            // The generator validates the compact form at build time; a single length goes through GridLength.Parse.
            string source = ShapesTestData.Compiles(@"
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height=""2.5*"" MaxHeight=""300"" />
    </Grid.RowDefinitions>
  </Grid>");

            Assert.That(source, Does.Contain("GridLength.Parse(\"2.5*\")"));
            Assert.That(GridLength.Parse("2.5*").Value, Is.EqualTo(2.5f));
            Assert.Throws<System.FormatException>(() => GridLength.Parse("tall"));
        }
    }

    /// <summary>Definitions at runtime: element-form tracks with limits, and both forms laying out the same way.</summary>
    [HeadlessTest]
    public partial class GridDefinitionTests : TestScene
    {
        private static Box box(float width = 0, float height = 0) => new Box { Width = width, Height = height };

        [Test]
        public void ElementDefinitionsLayOutLikeTheStringForm()
        {
            Grid compact = null, elements = null;
            Box a = null, b = null;

            AddStep("create", () =>
            {
                compact = new Grid { Size = new Vector2(300, 100), ColumnDefinitions = GridLengthCollection.Parse("100,*,2*") };
                compact.Add(a = box());
                Grid.SetColumn(a, 2);

                elements = new Grid { Size = new Vector2(300, 100), Y = 120 };
                elements.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                elements.ColumnDefinitions.Add(new ColumnDefinition());
                elements.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Parse("2*") });
                elements.Add(b = box());
                Grid.SetColumn(b, 2);

                Children = new Drawable[] { compact, elements };
            });

            AddUntilStep("same column widths", () => Precision.AlmostEquals(a.DrawWidth, 400f / 3, 0.01f) && b.DrawWidth == a.DrawWidth);
            AddAssert("actual widths reported", () =>
            {
                var column = (ColumnDefinition)elements.ColumnDefinitions[1];
                return Precision.AlmostEquals(column.ActualWidth, 200f / 3, 0.01f) && elements.ColumnDefinitions[0].ActualLength == 100;
            });
            AddAssert("string form round-trips", () => compact.ColumnDefinitions.ToString() == "100,*,2*");
        }

        [Test]
        public void MinWidthKeepsAStarColumnFromShrinking()
        {
            Grid grid = null;
            ColumnDefinition narrow = null, wide = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(300, 50) };
                grid.ColumnDefinitions.Add(narrow = new ColumnDefinition { Width = GridLength.Parse("*"), MinWidth = 120 });
                grid.ColumnDefinitions.Add(wide = new ColumnDefinition { Width = GridLength.Parse("4*") });
                Child = grid;
            });

            AddAssert("minimum wins over the weight", () => narrow.ActualWidth == 120 && wide.ActualWidth == 180);
            AddStep("cap the wide column", () => wide.MaxWidth = 100);
            AddUntilStep("maximum holds and the other takes the rest", () => wide.ActualWidth == 100 && narrow.ActualWidth == 200);
        }

        [Test]
        public void AutoAndPixelRowsRespectLimits()
        {
            Grid grid = null;
            RowDefinition auto = null, pixel = null, rest = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(100, 300) };
                grid.RowDefinitions.Add(auto = new RowDefinition { Height = GridLength.Auto, MaxHeight = 30 });
                grid.RowDefinitions.Add(pixel = new RowDefinition { Height = new GridLength(10), MinHeight = 50 });
                grid.RowDefinitions.Add(rest = new RowDefinition());

                var tall = box(height: 80);
                grid.Add(tall);
                Child = grid;
            });

            AddAssert("auto row capped", () => auto.ActualHeight == 30);
            AddAssert("pixel row raised to its minimum", () => pixel.ActualHeight == 50);
            AddAssert("star row takes the rest", () => rest.ActualHeight == 220);
        }

        [Test]
        public void LengthsChangedAtRuntimeApplyOnTheNextPass()
        {
            Grid grid = null;
            Box left = null;

            AddStep("create", () =>
            {
                grid = new Grid { Size = new Vector2(200, 50), ColumnDefinitions = GridLengthCollection.Parse("50,*") };
                grid.Add(left = box());
                Child = grid;
            });

            AddAssert("pixel column", () => left.DrawWidth == 50);
            AddStep("widen", () => grid.ColumnDefinitions[0].Length = new GridLength(120));
            AddUntilStep("follows the new length", () => left.DrawWidth == 120);
        }
    }
}
