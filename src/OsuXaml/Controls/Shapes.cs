using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Base for WPF-style shapes: <see cref="Fill"/> and <see cref="Stroke"/> brushes, the stroke's pen settings and
    /// <see cref="Stretch"/>. Like WPF, the stroke lies inside the shape's bounds, there's no fill or stroke until a brush is set,
    /// and a shape without a size stretches in its panel cell like any other element.
    /// </summary>
    public abstract partial class Shape : CompositeDrawable, ILayoutElement
    {
        private Brush fill;
        private Brush stroke;
        private float strokeThickness = 1;
        private PenLineCap strokeStartLineCap;
        private PenLineCap strokeEndLineCap;
        private PenLineCap strokeDashCap;
        private PenLineJoin strokeLineJoin;
        private float strokeMiterLimit = 10;
        private DoubleCollection strokeDashArray;
        private float strokeDashOffset;
        private Stretch stretch;

        private readonly ShapeLayer fillLayer;
        private readonly ShapeLayer strokeLayer;

        private bool fillVisible;
        private bool strokeVisible;

        private bool geometryValid;
        private bool brushesValid;
        private Vector2 geometrySize = new Vector2(-1);

        private ShapeMesh fillMesh = ShapeMesh.Empty;
        private ShapeMesh strokeMesh = ShapeMesh.Empty;
        private RectangleF fillBounds;
        private RectangleF strokeBounds;

        private bool sizeToContentX;
        private bool sizeToContentY;

        protected Shape(Stretch defaultStretch)
        {
            stretch = defaultStretch;

            InternalChildren = new Drawable[]
            {
                fillLayer = new ShapeLayer(),
                strokeLayer = new ShapeLayer(),
            };
        }

        /// <summary>What paints the inside: <c>Fill="#89b4fa"</c>, a colour name, or a gradient brush in <c>&lt;Rectangle.Fill&gt;</c>.</summary>
        public Brush Fill
        {
            get => fill;
            set => setBrush(ref fill, value);
        }

        /// <summary>What paints the outline.</summary>
        public Brush Stroke
        {
            get => stroke;
            set => setBrush(ref stroke, value);
        }

        /// <summary>The width of the outline (1 by default), drawn only while <see cref="Stroke"/> is set.</summary>
        public float StrokeThickness
        {
            get => strokeThickness;
            set => setGeometry(ref strokeThickness, value);
        }

        /// <summary>The cap at the start of an open line (Flat by default, as in WPF).</summary>
        public PenLineCap StrokeStartLineCap
        {
            get => strokeStartLineCap;
            set => setGeometry(ref strokeStartLineCap, value);
        }

        /// <summary>The cap at the end of an open line (Flat by default).</summary>
        public PenLineCap StrokeEndLineCap
        {
            get => strokeEndLineCap;
            set => setGeometry(ref strokeEndLineCap, value);
        }

        /// <summary>The cap at both ends of each dash (Flat by default).</summary>
        public PenLineCap StrokeDashCap
        {
            get => strokeDashCap;
            set => setGeometry(ref strokeDashCap, value);
        }

        /// <summary>The shape of corners (Miter by default).</summary>
        public PenLineJoin StrokeLineJoin
        {
            get => strokeLineJoin;
            set => setGeometry(ref strokeLineJoin, value);
        }

        /// <summary>How far a Miter corner may reach, in half stroke widths, before its point is cut off (10 by default, at least 1).</summary>
        public float StrokeMiterLimit
        {
            get => strokeMiterLimit;
            set => setGeometry(ref strokeMiterLimit, value);
        }

        /// <summary>
        /// Dash and gap lengths in multiples of <see cref="StrokeThickness"/>, as <c>StrokeDashArray="4 2"</c>. An odd count repeats;
        /// a zero-length dash with Round <see cref="StrokeDashCap"/> draws a dot.
        /// </summary>
        public DoubleCollection StrokeDashArray
        {
            get => strokeDashArray;
            set
            {
                if (ReferenceEquals(strokeDashArray, value))
                    return;

                if (strokeDashArray != null)
                    strokeDashArray.CollectionChanged -= onDashArrayChanged;

                strokeDashArray = value;

                if (strokeDashArray != null)
                    strokeDashArray.CollectionChanged += onDashArrayChanged;

                InvalidateGeometry();
            }
        }

        /// <summary>How far into the dash pattern the line starts, in multiples of <see cref="StrokeThickness"/>.</summary>
        public float StrokeDashOffset
        {
            get => strokeDashOffset;
            set => setGeometry(ref strokeDashOffset, value);
        }

        /// <summary>How the shape fills its bounds. Rectangle and Ellipse default to Fill, the point shapes to None.</summary>
        public Stretch Stretch
        {
            get => stretch;
            set => setGeometry(ref stretch, value);
        }

        /// <summary>The outline width that takes part in layout and drawing: none while there is no visible stroke.</summary>
        protected float EffectiveStrokeThickness => strokeVisible ? Math.Max(0, strokeThickness) : 0;

        /// <summary>The area <see cref="Fill"/> paints, in this shape's coordinates.</summary>
        internal ShapeMesh FillMesh => fillMesh;

        /// <summary>The area <see cref="Stroke"/> paints, in this shape's coordinates.</summary>
        internal ShapeMesh StrokeMesh => strokeMesh;

        /// <summary>The box a relative brush maps to for the fill and the stroke.</summary>
        internal RectangleF FillBrushBounds => fillBounds;

        internal RectangleF StrokeBrushBounds => strokeBounds;

        /// <summary>Rebuilds the drawn geometry before the next frame is drawn.</summary>
        protected void InvalidateGeometry() => geometryValid = false;

        /// <summary>The figures to draw for the shape's current size, in its own coordinates.</summary>
        protected abstract ShapeOutline BuildOutline(Vector2 size);

        public abstract Vector2 Measure(Vector2 availableSize);

        private void setGeometry<T>(ref T field, T value)
        {
            field = value;
            InvalidateGeometry();
        }

        private void setBrush(ref Brush field, Brush value)
        {
            if (ReferenceEquals(field, value))
                return;

            if (field != null)
                field.Changed -= onBrushChanged;

            field = value;

            if (field != null)
                field.Changed += onBrushChanged;

            onBrushChanged();
        }

        private void onBrushChanged()
        {
            bool fillNowVisible = fill?.IsVisible == true;
            bool strokeNowVisible = stroke?.IsVisible == true;

            // A brush appearing or disappearing changes what has to be built, and a stroke changes the space the shape needs.
            if (fillNowVisible != fillVisible || strokeNowVisible != strokeVisible)
            {
                fillVisible = fillNowVisible;
                strokeVisible = strokeNowVisible;
                InvalidateGeometry();
            }

            brushesValid = false;
        }

        private void onDashArrayChanged(object sender, NotifyCollectionChangedEventArgs e) => InvalidateGeometry();

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && Height == 0;
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
                rebuild(DrawSize);
                brushesValid = false;
            }

            if (!brushesValid)
            {
                brushesValid = true;
                fillLayer.Paint(fillMesh, fill, fillBounds);
                strokeLayer.Paint(strokeMesh, stroke, strokeBounds);
            }
        }

        private void rebuild(Vector2 size)
        {
            var outline = BuildOutline(size);
            float thickness = EffectiveStrokeThickness;

            fillMesh = fillVisible
                ? ShapeGeometry.Fill(outline.Figures.Where(f => f.IsFilled).Select(f => f.Points), outline.FillRule)
                : ShapeMesh.Empty;

            if (thickness <= 0)
                strokeMesh = ShapeMesh.Empty;
            else if (outline.StrokeArea != null)
                strokeMesh = ShapeGeometry.Fill(new[] { outline.StrokeArea }, FillRule.Nonzero);
            else
            {
                strokeMesh = ShapeGeometry.Stroke(outline.Figures.Select(f => (f.Points, f.IsClosed)), new StrokeStyle
                {
                    Thickness = thickness,
                    StartLineCap = strokeStartLineCap,
                    EndLineCap = strokeEndLineCap,
                    DashCap = strokeDashCap,
                    LineJoin = strokeLineJoin,
                    MiterLimit = strokeMiterLimit,
                    DashArray = strokeDashArray,
                    DashOffset = strokeDashOffset,
                });
            }

            // As in WPF, relative brushes map to the outline's bounds, without the stroke. A straight level or upright line has no
            // height or width there (WPF then draws nothing), so that side takes the stroke's width instead.
            fillBounds = outline.Bounds;
            strokeBounds = outline.Bounds;

            if (strokeBounds.Width <= 0)
                strokeBounds = new RectangleF(strokeBounds.X - thickness / 2, strokeBounds.Y, thickness, strokeBounds.Height);
            if (strokeBounds.Height <= 0)
                strokeBounds = new RectangleF(strokeBounds.X, strokeBounds.Y - thickness / 2, strokeBounds.Width, thickness);
        }

        protected override void Dispose(bool isDisposing)
        {
            // Brushes and dash arrays may be resources or view model values that outlive the shape.
            if (fill != null)
                fill.Changed -= onBrushChanged;
            if (stroke != null)
                stroke.Changed -= onBrushChanged;
            if (strokeDashArray != null)
                strokeDashArray.CollectionChanged -= onDashArrayChanged;

            base.Dispose(isDisposing);
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

        /// <summary>What a shape draws at its current size.</summary>
        protected readonly struct ShapeOutline
        {
            /// <summary>The figures, filled (those marked so) and stroked.</summary>
            public readonly IReadOnlyList<ShapeFigure> Figures;

            /// <summary>How overlapping filled figures combine.</summary>
            public readonly FillRule FillRule;

            /// <summary>The bounds of the figures, which relative brushes map to.</summary>
            public readonly RectangleF Bounds;

            /// <summary>When set, the stroke covers this whole area instead of following the figures (a stroke wider than the shape).</summary>
            public readonly IReadOnlyList<Vector2> StrokeArea;

            public ShapeOutline(IReadOnlyList<ShapeFigure> figures, FillRule fillRule, RectangleF bounds, IReadOnlyList<Vector2> strokeArea = null)
            {
                Figures = figures;
                FillRule = fillRule;
                Bounds = bounds;
                StrokeArea = strokeArea;
            }
        }
    }

    /// <summary>
    /// A rectangle, like WPF's: <c>&lt;Rectangle Width="80" Height="40" Fill="#89b4fa" Stroke="White" StrokeThickness="2" RadiusX="6" RadiusY="6" /&gt;</c>.
    /// Corners round into quarter ellipses when both radii are set.
    /// </summary>
    public partial class Rectangle : Shape
    {
        private float radiusX;
        private float radiusY;

        public Rectangle()
            : base(Stretch.Fill)
        {
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

        protected override ShapeOutline BuildOutline(Vector2 size)
        {
            var bounds = StretchedBounds(size);
            float thickness = EffectiveStrokeThickness;

            // The outline runs through the middle of the stroke, half its width inside the bounds.
            var inner = bounds.Inflate(-thickness / 2);

            if (inner.Width <= 0 || inner.Height <= 0)
                return new ShapeOutline(Array.Empty<ShapeFigure>(), FillRule.Nonzero, bounds, ShapeGeometry.Rectangle(bounds, radiusX, radiusY));

            return new ShapeOutline(new[] { new ShapeFigure(ShapeGeometry.Rectangle(inner, radiusX, radiusY), true, true) }, FillRule.Nonzero, inner);
        }
    }

    /// <summary>
    /// An ellipse filling its bounds, like WPF's: <c>&lt;Ellipse Width="40" Height="24" Fill="Pink" Stroke="White" /&gt;</c>.
    /// Equal width and height make a circle.
    /// </summary>
    public partial class Ellipse : Shape
    {
        public Ellipse()
            : base(Stretch.Fill)
        {
        }

        public override Vector2 Measure(Vector2 availableSize) => new Vector2(EffectiveStrokeThickness);

        protected override ShapeOutline BuildOutline(Vector2 size)
        {
            var bounds = StretchedBounds(size);
            float thickness = EffectiveStrokeThickness;

            // The fill reaches the middle of the stroke, as in WPF.
            var inner = bounds.Inflate(-thickness / 2);

            if (inner.Width <= 0 || inner.Height <= 0)
            {
                var area = bounds.Width > 0 && bounds.Height > 0 ? ShapeGeometry.Ellipse(bounds) : null;
                return new ShapeOutline(Array.Empty<ShapeFigure>(), FillRule.Nonzero, bounds, area);
            }

            return new ShapeOutline(new[] { new ShapeFigure(ShapeGeometry.Ellipse(inner), true, true) }, FillRule.Nonzero, inner);
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

        /// <summary>Point text converts on assignment, so a view model's string can bind to <c>Points</c>.</summary>
        public static implicit operator PointCollection(string text) => text == null ? null : Parse(text);

        public override string ToString()
            => string.Join(" ", this.Select(p => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>One run of connected points in a <see cref="FigureShape"/>.</summary>
    public readonly struct ShapeFigure
    {
        public readonly IReadOnlyList<Vector2> Points;

        /// <summary>Whether the outline returns to the first point.</summary>
        public readonly bool IsClosed;

        /// <summary>Whether <see cref="Shape.Fill"/> paints the area inside (as if closed).</summary>
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
        protected FigureShape()
            : base(Stretch.None)
        {
        }

        /// <summary>The figures to draw, in the shape's own coordinates before stretching.</summary>
        protected abstract IReadOnlyList<ShapeFigure> GetFigures();

        /// <summary>How overlapping parts of the filled figures combine.</summary>
        protected abstract FillRule FigureFillRule { get; }

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

        protected override ShapeOutline BuildOutline(Vector2 size)
        {
            var bounds = FigureBounds();
            var (scale, offset) = stretchTransform(size, bounds, EffectiveStrokeThickness);

            var figures = GetFigures()
                          .Select(f => new ShapeFigure(f.Points.Select(p => (p - bounds.TopLeft) * scale + offset).ToList(), f.IsClosed, f.IsFilled))
                          .ToList();

            return new ShapeOutline(figures, FigureFillRule, new RectangleF(offset, bounds.Size * scale));
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

        protected override FillRule FigureFillRule => FillRule.EvenOdd;

        protected override IReadOnlyList<ShapeFigure> GetFigures()
            => new[] { new ShapeFigure(new[] { new Vector2(x1, y1), new Vector2(x2, y2) }, false, false) };
    }

    /// <summary>
    /// Base for <see cref="Polyline"/> and <see cref="Polygon"/>: a <see cref="Points"/> collection that redraws when it changes,
    /// and the <see cref="FillRule"/> for outlines that cross themselves.
    /// </summary>
    public abstract partial class PointsShape : FigureShape
    {
        private PointCollection points = new PointCollection();
        private FillRule fillRule;

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

        /// <summary>Which parts of a self-crossing outline the fill paints: EvenOdd (the default, as in WPF) or Nonzero.</summary>
        public FillRule FillRule
        {
            get => fillRule;
            set
            {
                fillRule = value;
                InvalidateGeometry();
            }
        }

        protected override FillRule FigureFillRule => fillRule;

        protected override void Dispose(bool isDisposing)
        {
            // The collection may belong to a view model that outlives the shape.
            points.CollectionChanged -= OnPointsChanged;
            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// Connected straight lines through <see cref="PointsShape.Points"/>, like WPF's Polyline. The stroke stays open;
    /// <see cref="Shape.Fill"/> paints the area as if the last point joined the first.
    /// </summary>
    public partial class Polyline : PointsShape
    {
        protected override IReadOnlyList<ShapeFigure> GetFigures() => new[] { new ShapeFigure(Points, false, true) };
    }

    /// <summary>
    /// A closed shape through <see cref="PointsShape.Points"/>, like WPF's Polygon: <see cref="Shape.Fill"/> paints the
    /// inside and the stroke returns to the first point.
    /// </summary>
    public partial class Polygon : PointsShape
    {
        protected override IReadOnlyList<ShapeFigure> GetFigures() => new[] { new ShapeFigure(Points, true, true) };
    }
}
