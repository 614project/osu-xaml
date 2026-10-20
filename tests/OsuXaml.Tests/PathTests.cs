using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;
using Path = OsuXaml.Controls.Path;

namespace OsuXaml.Tests
{
    /// <summary>Path markup through the generator: inline data, a Geometry resource and icon-style stretching.</summary>
    [TestFixture]
    public class PathGeneratorTests
    {
        [Test]
        public void InlineDataAndGeometryResources()
        {
            string source = ShapesTestData.Compiles(@"
  <Canvas Width=""200"" Height=""100"">
    <Canvas.Resources>
      <Geometry x:Key=""CheckMark"">M 0,5 L 4,9 L 12,0</Geometry>
    </Canvas.Resources>
    <Path Data=""M 10,10 L 50,10 50,40 Z"" Fill=""#f38ba8"" Stroke=""White"" StrokeThickness=""2"" />
    <Path Canvas.Left=""60"" Data=""{StaticResource CheckMark}"" Stroke=""#a6e3a1"" StrokeThickness=""2"" Stretch=""Uniform"" Width=""16"" Height=""16"" />
    <Path Canvas.Left=""100"" Data=""F1 M0,0 C 10,0 20,10 20,20 A 10,10 0 0 1 0,20 z"" Fill=""{Bind Accent}"" />
  </Canvas>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Path()"));
            Assert.That(source, Does.Contain("Geometry.Parse(\"M 10,10 L 50,10 50,40 Z\")"));
            Assert.That(source, Does.Contain("Geometry.Parse(\"M 0,5 L 4,9 L 12,0\")"), "the resource becomes a literal at its use");
        }

        [Test]
        public void StringsFromTheViewModelBindToDataAndPoints()
        {
            ShapesTestData.Compiles(@"
  <StackPanel>
    <Path Data=""{Bind Outline}"" Stroke=""White"" />
    <Polygon Points=""{Binding Corners}"" Fill=""Pink"" />
  </StackPanel>");

            Path path = new Path { Data = "M 0,0 L 10,0" };
            Polyline line = new Polyline { Points = "0,0 5,5" };
            Assert.That(path.Data.Figures[0].Points, Has.Count.EqualTo(2));
            Assert.That(line.Points, Has.Count.EqualTo(2));
        }
    }

    /// <summary>The path markup parser on its own.</summary>
    [TestFixture]
    public class GeometryTests
    {
        private static Vector2[] points(string data, int figure = 0) => Geometry.Parse(data).Figures[figure].Points.ToArray();

        [Test]
        public void LinesAbsoluteAndRelative()
        {
            Assert.That(points("M 10,10 L 50,10 50,40"), Is.EqualTo(new[] { new Vector2(10, 10), new Vector2(50, 10), new Vector2(50, 40) }));
            Assert.That(points("m10 10 l40 0 0 30"), Is.EqualTo(new[] { new Vector2(10, 10), new Vector2(50, 10), new Vector2(50, 40) }));
            Assert.That(points("M0,0 H 20 V 10 h -5 v -5"), Is.EqualTo(new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 10), new Vector2(15, 10), new Vector2(15, 5) }));
            Assert.That(points("M0,0 10,0 10,10"), Has.Length.EqualTo(3), "coordinates after a move are lines");
            Assert.That(points("M-1.5-2L.5.5"), Is.EqualTo(new[] { new Vector2(-1.5f, -2), new Vector2(0.5f, 0.5f) }), "compact numbers");
        }

        [Test]
        public void ClosingAndFigures()
        {
            var geometry = Geometry.Parse("M0,0 L10,0 10,10 Z M20,0 L30,0 L30,10");

            Assert.That(geometry.Figures, Has.Count.EqualTo(2));
            Assert.That(geometry.Figures[0].IsClosed, Is.True);
            Assert.That(geometry.Figures[1].IsClosed, Is.False);
            Assert.That(geometry.Figures.All(f => f.IsFilled), Is.True, "WPF fills open figures too");

            // Drawing on after Z starts again from the figure's start.
            var restarted = Geometry.Parse("M5,5 L10,5 Z l0,10");
            Assert.That(restarted.Figures[1].Points, Is.EqualTo(new[] { new Vector2(5, 5), new Vector2(5, 15) }));
        }

        [Test]
        public void CurvesEndWhereTheyShould()
        {
            var cubic = points("M0,0 C 0,10 10,10 10,0");
            Assert.That(cubic.Last(), Is.EqualTo(new Vector2(10, 0)));
            Assert.That(cubic.Length, Is.GreaterThan(4), "flattened into several segments");
            Assert.That(cubic.Max(p => p.Y), Is.EqualTo(7.5f).Within(0.3f), "a cubic with these controls peaks at 3/4 of their height");

            var smooth = points("M0,0 C 0,10 10,10 10,0 S 20,-10 20,0");
            Assert.That(smooth.Last(), Is.EqualTo(new Vector2(20, 0)));
            Assert.That(smooth.Min(p => p.Y), Is.LessThan(-5), "the mirrored control bends the second half the other way");

            var quadratic = points("M0,0 Q 10,20 20,0 T 40,0");
            Assert.That(quadratic.Last(), Is.EqualTo(new Vector2(40, 0)));
            Assert.That(quadratic.Min(p => p.Y), Is.LessThan(-5));
        }

        [Test]
        public void ArcsFollowTheirCircle()
        {
            // A half circle of radius 10 from (0,0) to (20,0), sweeping clockwise (through y = -10 on screen).
            var arc = points("M0,0 A 10,10 0 0 1 20,0");

            Assert.That(arc.Last(), Is.EqualTo(new Vector2(20, 0)));
            Assert.That(arc.All(p => Precision.AlmostEquals(Vector2.Distance(p, new Vector2(10, 0)), 10, 0.01f)), Is.True);
            Assert.That(arc.Min(p => p.Y), Is.EqualTo(-10).Within(0.2f));

            var other = points("M0,0 A 10,10 0 0 0 20,0");
            Assert.That(other.Max(p => p.Y), Is.EqualTo(10).Within(0.2f), "the other sweep goes the other way");

            var small = points("M0,0 a 1,1 0 0 1 20,0");
            Assert.That(small.All(p => Precision.AlmostEquals(Vector2.Distance(p, new Vector2(10, 0)), 10, 0.01f)), Is.True, "radii too small grow to reach the end");
        }

        [Test]
        public void BadMarkupIsReported()
        {
            Assert.Throws<System.FormatException>(() => Geometry.Parse("10,10"));
            Assert.Throws<System.FormatException>(() => Geometry.Parse("M 0,0 L 10"));
            Assert.Throws<System.FormatException>(() => Geometry.Parse("M 0,0 X 10,10"));
            Assert.That(Geometry.Parse("").Figures, Is.Empty);
            Assert.That(Geometry.Parse("M 0,0 L 10,10").ToString(), Is.EqualTo("M 0,0 L 10,10"));
        }
    }

    /// <summary>Path at runtime: drawing its figures, its fill rule and stretching like an icon.</summary>
    [HeadlessTest]
    public partial class PathTests : TestScene
    {
        private static bool near(RectangleF a, RectangleF b, float within = 0.05f) =>
            Precision.AlmostEquals(a.X, b.X, within) && Precision.AlmostEquals(a.Y, b.Y, within)
            && Precision.AlmostEquals(a.Width, b.Width, within) && Precision.AlmostEquals(a.Height, b.Height, within);

        [Test]
        public void DrawsFiguresAtTheirCoordinates()
        {
            Path path = null;

            AddStep("create", () => Child = path = new Path
            {
                Data = Geometry.Parse("M 10,10 L 50,10 50,40 Z M 60,0 L 80,20"),
                Stroke = Colour4.White,
                StrokeThickness = 2,
                Fill = Colour4.Pink,
            });

            AddUntilStep("both figures stroked", () => path.StrokeMesh.Contains(new Vector2(30, 10)) && path.StrokeMesh.Contains(new Vector2(70, 10)));
            AddAssert("sized to its furthest point", () => path.DrawSize == new Vector2(81, 41));
            AddAssert("closed figure is stroked back to its start", () => path.StrokeMesh.Contains(new Vector2(30, 25)));
            AddAssert("the triangle is filled", () => near(path.FillMesh.Bounds, new RectangleF(10, 10, 40, 30)) && Precision.AlmostEquals(path.FillMesh.Area, 600, 0.5f));
            AddAssert("the open line has no area", () => !path.FillMesh.Contains(new Vector2(70, 10.2f)));
        }

        [Test]
        public void FillRuleComesFromTheMarkup()
        {
            Path evenOdd = null, nonzero = null;
            const string rings = "M 0,0 L 40,0 40,40 0,40 Z M 10,10 L 30,10 30,30 10,30 Z";

            AddStep("create", () =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Add(evenOdd = new Path { Data = Geometry.Parse(rings), Fill = Colour4.White });
                panel.Add(nonzero = new Path { Data = Geometry.Parse("F1 " + rings), Fill = Colour4.White });
                Child = panel;
            });

            AddUntilStep("F0 (the default) leaves a hole", () => Precision.AlmostEquals(evenOdd.FillMesh.Area, 1200, 0.5f) && !evenOdd.FillMesh.Contains(new Vector2(20, 20)));
            AddAssert("F1 fills it", () => Precision.AlmostEquals(nonzero.FillMesh.Area, 1600, 0.5f) && nonzero.FillMesh.Contains(new Vector2(20, 20)));
        }

        [Test]
        public void UniformStretchScalesLikeAnIcon()
        {
            Path path = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Add(path = new Path { Data = Geometry.Parse("M 0,0 L 10,0 10,5 0,5 Z"), Fill = Colour4.White, Stretch = Stretch.Uniform, Width = 40, Height = 40 });
                Child = panel;
            });

            AddUntilStep("filled across the width", () => near(path.FillMesh.Bounds, new RectangleF(0, 0, 40, 20), 0.01f));
        }
    }
}
