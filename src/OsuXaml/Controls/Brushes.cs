using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Transforms;
using osu.Framework.Utils;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// What paints a shape's <see cref="Shape.Fill"/> or <see cref="Shape.Stroke"/>, as in WPF: a <see cref="SolidColorBrush"/>,
    /// <see cref="LinearGradientBrush"/> or <see cref="RadialGradientBrush"/>. A colour converts to a solid brush, so
    /// <c>Fill="#89b4fa"</c> and a view model's <c>Colour4</c> still work. Changing a brush redraws the shapes that use it.
    /// </summary>
    public abstract class Brush : IInterpolable<Brush>
    {
        private float opacity = 1;

        /// <summary>Multiplies the brush's alpha, from 0 to 1.</summary>
        public float Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                OnChanged();
            }
        }

        /// <summary>Raised when anything that changes how the brush paints is set.</summary>
        public event Action Changed;

        /// <summary>Whether the brush paints anything at all; a transparent brush takes no room as a stroke, as before brushes existed.</summary>
        public abstract bool IsVisible { get; }

        protected void OnChanged() => Changed?.Invoke();

        public static implicit operator Brush(Colour4 colour) => new SolidColorBrush(colour);

        /// <summary>
        /// Solid colours fade into each other, so <c>Fill="{Bind Tint, Animate=300}"</c> keeps working with brushes.
        /// Anything else switches halfway through.
        /// </summary>
        public Brush ValueAt<TEasing>(double time, Brush startValue, Brush endValue, double startTime, double endTime, in TEasing easing)
            where TEasing : IEasingFunction
        {
            double progress = endTime <= startTime ? 1 : Math.Clamp((time - startTime) / (endTime - startTime), 0, 1);
            double eased = Interpolation.ApplyEasing(easing, progress);

            if (startValue is SolidColorBrush from && endValue is SolidColorBrush to)
            {
                return new SolidColorBrush(Interpolation.ValueAt(eased, from.Color, to.Color, 0, 1))
                {
                    Opacity = (float)(from.Opacity + (to.Opacity - from.Opacity) * eased),
                };
            }

            return eased < 0.5 ? startValue : endValue;
        }
    }

    /// <summary>One colour: <c>&lt;SolidColorBrush Color="#89b4fa" Opacity="0.5" /&gt;</c>.</summary>
    public sealed class SolidColorBrush : Brush
    {
        private Colour4 color;

        public SolidColorBrush()
        {
        }

        public SolidColorBrush(Colour4 color)
        {
            this.color = color;
        }

        public Colour4 Color
        {
            get => color;
            set
            {
                color = value;
                OnChanged();
            }
        }

        public override bool IsVisible => color.A > 0 && Opacity > 0;

        public static implicit operator SolidColorBrush(Colour4 colour) => new SolidColorBrush(colour);

        public override string ToString() => color.ToHex();
    }

    /// <summary>A colour at a point of a gradient, from 0 (start) to 1 (end): <c>&lt;GradientStop Color="Red" Offset="0.5" /&gt;</c>.</summary>
    public sealed class GradientStop
    {
        private Colour4 color;
        private float offset;

        public GradientStop()
        {
        }

        public GradientStop(Colour4 color, float offset)
        {
            this.color = color;
            this.offset = offset;
        }

        public Colour4 Color
        {
            get => color;
            set
            {
                color = value;
                Changed?.Invoke();
            }
        }

        public float Offset
        {
            get => offset;
            set
            {
                offset = value;
                Changed?.Invoke();
            }
        }

        public event Action Changed;
    }

    /// <summary>The stops of a gradient. Adding, removing or changing a stop redraws the shapes that use the brush.</summary>
    public sealed class GradientStopCollection : ObservableCollection<GradientStop>
    {
        public GradientStopCollection()
        {
        }

        public GradientStopCollection(IEnumerable<GradientStop> stops)
            : base(stops)
        {
            foreach (var stop in this)
                stop.Changed += onStopChanged;
        }

        /// <summary>Raised when the stops are added, removed or changed.</summary>
        public event Action Changed;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (GradientStop stop in e.OldItems)
                    stop.Changed -= onStopChanged;
            }

            if (e.NewItems != null)
            {
                foreach (GradientStop stop in e.NewItems)
                    stop.Changed += onStopChanged;
            }

            base.OnCollectionChanged(e);
            Changed?.Invoke();
        }

        protected override void ClearItems()
        {
            foreach (var stop in this)
                stop.Changed -= onStopChanged;

            base.ClearItems();
        }

        private void onStopChanged() => Changed?.Invoke();
    }

    /// <summary>Whether a gradient's points are fractions of the painted shape's bounds (the default) or the shape's own coordinates.</summary>
    public enum BrushMappingMode
    {
        Absolute,
        RelativeToBoundingBox,
    }

    /// <summary>What a gradient paints beyond its end: the end colours (Pad), the gradient again (Repeat), or the gradient mirrored (Reflect).</summary>
    public enum GradientSpreadMethod
    {
        Pad,
        Reflect,
        Repeat,
    }

    /// <summary>
    /// Base for <see cref="LinearGradientBrush"/> and <see cref="RadialGradientBrush"/>. Stops can be written straight inside the
    /// brush, as in WPF. Colours between stops mix in sRGB, WPF's default.
    /// </summary>
    public abstract class GradientBrush : Brush
    {
        private GradientStopCollection gradientStops = new GradientStopCollection();
        private BrushMappingMode mappingMode = BrushMappingMode.RelativeToBoundingBox;
        private GradientSpreadMethod spreadMethod;

        protected GradientBrush()
        {
            gradientStops.Changed += OnChanged;
        }

        public GradientStopCollection GradientStops
        {
            get => gradientStops;
            set
            {
                if (ReferenceEquals(gradientStops, value))
                    return;

                gradientStops.Changed -= OnChanged;
                gradientStops = value ?? new GradientStopCollection();
                gradientStops.Changed += OnChanged;
                OnChanged();
            }
        }

        public BrushMappingMode MappingMode
        {
            get => mappingMode;
            set
            {
                mappingMode = value;
                OnChanged();
            }
        }

        public GradientSpreadMethod SpreadMethod
        {
            get => spreadMethod;
            set
            {
                spreadMethod = value;
                OnChanged();
            }
        }

        /// <summary>Stops written inside the brush element go to <see cref="GradientStops"/>.</summary>
        public void Add(GradientStop stop) => gradientStops.Add(stop);

        public override bool IsVisible => Opacity > 0 && gradientStops.Any(s => s.Color.A > 0);

        /// <summary>The colour at <paramref name="t"/> along the gradient, after <see cref="SpreadMethod"/>, with alpha not premultiplied.</summary>
        public Colour4 ColourAt(double t)
        {
            switch (spreadMethod)
            {
                case GradientSpreadMethod.Repeat:
                    t -= Math.Floor(t);
                    break;

                case GradientSpreadMethod.Reflect:
                    t = Math.Abs(t) % 2;
                    if (t > 1)
                        t = 2 - t;
                    break;
            }

            return colourBetweenStops(t);
        }

        private Colour4 colourBetweenStops(double t)
        {
            if (gradientStops.Count == 0)
                return Colour4.Transparent;

            // Stops keep their written order among equal offsets, so two stops at one offset make a hard edge.
            var stops = gradientStops.OrderBy(s => s.Offset).ToList();

            if (t <= stops[0].Offset)
                return stops[0].Color;

            for (int i = 1; i < stops.Count; i++)
            {
                if (t > stops[i].Offset)
                    continue;

                var a = stops[i - 1];
                var b = stops[i];
                float span = b.Offset - a.Offset;
                return span <= 0 ? b.Color : mix(a.Color, b.Color, (float)((t - a.Offset) / span));
            }

            return stops[^1].Color;
        }

        /// <summary>Mixes with premultiplied alpha, so fading to a transparent stop doesn't pick up that stop's hidden colour.</summary>
        private static Colour4 mix(Colour4 a, Colour4 b, float amount)
        {
            float alpha = a.A + (b.A - a.A) * amount;

            if (alpha <= 0)
                return Colour4.Transparent;

            float channel(float x, float y) => (x * a.A + (y * b.A - x * a.A) * amount) / alpha;
            return new Colour4(channel(a.R, b.R), channel(a.G, b.G), channel(a.B, b.B), alpha);
        }

        /// <summary>Text that is equal for two brushes exactly when their gradient textures are.</summary>
        internal string TextureKey()
        {
            var key = new System.Text.StringBuilder(GetType().Name).Append(':').Append(spreadMethod);

            foreach (var stop in gradientStops.OrderBy(s => s.Offset))
                key.Append('|').Append(stop.Offset.ToString("R", CultureInfo.InvariantCulture)).Append('=').Append(stop.Color.ToHex());

            return key.ToString();
        }
    }

    /// <summary>
    /// A gradient along the line from <see cref="StartPoint"/> to <see cref="EndPoint"/>, by default fractions of the shape's
    /// bounds: <c>&lt;LinearGradientBrush StartPoint="0,0" EndPoint="1,0"&gt;&lt;GradientStop Color="#89b4fa" Offset="0" /&gt;...</c>.
    /// </summary>
    public sealed class LinearGradientBrush : GradientBrush
    {
        private Vector2 startPoint;
        private Vector2 endPoint = Vector2.One;

        public LinearGradientBrush()
        {
        }

        /// <summary>
        /// Two colours at <paramref name="angle"/> degrees (0 runs left to right, 90 top to bottom). As in WPF, the gradient
        /// starts at the top-left corner and ends one unit along the angle.
        /// </summary>
        public LinearGradientBrush(Colour4 startColor, Colour4 endColor, float angle)
        {
            float radians = MathHelper.DegreesToRadians(angle);
            endPoint = new Vector2(MathF.Cos(radians), MathF.Sin(radians));

            Add(new GradientStop(startColor, 0));
            Add(new GradientStop(endColor, 1));
        }

        public Vector2 StartPoint
        {
            get => startPoint;
            set
            {
                startPoint = value;
                OnChanged();
            }
        }

        public Vector2 EndPoint
        {
            get => endPoint;
            set
            {
                endPoint = value;
                OnChanged();
            }
        }
    }

    /// <summary>
    /// A gradient from <see cref="GradientOrigin"/> out to the ellipse around <see cref="Center"/> with radii
    /// <see cref="RadiusX"/> and <see cref="RadiusY"/>, by default fractions of the shape's bounds.
    /// </summary>
    public sealed class RadialGradientBrush : GradientBrush
    {
        private Vector2 center = new Vector2(0.5f);
        private Vector2 gradientOrigin = new Vector2(0.5f);
        private float radiusX = 0.5f;
        private float radiusY = 0.5f;

        public RadialGradientBrush()
        {
        }

        public RadialGradientBrush(Colour4 centerColor, Colour4 edgeColor)
        {
            Add(new GradientStop(centerColor, 0));
            Add(new GradientStop(edgeColor, 1));
        }

        public Vector2 Center
        {
            get => center;
            set
            {
                center = value;
                OnChanged();
            }
        }

        /// <summary>Where the gradient starts (offset 0); moving it off the centre gives a highlight.</summary>
        public Vector2 GradientOrigin
        {
            get => gradientOrigin;
            set
            {
                gradientOrigin = value;
                OnChanged();
            }
        }

        public float RadiusX
        {
            get => radiusX;
            set
            {
                radiusX = value;
                OnChanged();
            }
        }

        public float RadiusY
        {
            get => radiusY;
            set
            {
                radiusY = value;
                OnChanged();
            }
        }
    }

    /// <summary>
    /// Numbers written as <c>StrokeDashArray="4 2"</c> (spaces or commas between them). Changing the collection redraws
    /// the shape that uses it.
    /// </summary>
    public sealed class DoubleCollection : ObservableCollection<double>
    {
        public DoubleCollection()
        {
        }

        public DoubleCollection(IEnumerable<double> values)
            : base(values)
        {
        }

        /// <exception cref="FormatException">A part isn't a number.</exception>
        public static DoubleCollection Parse(string text)
        {
            var result = new DoubleCollection();

            foreach (string part in text.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new FormatException($"'{text}' is not a list of numbers.");

                result.Add(value);
            }

            return result;
        }

        /// <summary>Number text converts on assignment, so a view model's string can bind to <c>StrokeDashArray</c>.</summary>
        public static implicit operator DoubleCollection(string text) => text == null ? null : Parse(text);

        public override string ToString() => string.Join(" ", this.Select(v => v.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The shape at the end of an open line or a dash: none (Flat), a half square (Square), a half circle (Round) or a point (Triangle).</summary>
    public enum PenLineCap
    {
        Flat,
        Square,
        Round,
        Triangle,
    }

    /// <summary>The shape of the outside of a corner: sharp (Miter, cut at <see cref="Shape.StrokeMiterLimit"/>), cut off (Bevel) or rounded.</summary>
    public enum PenLineJoin
    {
        Miter,
        Bevel,
        Round,
    }

    /// <summary>
    /// Which overlapping parts of a filled outline are inside. EvenOdd (WPF's default) leaves areas crossed an even number of times
    /// empty, so an inner figure cuts a hole; Nonzero fills everything an outline goes around.
    /// </summary>
    public enum FillRule
    {
        EvenOdd,
        Nonzero,
    }
}
