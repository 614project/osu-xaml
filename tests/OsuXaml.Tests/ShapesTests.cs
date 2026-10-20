using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Transforms;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;
using Line = OsuXaml.Controls.Line;

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

        [Test]
        public void GradientBrushesAsPropertyElementsAndResources()
        {
            string source = ShapesTestData.Compiles(@"
  <StackPanel>
    <StackPanel.Resources>
      <LinearGradientBrush x:Key=""Sunset"" StartPoint=""0,0"" EndPoint=""1,0"" SpreadMethod=""Reflect"">
        <GradientStop Color=""#f38ba8"" Offset=""0"" />
        <GradientStop Color=""#fab387"" Offset=""1"" />
      </LinearGradientBrush>
      <SolidColorBrush x:Key=""Edge"" Color=""#cdd6f4"" Opacity=""0.5"" />
    </StackPanel.Resources>
    <Rectangle Width=""80"" Height=""40"">
      <Rectangle.Fill>
        <LinearGradientBrush StartPoint=""0,0"" EndPoint=""0,1"">
          <GradientStop Color=""#89b4fa"" Offset=""0"" />
          <GradientStop Color=""Transparent"" Offset=""1"" />
        </LinearGradientBrush>
      </Rectangle.Fill>
      <Rectangle.Stroke>
        <SolidColorBrush Color=""{Bind Accent}"" />
      </Rectangle.Stroke>
    </Rectangle>
    <Ellipse Width=""60"" Height=""60"" Fill=""{StaticResource Sunset}"" Stroke=""{StaticResource Edge}"">
      <Ellipse.Stroke>
        <RadialGradientBrush GradientOrigin=""0.3,0.3"" MappingMode=""RelativeToBoundingBox"">
          <RadialGradientBrush.GradientStops>
            <GradientStop Color=""White"" Offset=""0"" />
            <GradientStop Color=""#313244"" Offset=""1"" />
          </RadialGradientBrush.GradientStops>
        </RadialGradientBrush>
      </Ellipse.Stroke>
    </Ellipse>
    <Rectangle Height=""10"" Fill=""{Bind Accent, Animate=200}"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.LinearGradientBrush()"));
            Assert.That(source, Does.Contain("StartPoint = new global::osuTK.Vector2(0f, 1f)").Or.Contain("EndPoint = new global::osuTK.Vector2(0f, 1f)"));
            Assert.That(source, Does.Contain("SpreadMethod = global::OsuXaml.Controls.GradientSpreadMethod.Reflect"));
            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.GradientStop()"));
            Assert.That(source, Does.Contain(".GradientStops.Add("), "stops in a GradientStops element");
            Assert.That(source, Does.Match(@"e\d+\.Add\(e\d+\);"), "stops written straight inside the brush");
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.Brush value ="), "an animated colour transforms the Brush property");
        }

        [Test]
        public void PenSettingsAndFillRules()
        {
            string source = ShapesTestData.Compiles(@"
  <StackPanel>
    <Polyline Points=""0,20 20,0 40,20"" Stroke=""White"" StrokeThickness=""6"" StrokeLineJoin=""Round"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Triangle"" />
    <Line X2=""100"" Stroke=""White"" StrokeThickness=""2"" StrokeDashArray=""4 2"" StrokeDashOffset=""1"" StrokeDashCap=""Round"" StrokeMiterLimit=""4"" />
    <Polygon Points=""50,0 21,90 98,35 2,35 79,90"" Fill=""#f9e2af"" FillRule=""Nonzero"" />
    <Path Data=""F1 M 0,0 L 10,0 10,10 0,10 Z"" Fill=""White"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("StrokeLineJoin = global::OsuXaml.Controls.PenLineJoin.Round"));
            Assert.That(source, Does.Contain("StrokeEndLineCap = global::OsuXaml.Controls.PenLineCap.Triangle"));
            Assert.That(source, Does.Contain("StrokeDashArray = global::OsuXaml.Controls.DoubleCollection.Parse(\"4 2\")"));
            Assert.That(source, Does.Contain("FillRule = global::OsuXaml.Controls.FillRule.Nonzero"));
        }
    }

    /// <summary>Brushes on their own: colours along gradients, conversions and change notifications.</summary>
    [TestFixture]
    public class BrushTests
    {
        private static LinearGradientBrush blackToWhite(GradientSpreadMethod spread = GradientSpreadMethod.Pad)
            => new LinearGradientBrush(Colour4.Black, Colour4.White, 0) { SpreadMethod = spread };

        [Test]
        public void SpreadMethodsContinueTheGradient()
        {
            Assert.That(blackToWhite().ColourAt(0.25).R, Is.EqualTo(0.25f).Within(1e-4));
            Assert.That(blackToWhite().ColourAt(-1).R, Is.EqualTo(0));
            Assert.That(blackToWhite().ColourAt(1.5).R, Is.EqualTo(1));
            Assert.That(blackToWhite(GradientSpreadMethod.Repeat).ColourAt(1.25).R, Is.EqualTo(0.25f).Within(1e-4));
            Assert.That(blackToWhite(GradientSpreadMethod.Reflect).ColourAt(1.25).R, Is.EqualTo(0.75f).Within(1e-4));
            Assert.That(blackToWhite(GradientSpreadMethod.Reflect).ColourAt(-0.25).R, Is.EqualTo(0.25f).Within(1e-4));
        }

        [Test]
        public void StopsSortByOffsetAndMixWithPremultipliedAlpha()
        {
            var brush = new LinearGradientBrush();
            brush.Add(new GradientStop(Colour4.Red, 1));
            brush.Add(new GradientStop(Colour4.Blue, 0));
            brush.Add(new GradientStop(new Colour4(0, 1, 0, 0), 2));

            Assert.That(brush.ColourAt(0), Is.EqualTo(Colour4.Blue));
            Assert.That(brush.ColourAt(0.5).R, Is.EqualTo(0.5f).Within(1e-4));

            // Halfway to a transparent green the colour is still red, only fainter.
            var fading = brush.ColourAt(1.5);
            Assert.That(fading.A, Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(fading.R, Is.EqualTo(1).Within(1e-4));
            Assert.That(fading.G, Is.EqualTo(0).Within(1e-4));

            Assert.That(new RadialGradientBrush().ColourAt(0.5), Is.EqualTo(Colour4.Transparent), "no stops paint nothing");
        }

        [Test]
        public void AngleConstructorFollowsWpf()
        {
            var down = new LinearGradientBrush(Colour4.Red, Colour4.Blue, 90);

            Assert.That(down.StartPoint, Is.EqualTo(Vector2.Zero));
            Assert.That(Precision.AlmostEquals(down.EndPoint, new Vector2(0, 1), 1e-5f));
            Assert.That(down.GradientStops.Select(s => s.Offset), Is.EqualTo(new[] { 0f, 1f }));
        }

        [Test]
        public void ColoursConvertAndChangesAreAnnounced()
        {
            Brush brush = Colour4.Pink;
            Assert.That(((SolidColorBrush)brush).Color, Is.EqualTo(Colour4.Pink));
            Assert.That(((Brush)Colour4.Transparent).IsVisible, Is.False);

            var gradient = blackToWhite();
            int changes = 0;
            gradient.Changed += () => changes++;

            gradient.GradientStops[0].Color = Colour4.Red;
            gradient.Add(new GradientStop(Colour4.Green, 0.5f));
            gradient.GradientStops.RemoveAt(0);
            gradient.EndPoint = Vector2.One;
            gradient.Opacity = 0.5f;
            Assert.That(changes, Is.EqualTo(5));

            var oldStops = gradient.GradientStops;
            gradient.GradientStops = new GradientStopCollection();
            oldStops.Add(new GradientStop());
            Assert.That(changes, Is.EqualTo(6), "the replaced collection is no longer watched");
        }

        [Test]
        public void SolidBrushesAnimateBetweenColours()
        {
            Brush from = Colour4.Black, to = Colour4.White;
            var middle = (SolidColorBrush)from.ValueAt(50, from, to, 0, 100, new DefaultEasingFunction(Easing.None));

            Assert.That(middle.Color.R, Is.GreaterThan(0.1f).And.LessThan(0.9f));
            Assert.That(blackToWhite().ValueAt(75, blackToWhite(), to, 0, 100, new DefaultEasingFunction(Easing.None)), Is.SameAs(to));
        }

        [Test]
        public void DashArraysParse()
        {
            Assert.That(DoubleCollection.Parse("4 2,1"), Is.EqualTo(new[] { 4.0, 2, 1 }));
            Assert.That(DoubleCollection.Parse("4 2.5").ToString(), Is.EqualTo("4 2.5"));
            Assert.Throws<System.FormatException>(() => DoubleCollection.Parse("4 x"));
        }
    }

    /// <summary>Filling by fill rule, stroking with caps, joins and dashes, without a game.</summary>
    [TestFixture]
    public class ShapeGeometryTests
    {
        private static Vector2[] points(string text) => PointCollection.Parse(text).ToArray();

        private static ShapeMesh stroke(string figure, bool closed, StrokeStyle style)
            => ShapeGeometry.Stroke(new[] { ((IReadOnlyList<Vector2>)points(figure), closed) }, style);

        /// <summary>Every outline edge has the painted area just inside it and nothing just outside.</summary>
        private static void assertOutlineFacesOut(ShapeMesh mesh)
        {
            Assert.That(mesh.Boundary, Is.Not.Empty);

            foreach (var edge in mesh.Boundary)
            {
                var middle = (edge.Start + edge.End) / 2;
                Assert.That(mesh.Contains(middle - edge.Outward * 0.05f), Is.True, $"inside {edge.Start} -> {edge.End}");
                Assert.That(mesh.Contains(middle + edge.Outward * 0.05f), Is.False, $"outside {edge.Start} -> {edge.End}");
            }
        }

        private static float perimeter(ShapeMesh mesh) => mesh.Boundary.Sum(e => (e.End - e.Start).Length);

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

        [Test]
        public void ConcavePolygonFillsItsArea()
        {
            // An L shape: a 20x20 square with the top-right 10x10 quarter missing, in both windings.
            var shape = points("0,0 10,0 10,10 20,10 20,20 0,20");

            foreach (var outline in new[] { shape, shape.Reverse().ToArray() })
            {
                var mesh = ShapeGeometry.Fill(new[] { outline }, FillRule.EvenOdd);

                Assert.That(mesh.Area, Is.EqualTo(300).Within(0.01));
                Assert.That(perimeter(mesh), Is.EqualTo(80).Within(0.01));
                Assert.That(mesh.Quads.Count, Is.EqualTo(8), "a quad per straight run, not per slab");
                assertOutlineFacesOut(mesh);
            }
        }

        [Test]
        public void DegenerateInputsFillNothing()
        {
            Assert.That(ShapeGeometry.Fill(new[] { points("0,0 10,10") }, FillRule.Nonzero).IsEmpty);
            Assert.That(ShapeGeometry.Fill(new[] { points("0,0 10,10 20,20") }, FillRule.Nonzero).IsEmpty);

            // Repeated corners and a closing point equal to the first are ignored.
            var triangle = ShapeGeometry.Fill(new[] { points("0,0 0,0 10,0 10,10 0,0") }, FillRule.Nonzero);
            Assert.That(triangle.Area, Is.EqualTo(50).Within(0.01));
        }

        [Test]
        public void FillRulesDecideWhatOverlapsFill()
        {
            // A five-pointed star drawn in one stroke crosses itself around a pentagon in the middle.
            var star = points("50,0 21,90 98,35 2,35 79,90");
            var evenOdd = ShapeGeometry.Fill(new[] { star }, FillRule.EvenOdd);
            var nonzero = ShapeGeometry.Fill(new[] { star }, FillRule.Nonzero);

            Assert.That(evenOdd.Contains(new Vector2(50, 50)), Is.False, "EvenOdd leaves the middle empty");
            Assert.That(nonzero.Contains(new Vector2(50, 50)), Is.True, "Nonzero fills it");
            Assert.That(evenOdd.Contains(new Vector2(50, 10)) && nonzero.Contains(new Vector2(50, 10)), "both fill the points");
            Assert.That(nonzero.Area - evenOdd.Area, Is.GreaterThan(500));
            assertOutlineFacesOut(evenOdd);
            assertOutlineFacesOut(nonzero);

            // Two squares wound the same way: EvenOdd cuts a hole, Nonzero doesn't; wound against each other, both cut it.
            var outer = points("0,0 30,0 30,30 0,30");
            var inner = points("10,10 20,10 20,20 10,20");
            var innerReversed = inner.Reverse().ToArray();

            Assert.That(ShapeGeometry.Fill(new[] { outer, inner }, FillRule.EvenOdd).Area, Is.EqualTo(800).Within(0.01));
            Assert.That(ShapeGeometry.Fill(new[] { outer, inner }, FillRule.Nonzero).Area, Is.EqualTo(900).Within(0.01));
            Assert.That(ShapeGeometry.Fill(new[] { outer, innerReversed }, FillRule.Nonzero).Area, Is.EqualTo(800).Within(0.01));
            Assert.That(perimeter(ShapeGeometry.Fill(new[] { outer, inner }, FillRule.EvenOdd)), Is.EqualTo(160).Within(0.01), "the hole has an edge");
        }

        [Test]
        public void OverlapsCountOnceAndTouchingPiecesLeaveNoSeam()
        {
            var a = points("0,0 10,0 10,10 0,10");
            var b = points("5,5 15,5 15,15 5,15");
            var beside = points("10,0 20,0 20,10 10,10");

            Assert.That(ShapeGeometry.Fill(new[] { a, b }, FillRule.Nonzero).Area, Is.EqualTo(175).Within(0.01));

            var joined = ShapeGeometry.Fill(new[] { a, beside }, FillRule.Nonzero);
            Assert.That(joined.Area, Is.EqualTo(200).Within(0.01));
            Assert.That(perimeter(joined), Is.EqualTo(60).Within(0.01), "no edge where the squares touch");
        }

        [Test]
        public void CapsExtendTheEnds()
        {
            StrokeStyle capped(PenLineCap cap) => new StrokeStyle { Thickness = 2, StartLineCap = cap, EndLineCap = cap };

            var flat = stroke("0,0 10,0", false, capped(PenLineCap.Flat));
            var square = stroke("0,0 10,0", false, capped(PenLineCap.Square));
            var round = stroke("0,0 10,0", false, capped(PenLineCap.Round));
            var triangle = stroke("0,0 10,0", false, capped(PenLineCap.Triangle));

            Assert.That(flat.Area, Is.EqualTo(20).Within(0.01));
            Assert.That(flat.Bounds, Is.EqualTo(new RectangleF(0, -1, 10, 2)));
            Assert.That(square.Area, Is.EqualTo(24).Within(0.01));
            Assert.That(square.Bounds, Is.EqualTo(new RectangleF(-1, -1, 12, 2)));
            Assert.That(triangle.Area, Is.EqualTo(22).Within(0.01));
            Assert.That(round.Area, Is.EqualTo(20 + System.MathF.PI).Within(0.25), "a half circle at each end, in steps too small to see");
            assertOutlineFacesOut(round);

            var mixed = stroke("0,0 10,0", false, new StrokeStyle { Thickness = 2, StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Flat });
            Assert.That(mixed.Bounds, Is.EqualTo(new RectangleF(-1, -1, 11, 2)));
        }

        [Test]
        public void JoinsShapeTheOutsideOfCorners()
        {
            StrokeStyle joined(PenLineJoin join) => new StrokeStyle { Thickness = 2, LineJoin = join };

            var miter = stroke("0,0 10,0 10,10", false, joined(PenLineJoin.Miter));
            var bevel = stroke("0,0 10,0 10,10", false, joined(PenLineJoin.Bevel));
            var round = stroke("0,0 10,0 10,10", false, joined(PenLineJoin.Round));

            // Two 10x2 bands overlapping by 1x1 inside the corner, plus the 1x1 square the miter adds outside it.
            Assert.That(miter.Area, Is.EqualTo(40).Within(0.01));
            Assert.That(miter.Contains(new Vector2(10.9f, -0.9f)), Is.True);
            Assert.That(bevel.Area, Is.EqualTo(39.5).Within(0.01));
            Assert.That(bevel.Contains(new Vector2(10.9f, -0.9f)), Is.False);
            Assert.That(bevel.Contains(new Vector2(10.4f, -0.4f)), Is.True);
            Assert.That(round.Contains(new Vector2(10.6f, -0.6f)), Is.True);
            Assert.That(round.Contains(new Vector2(10.9f, -0.9f)), Is.False);
            assertOutlineFacesOut(round);
        }

        [Test]
        public void MiterLimitCutsSharpCorners()
        {
            // A sharp V whose miter would reach about 10 half-widths past the corner.
            var sharp = stroke("0,0 20,2 0,4", false, new StrokeStyle { Thickness = 2, MiterLimit = 11 });
            var cut = stroke("0,0 20,2 0,4", false, new StrokeStyle { Thickness = 2, MiterLimit = 4 });
            var tight = stroke("0,0 20,2 0,4", false, new StrokeStyle { Thickness = 2, MiterLimit = 1 });

            Assert.That(sharp.Bounds.Right, Is.GreaterThan(29));
            Assert.That(cut.Bounds.Right, Is.EqualTo(24).Within(0.05), "cut square to the corner, at the limit's distance");
            Assert.That(tight.Bounds.Right, Is.LessThan(21.1f));
        }

        [Test]
        public void ClosedOutlinesJoinAllRound()
        {
            var square = stroke("1,1 11,1 11,11 1,11", true, new StrokeStyle { Thickness = 2 });

            Assert.That(square.Area, Is.EqualTo(12 * 12 - 8 * 8).Within(0.01));
            Assert.That(square.Bounds, Is.EqualTo(new RectangleF(0, 0, 12, 12)));
            Assert.That(perimeter(square), Is.EqualTo(48 + 32).Within(0.01));
            assertOutlineFacesOut(square);

            var bevelled = stroke("1,1 11,1 11,11 1,11", true, new StrokeStyle { Thickness = 2, LineJoin = PenLineJoin.Bevel });
            Assert.That(bevelled.Area, Is.EqualTo(80 - 4 * 0.5).Within(0.01));
        }

        [Test]
        public void DashesFollowThePattern()
        {
            var dashed = stroke("0,0 10,0", false, new StrokeStyle { Thickness = 1, DashArray = new[] { 2.0, 2 } });

            // Dashes at 0-2, 4-6 and 8-10.
            Assert.That(dashed.Area, Is.EqualTo(6).Within(0.01));
            Assert.That(dashed.Contains(new Vector2(1, 0)) && dashed.Contains(new Vector2(5, 0)) && dashed.Contains(new Vector2(9, 0)));
            Assert.That(dashed.Contains(new Vector2(3, 0)) || dashed.Contains(new Vector2(7, 0)), Is.False);

            var shifted = stroke("0,0 10,0", false, new StrokeStyle { Thickness = 1, DashArray = new[] { 2.0, 2 }, DashOffset = 1 });
            Assert.That(shifted.Contains(new Vector2(0.5f, 0)) && shifted.Contains(new Vector2(4, 0)) && !shifted.Contains(new Vector2(1.5f, 0)));

            // Lengths are in stroke widths, and an odd count repeats ("1" is "1 1").
            var thick = stroke("0,0 12,0", false, new StrokeStyle { Thickness = 2, DashArray = new[] { 1.0 } });
            Assert.That(thick.Area, Is.EqualTo(12).Within(0.01));
            Assert.That(thick.Contains(new Vector2(1, 0)) && !thick.Contains(new Vector2(3, 0)) && thick.Contains(new Vector2(5, 0)));
        }

        [Test]
        public void DashesTurnCornersAndDotsUseTheDashCap()
        {
            // A 6-long dash starting 2 before the corner goes round it with a join.
            var corner = stroke("0,0 10,0 10,10", false, new StrokeStyle { Thickness = 2, DashArray = new[] { 3.0, 2 }, DashOffset = 1 });
            Assert.That(corner.Contains(new Vector2(10.9f, -0.9f)), Is.True, "the miter of a dash crossing the corner");

            // Zero-length dashes with round caps are dots, every two widths.
            var dots = stroke("0,0 20,0", false, new StrokeStyle { Thickness = 2, DashArray = new[] { 0.0, 2 }, DashCap = PenLineCap.Round });
            Assert.That(dots.Contains(new Vector2(4, 0.9f)) && dots.Contains(new Vector2(8.9f, 0)) && dots.Contains(new Vector2(16, -0.9f)));
            Assert.That(dots.Contains(new Vector2(6, 0)), Is.False);
            Assert.That(stroke("0,0 20,0", false, new StrokeStyle { Thickness = 2, DashArray = new[] { 0.0, 2 } }).IsEmpty, "flat dots have no area");
        }

        [Test]
        public void StrokesCrossingThemselvesPaintOnce()
        {
            var cross = stroke("0,0 10,10 10,0 0,10", false, new StrokeStyle { Thickness = 2 });

            // No two quads overlap, so the area is that of the union.
            float sum = cross.Area;
            var quads = cross.Quads;
            int samples = 0, covered = 0;

            for (float x = -1; x <= 11; x += 0.25f)
            {
                for (float y = -1; y <= 11; y += 0.25f)
                {
                    int hits = 0;

                    for (int i = 0; i < quads.Count; i += 4)
                    {
                        var single = new ShapeMesh();
                        single.Quads.AddRange(quads.Skip(i).Take(4));
                        if (single.Contains(new Vector2(x + 0.01f, y + 0.013f)))
                            hits++;
                    }

                    Assert.That(hits, Is.LessThanOrEqualTo(1), $"({x}, {y}) painted {hits} times");
                    samples++;
                    if (hits > 0) covered++;
                }
            }

            Assert.That(sum, Is.EqualTo(144f * covered / samples).Within(8));
        }

        [Test]
        public void LongChartLinesStayQuickAndClean()
        {
            // A line chart: thousands of points across and not much height, so slabs run upright and each meets only a few edges.
            var random = new System.Random(1);
            var chart = Enumerable.Range(0, 2000).Select(i => new Vector2(i * 0.4f, 100 + (float)random.NextDouble() * 50)).ToList();
            var style = new StrokeStyle { Thickness = 2, LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var mesh = ShapeGeometry.Stroke(new[] { ((IReadOnlyList<Vector2>)chart, false) }, style);
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000), "every slab crossing the whole line took minutes");
            Assert.That(mesh.Bounds.Left, Is.EqualTo(-1).Within(0.01));
            Assert.That(mesh.Bounds.Right, Is.EqualTo(800.6f).Within(0.01));

            // Swept across, the outline still comes out whole.
            var wave = Enumerable.Range(0, 200).Select(i => new Vector2(i * 2, 100 + 30 * System.MathF.Sin(i * 0.15f))).ToList();
            var smooth = ShapeGeometry.Stroke(new[] { ((IReadOnlyList<Vector2>)wave, false) }, style);
            assertOutlineFacesOut(smooth);
            Assert.That(wave.All(p => smooth.Contains(p + new Vector2(0.01f, 0.01f))), Is.True);
            Assert.That(smooth.Contains(new Vector2(200, 100)), Is.False);
        }

        [Test]
        public void RoundedRectanglesUseBothRadii()
        {
            var plain = ShapeGeometry.Rectangle(new RectangleF(0, 0, 40, 20), 0, 8);
            Assert.That(plain.Count, Is.EqualTo(4), "one radius alone leaves square corners");

            var rounded = ShapeGeometry.Fill(new[] { ShapeGeometry.Rectangle(new RectangleF(0, 0, 40, 20), 10, 4) }, FillRule.Nonzero);
            float corners = 4 * 10 * 4 * (1 - System.MathF.PI / 4);
            Assert.That(rounded.Area, Is.EqualTo(800 - corners).Within(1));
            Assert.That(rounded.Contains(new Vector2(0.5f, 0.5f)), Is.False);
            Assert.That(rounded.Contains(new Vector2(5, 3)), Is.True);

            var capped = ShapeGeometry.Fill(new[] { ShapeGeometry.Rectangle(new RectangleF(0, 0, 40, 20), 100, 100) }, FillRule.Nonzero);
            Assert.That(capped.Area, Is.EqualTo(System.MathF.PI * 20 * 10).Within(4), "radii are capped at half the sides");
        }
    }

    /// <summary>Shapes at runtime: sizes in panels, stroke placement, stretching of point shapes and brushes.</summary>
    [HeadlessTest]
    public partial class ShapesTests : TestScene
    {
        private static Vector2 posOf(Drawable d, Drawable relativeTo) => d.ToSpaceOfOtherDrawable(Vector2.Zero, relativeTo);

        private static bool near(RectangleF a, RectangleF b, float within = 0.05f)
            => Precision.AlmostEquals(a.TopLeft, b.TopLeft, within) && Precision.AlmostEquals(a.BottomRight, b.BottomRight, within);

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

            AddUntilStep("fills its cell", () => filling.DrawSize == new Vector2(100, 100) && near(filling.FillMesh.Bounds, new RectangleF(0, 0, 100, 100)));
            AddAssert("explicit size centred", () => sized.DrawSize == new Vector2(40, 20) && posOf(sized, sized.Parent.Parent) == new Vector2(130, 40));
            AddUntilStep("stroke inside the bounds, fill to its middle", () =>
                near(sized.StrokeMesh.Bounds, new RectangleF(0, 0, 40, 20)) && near(sized.FillMesh.Bounds, new RectangleF(1.5f, 1.5f, 37, 17)));
            AddAssert("stroke is a ring", () => sized.StrokeMesh.Contains(new Vector2(1, 10)) && !sized.StrokeMesh.Contains(new Vector2(20, 10)));
            AddAssert("corners are rounded", () => !sized.StrokeMesh.Contains(new Vector2(0.5f, 0.5f)) && !sized.FillMesh.Contains(new Vector2(2, 2)));
            AddAssert("no stroke without a stroke brush", () => filling.StrokeMesh.IsEmpty);
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
            AddAssert("no height without one, like WPF", () => thin.DrawHeight == 0 && thin.FillMesh.IsEmpty);
        }

        [Test]
        public void UniformRectangleIsACentredSquare()
        {
            Rectangle square = null;

            AddStep("create", () => Child = square = new Rectangle { Size = new Vector2(100, 40), Stretch = Stretch.Uniform, Fill = Colour4.Red });
            AddUntilStep("square in the middle", () => near(square.FillMesh.Bounds, new RectangleF(30, 0, 40, 40)));
        }

        [Test]
        public void StrokeWiderThanTheShapeCoversIt()
        {
            Rectangle rectangle = null;

            AddStep("create", () => Child = rectangle = new Rectangle { Size = new Vector2(10, 30), Fill = Colour4.Red, Stroke = Colour4.White, StrokeThickness = 12 });
            AddUntilStep("all stroke", () => Precision.AlmostEquals(rectangle.StrokeMesh.Area, 300, 0.1f) && rectangle.FillMesh.IsEmpty);
        }

        [Test]
        public void EllipseFillsItsBoundsInsideTheStroke()
        {
            Ellipse ellipse = null;

            AddStep("create", () => Child = ellipse = new Ellipse { Size = new Vector2(100, 50), Fill = Colour4.Pink, Stroke = Colour4.White, StrokeThickness = 4 });

            AddUntilStep("outline stays inside the bounds", () => near(ellipse.StrokeMesh.Bounds, new RectangleF(0, 0, 100, 50), 0.1f));
            AddAssert("fill reaches the middle of the stroke", () => near(ellipse.FillMesh.Bounds, new RectangleF(2, 2, 96, 46), 0.1f));
            AddAssert("a ring, not a disc", () => ellipse.StrokeMesh.Contains(new Vector2(1, 25)) && !ellipse.StrokeMesh.Contains(new Vector2(50, 25)));
            AddAssert("corners outside", () => !ellipse.FillMesh.Contains(new Vector2(5, 5)));

            AddStep("remove the stroke", () => ellipse.Stroke = Colour4.Transparent);
            AddUntilStep("outline gone, fill to the edge", () => ellipse.StrokeMesh.IsEmpty && near(ellipse.FillMesh.Bounds, new RectangleF(0, 0, 100, 50), 0.1f));
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
            AddAssert("stroke runs between the points", () =>
                line.StrokeMesh.Contains(new Vector2(60, 25)) && !line.StrokeMesh.Contains(new Vector2(60, 30)) && !line.StrokeMesh.Contains(new Vector2(9, 4.5f)));
            AddAssert("flat ends by default, as in WPF", () => !line.StrokeMesh.Contains(new Vector2(110.5f, 45.2f)));
            AddStep("move the end", () => line.X2 = 60);
            AddUntilStep("redrawn", () => line.StrokeMesh.Contains(new Vector2(35, 25)) && !line.StrokeMesh.Contains(new Vector2(60, 25)));
            AddStep("round the ends", () => line.StrokeStartLineCap = line.StrokeEndLineCap = PenLineCap.Round);
            AddUntilStep("ends stick out", () => line.StrokeMesh.Contains(new Vector2(60.5f, 45.2f)));
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
                near(fill.StrokeBrushBounds, new RectangleF(1, 1, 100, 30)) && fill.StrokeMesh.Contains(new Vector2(51, 30.5f)) && fill.StrokeMesh.Contains(new Vector2(100.5f, 1.5f)));
            AddAssert("uniform keeps the aspect ratio", () =>
                near(uniform.StrokeBrushBounds, new RectangleF(1, 1, 60, 30)) && uniform.StrokeMesh.Contains(new Vector2(31, 30.5f)) && !uniform.StrokeMesh.Contains(new Vector2(90, 1.5f)));
            AddAssert("measure follows the stretch", () =>
                uniform.Measure(new Vector2(202, float.PositiveInfinity)) == new Vector2(202, 102) && fill.Measure(new Vector2(float.PositiveInfinity)) == new Vector2(22, 12));
            AddAssert("nothing filled without a fill", () => fill.FillMesh.IsEmpty);
            AddStep("fill it", () => fill.Fill = Colour4.Pink);
            AddUntilStep("filled as if closed, like WPF", () => fill.FillMesh.Contains(new Vector2(51, 10)));
        }

        [Test]
        public void PointCollectionChangesRedraw()
        {
            Polyline line = null;
            PointCollection points = null;
            ShapeMesh replaced = null;

            AddStep("create", () => Child = line = new Polyline { Points = points = PointCollection.Parse("0,0 10,10"), Stroke = Colour4.White });
            AddUntilStep("drawn", () => line.StrokeMesh.Contains(new Vector2(5, 5)));
            AddAssert("sized to its points without a size", () => line.DrawSize == new Vector2(10.5f));

            AddStep("add a point", () => points.Add(new Vector2(30, 0)));
            AddUntilStep("third point drawn", () => line.StrokeMesh.Contains(new Vector2(20, 5)));

            AddStep("replace the collection", () => line.Points = PointCollection.Parse("0,0 5,5 10,0 15,5"));
            AddUntilStep("new points drawn", () => line.StrokeMesh.Contains(new Vector2(12.5f, 2.5f)) && !line.StrokeMesh.Contains(new Vector2(20, 5)));
            AddStep("old collection no longer watched", () =>
            {
                replaced = line.StrokeMesh;
                points.Add(new Vector2(1, 1));
            });
            AddWaitStep("wait", 2);
            AddAssert("not rebuilt", () => ReferenceEquals(line.StrokeMesh, replaced));
        }

        [Test]
        public void PolygonClosesItsStrokeAndFills()
        {
            Polygon polygon = null;

            AddStep("create", () => Child = polygon = new Polygon { Points = PointCollection.Parse("50,0 100,100 0,100"), Fill = Colour4.Pink, Stroke = Colour4.White });

            AddUntilStep("stroke returns to the start", () => polygon.StrokeMesh.Contains(new Vector2(25, 50)));
            AddAssert("fill covers the triangle", () => Precision.AlmostEquals(polygon.FillMesh.Area, 5000, 0.5f));

            AddStep("change the fill colour", () => polygon.Fill = Colour4.Green);
            AddUntilStep("fill layer follows", () => polygon.ChildrenOfType<ShapeLayer>().First().Brush is SolidColorBrush { Color: var c } && c == Colour4.Green);
            AddStep("clear the fill colour", () => polygon.Fill = Colour4.Transparent);
            AddUntilStep("nothing left to fill", () => polygon.FillMesh.IsEmpty);
            AddStep("fill again", () => polygon.Fill = Colour4.Pink);
            AddUntilStep("fill comes back", () => !polygon.FillMesh.IsEmpty);
        }

        [Test]
        public void FillRuleOnPolygons()
        {
            Polygon polygon = null;

            AddStep("create", () => Child = polygon = new Polygon { Points = PointCollection.Parse("50,0 21,90 98,35 2,35 79,90"), Fill = Colour4.Gold });
            AddUntilStep("EvenOdd by default leaves the middle empty", () => !polygon.FillMesh.IsEmpty && !polygon.FillMesh.Contains(new Vector2(50, 50)));
            AddStep("switch to Nonzero", () => polygon.FillRule = FillRule.Nonzero);
            AddUntilStep("middle filled", () => polygon.FillMesh.Contains(new Vector2(50, 50)));
        }

        [Test]
        public void GradientsMapToTheOutlineBounds()
        {
            Rectangle rectangle = null;
            LinearGradientBrush brush = null;

            AddStep("create", () => Child = rectangle = new Rectangle
            {
                Position = new Vector2(20),
                Size = new Vector2(100, 50),
                Fill = brush = new LinearGradientBrush { StartPoint = Vector2.Zero, EndPoint = new Vector2(1, 0) },
            });
            AddStep("add stops", () =>
            {
                brush.Add(new GradientStop(Colour4.Red, 0));
                brush.Add(new GradientStop(Colour4.Blue, 1));
            });

            AddUntilStep("texture runs across the width", () =>
            {
                var layer = rectangle.ChildrenOfType<ShapeLayer>().First();
                return layer.BrushTexture != null && layer.BrushTexture != layer.WhitePixel
                                                  && Precision.AlmostEquals(layer.TextureCoordinateAt(new Vector2(0, 25)).X, 0.5f / 256, 1e-4f)
                                                  && Precision.AlmostEquals(layer.TextureCoordinateAt(new Vector2(50, 0)).X, 0.5f, 1e-4f)
                                                  && Precision.AlmostEquals(layer.TextureCoordinateAt(new Vector2(100, 40)).X, 1 - 0.5f / 256, 1e-4f);
            });

            AddStep("absolute mapping", () =>
            {
                brush.MappingMode = BrushMappingMode.Absolute;
                brush.EndPoint = new Vector2(200, 0);
            });
            AddUntilStep("follows the shape's coordinates", () =>
                Precision.AlmostEquals(rectangle.ChildrenOfType<ShapeLayer>().First().TextureCoordinateAt(new Vector2(100, 0)).X, 0.5f, 1e-4f));
        }

        [Test]
        public void ShapesShareGradientTextures()
        {
            Ellipse first = null, second = null;

            RadialGradientBrush glow() => new RadialGradientBrush(Colour4.White, Colour4.Transparent) { GradientOrigin = new Vector2(0.3f) };

            AddStep("create", () => Children = new Drawable[]
            {
                first = new Ellipse { Size = new Vector2(40), Fill = glow() },
                second = new Ellipse { X = 50, Size = new Vector2(80, 40), Fill = glow() },
            });

            AddUntilStep("one texture for both", () =>
            {
                var a = first.ChildrenOfType<ShapeLayer>().First().BrushTexture;
                var b = second.ChildrenOfType<ShapeLayer>().First().BrushTexture;
                return a != null && ReferenceEquals(a, b);
            });

            AddStep("change one gradient", () => ((GradientBrush)second.Fill).GradientStops[1].Color = Colour4.Black);
            AddUntilStep("it gets its own", () =>
                !ReferenceEquals(first.ChildrenOfType<ShapeLayer>().First().BrushTexture, second.ChildrenOfType<ShapeLayer>().First().BrushTexture));
        }

        [Test]
        public void LevelLineGradientUsesTheStrokeWidth()
        {
            Line line = null;

            AddStep("create", () => Child = line = new Line
            {
                X2 = 100, Y1 = 10, Y2 = 10, StrokeThickness = 4,
                Stroke = new LinearGradientBrush(Colour4.White, Colour4.Black, 90),
            });
            AddUntilStep("brush box is as tall as the stroke", () => near(line.StrokeBrushBounds, new RectangleF(0, 8, 100, 4)));
        }
    }
}
