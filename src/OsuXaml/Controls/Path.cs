using System;
using System.Collections.Generic;
using System.Globalization;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A shape drawn from WPF's path markup: <c>&lt;Path Data="M 0,10 L 6,16 L 18,2" Stroke="#a6e3a1" StrokeThickness="3" /&gt;</c>.
    /// Every figure is filled with <see cref="Shape.Fill"/> (open ones as if closed, like WPF) and outlined with the stroke.
    /// With <c>Stretch="Uniform"</c> and a size it scales like a vector icon. See <see cref="Geometry"/> for the markup.
    /// </summary>
    public partial class Path : FigureShape
    {
        private Geometry data;

        /// <summary>The outline, in XAML as path markup: <c>Data="M 0,0 L 10,0 10,10 Z"</c>.</summary>
        public Geometry Data
        {
            get => data;
            set
            {
                data = value;
                InvalidateGeometry();
            }
        }

        protected override IReadOnlyList<ShapeFigure> GetFigures() => data?.Figures ?? Array.Empty<ShapeFigure>();
    }

    /// <summary>
    /// Figures parsed from WPF/SVG path markup, curves already flattened into short straight segments.
    /// Supports M (move), L (line), H and V (horizontal and vertical lines), C and S (cubic curves), Q and T (quadratic
    /// curves), A (elliptical arcs) and Z (close), in upper case for absolute and lower case for relative coordinates,
    /// with repeated coordinates continuing the last command. A leading fill rule (F0 or F1) is accepted; overlapping
    /// figures fill as one area, so holes need separate shapes.
    /// </summary>
    public sealed class Geometry
    {
        private readonly string text;

        private Geometry(string text, IReadOnlyList<ShapeFigure> figures)
        {
            this.text = text;
            Figures = figures;
        }

        public IReadOnlyList<ShapeFigure> Figures { get; }

        /// <exception cref="FormatException">The markup has an unknown command or a missing number.</exception>
        public static Geometry Parse(string data) => new Geometry(data, new PathMarkupParser(data).Parse());

        /// <summary>Path markup converts on assignment, so a view model's string can bind to <c>Data</c>.</summary>
        public static implicit operator Geometry(string data) => data == null ? null : Parse(data);

        public override string ToString() => text;

        /// <summary>Reads path markup one command at a time, keeping the pen position and the last control point.</summary>
        private sealed class PathMarkupParser
        {
            private readonly string text;
            private readonly List<ShapeFigure> figures = new List<ShapeFigure>();

            private int position;
            private List<Vector2> figure;
            private Vector2 current;
            private Vector2 figureStart;
            private Vector2? lastCubicControl;
            private Vector2? lastQuadraticControl;

            public PathMarkupParser(string text)
            {
                this.text = text ?? string.Empty;
            }

            public List<ShapeFigure> Parse()
            {
                skipSeparators();

                // F0 (even-odd) or F1 (non-zero) may lead the markup.
                if (position < text.Length - 1 && (text[position] == 'F' || text[position] == 'f') && (text[position + 1] == '0' || text[position + 1] == '1'))
                {
                    position += 2;
                    skipSeparators();
                }

                char command = '\0';

                while (true)
                {
                    skipSeparators();
                    if (position >= text.Length)
                        break;

                    if (char.IsLetter(text[position]))
                        command = text[position++];
                    else if (command == '\0')
                        throw error("expected a command such as M");

                    // After a move, more coordinate pairs are lines; other commands simply repeat.
                    command = run(command);
                }

                endFigure(false);
                return figures;
            }

            /// <summary>Runs one command and returns the command that repeated coordinates continue with.</summary>
            private char run(char command)
            {
                bool relative = char.IsLower(command);
                var origin = relative ? current : Vector2.Zero;

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        endFigure(false);
                        current = figureStart = origin + point();
                        figure = new List<Vector2> { current };
                        resetControls();
                        return relative ? 'l' : 'L';

                    case 'L':
                        lineTo(origin + point());
                        resetControls();
                        break;

                    case 'H':
                        lineTo(new Vector2((relative ? current.X : 0) + number(), current.Y));
                        resetControls();
                        break;

                    case 'V':
                        lineTo(new Vector2(current.X, (relative ? current.Y : 0) + number()));
                        resetControls();
                        break;

                    case 'C':
                    {
                        var c1 = origin + point();
                        var c2 = origin + point();
                        cubicTo(c1, c2, origin + point());
                        break;
                    }

                    case 'S':
                    {
                        // The first control point mirrors the previous curve's second one.
                        var c1 = lastCubicControl.HasValue ? 2 * current - lastCubicControl.Value : current;
                        var c2 = origin + point();
                        cubicTo(c1, c2, origin + point());
                        break;
                    }

                    case 'Q':
                    {
                        var c = origin + point();
                        quadraticTo(c, origin + point());
                        break;
                    }

                    case 'T':
                    {
                        var c = lastQuadraticControl.HasValue ? 2 * current - lastQuadraticControl.Value : current;
                        quadraticTo(c, origin + point());
                        break;
                    }

                    case 'A':
                    {
                        var radius = new Vector2(Math.Abs(number()), Math.Abs(number()));
                        float rotation = number();
                        bool largeArc = flag();
                        bool sweep = flag();
                        arcTo(radius, rotation, largeArc, sweep, origin + point());
                        resetControls();
                        break;
                    }

                    case 'Z':
                        endFigure(true);
                        current = figureStart;
                        resetControls();
                        return '\0';

                    default:
                        throw error($"unknown command '{command}'");
                }

                return command;
            }

            private void resetControls()
            {
                lastCubicControl = null;
                lastQuadraticControl = null;
            }

            private void ensureFigure()
            {
                // Drawing after Z (without a move) starts a new figure where the last one began.
                figure ??= new List<Vector2> { current };
            }

            private void lineTo(Vector2 point)
            {
                ensureFigure();
                figure.Add(point);
                current = point;
            }

            private void cubicTo(Vector2 c1, Vector2 c2, Vector2 end)
            {
                ensureFigure();
                var start = current;
                int steps = segments(Vector2.Distance(start, c1) + Vector2.Distance(c1, c2) + Vector2.Distance(c2, end));

                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps, u = 1 - t;
                    figure.Add(u * u * u * start + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * end);
                }

                current = end;
                lastCubicControl = c2;
                lastQuadraticControl = null;
            }

            private void quadraticTo(Vector2 control, Vector2 end)
            {
                ensureFigure();
                var start = current;
                int steps = segments(Vector2.Distance(start, control) + Vector2.Distance(control, end));

                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps, u = 1 - t;
                    figure.Add(u * u * start + 2 * u * t * control + t * t * end);
                }

                current = end;
                lastQuadraticControl = control;
                lastCubicControl = null;
            }

            /// <summary>An elliptical arc by the SVG rules: the endpoint form is turned into a centre and two angles.</summary>
            private void arcTo(Vector2 radius, float rotationDegrees, bool largeArc, bool sweep, Vector2 end)
            {
                ensureFigure();
                var start = current;

                if (radius.X == 0 || radius.Y == 0 || start == end)
                {
                    lineTo(end);
                    return;
                }

                float phi = MathHelper.DegreesToRadians(rotationDegrees);
                float cos = MathF.Cos(phi), sin = MathF.Sin(phi);

                var half = (start - end) / 2;
                var p = new Vector2(cos * half.X + sin * half.Y, -sin * half.X + cos * half.Y);

                // Radii too small to reach the end point grow just enough.
                float lambda = p.X * p.X / (radius.X * radius.X) + p.Y * p.Y / (radius.Y * radius.Y);
                if (lambda > 1)
                    radius *= MathF.Sqrt(lambda);

                float rx2 = radius.X * radius.X, ry2 = radius.Y * radius.Y;
                float numerator = rx2 * ry2 - rx2 * p.Y * p.Y - ry2 * p.X * p.X;
                float denominator = rx2 * p.Y * p.Y + ry2 * p.X * p.X;
                float coefficient = (largeArc == sweep ? -1 : 1) * MathF.Sqrt(Math.Max(0, numerator / denominator));

                var centreRotated = coefficient * new Vector2(radius.X * p.Y / radius.Y, -radius.Y * p.X / radius.X);
                var centre = new Vector2(cos * centreRotated.X - sin * centreRotated.Y, sin * centreRotated.X + cos * centreRotated.Y) + (start + end) / 2;

                var from = new Vector2((p.X - centreRotated.X) / radius.X, (p.Y - centreRotated.Y) / radius.Y);
                var to = new Vector2((-p.X - centreRotated.X) / radius.X, (-p.Y - centreRotated.Y) / radius.Y);

                float startAngle = angle(Vector2.UnitX, from);
                float sweepAngle = angle(from, to);

                if (!sweep && sweepAngle > 0)
                    sweepAngle -= MathF.Tau;
                else if (sweep && sweepAngle < 0)
                    sweepAngle += MathF.Tau;

                int steps = segments(Math.Abs(sweepAngle) * Math.Max(radius.X, radius.Y));

                for (int i = 1; i < steps; i++)
                {
                    float theta = startAngle + sweepAngle * i / steps;
                    float x = radius.X * MathF.Cos(theta), y = radius.Y * MathF.Sin(theta);
                    figure.Add(centre + new Vector2(cos * x - sin * y, sin * x + cos * y));
                }

                // The exact end point, so rounding never leaves a gap to the next segment.
                figure.Add(end);
                current = end;
            }

            private static float angle(Vector2 u, Vector2 v) => MathF.Atan2(u.X * v.Y - u.Y * v.X, u.X * v.X + u.Y * v.Y);

            /// <summary>Enough straight segments that a curve of this length looks smooth, about one per 4 pixels.</summary>
            private static int segments(float length) => Math.Clamp((int)MathF.Ceiling(length / 4), 4, 64);

            private void endFigure(bool closed)
            {
                if (figure != null && figure.Count > 1)
                    figures.Add(new ShapeFigure(figure, closed, true));

                figure = null;
            }

            private Vector2 point() => new Vector2(number(), number());

            private bool flag()
            {
                skipSeparators();

                // Flags are single digits and may be written without separators ("a10 10 0 0110 10").
                if (position < text.Length && (text[position] == '0' || text[position] == '1'))
                    return text[position++] == '1';

                throw error("expected an arc flag (0 or 1)");
            }

            private float number()
            {
                skipSeparators();
                int start = position;

                if (position < text.Length && (text[position] == '+' || text[position] == '-'))
                    position++;

                bool digits = false, dot = false;

                while (position < text.Length)
                {
                    char c = text[position];

                    if (char.IsDigit(c))
                        digits = true;
                    else if (c == '.' && !dot)
                        dot = true;
                    else
                        break;

                    position++;
                }

                if (digits && position < text.Length && (text[position] == 'e' || text[position] == 'E'))
                {
                    int exponent = position + 1;
                    if (exponent < text.Length && (text[exponent] == '+' || text[exponent] == '-'))
                        exponent++;

                    if (exponent < text.Length && char.IsDigit(text[exponent]))
                    {
                        position = exponent;
                        while (position < text.Length && char.IsDigit(text[position]))
                            position++;
                    }
                }

                if (!digits)
                    throw error("expected a number");

                return float.Parse(text.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void skipSeparators()
            {
                while (position < text.Length && (char.IsWhiteSpace(text[position]) || text[position] == ','))
                    position++;
            }

            private FormatException error(string problem) => new FormatException($"Path markup '{text}': {problem} at position {position}.");
        }
    }
}
