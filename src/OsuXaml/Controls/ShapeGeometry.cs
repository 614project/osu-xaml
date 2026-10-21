using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Primitives;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// The area a shape paints with one brush: quads (four corners each, in order around the quad) and the edges of the area,
    /// each with the direction pointing out of it so the renderer can soften them.
    /// </summary>
    public sealed class ShapeMesh
    {
        public static readonly ShapeMesh Empty = new ShapeMesh();

        /// <summary>Four corners per quad. Triangles repeat their last corner.</summary>
        public readonly List<Vector2> Quads = new List<Vector2>();

        /// <summary>The outline of the painted area, with the unit normal of each edge pointing outside.</summary>
        public readonly List<BoundaryEdge> Boundary = new List<BoundaryEdge>();

        public bool IsEmpty => Quads.Count == 0;

        /// <summary>The bounds of every quad, or an empty rectangle.</summary>
        public RectangleF Bounds
        {
            get
            {
                if (Quads.Count == 0)
                    return RectangleF.Empty;

                Vector2 min = Quads[0], max = Quads[0];

                foreach (var p in Quads)
                {
                    min = Vector2.ComponentMin(min, p);
                    max = Vector2.ComponentMax(max, p);
                }

                return new RectangleF(min, max - min);
            }
        }

        /// <summary>The painted area. Quads never overlap, so this is the sum of their areas.</summary>
        public float Area
        {
            get
            {
                float total = 0;

                for (int i = 0; i + 3 < Quads.Count; i += 4)
                {
                    var a = Quads[i];
                    var b = Quads[i + 1];
                    var c = Quads[i + 2];
                    var d = Quads[i + 3];
                    total += Math.Abs((a.X * b.Y - b.X * a.Y) + (b.X * c.Y - c.X * b.Y) + (c.X * d.Y - d.X * c.Y) + (d.X * a.Y - a.X * d.Y)) / 2;
                }

                return total;
            }
        }

        /// <summary>Whether <paramref name="point"/> lies in one of the quads.</summary>
        public bool Contains(Vector2 point)
        {
            for (int i = 0; i + 3 < Quads.Count; i += 4)
            {
                if (inTriangle(point, Quads[i], Quads[i + 1], Quads[i + 3]) || inTriangle(point, Quads[i + 2], Quads[i + 3], Quads[i + 1]))
                    return true;
            }

            return false;
        }

        private static bool inTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            float d2 = (c.X - b.X) * (p.Y - b.Y) - (c.Y - b.Y) * (p.X - b.X);
            float d3 = (a.X - c.X) * (p.Y - c.Y) - (a.Y - c.Y) * (p.X - c.X);

            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive) && (d1 != 0 || d2 != 0 || d3 != 0);
        }
    }

    /// <summary>A straight piece of a painted area's outline.</summary>
    public readonly struct BoundaryEdge
    {
        public readonly Vector2 Start;
        public readonly Vector2 End;

        /// <summary>The unit direction pointing out of the area.</summary>
        public readonly Vector2 Outward;

        public BoundaryEdge(Vector2 start, Vector2 end, Vector2 outward)
        {
            Start = start;
            End = end;
            Outward = outward;
        }
    }

    /// <summary>How a stroke is drawn: WPF's pen settings, with dash lengths still in multiples of the thickness.</summary>
    public sealed class StrokeStyle
    {
        public float Thickness { get; init; } = 1;
        public PenLineCap StartLineCap { get; init; }
        public PenLineCap EndLineCap { get; init; }
        public PenLineCap DashCap { get; init; }
        public PenLineJoin LineJoin { get; init; }
        public float MiterLimit { get; init; } = 10;
        public IReadOnlyList<double> DashArray { get; init; }
        public double DashOffset { get; init; }
    }

    /// <summary>
    /// Geometry behind the shapes: filling outlines by a fill rule, turning lines into the area their stroke covers,
    /// and the outlines of ellipses and rounded rectangles.
    /// </summary>
    public static class ShapeGeometry
    {
        /// <summary>Corners closer than this (in the shape's units) are one corner.</summary>
        private const double same_point = 1e-5;

        /// <summary>
        /// Arcs are split finely enough that they stray at most this far from the true curve, small enough to stay smooth when a parent scales the shape up.
        /// </summary>
        private const float arc_tolerance = 0.05f;

        /// <summary>How many straight steps an arc of <paramref name="radius"/> turning by <paramref name="angle"/> needs, at least <paramref name="minimum"/>.</summary>
        private static int arcSteps(float radius, float angle, int minimum, int maximum)
        {
            float step = radius > arc_tolerance ? 2 * MathF.Acos(1 - arc_tolerance / radius) : MathF.PI / 2;
            return Math.Clamp((int)MathF.Ceiling(Math.Abs(angle) / step), minimum, maximum);
        }

        #region Fill

        /// <summary>
        /// The area inside closed outlines under <paramref name="rule"/>. Outlines may cross themselves and each other.
        /// </summary>
        /// <remarks>
        /// The plane is cut into horizontal slabs at every corner and crossing, so no two edges cross inside a slab. In each slab
        /// the edges are sorted left to right and the winding number decides which spans between them are inside. Spans continuing
        /// along the same two edges grow into one quad, and the outline comes from the sides of the spans plus the parts of each slab
        /// border covered on one side only.
        /// </remarks>
        public static ShapeMesh Fill(IEnumerable<IReadOnlyList<Vector2>> outlines, FillRule rule)
        {
            var filled = outlines.Where(o => o != null && o.Count >= 3).ToList();

            // Each slab costs about as much as the edges running through it, so a wide, flat outline (a long chart line) is swept
            // from left to right instead: mirrored over the diagonal here and mirrored back at the end.
            bool across = sweepsFasterAcross(filled);

            // Edges by their ends, top first, with the winding they add up to. Pieces sharing a side (a stroke's bands, corners and
            // caps) run along it in opposite directions, so it cancels out and never has to be swept.
            var windings = new Dictionary<(Vector2 Top, Vector2 Bottom), int>();
            var found = new List<(Vector2 Top, Vector2 Bottom)>();

            foreach (var outline in filled)
            {
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];

                    if (across)
                    {
                        a = new Vector2(a.Y, a.X);
                        b = new Vector2(b.Y, b.X);
                    }

                    // Level edges never change the winding along a scanline, so they only matter as slab borders.
                    if (a.Y == b.Y || !float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(b.X) || !float.IsFinite(b.Y))
                        continue;

                    var key = a.Y < b.Y ? (a, b) : (b, a);
                    int winding = a.Y < b.Y ? 1 : -1;

                    if (windings.TryGetValue(key, out int sum))
                        windings[key] = sum + winding;
                    else
                    {
                        windings[key] = winding;
                        found.Add(key);
                    }
                }
            }

            var edges = new List<Edge>(found.Count);
            var ys = new List<double>(found.Count * 2);

            foreach (var key in found)
            {
                int winding = windings[key];
                if (winding == 0)
                    continue;

                edges.Add(new Edge(key.Top, key.Bottom, winding, edges.Count));
                ys.Add(key.Top.Y);
                ys.Add(key.Bottom.Y);
            }

            var mesh = new ShapeMesh();

            if (edges.Count == 0)
                return mesh;

            edges.Sort((a, b) => a.Y0.CompareTo(b.Y0));
            addCrossings(edges, ys);

            ys.Sort();
            var borders = new List<double>(ys.Count);

            foreach (double y in ys)
            {
                if (borders.Count == 0 || y - borders[^1] > 1e-9)
                    borders.Add(y);
            }

            new Sweep(mesh, rule).Run(edges, borders);

            if (across)
            {
                for (int i = 0; i < mesh.Quads.Count; i++)
                    mesh.Quads[i] = mirror(mesh.Quads[i]);

                for (int i = 0; i < mesh.Boundary.Count; i++)
                {
                    var edge = mesh.Boundary[i];
                    mesh.Boundary[i] = new BoundaryEdge(mirror(edge.Start), mirror(edge.End), mirror(edge.Outward));
                }
            }

            return mesh;

            static Vector2 mirror(Vector2 p) => new Vector2(p.Y, p.X);
        }

        /// <summary>
        /// Whether fewer edges run through each slab when slabs are upright: the edges' total width over the outlines' width
        /// (how many a vertical line meets on average) is smaller than their total height over the height.
        /// </summary>
        private static bool sweepsFasterAcross(List<IReadOnlyList<Vector2>> outlines)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            double width = 0, height = 0;

            foreach (var outline in outlines)
            {
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];

                    minX = Math.Min(minX, a.X);
                    maxX = Math.Max(maxX, a.X);
                    minY = Math.Min(minY, a.Y);
                    maxY = Math.Max(maxY, a.Y);
                    width += Math.Abs(b.X - a.X);
                    height += Math.Abs(b.Y - a.Y);
                }
            }

            return maxX > minX && maxY > minY && width / (maxX - minX) < height / (maxY - minY);
        }

        /// <summary>Adds the height of every point where two edges cross, so slabs never contain a crossing.</summary>
        private static void addCrossings(List<Edge> edges, List<double> ys)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                double eLeft = Math.Min(e.X0, e.X1), eRight = Math.Max(e.X0, e.X1);

                for (int j = i + 1; j < edges.Count && edges[j].Y0 < e.Y1; j++)
                {
                    var f = edges[j];

                    if (Math.Max(f.X0, f.X1) < eLeft || Math.Min(f.X0, f.X1) > eRight)
                        continue;

                    double top = f.Y0, bottom = Math.Min(e.Y1, f.Y1);
                    if (bottom <= top)
                        continue;

                    double dTop = e.XAt(top) - f.XAt(top);
                    double dBottom = e.XAt(bottom) - f.XAt(bottom);

                    if ((dTop < 0 && dBottom > 0) || (dTop > 0 && dBottom < 0))
                        ys.Add(top + (bottom - top) * dTop / (dTop - dBottom));
                }
            }
        }

        private sealed class Edge
        {
            public readonly double X0, Y0, X1, Y1;
            public readonly int Winding;
            public readonly int Id;
            private readonly double slope;

            public Edge(Vector2 top, Vector2 bottom, int winding, int id)
            {
                X0 = top.X;
                Y0 = top.Y;
                X1 = bottom.X;
                Y1 = bottom.Y;
                Winding = winding;
                Id = id;
                slope = (X1 - X0) / (Y1 - Y0);
            }

            public double XAt(double y) => y <= Y0 ? X0 : y >= Y1 ? X1 : X0 + (y - Y0) * slope;
        }

        /// <summary>One pass down the slabs, building the mesh.</summary>
        private sealed class Sweep
        {
            private const double same_x = 1e-6;

            private readonly ShapeMesh mesh;
            private readonly FillRule rule;

            // Spans and outline pieces that reach the bottom of the last slab, by their edges, so the next slab can extend them.
            private Dictionary<(int, int), int> openQuads = new Dictionary<(int, int), int>();
            private Dictionary<(int, bool), int> openSides = new Dictionary<(int, bool), int>();
            private Dictionary<(int, int), int> nextQuads = new Dictionary<(int, int), int>();
            private Dictionary<(int, bool), int> nextSides = new Dictionary<(int, bool), int>();

            private List<(double Left, double Right)> coveredAbove = new List<(double, double)>();
            private List<(double Left, double Right)> coveredBelow = new List<(double, double)>();

            public Sweep(ShapeMesh mesh, FillRule rule)
            {
                this.mesh = mesh;
                this.rule = rule;
            }

            private bool inside(int winding) => rule == FillRule.Nonzero ? winding != 0 : (winding & 1) != 0;

            public void Run(List<Edge> edges, List<double> borders)
            {
                // The edges running through the slab, left to right. Edges only change places where they cross, which is always
                // on a slab border, so the order carries over from slab to slab and an insertion sort puts it right cheaply.
                var order = new List<(double Middle, double Top, double Bottom, Edge Edge)>();
                var coveredTop = new List<(double, double)>();
                int next = 0;

                for (int k = 0; k + 1 < borders.Count; k++)
                {
                    double y0 = borders[k], y1 = borders[k + 1], middle = (y0 + y1) / 2;
                    int kept = 0;

                    for (int i = 0; i < order.Count; i++)
                    {
                        if (order[i].Edge.Y1 > middle)
                            order[kept++] = order[i];
                    }

                    order.RemoveRange(kept, order.Count - kept);

                    while (next < edges.Count && edges[next].Y0 < middle)
                    {
                        if (edges[next].Y1 > middle)
                            order.Add((0, 0, 0, edges[next]));
                        next++;
                    }

                    for (int i = 0; i < order.Count; i++)
                    {
                        var e = order[i].Edge;
                        order[i] = (e.XAt(middle), e.XAt(y0), e.XAt(y1), e);
                    }

                    for (int i = 1; i < order.Count; i++)
                    {
                        var item = order[i];
                        int j = i - 1;

                        for (; j >= 0 && order[j].Middle > item.Middle; j--)
                            order[j + 1] = order[j];

                        order[j + 1] = item;
                    }

                    coveredTop.Clear();
                    coveredBelow.Clear();
                    nextQuads.Clear();
                    nextSides.Clear();

                    int winding = 0;
                    int start = -1;

                    for (int i = 0; i < order.Count;)
                    {
                        // Edges lying on each other (shared sides of touching pieces) count together, so they leave no seam.
                        int j = i, sum = 0;

                        while (j < order.Count && Math.Abs(order[j].Top - order[i].Top) <= same_x && Math.Abs(order[j].Bottom - order[i].Bottom) <= same_x)
                            sum += order[j++].Edge.Winding;

                        bool wasInside = inside(winding);
                        winding += sum;
                        bool isInside = inside(winding);

                        if (!wasInside && isInside)
                            start = i;
                        else if (wasInside && !isInside)
                            addSpan(order[start], order[i], y0, y1, coveredTop);

                        i = j;
                    }

                    addLevelEdges(coveredTop, y0);

                    (openQuads, nextQuads) = (nextQuads, openQuads);
                    (openSides, nextSides) = (nextSides, openSides);
                    (coveredAbove, coveredBelow) = (coveredBelow, coveredAbove);
                }

                coveredBelow.Clear();
                addLevelEdges(coveredBelow, borders[^1]);
            }

            private void addSpan((double Middle, double Top, double Bottom, Edge Edge) left, (double Middle, double Top, double Bottom, Edge Edge) right,
                                 double y0, double y1, List<(double, double)> coveredTop)
            {
                var bottomLeft = point(left.Bottom, y1);
                var bottomRight = point(right.Bottom, y1);

                if (openQuads.TryGetValue((left.Edge.Id, right.Edge.Id), out int quad))
                {
                    mesh.Quads[quad + 2] = bottomRight;
                    mesh.Quads[quad + 3] = bottomLeft;
                }
                else
                {
                    quad = mesh.Quads.Count;
                    mesh.Quads.Add(point(left.Top, y0));
                    mesh.Quads.Add(point(right.Top, y0));
                    mesh.Quads.Add(bottomRight);
                    mesh.Quads.Add(bottomLeft);
                }

                nextQuads[(left.Edge.Id, right.Edge.Id)] = quad;

                addSide(left, y0, y1, true);
                addSide(right, y0, y1, false);

                append(coveredTop, left.Top, right.Top);
                append(coveredBelow, left.Bottom, right.Bottom);
            }

            private void addSide((double Middle, double Top, double Bottom, Edge Edge) side, double y0, double y1, bool isLeft)
            {
                var top = point(side.Top, y0);
                var bottom = point(side.Bottom, y1);

                if (openSides.TryGetValue((side.Edge.Id, isLeft), out int index))
                {
                    var existing = mesh.Boundary[index];
                    mesh.Boundary[index] = new BoundaryEdge(existing.Start, bottom, existing.Outward);
                }
                else
                {
                    var along = bottom - top;
                    var outward = isLeft ? new Vector2(-along.Y, along.X) : new Vector2(along.Y, -along.X);
                    index = mesh.Boundary.Count;
                    mesh.Boundary.Add(new BoundaryEdge(top, bottom, outward.LengthSquared > 0 ? outward.Normalized() : new Vector2(isLeft ? -1 : 1, 0)));
                }

                nextSides[(side.Edge.Id, isLeft)] = index;
            }

            /// <summary>Adds an interval, joining it to the previous one when they touch.</summary>
            private static void append(List<(double Left, double Right)> intervals, double left, double right)
            {
                if (intervals.Count > 0 && left - intervals[^1].Right <= same_x)
                    intervals[^1] = (intervals[^1].Left, Math.Max(right, intervals[^1].Right));
                else
                    intervals.Add((left, right));
            }

            /// <summary>The level parts of the outline at <paramref name="y"/>: covered just below and not above, or the other way round.</summary>
            private void addLevelEdges(List<(double Left, double Right)> below, double y)
            {
                subtract(below, coveredAbove, y, new Vector2(0, -1));
                subtract(coveredAbove, below, y, new Vector2(0, 1));
            }

            private void subtract(List<(double Left, double Right)> from, List<(double Left, double Right)> remove, double y, Vector2 outward)
            {
                int j = 0;

                foreach (var (left, right) in from)
                {
                    double start = left;

                    while (j < remove.Count && remove[j].Right <= start)
                        j++;

                    for (int k = j; k < remove.Count && remove[k].Left < right; k++)
                    {
                        if (remove[k].Left > start)
                            addLevel(start, remove[k].Left, y, outward);

                        start = Math.Max(start, remove[k].Right);
                    }

                    if (start < right)
                        addLevel(start, right, y, outward);
                }
            }

            private void addLevel(double left, double right, double y, Vector2 outward)
            {
                if (right - left > same_x)
                    mesh.Boundary.Add(new BoundaryEdge(point(left, y), point(right, y), outward));
            }

            private static Vector2 point(double x, double y) => new Vector2((float)x, (float)y);
        }

        #endregion

        #region Stroke

        /// <summary>The area covered by stroking each figure with <paramref name="style"/>, overlaps counted once.</summary>
        public static ShapeMesh Stroke(IEnumerable<(IReadOnlyList<Vector2> Points, bool IsClosed)> figures, StrokeStyle style)
        {
            var pieces = new List<IReadOnlyList<Vector2>>();

            foreach (var (points, closed) in figures)
                StrokePieces(points, closed, style, pieces);

            return Fill(pieces, FillRule.Nonzero);
        }

        /// <summary>
        /// The convex pieces whose union is the stroke of one figure: a band per segment, a wedge per corner and the caps,
        /// all wound the same way so they add up under the nonzero rule.
        /// </summary>
        public static void StrokePieces(IReadOnlyList<Vector2> figure, bool closed, StrokeStyle style, List<IReadOnlyList<Vector2>> output)
        {
            float halfWidth = style.Thickness / 2;
            if (!(halfWidth > 0) || figure == null)
                return;

            var points = new List<Vector2>(figure.Count);

            foreach (var p in figure)
            {
                if (points.Count == 0 || (p - points[^1]).Length > same_point)
                    points.Add(p);
            }

            if (closed && points.Count > 1 && (points[0] - points[^1]).Length <= same_point)
                points.RemoveAt(points.Count - 1);

            // A single point has no direction to draw caps along.
            if (points.Count < 2)
                return;

            var stroker = new Stroker(style, halfWidth, output);
            var pattern = dashPattern(style);

            if (pattern != null)
            {
                foreach (var dash in dashes(points, closed, pattern, style))
                    stroker.Open(dash.Points, dash.StartCap, dash.EndCap, dash.StartDirection, dash.EndDirection);
            }
            else if (closed && points.Count > 2)
                stroker.Closed(points);
            else
                stroker.Open(points, style.StartLineCap, style.EndLineCap, Vector2.Zero, Vector2.Zero);
        }

        /// <summary>Dash and gap lengths in the shape's units, or null for a solid line.</summary>
        private static double[] dashPattern(StrokeStyle style)
        {
            var dashArray = style.DashArray;
            if (dashArray == null || dashArray.Count == 0 || dashArray.Any(d => d < 0 || !double.IsFinite(d)))
                return null;

            // An odd count repeats, so dashes and gaps keep alternating ("1 2 3" is "1 2 3 1 2 3").
            var pattern = (dashArray.Count % 2 == 0 ? dashArray : dashArray.Concat(dashArray)).Select(d => d * style.Thickness).ToArray();
            return pattern.Sum() > same_point ? pattern : null;
        }

        private readonly struct Dash
        {
            public readonly List<Vector2> Points;
            public readonly PenLineCap StartCap;
            public readonly PenLineCap EndCap;
            public readonly Vector2 StartDirection;
            public readonly Vector2 EndDirection;

            public Dash(List<Vector2> points, PenLineCap startCap, PenLineCap endCap, Vector2 startDirection, Vector2 endDirection)
            {
                Points = points;
                StartCap = startCap;
                EndCap = endCap;
                StartDirection = startDirection;
                EndDirection = endDirection;
            }
        }

        /// <summary>
        /// Cuts the line into dashes along its length. A dash keeps the corners it passes; its ends get the dash cap, except
        /// where it starts or ends the line itself.
        /// </summary>
        private static List<Dash> dashes(List<Vector2> points, bool closed, double[] pattern, StrokeStyle style)
        {
            var result = new List<Dash>();
            int segmentCount = closed ? points.Count : points.Count - 1;
            double total = pattern.Sum();

            double length = 0;
            for (int i = 0; i < segmentCount; i++)
                length += (points[(i + 1) % points.Count] - points[i]).Length;

            // Dashes too fine to see would only cost time; draw the line solid instead.
            if (length / total > 20000)
            {
                var whole = new List<Vector2>(points);
                if (closed)
                    whole.Add(points[0]);
                result.Add(new Dash(whole, closed ? style.DashCap : style.StartLineCap, closed ? style.DashCap : style.EndLineCap, Vector2.Zero, Vector2.Zero));
                return result;
            }

            double phase = style.DashOffset * style.Thickness % total;
            if (phase < 0)
                phase += total;

            int index = 0;
            while (phase >= pattern[index])
            {
                phase -= pattern[index];
                index = (index + 1) % pattern.Length;
            }

            double remaining = pattern[index] - phase;
            bool on = index % 2 == 0;

            var firstDirection = (points[1] - points[0]).Normalized();
            List<Vector2> current = on ? new List<Vector2> { points[0] } : null;
            var currentStartCap = closed ? style.DashCap : style.StartLineCap;
            var currentStartDirection = firstDirection;
            var lastDirection = firstDirection;

            for (int i = 0; i < segmentCount; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                double segmentLength = (b - a).Length;
                var direction = (b - a) / (float)segmentLength;
                double position = 0;
                lastDirection = direction;

                while (true)
                {
                    if (remaining > segmentLength - position)
                    {
                        if (on)
                            addPoint(current, b);

                        remaining -= segmentLength - position;
                        break;
                    }

                    position += remaining;
                    var cut = a + direction * (float)position;

                    if (on)
                    {
                        addPoint(current, cut);
                        result.Add(new Dash(current, currentStartCap, style.DashCap, currentStartDirection, direction));
                        current = null;
                    }
                    else
                    {
                        current = new List<Vector2> { cut };
                        currentStartCap = style.DashCap;
                        currentStartDirection = direction;
                    }

                    on = !on;
                    index = (index + 1) % pattern.Length;
                    remaining = pattern[index];
                }
            }

            if (on && current != null)
                result.Add(new Dash(current, currentStartCap, closed ? style.DashCap : style.EndLineCap, currentStartDirection, lastDirection));

            return result;
        }

        private static void addPoint(List<Vector2> points, Vector2 point)
        {
            if ((point - points[^1]).Length > same_point)
                points.Add(point);
        }

        /// <summary>Builds the pieces of one stroke.</summary>
        private sealed class Stroker
        {
            private readonly StrokeStyle style;
            private readonly float halfWidth;
            private readonly List<IReadOnlyList<Vector2>> output;

            public Stroker(StrokeStyle style, float halfWidth, List<IReadOnlyList<Vector2>> output)
            {
                this.style = style;
                this.halfWidth = halfWidth;
                this.output = output;
            }

            /// <summary>
            /// An open run of points. A single point is a zero-length dash: only its caps are drawn, facing along the given directions.
            /// </summary>
            public void Open(List<Vector2> points, PenLineCap startCap, PenLineCap endCap, Vector2 startDirection, Vector2 endDirection)
            {
                for (int i = 0; i + 1 < points.Count; i++)
                    segment(points[i], points[i + 1]);

                for (int i = 1; i + 1 < points.Count; i++)
                    join(points[i], direction(points[i - 1], points[i]), direction(points[i], points[i + 1]));

                if (points.Count > 1)
                {
                    startDirection = direction(points[0], points[1]);
                    endDirection = direction(points[^2], points[^1]);
                }

                if (startDirection.LengthSquared == 0)
                    return;

                cap(points[0], -startDirection, startCap);
                cap(points[^1], endDirection, endCap);
            }

            public void Closed(List<Vector2> points)
            {
                int n = points.Count;

                for (int i = 0; i < n; i++)
                {
                    var previous = points[(i + n - 1) % n];
                    var corner = points[i];
                    var following = points[(i + 1) % n];

                    segment(corner, following);
                    join(corner, direction(previous, corner), direction(corner, following));
                }
            }

            private static Vector2 direction(Vector2 from, Vector2 to) => (to - from).Normalized();

            /// <summary>The left-hand normal of a direction, as long as half the stroke.</summary>
            private Vector2 normal(Vector2 direction) => new Vector2(-direction.Y, direction.X) * halfWidth;

            /// <summary>The band along a segment. Its ends pass through the points themselves, where corners and caps meet them.</summary>
            private void segment(Vector2 a, Vector2 b)
            {
                var n = normal(direction(a, b));
                add(a + n, b + n, b, b - n, a - n, a);
            }

            /// <summary>Fills the outside of the corner at <paramref name="p"/>; the inside is already covered where the two bands overlap.</summary>
            private void join(Vector2 p, Vector2 incoming, Vector2 outgoing)
            {
                float cross = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
                float dot = Vector2.Dot(incoming, outgoing);

                if (Math.Abs(cross) < 1e-6f && dot > 0)
                    return;

                // The outside of the corner is opposite the way the line turns.
                float side = cross > 0 ? -1 : 1;
                var n1 = normal(incoming) * side;
                var n2 = normal(outgoing) * side;
                var a = p + n1;
                var b = p + n2;

                switch (style.LineJoin)
                {
                    case PenLineJoin.Bevel:
                        add(p, a, b);
                        break;

                    case PenLineJoin.Round:
                    {
                        var piece = new List<Vector2> { p };
                        arc(piece, p, n1, sweep(n1, n2, incoming));
                        piece.Add(b);
                        add(piece);
                        break;
                    }

                    default:
                    {
                        var bisector = n1 + n2;
                        bisector = bisector.LengthSquared > 1e-12f * halfWidth * halfWidth ? bisector.Normalized() : incoming;

                        float cosHalf = Vector2.Dot(bisector, n1) / halfWidth;
                        float limit = Math.Max(1, style.MiterLimit);

                        if (cosHalf > 1e-6f && 1 / cosHalf <= limit)
                        {
                            add(p, a, p + bisector * (halfWidth / cosHalf), b);
                            break;
                        }

                        // Past the limit, WPF cuts the point off square to the bisector at the limit's distance.
                        float reach = limit * halfWidth;
                        float alongA = (reach - Vector2.Dot(a - p, bisector)) / Math.Max(1e-6f, Vector2.Dot(incoming, bisector));
                        float alongB = (reach - Vector2.Dot(b - p, bisector)) / Math.Max(1e-6f, Vector2.Dot(-outgoing, bisector));
                        add(p, a, a + incoming * alongA, b - outgoing * alongB, b);
                        break;
                    }
                }
            }

            /// <summary>A cap at <paramref name="end"/>, sticking out along <paramref name="outward"/>.</summary>
            private void cap(Vector2 end, Vector2 outward, PenLineCap type)
            {
                var n = normal(outward);
                var tip = outward * halfWidth;

                switch (type)
                {
                    case PenLineCap.Square:
                        add(end + n, end + n + tip, end - n + tip, end - n, end);
                        break;

                    case PenLineCap.Triangle:
                        add(end + n, end + tip, end - n, end);
                        break;

                    case PenLineCap.Round:
                    {
                        var piece = new List<Vector2>();
                        arc(piece, end, n, -MathF.PI);
                        piece.Add(end - n);
                        piece.Add(end);
                        add(piece);
                        break;
                    }
                }
            }

            /// <summary>The signed angle from <paramref name="from"/> to <paramref name="to"/> that passes on the side <paramref name="forward"/> points to.</summary>
            private static float sweep(Vector2 from, Vector2 to, Vector2 forward)
            {
                float angle = MathF.Atan2(from.X * to.Y - from.Y * to.X, Vector2.Dot(from, to));
                float middle = MathF.Atan2(from.Y, from.X) + angle / 2;

                // Only a full turn back is ambiguous: go round the front of the line.
                if (Vector2.Dot(new Vector2(MathF.Cos(middle), MathF.Sin(middle)), forward) < 0)
                    angle = -angle;

                return angle;
            }

            /// <summary>Points on the circle around <paramref name="centre"/> from <paramref name="from"/>, turning by <paramref name="angle"/> (the last point left out).</summary>
            private void arc(List<Vector2> points, Vector2 centre, Vector2 from, float angle)
            {
                int steps = arcSteps(halfWidth, angle, Math.Max(1, (int)MathF.Ceiling(Math.Abs(angle) / (MathF.PI / 2))), 128);
                float start = MathF.Atan2(from.Y, from.X);

                // Exactly where the band ends, so the side they share cancels out.
                points.Add(centre + from);

                for (int i = 1; i < steps; i++)
                {
                    float theta = start + angle * i / steps;
                    points.Add(centre + new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * halfWidth);
                }
            }

            private void add(params Vector2[] piece) => add((IReadOnlyList<Vector2>)piece);

            /// <summary>Adds a piece wound the same way as every other, so overlaps add up instead of cancelling.</summary>
            private void add(IReadOnlyList<Vector2> piece)
            {
                // Measured from its first corner, so a sliver (the corner of an almost straight line) far from the origin keeps its sign.
                double area = 0;
                var origin = piece[0];

                for (int i = 1; i + 1 < piece.Count; i++)
                {
                    var a = piece[i] - origin;
                    var b = piece[i + 1] - origin;
                    area += (double)a.X * b.Y - (double)b.X * a.Y;
                }

                if (Math.Abs(area) < 1e-12)
                    return;

                output.Add(area > 0 ? piece : piece.Reverse().ToArray());
            }
        }

        #endregion

        #region Outlines

        /// <summary>The corners of an ellipse inside <paramref name="bounds"/>, enough of them to look smooth at its size.</summary>
        public static List<Vector2> Ellipse(RectangleF bounds)
        {
            var centre = bounds.Centre;
            var radius = bounds.Size / 2;
            int segments = arcSteps(Math.Max(radius.X, radius.Y), MathF.Tau, 16, 512);
            var points = new List<Vector2>(segments);

            for (int i = 0; i < segments; i++)
            {
                float angle = MathF.Tau * i / segments;
                points.Add(centre + new Vector2(radius.X * MathF.Cos(angle), radius.Y * MathF.Sin(angle)));
            }

            return points;
        }

        /// <summary>
        /// A rectangle's corners, rounded into quarter ellipses when both radii are set (each at most half the side it lies on), as in WPF.
        /// </summary>
        public static List<Vector2> Rectangle(RectangleF rect, float radiusX, float radiusY)
        {
            if (!(radiusX > 0 && radiusY > 0))
                return new List<Vector2> { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft };

            var radius = new Vector2(Math.Min(radiusX, rect.Width / 2), Math.Min(radiusY, rect.Height / 2));
            int steps = arcSteps(Math.Max(radius.X, radius.Y), MathF.PI / 2, 2, 128);
            var points = new List<Vector2>(4 * (steps + 1));

            void corner(Vector2 centre, float startAngle)
            {
                for (int i = 0; i <= steps; i++)
                {
                    float angle = startAngle + MathF.PI / 2 * i / steps;
                    var p = centre + new Vector2(radius.X * MathF.Cos(angle), radius.Y * MathF.Sin(angle));

                    if (points.Count == 0 || (p - points[^1]).Length > same_point)
                        points.Add(p);
                }
            }

            corner(new Vector2(rect.Right - radius.X, rect.Top + radius.Y), -MathF.PI / 2);
            corner(new Vector2(rect.Right - radius.X, rect.Bottom - radius.Y), 0);
            corner(new Vector2(rect.Left + radius.X, rect.Bottom - radius.Y), MathF.PI / 2);
            corner(new Vector2(rect.Left + radius.X, rect.Top + radius.Y), MathF.PI);

            if (points.Count > 1 && (points[0] - points[^1]).Length <= same_point)
                points.RemoveAt(points.Count - 1);

            return points;
        }

        #endregion
    }
}
