using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using RgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Paints one <see cref="ShapeMesh"/> with one <see cref="Brush"/>: a shape's fill or its stroke. Gradients come from a small
    /// texture looked up at coordinates that follow the brush, so they're exact across any mesh, and the mesh's outline gets a
    /// half-pixel fade so edges look smooth at any scale.
    /// </summary>
    internal partial class ShapeLayer : Drawable, ITexturedShaderDrawable
    {
        /// <summary>How far, in screen pixels, the fade around the outline reaches.</summary>
        private const float fringe_width = 0.5f;

        private ShapeMesh mesh = ShapeMesh.Empty;
        private Brush brush;
        private RectangleF brushBounds;

        private BrushMapping mapping;
        private GradientTextures.Lease textureLease;

        [Resolved]
        private IRenderer renderer { get; set; }

        public IShader TextureShader { get; private set; }

        public ShapeLayer()
        {
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>What this layer paints, in its parent's coordinates.</summary>
        public ShapeMesh Mesh => mesh;

        public Brush Brush => brush;

        /// <summary>The box a brush in <see cref="BrushMappingMode.RelativeToBoundingBox"/> maps to, in the parent's coordinates.</summary>
        public RectangleF BrushBounds => brushBounds;

        // For tests: what the brush samples, and where a point of the mesh samples it.
        internal Texture BrushTexture => mapping.Texture;

        internal Texture WhitePixel => renderer.WhitePixel;

        internal Vector2 TextureCoordinateAt(Vector2 point) => mapping.TextureCoordinate(point);

        /// <summary>Paints <paramref name="newMesh"/> (in the parent's coordinates) with <paramref name="newBrush"/>. Call again when the brush changes inside.</summary>
        public void Paint(ShapeMesh newMesh, Brush newBrush, RectangleF newBrushBounds)
        {
            mesh = newMesh ?? ShapeMesh.Empty;
            brush = newBrush;
            brushBounds = newBrushBounds;

            // Sized to the mesh (plus the fade), so culling and masking see where it draws.
            var bounds = mesh.Bounds;
            Position = bounds.TopLeft - new Vector2(1);
            Size = bounds.Size + new Vector2(2);

            updateMapping();
            Invalidate(Invalidation.DrawNode);
        }

        private void updateMapping()
        {
            var previous = textureLease;
            textureLease = null;

            switch (brush)
            {
                case SolidColorBrush solid:
                    mapping = BrushMapping.Solid(renderer, solid.Color, solid.Opacity);
                    break;

                case LinearGradientBrush linear:
                    mapping = BrushMapping.Linear(renderer, linear, brushBounds, out textureLease);
                    break;

                case RadialGradientBrush radial:
                    mapping = BrushMapping.Radial(renderer, radial, brushBounds, mesh.Bounds, out textureLease);
                    break;

                default:
                    mapping = default;
                    break;
            }

            // Released after acquiring the new one, so an unchanged gradient keeps its texture.
            previous?.Dispose();
        }

        protected override void Dispose(bool isDisposing)
        {
            textureLease?.Dispose();
            textureLease = null;
            base.Dispose(isDisposing);
        }

        protected override DrawNode CreateDrawNode() => new ShapeLayerDrawNode(this);

        /// <summary>
        /// How a brush colours a point: a texture, the texture coordinates as an affine function of the point
        /// (in the layer's parent's coordinates), and a colour multiplied in.
        /// </summary>
        private readonly struct BrushMapping
        {
            public readonly Texture Texture;
            public readonly SRGBColour Colour;
            public readonly Vector3 U;
            public readonly Vector3 V;

            /// <summary>The texture coordinates are kept inside this rectangle, as in osu!framework's shaders.</summary>
            public readonly Vector4 ClampRect;

            public bool IsVisible => Texture != null && Colour.Alpha > 0;

            private BrushMapping(Texture texture, SRGBColour colour, Vector3 u, Vector3 v, Vector4 clampRect)
            {
                Texture = texture;
                Colour = colour;
                U = u;
                V = v;
                ClampRect = clampRect;
            }

            public Vector2 TextureCoordinate(Vector2 p) => new Vector2(U.X * p.X + U.Y * p.Y + U.Z, V.X * p.X + V.Y * p.Y + V.Z);

            public static BrushMapping Solid(IRenderer renderer, Colour4 colour, float opacity)
            {
                var texture = renderer.WhitePixel;
                var rect = texture.GetTextureRect();
                var centre = rect.Centre;
                return new BrushMapping(texture, colour.MultiplyAlpha(opacity), new Vector3(0, 0, centre.X), new Vector3(0, 0, centre.Y),
                    new Vector4(rect.Left, rect.Top, rect.Right, rect.Bottom));
            }

            public static BrushMapping Linear(IRenderer renderer, LinearGradientBrush brush, RectangleF bounds, out GradientTextures.Lease lease)
            {
                // t, the position along the gradient, is affine in the point: t = a.x * x + a.y * y + c.
                var (scale, offset) = brushSpace(brush, bounds);
                var along = brush.EndPoint - brush.StartPoint;
                float lengthSquared = along.LengthSquared;
                Vector3 t;

                if (lengthSquared <= 0)
                    t = new Vector3(0, 0, 1);
                else
                {
                    var a = along / lengthSquared;
                    t = new Vector3(a.X * scale.X, a.Y * scale.Y, Vector2.Dot(a, offset - brush.StartPoint));
                }

                lease = GradientTextures.Acquire(renderer, brush.TextureKey(), () => rampImage(brush));
                var texture = lease.Texture;
                int width = texture.Width;

                Vector3 u;
                Vector4 clamp;

                if (brush.SpreadMethod == GradientSpreadMethod.Pad)
                {
                    // Texel i holds offset i / (n - 1); coordinates stay between the first and last texel centres.
                    u = t * ((width - 1f) / width) + new Vector3(0, 0, 0.5f / width);
                    clamp = new Vector4(0.5f / width, 0.5f, 1 - 0.5f / width, 0.5f);
                }
                else
                {
                    // Texel i holds offset (i + 0.5) / n, and the shader repeats the texture; Reflect's texture holds the gradient twice.
                    u = brush.SpreadMethod == GradientSpreadMethod.Reflect ? t / 2 : t;
                    clamp = new Vector4(0, 0, 1, 1);
                }

                return inTexture(texture, brush.Opacity, u, new Vector3(0, 0, 0.5f), clamp);
            }

            public static BrushMapping Radial(IRenderer renderer, RadialGradientBrush brush, RectangleF bounds, RectangleF meshBounds, out GradientTextures.Lease lease)
            {
                // c, the point relative to the gradient's ellipse (the unit circle), is affine in the point.
                var (scale, offset) = brushSpace(brush, bounds);
                var radius = new Vector2(Math.Max(1e-6f, brush.RadiusX), Math.Max(1e-6f, brush.RadiusY));
                var cScale = Vector2.Divide(scale, radius);
                var cOffset = Vector2.Divide(offset - brush.Center, radius);

                var origin = Vector2.Divide(brush.GradientOrigin - brush.Center, radius);
                if (origin.Length > 0.999f)
                    origin = origin.Normalized() * 0.999f;

                // The texture covers [-extent, extent] around the centre: just past the ellipse when padding (the edge texels carry
                // the last colour outwards), or everything the shape covers when the gradient repeats.
                float extent = 1.02f;

                if (brush.SpreadMethod != GradientSpreadMethod.Pad)
                {
                    foreach (var corner in new[] { meshBounds.TopLeft, meshBounds.TopRight, meshBounds.BottomLeft, meshBounds.BottomRight })
                    {
                        var c = corner * cScale + cOffset;
                        extent = Math.Max(extent, Math.Max(Math.Abs(c.X), Math.Abs(c.Y)));
                    }

                    extent = MathF.Ceiling(Math.Min(extent, 16) * 4) / 4;
                }

                int size = Math.Clamp((int)MathF.Ceiling(128 * extent), 256, 512);
                string key = $"{brush.TextureKey()}@{origin.X:R},{origin.Y:R}/{extent:R}/{size}";

                lease = GradientTextures.Acquire(renderer, key, () => radialImage(brush, origin, extent, size));

                // u = (c + extent) / (2 * extent), so texel centres line up with the samples taken when building it.
                var u = new Vector3(cScale.X, 0, cOffset.X + extent) / (2 * extent);
                var v = new Vector3(0, cScale.Y, cOffset.Y + extent) / (2 * extent);
                float half = 0.5f / size;

                return inTexture(lease.Texture, brush.Opacity, u, v, new Vector4(half, half, 1 - half, 1 - half));
            }

            /// <summary>Maps the brush's points to the layer's: relative points are fractions of <paramref name="bounds"/>.</summary>
            private static (Vector2 scale, Vector2 offset) brushSpace(GradientBrush brush, RectangleF bounds)
            {
                if (brush.MappingMode == BrushMappingMode.Absolute)
                    return (Vector2.One, Vector2.Zero);

                var size = new Vector2(Math.Max(bounds.Width, 1e-6f), Math.Max(bounds.Height, 1e-6f));
                return (Vector2.Divide(Vector2.One, size), Vector2.Divide(-bounds.TopLeft, size));
            }

            /// <summary>Moves 0..1 coordinates into where the texture sits in its native texture.</summary>
            private static BrushMapping inTexture(Texture texture, float opacity, Vector3 u, Vector3 v, Vector4 clamp)
            {
                var rect = texture.GetTextureRect();
                u = u * rect.Width + new Vector3(0, 0, rect.Left);
                v = v * rect.Height + new Vector3(0, 0, rect.Top);
                clamp = new Vector4(rect.Left + clamp.X * rect.Width, rect.Top + clamp.Y * rect.Height, rect.Left + clamp.Z * rect.Width, rect.Top + clamp.W * rect.Height);

                return new BrushMapping(texture, new Colour4(1, 1, 1, opacity), u, v, clamp);
            }

            private static RgbaImage rampImage(GradientBrush brush)
            {
                const int n = 256;
                bool pad = brush.SpreadMethod == GradientSpreadMethod.Pad;
                int width = brush.SpreadMethod == GradientSpreadMethod.Reflect ? 2 * n : n;
                var image = new RgbaImage(width, 1);

                for (int i = 0; i < width; i++)
                    image[i, 0] = rgba(brush.ColourAt(pad ? i / (n - 1.0) : (i + 0.5) / n));

                return image;
            }

            private static RgbaImage radialImage(GradientBrush brush, Vector2 origin, float extent, int size)
            {
                var image = new RgbaImage(size, size);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var c = new Vector2(-extent + (x + 0.5f) * 2 * extent / size, -extent + (y + 0.5f) * 2 * extent / size);
                        image[x, y] = rgba(brush.ColourAt(radialOffset(c, origin)));
                    }
                }

                return image;
            }

            /// <summary>
            /// How far <paramref name="c"/> is from the origin towards the unit circle, along the ray from the origin through it:
            /// 0 at the origin, 1 on the circle.
            /// </summary>
            private static double radialOffset(Vector2 c, Vector2 origin)
            {
                double dx = c.X - origin.X, dy = c.Y - origin.Y;
                double dd = dx * dx + dy * dy;

                if (dd < 1e-12)
                    return 0;

                // Solve |origin + s * d| = 1 for s > 0; the circle is reached at s, so c sits at 1 / s of the way.
                double b = origin.X * dx + origin.Y * dy;
                double c0 = origin.X * origin.X + origin.Y * origin.Y - 1;
                double s = (-b + Math.Sqrt(Math.Max(0, b * b - dd * c0))) / dd;
                return 1 / s;
            }

            private static Rgba32 rgba(Colour4 colour)
                => new Rgba32((byte)Math.Round(Math.Clamp(colour.R, 0, 1) * 255), (byte)Math.Round(Math.Clamp(colour.G, 0, 1) * 255),
                    (byte)Math.Round(Math.Clamp(colour.B, 0, 1) * 255), (byte)Math.Round(Math.Clamp(colour.A, 0, 1) * 255));
        }

        private class ShapeLayerDrawNode : TexturedShaderDrawNode
        {
            private readonly List<(Vector2 Position, Color4 Colour, Vector2 TexturePosition)> vertices = new List<(Vector2, Color4, Vector2)>();

            private Texture texture;
            private Vector4 clampRect;
            private IVertexBatch<TexturedVertex2D> quadBatch;

            public ShapeLayerDrawNode(ShapeLayer source)
                : base(source)
            {
            }

            protected new ShapeLayer Source => (ShapeLayer)base.Source;

            public override void ApplyState()
            {
                base.ApplyState();

                vertices.Clear();

                var mapping = Source.mapping;
                var mesh = Source.mesh;
                texture = mapping.Texture;
                clampRect = mapping.ClampRect;

                if (!mapping.IsVisible || mesh.IsEmpty)
                    return;

                var matrix = DrawInfo.Matrix;
                var drawColour = DrawColourInfo.Colour;
                var origin = Source.Position;
                var size = Vector2.ComponentMax(Source.DrawSize, new Vector2(1e-6f));

                // Points are in the parent's coordinates; the draw matrix starts from this layer's own corner.
                Vector2 toScreen(Vector2 p) => Vector2Extensions.Transform(p - origin, matrix);

                SRGBColour colourAt(Vector2 p, float coverage)
                {
                    var c = (drawColour.HasSingleColour ? drawColour.TopLeft : drawColour.Interpolate(Vector2.Divide(p - origin, size))) * mapping.Colour;
                    c.MultiplyAlpha(coverage);
                    return c;
                }

                var quads = mesh.Quads;

                for (int i = 0; i < quads.Count; i++)
                    vertices.Add((toScreen(quads[i]), colourAt(quads[i], 1).SRGB, mapping.TextureCoordinate(quads[i])));

                // A thin band outside each edge fades from half coverage to none, standing in for the pixels the edge cuts.
                foreach (var edge in mesh.Boundary)
                {
                    var a = toScreen(edge.Start);
                    var b = toScreen(edge.End);
                    var along = b - a;
                    float length = along.Length;

                    if (length < 1e-4f)
                        continue;

                    var normal = new Vector2(-along.Y, along.X) / length;
                    if (Vector2.Dot(normal, toScreen(edge.Start + edge.Outward) - a) < 0)
                        normal = -normal;

                    var offset = normal * fringe_width;
                    var startColour = colourAt(edge.Start, 0.5f).SRGB;
                    var endColour = colourAt(edge.End, 0.5f).SRGB;
                    var startTexture = mapping.TextureCoordinate(edge.Start);
                    var endTexture = mapping.TextureCoordinate(edge.End);

                    vertices.Add((a, startColour, startTexture));
                    vertices.Add((b, endColour, endTexture));
                    vertices.Add((b + offset, transparent(endColour), endTexture));
                    vertices.Add((a + offset, transparent(startColour), startTexture));
                }
            }

            private static Color4 transparent(Color4 colour) => new Color4(colour.R, colour.G, colour.B, 0);

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (vertices.Count == 0 || texture == null || !texture.Available)
                    return;

                BindTextureShader(renderer);

                if (renderer.BindTexture(texture))
                {
                    quadBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(1000, 10);
                    var add = quadBatch.AddAction;

                    foreach (var (position, colour, texturePosition) in vertices)
                    {
                        add(new TexturedVertex2D(renderer)
                        {
                            Position = position,
                            Colour = colour,
                            TexturePosition = texturePosition,
                            TextureRect = clampRect,
                        });
                    }
                }

                UnbindTextureShader(renderer);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                quadBatch?.Dispose();
            }
        }
    }

    /// <summary>
    /// Gradient textures shared by every shape painting the same gradient on one renderer, released when the last one lets go.
    /// Only touched from the update thread.
    /// </summary>
    internal static class GradientTextures
    {
        private static readonly ConditionalWeakTable<IRenderer, Dictionary<string, Entry>> caches = new ConditionalWeakTable<IRenderer, Dictionary<string, Entry>>();

        internal sealed class Entry
        {
            public Texture Texture;
            public int Users;
        }

        public static Lease Acquire(IRenderer renderer, string key, Func<RgbaImage> createImage)
        {
            var cache = caches.GetOrCreateValue(renderer);

            if (!cache.TryGetValue(key, out var entry))
            {
                // The upload owns the image and disposes it once it reaches the GPU.
                var image = createImage();

                bool repeats = key.Contains(":" + GradientSpreadMethod.Repeat, StringComparison.Ordinal) || key.Contains(":" + GradientSpreadMethod.Reflect, StringComparison.Ordinal);
                bool radial = key.StartsWith(nameof(RadialGradientBrush), StringComparison.Ordinal);
                var wrap = repeats && !radial ? WrapMode.Repeat : WrapMode.ClampToEdge;

                var texture = renderer.CreateTexture(image.Width, image.Height, false, TextureFilteringMode.Linear, wrap, WrapMode.ClampToEdge);
                texture.SetData(new TextureUpload(image));

                cache[key] = entry = new Entry { Texture = texture };
            }

            entry.Users++;
            return new Lease(cache, key, entry);
        }

        /// <summary>One shape's hold on a texture.</summary>
        public sealed class Lease : IDisposable
        {
            private readonly Dictionary<string, Entry> cache;
            private readonly string key;
            private Entry entry;

            internal Lease(Dictionary<string, Entry> cache, string key, Entry entry)
            {
                this.cache = cache;
                this.key = key;
                this.entry = entry;
            }

            public Texture Texture => entry?.Texture;

            public void Dispose()
            {
                if (entry == null)
                    return;

                if (--entry.Users == 0)
                {
                    cache.Remove(key);
                    entry.Texture.Dispose();
                }

                entry = null;
            }
        }
    }
}
