using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>Shapes written the way a WPF developer would, through the generator.</summary>
    [TestFixture]
    public class ShapesGeneratorTests
    {
        [Test]
        public void ShapesWithLiteralsAndBindings()
        {
            string source = ShapesTestData.Compiles(@"
  <Canvas Width=""300"" Height=""200"">
    <Rectangle Canvas.Left=""10"" Canvas.Top=""10"" Width=""80"" Height=""40"" Fill=""#89b4fa"" Stroke=""White"" StrokeThickness=""2"" RadiusX=""6"" RadiusY=""6"" />
    <Ellipse Canvas.Left=""100"" Width=""40"" Height=""24"" Fill=""{Bind Accent}"" Stroke=""#cdd6f4"" StrokeThickness=""{Bind Thickness}"" />
    <Line X1=""0"" Y1=""0"" X2=""120"" Y2=""{Bind Progress}"" Stroke=""Pink"" StrokeThickness=""3"" />
    <Polyline Points=""0,0 10,20 20,0 30,20"" Stroke=""#a6e3a1"" StrokeThickness=""2"" Stretch=""Uniform"" Width=""60"" Height=""30"" />
    <Polyline Points=""{Bind Trend}"" Stroke=""White"" />
    <Polygon Points=""50,0 100,100 0,100"" Fill=""#f38ba8"" Stroke=""White"" StrokeThickness=""1"" />
  </Canvas>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Rectangle()"));
            Assert.That(source, Does.Contain("RadiusX = 6f"));
            Assert.That(source, Does.Contain("Fill = global::osu.Framework.Graphics.Colour4.FromHex(\"89b4fa\")"));
            Assert.That(source, Does.Contain("PointCollection.Parse(\"0,0 10,20 20,0 30,20\")"));
            Assert.That(source, Does.Contain("Stretch = global::OsuXaml.Controls.Stretch.Uniform"));
            Assert.That(source, Does.Contain("dataContext.Accent"));
        }

        [Test]
        public void ShapesInPanelsWithAlignment()
        {
            ShapesTestData.Compiles(@"
  <StackPanel Spacing=""8"">
    <Rectangle Height=""4"" Fill=""#45475a"" />
    <Ellipse Width=""24"" Height=""24"" Fill=""#a6e3a1"" HorizontalAlignment=""Left"" Margin=""4"" />
    <Grid ColumnDefinitions=""*,*"" Height=""40"">
      <Rectangle Fill=""#313244"" />
      <Ellipse Grid.Column=""1"" Stroke=""White"" Stretch=""Uniform"" />
    </Grid>
  </StackPanel>");
        }
    }

    /// <summary>Point parsing and triangulation, without a game.</summary>
    [TestFixture]
    public class ShapeGeometryTests
    {
        [Test]
        public void PointsParseFromPairsOrCommas()
        {
            var spaced = PointCollection.Parse("0,0 10,20 20,0");
            var commas = PointCollection.Parse("0,0,10,20,20,0");

            Assert.That(spaced, Is.EqualTo(new[] { new Vector2(0, 0), new Vector2(10, 20), new Vector2(20, 0) }));
            Assert.That(commas, Is.EqualTo(spaced));
            Assert.That(spaced.ToString(), Is.EqualTo("0,0 10,20 20,0"));
            Assert.Throws<System.FormatException>(() => PointCollection.Parse("0,0 10"));
            Assert.Throws<System.FormatException>(() => PointCollection.Parse("a,b"));
        }

        private static float area(IReadOnlyList<Vector2> triangles)
        {
            float total = 0;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                total += System.Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
            }

            return total;
        }

        [Test]
        public void ConcavePolygonTriangulatesToItsArea()
        {
            // An L shape: a 20x20 square with the top-right 10x10 quarter missing, in both windings.
            var shape = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(20, 10), new Vector2(20, 20), new Vector2(0, 20) };

            var clockwise = new List<Vector2>();
            ShapeGeometry.Triangulate(shape, clockwise);
            var counter = new List<Vector2>();
            ShapeGeometry.Triangulate(shape.Reverse().ToArray(), counter);

            Assert.That(clockwise.Count, Is.EqualTo(12), "four triangles for six corners");
            Assert.That(area(clockwise), Is.EqualTo(300).Within(0.01));
            Assert.That(area(counter), Is.EqualTo(300).Within(0.01));
        }

        [Test]
        public void DegenerateInputsDrawNothing()
        {
            var output = new List<Vector2>();

            ShapeGeometry.Triangulate(new[] { new Vector2(0, 0), new Vector2(10, 10) }, output);
            ShapeGeometry.Triangulate(new[] { new Vector2(0, 0), new Vector2(10, 10), new Vector2(20, 20) }, output);
            Assert.That(output, Is.Empty);

            // Repeated corners and a closing point equal to the first are ignored.
            ShapeGeometry.Triangulate(new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 0) }, output);
            Assert.That(area(output), Is.EqualTo(50).Within(0.01));
        }
    }

    /// <summary>Shapes at runtime: sizes in panels, stroke placement and stretching of point shapes.</summary>
    [HeadlessTest]
    public partial class ShapesTests : TestScene
    {
        private static Vector2 posOf(Drawable d, Drawable relativeTo) => d.ToSpaceOfOtherDrawable(Vector2.Zero, relativeTo);

        /// <summary>Where a path's vertices land in <paramref name="relativeTo"/>'s space.</summary>
        private static IEnumerable<Vector2> vertices(SmoothPath path, Drawable relativeTo)
            => path.Vertices.Select(v => path.ToSpaceOfOtherDrawable(path.PositionInBoundingBox(v), relativeTo));

        [Test]
        public void RectangleStretchesInAGridCellAndKeepsAnExplicitSize()
        {
            Rectangle filling = null, sized = null;

            AddStep("create", () =>
            {
                var grid = new Grid { Size = new Vector2(200, 100), ColumnDefinitions = GridLengthCollection.Parse("*,*") };
                grid.Add(filling = new Rectangle { Fill = Colour4.Red });
                grid.Add(sized = new Rectangle { Width = 40, Height = 20, Fill = Colour4.Blue, Stroke = Colour4.White, StrokeThickness = 3, RadiusX = 5, RadiusY = 8 });
                Grid.SetColumn(sized, 1);
                Child = grid;
            });

            AddUntilStep("fills its cell", () => filling.DrawSize == new Vector2(100, 100));
            AddAssert("explicit size centred", () => sized.DrawSize == new Vector2(40, 20) && posOf(sized, sized.Parent.Parent) == new Vector2(130, 40));
            AddUntilStep("stroke and corners", () =>
            {
                var body = sized.ChildrenOfType<Container>().First();
                return body.BorderThickness == 3 && body.CornerRadius == 5 && body.DrawSize == new Vector2(40, 20);
            });
            AddAssert("no stroke without a stroke colour", () => filling.ChildrenOfType<Container>().First().BorderThickness == 0);
        }

        [Test]
        public void RectangleInAStackPanelNeedsAHeight()
        {
            Rectangle thin = null, bar = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 120 };
                panel.Add(thin = new Rectangle { Fill = Colour4.Red });
                panel.Add(bar = new Rectangle { Height = 6, Fill = Colour4.Gray });
                Child = panel;
            });

            AddUntilStep("bar stretches across", () => bar.DrawSize == new Vector2(120, 6));
            AddAssert("no height without one, like WPF", () => thin.DrawHeight == 0);
        }

        [Test]
        public void UniformRectangleIsACentredSquare()
        {
            Rectangle square = null;

            AddStep("create", () => Child = square = new Rectangle { Size = new Vector2(100, 40), Stretch = Stretch.Uniform, Fill = Colour4.Red });
            AddUntilStep("square in the middle", () =>
            {
                var body = square.ChildrenOfType<Container>().First();
                return body.DrawSize == new Vector2(40) && body.Position == new Vector2(30, 0);
            });
        }

        [Test]
        public void EllipseFillsItsBoundsInsideTheStroke()
        {
            Ellipse ellipse = null;

            AddStep("create", () => Child = ellipse = new Ellipse { Size = new Vector2(100, 50), Fill = Colour4.Pink, Stroke = Colour4.White, StrokeThickness = 4 });

            AddUntilStep("outline drawn", () => ellipse.ChildrenOfType<SmoothPath>().Single().Alpha == 1);
            AddAssert("outline stays inside the bounds", () =>
            {
                var path = ellipse.ChildrenOfType<SmoothPath>().Single();
                var points = vertices(path, ellipse).ToList();
                float left = points.Min(p => p.X), right = points.Max(p => p.X), top = points.Min(p => p.Y), bottom = points.Max(p => p.Y);

                // The stroke's centre line runs half the thickness in from the edge.
                return path.PathRadius == 2 && Precision.AlmostEquals(left, 2, 0.05f) && Precision.AlmostEquals(right, 98, 0.05f)
                       && Precision.AlmostEquals(top, 2, 0.05f) && Precision.AlmostEquals(bottom, 48, 0.05f);
            });
            AddAssert("fill is an evenly scaled circle", () =>
            {
                var circle = ellipse.ChildrenOfType<CircularContainer>().Single();
                return Precision.AlmostEquals(circle.DrawSize.X * circle.Scale.X, 96, 0.01f) && Precision.AlmostEquals(circle.DrawSize.Y * circle.Scale.Y, 46, 0.01f);
            });

            AddStep("remove the stroke", () => ellipse.Stroke = Colour4.Transparent);
            AddUntilStep("outline hidden", () => ellipse.ChildrenOfType<SmoothPath>().Single().Alpha == 0);
        }

        [Test]
        public void LineDrawsAtItsCoordinatesAndMeasuresToItsEnd()
        {
            Line line = null;
            StackPanel panel = null;

            AddStep("create", () =>
            {
                panel = new StackPanel { Width = 200 };
                panel.Add(line = new Line { X1 = 10, Y1 = 5, X2 = 110, Y2 = 45, Stroke = Colour4.White, StrokeThickness = 2 });
                Child = panel;
            });

            AddUntilStep("panel gives it room to the end point", () => panel.DrawHeight == 46 && line.DrawHeight == 46);
            AddAssert("path runs between the points", () =>
            {
                var points = vertices(line.ChildrenOfType<SmoothPath>().Single(), line).ToList();
                return points.Count == 2 && Precision.AlmostEquals(points[0], new Vector2(10, 5), 0.01f) && Precision.AlmostEquals(points[1], new Vector2(110, 45), 0.01f);
            });
            AddStep("move the end", () => line.X2 = 60);
            AddUntilStep("redrawn", () => Precision.AlmostEquals(vertices(line.ChildrenOfType<SmoothPath>().Single(), line).Last(), new Vector2(60, 45), 0.01f));
        }

        [Test]
        public void PolylineStretchesIntoItsBounds()
        {
            Polyline fill = null, uniform = null;

            AddStep("create", () => Children = new Drawable[]
            {
                fill = new Polyline { Size = new Vector2(102, 32), Points = PointCollection.Parse("0,0 10,10 20,0"), Stretch = Stretch.Fill, Stroke = Colour4.White, StrokeThickness = 2 },
                uniform = new Polyline { Y = 100, Size = new Vector2(102, 32), Points = PointCollection.Parse("0,0 10,10 20,0"), Stretch = Stretch.Uniform, Stroke = Colour4.White, StrokeThickness = 2 },
            });

            AddUntilStep("fill scales each axis to the bounds inside the stroke", () =>
            {
                var points = vertices(fill.ChildrenOfType<SmoothPath>().Single(), fill).ToList();
                return Precision.AlmostEquals(points[0], new Vector2(1, 1), 0.01f) && Precision.AlmostEquals(points[1], new Vector2(51, 31), 0.01f)
                       && Precision.AlmostEquals(points[2], new Vector2(101, 1), 0.01f);
            });
            AddAssert("uniform keeps the aspect ratio", () =>
            {
                var points = vertices(uniform.ChildrenOfType<SmoothPath>().Single(), uniform).ToList();
                return Precision.AlmostEquals(points[1], new Vector2(31, 31), 0.01f) && Precision.AlmostEquals(points[2], new Vector2(61, 1), 0.01f);
            });
            AddAssert("measure follows the stretch", () =>
                uniform.Measure(new Vector2(202, float.PositiveInfinity)) == new Vector2(202, 102) && fill.Measure(new Vector2(float.PositiveInfinity)) == new Vector2(22, 12));
        }

        [Test]
        public void PointCollectionChangesRedraw()
        {
            Polyline line = null;
            PointCollection points = null;

            AddStep("create", () => Child = line = new Polyline { Points = points = PointCollection.Parse("0,0 10,10"), Stroke = Colour4.White });
            AddUntilStep("two vertices", () => line.ChildrenOfType<SmoothPath>().Single().Vertices.Count == 2);
            AddAssert("sized to its points without a size", () => line.DrawSize == new Vector2(10.5f));

            AddStep("add a point", () => points.Add(new Vector2(30, 0)));
            AddUntilStep("three vertices", () => line.ChildrenOfType<SmoothPath>().Single().Vertices.Count == 3);

            AddStep("replace the collection", () => line.Points = PointCollection.Parse("0,0 5,5 10,0 15,5"));
            AddUntilStep("four vertices", () => line.ChildrenOfType<SmoothPath>().Single().Vertices.Count == 4);
            AddStep("old collection no longer watched", () => points.Add(new Vector2(1, 1)));
            AddWaitStep("wait", 2);
            AddAssert("still four", () => line.ChildrenOfType<SmoothPath>().Single().Vertices.Count == 4);
        }

        [Test]
        public void PolygonClosesItsStrokeAndFills()
        {
            Polygon polygon = null;

            AddStep("create", () => Child = polygon = new Polygon { Points = PointCollection.Parse("50,0 100,100 0,100"), Fill = Colour4.Pink, Stroke = Colour4.White });

            AddUntilStep("stroke returns to the start", () =>
            {
                var points = polygon.ChildrenOfType<SmoothPath>().Single().Vertices;
                return points.Count == 4 && points[0] == points[3];
            });
            AddAssert("fill drawable covers the triangle", () =>
            {
                var fill = polygon.ChildrenOfType<Drawable>().Single(d => d.GetType().Name == "PolygonFill");
                return fill.DrawSize == new Vector2(100) && fill.Colour.Equals((ColourInfo)Colour4.Pink);
            });

            AddStep("clear the fill colour", () => polygon.Fill = Colour4.Transparent);
            AddUntilStep("fill follows", () => polygon.ChildrenOfType<Drawable>().Single(d => d.GetType().Name == "PolygonFill").Colour.Equals((ColourInfo)Colour4.Transparent));
        }
    }
}
