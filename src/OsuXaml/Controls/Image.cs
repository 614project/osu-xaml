using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Shows a picture, like WPF's Image: <c>&lt;Image Source="Textures/logo" Width="120" Stretch="Uniform" /&gt;</c>.
    /// <see cref="Source"/> is a file path (absolute, or relative to the app folder), a texture name from the game's
    /// TextureStore (<c>logo</c> or <c>Textures/logo</c> for <c>Resources/Textures/logo.png</c>), or a URL the store can
    /// download. Without a size the image takes the picture's natural size (or, given one dimension, keeps its aspect
    /// ratio); <see cref="Stretch"/> decides how the picture fills the space it gets. A missing source leaves the image
    /// empty and raises <see cref="ImageFailed"/>.
    /// </summary>
    public partial class Image : CompositeDrawable, ILayoutElement
    {
        private readonly Sprite sprite;

        private string source;
        private Texture texture;
        private bool ownsTexture;
        private int loadVersion;
        private bool loadedOnce;

        private Stretch stretch = Stretch.Uniform;
        private StretchDirection stretchDirection = StretchDirection.Both;

        private bool sizeToContentX;
        private bool sizeToContentY;
        private bool? explicitWidth;
        private bool? explicitHeight;

        [Resolved]
        private IRenderer renderer { get; set; }

        [Resolved(canBeNull: true)]
        private LargeTextureStore largeTextures { get; set; }

        [Resolved(canBeNull: true)]
        private TextureStore textures { get; set; }

        public Image()
        {
            InternalChild = sprite = new Sprite { Anchor = Anchor.Centre, Origin = Anchor.Centre, Alpha = 0 };
        }

        /// <summary>Raised on the update thread when <see cref="Source"/> can't be loaded, with the source.</summary>
        public event Action<string> ImageFailed;

        /// <summary>Raised on the update thread when a picture from <see cref="Source"/> is shown.</summary>
        public event Action ImageOpened;

        /// <summary>Where the picture comes from; see the class summary. Changing it loads the new picture in the background.</summary>
        public string Source
        {
            get => source;
            set
            {
                if (source == value)
                    return;

                source = value;

                if (loadedOnce)
                    loadInBackground();
            }
        }

        /// <summary>The picture itself, for textures made in code. Setting it replaces whatever <see cref="Source"/> loaded.</summary>
        public Texture Texture
        {
            get => texture;
            set
            {
                loadVersion++;
                setTexture(value, false);
            }
        }

        /// <summary>How the picture fills the image's bounds (Uniform by default).</summary>
        public Stretch Stretch
        {
            get => stretch;
            set => stretch = value;
        }

        /// <summary>Whether stretching may enlarge the picture, shrink it, or both (the default).</summary>
        public StretchDirection StretchDirection
        {
            get => stretchDirection;
            set => stretchDirection = value;
        }

        /// <summary>The picture's own size in layout units, or zero while there is no picture.</summary>
        public Vector2 NaturalSize => texture?.DisplaySize ?? Vector2.Zero;

        /// <summary>The size the picture is drawn at inside the image, after stretching.</summary>
        public Vector2 PictureSize => texture != null ? sprite.Size : Vector2.Zero;

        [BackgroundDependencyLoader]
        private void load()
        {
            loadedOnce = true;

            // The first picture loads with the image, so it appears together with the rest of the page.
            if (!string.IsNullOrEmpty(source) && texture == null)
            {
                string name = source;
                var (loaded, owned) = resolve(name);
                setTexture(loaded, owned);
                Schedule(() => raiseLoaded(loaded, name));
            }
        }

        private void loadInBackground()
        {
            int version = ++loadVersion;
            string name = source;

            if (string.IsNullOrEmpty(name))
            {
                setTexture(null, false);
                return;
            }

            Task.Run(() =>
            {
                var (loaded, owned) = resolve(name);

                Schedule(() =>
                {
                    // A newer source (or a Texture set in code) won the race.
                    if (version != loadVersion)
                    {
                        if (owned)
                            loaded?.Dispose();
                        return;
                    }

                    setTexture(loaded, owned);
                    raiseLoaded(loaded, name);
                });
            });
        }

        private void raiseLoaded(Texture loaded, string name)
        {
            if (loaded == null)
                ImageFailed?.Invoke(name);
            else
                ImageOpened?.Invoke();
        }

        /// <summary>Finds the picture for <paramref name="name"/>: a file first, then the texture stores.</summary>
        private (Texture texture, bool owned) resolve(string name)
        {
            try
            {
                string file = FindFile(name);

                if (file != null)
                {
                    using (var stream = File.OpenRead(file))
                        return (Texture.FromStream(renderer, stream), true);
                }

                foreach (string candidate in TextureNames(name))
                {
                    var found = largeTextures?.Get(candidate) ?? textures?.Get(candidate);
                    if (found != null)
                        return (found, false);
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Image source '{name}' could not be loaded: {e.Message}");
            }

            return (null, false);
        }

        /// <summary>The file a source names: a file URI, an absolute path, or a path relative to the app folder that exists.</summary>
        public static string FindFile(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            string path = source;

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile)
                    return null;

                path = uri.LocalPath;
            }

            if (System.IO.Path.IsPathRooted(path))
                return File.Exists(path) ? path : null;

            string local = System.IO.Path.Combine(AppContext.BaseDirectory, path);
            return File.Exists(local) ? local : null;
        }

        /// <summary>
        /// The texture store names to try for a source. The game's store already looks inside <c>Resources/Textures</c>,
        /// so <c>Textures/logo</c> is also tried as <c>logo</c>; a leading slash (WPF's site-of-origin form) is dropped.
        /// </summary>
        public static IEnumerable<string> TextureNames(string source)
        {
            string name = source.Replace('\\', '/').TrimStart('/');
            yield return name;

            const string folder = "Textures/";
            if (name.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && name.Length > folder.Length)
                yield return name.Substring(folder.Length);
        }

        private void setTexture(Texture value, bool owned)
        {
            if (ownsTexture && texture != null && texture != value)
                texture.Dispose();

            texture = value;
            ownsTexture = owned;
            sprite.Texture = value;
            sprite.Alpha = value != null ? 1 : 0;
        }

        private void decideExplicitSize()
        {
            // Decided once, before any panel hands the image its measured size, like a Slot deciding what is explicit.
            explicitWidth ??= !RelativeSizeAxes.HasFlag(Axes.X) && Width > 0;
            explicitHeight ??= !RelativeSizeAxes.HasFlag(Axes.Y) && Height > 0;
        }

        public Vector2 Measure(Vector2 availableSize)
        {
            decideExplicitSize();

            // A size of its own limits the picture like the space on offer does, so Width="100" alone keeps the aspect ratio.
            var constraint = new Vector2(
                explicitWidth == true ? Width : availableSize.X,
                explicitHeight == true ? Height : availableSize.Y);

            return StretchMath.ComputeSize(constraint, NaturalSize, stretch, stretchDirection);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            decideExplicitSize();

            sizeToContentX = !(Parent is Slot) && explicitWidth == false && !RelativeSizeAxes.HasFlag(Axes.X);
            sizeToContentY = !(Parent is Slot) && explicitHeight == false && !RelativeSizeAxes.HasFlag(Axes.Y);
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

            var natural = NaturalSize;
            if (natural.X <= 0 || natural.Y <= 0)
                return;

            sprite.Size = StretchMath.ComputeSize(DrawSize, natural, stretch, stretchDirection);

            // Only a picture larger than the image (UniformToFill, or None in a small space) needs cutting off.
            bool overflows = sprite.Width > DrawWidth + 0.01f || sprite.Height > DrawHeight + 0.01f;
            if (Masking != overflows)
                Masking = overflows;
        }

        protected override void Dispose(bool isDisposing)
        {
            loadVersion++;

            if (ownsTexture)
                texture?.Dispose();

            texture = null;
            base.Dispose(isDisposing);
        }
    }
}
