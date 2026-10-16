using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osuTK;
using Triangle = osu.Framework.Graphics.Primitives.Triangle;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Base for WPF-style shapes: <see cref="Fill"/>, <see cref="Stroke"/>, <see cref="StrokeThickness"/> and
    /// <see cref="Stretch"/>. Like WPF, the stroke lies inside the shape's bounds, and both brushes are transparent
    /// until set. A shape without a size stretches in its panel cell like any other element.
    /// </summary>
    public abstract partial class Shape : CompositeDrawable, ILayoutElement
    {
        private Colour4 fill = Colour4.Transparent;
        private Colour4 stroke = Colour4.Transparent;
        private float strokeThickness = 1;
        private Stretch stretch;

        private bool geometryValid;
        private Vector2 geometrySize = new Vector2(-1);

        private bool sizeToContentX;
        private bool sizeToContentY;

        protected Shape(Stretch defaultStretch)
        {
            stretch = defaultStretch;
        }

        /// <summary>The colour inside the shape (<c>Fill="#89b4fa"</c> or a colour name).</summary>
        public Colour4 Fill
        {
            get => fill;
            set
            {
                bool wasVisible = fill.A > 0;
                fill = value;
                brushChanged(wasVisible != value.A > 0);
            }
        }

        /// <summary>The colour of the outline.</summary>
        public Colour4 Stroke
        {
            get => stroke;
            set
            {
                bool wasVisible = stroke.A > 0;
                stroke = value;
                brushChanged(wasVisible != value.A > 0);
            }
        }

        /// <summary>The width of the outline (1 by default), drawn only while <see cref="Stroke"/> is set.</summary>
        public float StrokeThickness
        {
            get => strokeThickness;
            set
            {
                strokeThickness = value;
                InvalidateGeometry();
            }
        }

        /// <summary>How the shape fills its bounds. Rectangle and Ellipse default to Fill, the point shapes to None.</summary>
        public Stretch Stretch
        {
            get => stretch;
            set
            {
                stretch = value;
                InvalidateGeometry();
            }
        }

        /// <summary>The outline width that takes part in layout and drawing: none while there is no visible stroke, as in WPF.</summary>
        protected float EffectiveStrokeThickness => stroke.A > 0 ? Math.Max(0, strokeThickness) : 0;

        /// <summary>Rebuilds the drawn geometry before the next frame is drawn.</summary>
        protected void InvalidateGeometry() => geometryValid = false;

        /// <summary>Lays out the drawables that draw the shape for its current size.</summary>
        protected abstract void UpdateGeometry(Vector2 size);

        /// <summary>Applies <see cref="Fill"/> and <see cref="Stroke"/> to the drawables, without rebuilding them.</summary>
        protected abstract void ApplyColours();

        public abstract Vector2 Measure(Vector2 availableSize);

        private void brushChanged(bool visibilityChanged)
        {
            ApplyColours();

            // A stroke appearing or disappearing changes the space the shape needs.
            if (visibilityChanged)
                InvalidateGeometry();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && Height == 0;

            ApplyColours();
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (sizeToContentX || sizeToContentY)
            {
                var desired = Measure(new Vector2(sizeToContentX ? float.PositiveInfinity : DrawWidth, sizeToContentY ? float.PositiveInfinity : DrawHeight));
                if (sizeToContentX) Width = desired.X;
                if (sizeToContentY) Height = desired.Y;
            }

            if (!geometryValid || DrawSize != geometrySize)
            {
                geometryValid = true;
                geometrySize = DrawSize;
                UpdateGeometry(DrawSize);
            }
        }

        /// <summary>The box a Rectangle or Ellipse draws in: all of its bounds for Fill, a centred square for the uniform modes.</summary>
        protected RectangleF StretchedBounds(Vector2 size)
        {
            switch (stretch)
            {
                case Stretch.None:
                    return new RectangleF(size / 2, Vector2.Zero);

                case Stretch.Uniform:
                case Stretch.UniformToFill:
                {
                    float side = stretch == Stretch.Uniform ? Math.Min(size.X, size.Y) : Math.Max(size.X, size.Y);
                    return new RectangleF((size - new Vector2(side)) / 2, new Vector2(side));
                }

                default:
                    return new RectangleF(Vector2.Zero, size);
            }
        }
    }

    /// <summary>
    /// A rectangle, like WPF's: <c>&lt;Rectangle Width="80" Height="40" Fill="#89b4fa" Stroke="White" StrokeThickness="2" RadiusX="6" RadiusY="6" /&gt;</c>.
    /// Corners round only when both radii are set, and use the smaller one (osu!framework masks with circular corners).
    /// </summary>
    public partial class Rectangle : Shape
    {
        private readonly Container body;
        private readonly Box fillBox;
        private float radiusX;
        private float radiusY;

        public Rectangle()
            : base(Stretch.Fill)
        {
            InternalChild = body = new Container
            {
                Masking = true,
                // The masking border only shows over drawn pixels, so the fill is always drawn, even when transparent.
                Child = fillBox = new Box { RelativeSizeAxes = Axes.Both, AlwaysPresent = true, Colour = Colour4.Transparent },
            };
        }

        public float RadiusX
        {
            get => radiusX;
            set
            {
                radiusX = value;
                InvalidateGeometry();
            }
        }

        public float RadiusY
        {
            get => radiusY;
            set
            {
                radiusY = value;
                InvalidateGeometry();
            }
        }

        public override Vector2 Measure(Vector2 availableSize) => new Vector2(EffectiveStrokeThickness);

        protected override void UpdateGeometry(Vector2 size)
        {
            var bounds = StretchedBounds(size);
            float shortest = Math.Min(bounds.Width, bounds.Height);

            body.Position = bounds.TopLeft;
            body.Size = bounds.Size;
            body.CornerRadius = radiusX > 0 && radiusY > 0 ? Math.Min(Math.Min(radiusX, radiusY), shortest / 2) : 0;
            body.BorderThickness = Math.Min(EffectiveStrokeThickness, shortest / 2);
        }

        protected override void ApplyColours()
        {
            fillBox.Colour = Fill;
            body.BorderColour = Stroke;
        }
    }

    /// <summary>
    /// An ellipse filling its bounds, like WPF's: <c>&lt;Ellipse Width="40" Height="24" Fill="Pink" Stroke="White" /&gt;</c>.
    /// Equal width and height make a circle.
    /// </summary>
    public partial class Ellipse : Shape
    {
        private const float unit_diameter = 100;

        private readonly CircularContainer fillCircle;
        private readonly Box fillBox;
        private readonly SmoothPath outline;

        public Ellipse()
            : base(Stretch.Fill)
        {
            InternalChildren = new Drawable[]
            {
                // A circle scaled unevenly is an ellipse; osu!framework's masking follows the scale.
                fillCircle = new CircularContainer
                {
                    Size = new Vector2(unit_diameter),
                    Masking = true,
                    Child = fillBox = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Transparent },
                },
                // The outline is a path rather than the circle's border, which an uneven scale would stretch.
                outline = new SmoothPath { PathRadius = 0.5f, Alpha = 0 },
            };
        }

        public override Vector2 Measure(Vector2 availableSize) => new Vector2(EffectiveStrokeThickness);

        protected override void UpdateGeometry(Vector2 size)
        {
            var bounds = StretchedBounds(size);
            float thickness = Math.Min(EffectiveStrokeThickness, Math.Min(bounds.Width, bounds.Height) / 2);

            // The fill reaches the middle of the stroke, as in WPF.
            var inner = bounds.Inflate(-thickness / 2);

            fillCircle.Position = inner.TopLeft;
            fillCircle.Scale = new Vector2(Math.Max(0, inner.Width), Math.Max(0, inner.Height)) / unit_diameter;
            fillCircle.Alpha = inner.Width > 0 && inner.Height > 0 ? 1 : 0;

            if (thickness <= 0 || inner.Width <= 0 || inner.Height <= 0)
            {
                outline.Alpha = 0;
                return;
            }

            var centre = inner.Centre;
            var radius = inner.Size / 2;
            int segments = Math.Clamp((int)(MathF.PI * (radius.X + radius.Y) / 3), 24, 256);
            var points = new List<Vector2>(segments + 1);

            for (int i = 0; i <= segments; i++)
            {
                float angle = MathF.Tau * i / segments;
                points.Add(centre + new Vector2(radius.X * MathF.Cos(angle), radius.Y * MathF.Sin(angle)));
            }

            ShapeGeometry.SetStroke(outline, points, thickness);
        }

        protected override void ApplyColours()
        {
            fillBox.Colour = Fill;
            outline.Colour = Stroke;
        }
    }

    /// <summary>
    /// Points written as <c>Points="0,0 10,20 20,0"</c> (pairs separated by spaces, or all numbers separated by commas).
    /// Changing the collection redraws the shape that uses it.
    /// </summary>
    public sealed class PointCollection : ObservableCollection<Vector2>
    {
        public PointCollection()
        {
        }

        public PointCollection(IEnumerable<Vector2> points)
            : base(points)
        {
        }

        /// <exception cref="FormatException">The text isn't an even count of numbers.</exception>
        public static PointCollection Parse(string text)
        {
            var parts = text.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length % 2 != 0)
                throw new FormatException($"'{text}' needs an even count of numbers, as x,y pairs.");

            var result = new PointCollection();

            for (int i = 0; i < parts.Length; i += 2)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                    throw new FormatException($"'{text}' is not a list of x,y points.");

                result.Add(new Vector2(x, y));
            }

            return result;
        }

        public override string ToString()
            => string.Join(" ", this.Select(p => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>One run of connected points in a <see cref="FigureShape"/>.</summary>
    public readonly struct ShapeFigure
    {
        public readonly IReadOnlyList<Vector2> Points;

        /// <summary>Whether the outline returns to the first point.</summary>
        public readonly bool IsClosed;

        /// <summary>Whether <see cref="Shape.Fill"/> paints the area inside.</summary>
        public readonly bool IsFilled;

        public ShapeFigure(IReadOnlyList<Vector2> points, bool isClosed, bool isFilled)
        {
            Points = points;
            IsClosed = isClosed;
            IsFilled = isFilled;
        }
    }

    /// <summary>
    /// Base for shapes drawn from point coordinates (Line, Polyline, Polygon, Path). With Stretch None (the default)
    /// the points are drawn where they are and the shape asks for room up to its furthest point; the other modes scale
    /// the drawing into the shape's bounds like WPF.
    /// </summary>
    public abstract partial class FigureShape : Shape
    {
        private readonly Container<SmoothPath> strokes;
        private readonly Container<PolygonFill> fills;

        protected FigureShape()
            : base(Stretch.None)
        {
            InternalChildren = new Drawable[]
            {
                fills = new Container<PolygonFill>(),
                strokes = new Container<SmoothPath>(),
            };
        }

        /// <summary>The figures to draw, in the shape's own coordinates before stretching.</summary>
        protected abstract IReadOnlyList<ShapeFigure> GetFigures();

        /// <summary>The bounds of every point of every figure, or an empty rectangle at the origin.</summary>
        protected RectangleF FigureBounds()
        {
            bool any = false;
            Vector2 min = default, max = default;

            foreach (var figure in GetFigures())
            {
                foreach (var p in figure.Points)
                {
                    min = any ? Vector2.ComponentMin(min, p) : p;
                    max = any ? Vector2.ComponentMax(max, p) : p;
                    any = true;
                }
            }

            return any ? new RectangleF(min, max - min) : RectangleF.Empty;
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var bounds = FigureBounds();
            float thickness = EffectiveStrokeThickness;

            if (Stretch == Stretch.None)
                return Vector2.ComponentMax(Vector2.Zero, new Vector2(bounds.Right, bounds.Bottom) + new Vector2(thickness / 2));

            // An unlimited axis follows the limited one in the uniform modes, and keeps its natural size otherwise.
            float scaleX = float.IsFinite(availableSize.X) && bounds.Width > 0 ? Math.Max(0, availableSize.X - thickness) / bounds.Width : float.NaN;
            float scaleY = float.IsFinite(availableSize.Y) && bounds.Height > 0 ? Math.Max(0, availableSize.Y - thickness) / bounds.Height : float.NaN;

            if (Stretch == Stretch.Fill)
            {
                if (float.IsNaN(scaleX)) scaleX = 1;
                if (float.IsNaN(scaleY)) scaleY = 1;
            }
            else
            {
                float uniform = float.IsNaN(scaleX) ? float.IsNaN(scaleY) ? 1 : scaleY
                    : float.IsNaN(scaleY) ? scaleX
                    : Stretch == Stretch.Uniform ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
                scaleX = scaleY = uniform;
            }

            return new Vector2(bounds.Width * scaleX, bounds.Height * scaleY) + new Vector2(thickness);
        }

        protected override void UpdateGeometry(Vector2 size)
        {
            var figures = GetFigures();
            var bounds = FigureBounds();
            float thickness = EffectiveStrokeThickness;
            var (scale, offset) = stretchTransform(size, bounds, thickness);

            int strokeCount = 0, fillCount = 0;

            foreach (var figure in figures)
            {
                var points = figure.Points.Select(p => (p - bounds.TopLeft) * scale + offset).ToList();

                // A transparent fill isn't triangulated at all; making it visible invalidates the geometry.
                if (figure.IsFilled && Fill.A > 0 && points.Count >= 3)
                {
                    if (fillCount == fills.Count)
                        fills.Add(new PolygonFill { Colour = Fill });
                    fills[fillCount++].SetPolygon(points);
                }

                if (thickness > 0 && points.Count >= 2)
                {
                    if (strokeCount == strokes.Count)
                        strokes.Add(new SmoothPath { Colour = Stroke });

                    if (figure.IsClosed)
                        points.Add(points[0]);

                    ShapeGeometry.SetStroke(strokes[strokeCount++], points, thickness);
                }
            }

            while (strokes.Count > strokeCount)
                strokes.Remove(strokes[^1], true);
            while (fills.Count > fillCount)
                fills.Remove(fills[^1], true);
        }

        /// <summary>WPF's stretch metrics: the scale, and where the top-left of the point bounds lands.</summary>
        private (Vector2 scale, Vector2 offset) stretchTransform(Vector2 size, RectangleF bounds, float thickness)
        {
            if (Stretch == Stretch.None)
                return (Vector2.One, bounds.TopLeft);

            float scaleX = Math.Max(0, size.X - thickness);
            float scaleY = Math.Max(0, size.Y - thickness);
            bool thin = false;

            if (bounds.Width > 0)
                scaleX /= bounds.Width;
            else
            {
                scaleX = 1;
                thin = true;
            }

            if (bounds.Height > 0)
                scaleY /= bounds.Height;
            else
            {
                scaleY = 1;
                thin = true;
            }

            // A straight horizontal or vertical line can't keep an aspect ratio, so it just fills.
            if (Stretch != Stretch.Fill && !thin)
                scaleX = scaleY = Stretch == Stretch.Uniform ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);

            return (new Vector2(scaleX, scaleY), new Vector2(thickness / 2));
        }

        protected override void ApplyColours()
        {
            foreach (var path in strokes)
                path.Colour = Stroke;
            foreach (var fill in fills)
                fill.Colour = Fill;
        }

        /// <summary>Redraws when a bound point collection changes.</summary>
        protected void OnPointsChanged(object sender, NotifyCollectionChangedEventArgs e) => InvalidateGeometry();
    }

    /// <summary>A straight line from (X1, Y1) to (X2, Y2): <c>&lt;Line X1="0" Y1="0" X2="120" Y2="40" Stroke="White" StrokeThickness="2" /&gt;</c>.</summary>
    public partial class Line : FigureShape
    {
        private float x1, y1, x2, y2;

        public float X1
        {
            get => x1;
            set
            {
                x1 = value;
                InvalidateGeometry();
            }
        }

        public float Y1
        {
            get => y1;
            set
            {
                y1 = value;
                InvalidateGeometry();
            }
        }

        public float X2
        {
            get => x2;
            set
            {
                x2 = value;
                InvalidateGeometry();
            }
        }

        public float Y2
        {
            get => y2;
            set
            {
                y2 = value;
                InvalidateGeometry();
            }
        }

        protected override IReadOnlyList<ShapeFigure> GetFigures()
            => new[] { new ShapeFigure(new[] { new Vector2(x1, y1), new Vector2(x2, y2) }, false, false) };
    }

    /// <summary>
    /// Base for <see cref="Polyline"/> and <see cref="Polygon"/>: a <see cref="Points"/> collection that redraws when it changes.
    /// </summary>
    public abstract partial class PointsShape : FigureShape
    {
        private PointCollection points = new PointCollection();

        protected PointsShape()
        {
            points.CollectionChanged += OnPointsChanged;
        }

        /// <summary>The corners, in XAML as <c>Points="0,0 10,20 20,0"</c>.</summary>
        public PointCollection Points
        {
            get => points;
            set
            {
                if (ReferenceEquals(points, value))
                    return;

                points.CollectionChanged -= OnPointsChanged;
                points = value ?? new PointCollection();
                points.CollectionChanged += OnPointsChanged;
                InvalidateGeometry();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            // The collection may belong to a view model that outlives the shape.
            points.CollectionChanged -= OnPointsChanged;
            base.Dispose(isDisposing);
        }
    }

    /// <summary>Connected straight lines through <see cref="PointsShape.Points"/>, like WPF's Polyline. Only the stroke is drawn.</summary>
    public partial class Polyline : PointsShape
    {
        protected override IReadOnlyList<ShapeFigure> GetFigures() => new[] { new ShapeFigure(Points, false, false) };
    }

    /// <summary>
    /// A closed shape through <see cref="PointsShape.Points"/>, like WPF's Polygon: <see cref="Shape.Fill"/> paints the
    /// inside and the stroke returns to the first point. Self-intersecting outlines fill approximately.
    /// </summary>
    public partial class Polygon : PointsShape
    {
        protected override IReadOnlyList<ShapeFigure> GetFigures() => new[] { new ShapeFigure(Points, true, true) };
    }

    /// <summary>Geometry helpers behind the shapes: strokes as osu!framework paths, and polygon triangulation.</summary>
    public static class ShapeGeometry
    {
        /// <summary>
        /// Draws <paramref name="points"/> (in the parent's coordinates) as a path <paramref name="thickness"/> wide.
        /// The vertices are moved next to the origin so the path's buffer only covers the stroke itself.
        /// </summary>
        public static void SetStroke(SmoothPath path, IReadOnlyList<Vector2> points, float thickness)
        {
            float radius = thickness / 2;
            var min = points.Aggregate(points[0], Vector2.ComponentMin);
            var origin = min - new Vector2(radius);

            path.PathRadius = radius;
            path.Vertices = points.Select(p => p - origin).ToList();
            path.Position = origin;
            path.Alpha = 1;
        }

        /// <summary>
        /// Splits a simple polygon into triangles by ear clipping, adding three points per triangle to <paramref name="output"/>.
        /// What can't be clipped (self-intersecting outlines) is fanned from its first corner.
        /// </summary>
        public static void Triangulate(IReadOnlyList<Vector2> polygon, List<Vector2> output)
        {
            var points = new List<Vector2>(polygon.Count);

            foreach (var p in polygon)
            {
                if (points.Count == 0 || points[^1] != p)
                    points.Add(p);
            }

            if (points.Count > 1 && points[0] == points[^1])
                points.RemoveAt(points.Count - 1);

            if (points.Count < 3)
                return;

            float area = 0;
            for (int i = 0; i < points.Count; i++)
                area += cross(points[i], points[(i + 1) % points.Count]);

            if (area == 0)
                return;

            float winding = Math.Sign(area);
            var remaining = Enumerable.Range(0, points.Count).ToList();

            while (remaining.Count > 3)
            {
                bool clipped = false;

                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int b = remaining[i];
                    int c = remaining[(i + 1) % remaining.Count];
                    float turn = cross(points[b] - points[a], points[c] - points[b]) * winding;

                    // A corner on a straight edge adds no area, so it can simply go.
                    if (Math.Abs(turn) < 1e-6f)
                    {
                        remaining.RemoveAt(i);
                        clipped = true;
                        break;
                    }

                    if (turn < 0 || remaining.Any(j => j != a && j != b && j != c && inTriangle(points[j], points[a], points[b], points[c])))
                        continue;

                    output.Add(points[a]);
                    output.Add(points[b]);
                    output.Add(points[c]);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    break;
            }

            for (int k = 1; k + 1 < remaining.Count; k++)
            {
                output.Add(points[remaining[0]]);
                output.Add(points[remaining[k]]);
                output.Add(points[remaining[k + 1]]);
            }
        }

        private static float cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static bool inTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = cross(b - a, p - a);
            float d2 = cross(c - b, p - b);
            float d3 = cross(a - c, p - c);

            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }
    }

    /// <summary>Draws the inside of a polygon as triangles in its own colour.</summary>
    internal partial class PolygonFill : Drawable, ITexturedShaderDrawable
    {
        private readonly List<Vector2> triangles = new List<Vector2>();

        [Resolved]
        private IRenderer renderer { get; set; }

        public IShader TextureShader { get; private set; }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>The triangles from the last <see cref="SetPolygon"/>, three corners each, in this drawable's space.</summary>
        public IReadOnlyList<Vector2> Triangles => triangles;

        /// <summary>Fills <paramref name="points"/>, given in the parent's coordinates.</summary>
        public void SetPolygon(IReadOnlyList<Vector2> points)
        {
            var min = points.Aggregate(points[0], Vector2.ComponentMin);
            var max = points.Aggregate(points[0], Vector2.ComponentMax);

            // Sized to the polygon, so culling and masking see where it draws.
            Position = min;
            Size = Vector2.ComponentMax(max - min, new Vector2(1));

            triangles.Clear();
            ShapeGeometry.Triangulate(points.Select(p => p - min).ToList(), triangles);
            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new PolygonFillDrawNode(this);

        private class PolygonFillDrawNode : TexturedShaderDrawNode
        {
            private readonly List<Vector2> screenSpace = new List<Vector2>();
            private Texture texture;

            public PolygonFillDrawNode(PolygonFill source)
                : base(source)
            {
            }

            protected new PolygonFill Source => (PolygonFill)base.Source;

            public override void ApplyState()
            {
                base.ApplyState();

                texture = Source.renderer.WhitePixel;
                screenSpace.Clear();

                foreach (var p in Source.triangles)
                    screenSpace.Add(Vector2Extensions.Transform(p, DrawInfo.Matrix));
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (screenSpace.Count < 3)
                    return;

                BindTextureShader(renderer);

                for (int i = 0; i + 2 < screenSpace.Count; i += 3)
                    renderer.DrawTriangle(texture, new Triangle(screenSpace[i], screenSpace[i + 1], screenSpace[i + 2]), DrawColourInfo.Colour);

                UnbindTextureShader(renderer);
            }
        }
    }
}
