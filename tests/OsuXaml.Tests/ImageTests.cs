using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = OsuXaml.Controls.Image;

namespace OsuXaml.Tests
{
    /// <summary>Image and Viewbox written the way a WPF developer would, through the generator.</summary>
    [TestFixture]
    public class ImageGeneratorTests
    {
        [Test]
        public void ImageSourcesAndStretch()
        {
            string source = ShapesTestData.Compiles(@"
  <StackPanel Spacing=""8"">
    <Image Source=""Textures/logo"" Width=""120"" Stretch=""Uniform"" HorizontalAlignment=""Left"" />
    <Image Source=""{Bind Logo}"" Height=""48"" Stretch=""UniformToFill"" StretchDirection=""DownOnly"" ImageFailed=""OnImageFailed"" />
    <Image Source=""/home/me/pictures/cat.png"" Stretch=""None"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Image()"));
            Assert.That(source, Does.Contain("Source = \"Textures/logo\""));
            Assert.That(source, Does.Contain("Stretch = global::OsuXaml.Controls.Stretch.UniformToFill"));
            Assert.That(source, Does.Contain("StretchDirection = global::OsuXaml.Controls.StretchDirection.DownOnly"));
            Assert.That(source, Does.Contain(".ImageFailed += (a0) => this.OnImageFailed(a0)"));
        }

        [Test]
        public void ViewboxScalesAChildElement()
        {
            string source = ShapesTestData.Compiles(@"
  <Grid ColumnDefinitions=""*,*"">
    <Viewbox Stretch=""Uniform"">
      <TextBlock Text=""Big"" FontWeight=""Bold"" />
    </Viewbox>
    <Viewbox Grid.Column=""1"" Stretch=""Fill"" StretchDirection=""UpOnly"" Width=""100"">
      <Canvas Width=""24"" Height=""24"">
        <Ellipse Width=""24"" Height=""24"" Fill=""Pink"" />
      </Canvas>
    </Viewbox>
  </Grid>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Viewbox()"));
            Assert.That(source, Does.Contain(".Child = e"));
        }
    }

    /// <summary>The stretching rules on their own, as WPF's Viewbox computes them.</summary>
    [TestFixture]
    public class StretchMathTests
    {
        private static readonly Vector2 content = new Vector2(200, 100);

        [Test]
        public void ModesInALimitedSpace()
        {
            var space = new Vector2(100, 100);

            Assert.That(StretchMath.ComputeSize(space, content, Stretch.None), Is.EqualTo(new Vector2(200, 100)));
            Assert.That(StretchMath.ComputeSize(space, content, Stretch.Fill), Is.EqualTo(new Vector2(100, 100)));
            Assert.That(StretchMath.ComputeSize(space, content, Stretch.Uniform), Is.EqualTo(new Vector2(100, 50)));
            Assert.That(StretchMath.ComputeSize(space, content, Stretch.UniformToFill), Is.EqualTo(new Vector2(200, 100)));
        }

        [Test]
        public void DirectionLimitsScaling()
        {
            var big = new Vector2(400, 400);

            Assert.That(StretchMath.ComputeScale(big, content, Stretch.Uniform), Is.EqualTo(new Vector2(2)));
            Assert.That(StretchMath.ComputeScale(big, content, Stretch.Uniform, StretchDirection.DownOnly), Is.EqualTo(Vector2.One));
            Assert.That(StretchMath.ComputeScale(new Vector2(50), content, Stretch.Uniform, StretchDirection.UpOnly), Is.EqualTo(Vector2.One));
            Assert.That(StretchMath.ComputeScale(new Vector2(50), content, Stretch.Uniform, StretchDirection.DownOnly), Is.EqualTo(new Vector2(0.25f)));
        }

        [Test]
        public void UnlimitedAxisFollowsTheLimitedOne()
        {
            // A vertical StackPanel offers its width and unlimited height: the picture scales to the width.
            Assert.That(StretchMath.ComputeSize(new Vector2(300, float.PositiveInfinity), content, Stretch.Uniform), Is.EqualTo(new Vector2(300, 150)));
            Assert.That(StretchMath.ComputeSize(new Vector2(float.PositiveInfinity, 50), content, Stretch.Fill), Is.EqualTo(new Vector2(100, 50)));
            Assert.That(StretchMath.ComputeSize(new Vector2(float.PositiveInfinity), content, Stretch.Uniform), Is.EqualTo(content));
        }

        [Test]
        public void EmptyContentStaysEmpty()
        {
            Assert.That(StretchMath.ComputeSize(new Vector2(100), Vector2.Zero, Stretch.Uniform), Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void SourceNamesForTheTextureStore()
        {
            Assert.That(Image.TextureNames("Textures/logo"), Is.EqualTo(new[] { "Textures/logo", "logo" }));
            Assert.That(Image.TextureNames("/Images/logo.png"), Is.EqualTo(new[] { "Images/logo.png" }));
            Assert.That(Image.FindFile("Textures/definitely-missing"), Is.Null);
            Assert.That(Image.FindFile("https://example.com/a.png"), Is.Null);
        }
    }

    /// <summary>Image at runtime: natural size, stretching in panels, and loading from the texture store and files.</summary>
    [HeadlessTest]
    public partial class ImageTests : TestScene
    {
        [Resolved]
        private IRenderer renderer { get; set; }

        private Texture texture(int width, int height) => renderer.CreateTexture(width, height);

        [Test]
        public void SizesToItsPictureWithoutASize()
        {
            Image image = null;

            AddStep("create", () => Child = image = new Image { Texture = texture(200, 100) });
            AddUntilStep("natural size", () => image.DrawSize == new Vector2(200, 100) && image.PictureSize == new Vector2(200, 100));
        }

        [Test]
        public void OneDimensionKeepsTheAspectRatio()
        {
            Image image = null;

            AddStep("create", () => Child = image = new Image { Width = 100, Texture = texture(200, 100) });
            AddUntilStep("height from the aspect ratio", () => image.DrawSize == new Vector2(100, 50));
        }

        [Test]
        public void StretchModesInAFixedBox()
        {
            Image uniform = null, fill = null, cover = null, none = null;

            AddStep("create", () =>
            {
                var grid = new Grid { Size = new Vector2(400, 100), ColumnDefinitions = GridLengthCollection.Parse("*,*,*,*") };
                grid.Add(uniform = new Image { Texture = texture(200, 100) });
                grid.Add(fill = new Image { Texture = texture(200, 100), Stretch = Stretch.Fill });
                grid.Add(cover = new Image { Texture = texture(200, 100), Stretch = Stretch.UniformToFill });
                grid.Add(none = new Image { Texture = texture(50, 20), Stretch = Stretch.None });
                Grid.SetColumn(fill, 1);
                Grid.SetColumn(cover, 2);
                Grid.SetColumn(none, 3);
                Child = grid;
            });

            AddUntilStep("images fill their cells", () => uniform.DrawSize == new Vector2(100) && none.DrawSize == new Vector2(100));
            AddAssert("uniform letterboxes", () => uniform.PictureSize == new Vector2(100, 50) && !uniform.Masking);
            AddAssert("fill distorts", () => fill.PictureSize == new Vector2(100, 100));
            AddAssert("uniform to fill covers and clips", () => cover.PictureSize == new Vector2(200, 100) && cover.Masking);
            AddAssert("none keeps the natural size", () => none.PictureSize == new Vector2(50, 20));
        }

        [Test]
        public void ScalesToTheWidthOfAVerticalStackPanel()
        {
            Image image = null;
            StackPanel panel = null;

            AddStep("create", () =>
            {
                panel = new StackPanel { Width = 300 };
                panel.Add(image = new Image { Texture = texture(200, 100) });
                Child = panel;
            });

            AddUntilStep("width of the panel, height from the ratio", () => image.DrawSize == new Vector2(300, 150) && panel.DrawHeight == 150);
        }

        [Test]
        public void MissingSourceLeavesItEmpty()
        {
            Image image = null;
            string failed = null;

            AddStep("create", () =>
            {
                image = new Image { Source = "Textures/no-such-picture" };
                image.ImageFailed += s => failed = s;
                Child = image;
            });

            AddUntilStep("failure reported", () => failed == "Textures/no-such-picture");
            AddAssert("nothing shown, no size", () => image.Texture == null && image.NaturalSize == Vector2.Zero && image.DrawSize == Vector2.Zero);

            AddStep("clear the source", () => image.Source = null);
            AddAssert("still empty", () => image.Texture == null);
        }

        [Test]
        public void LoadsFromTheGameTextureStore()
        {
            Image named = null, prefixed = null;

            // osu!framework ships Resources/Textures/Checkerboard.png, which the game's TextureStore serves as "Checkerboard".
            AddStep("create", () => Children = new Drawable[]
            {
                named = new Image { Source = "Checkerboard" },
                prefixed = new Image { Source = "Textures/Checkerboard", X = 300 },
            });

            AddUntilStep("loaded", () => named.Texture != null && prefixed.Texture != null);
            AddAssert("natural size from the texture", () => named.NaturalSize.X > 0 && named.DrawSize == named.NaturalSize && prefixed.NaturalSize == named.NaturalSize);
        }

        [Test]
        public void LoadsFromAFileAndSwitchesSource()
        {
            Image image = null;
            string directory = null, wide = null, tall = null;
            int opened = 0;

            AddStep("write pictures", () =>
            {
                directory = Path.Combine(Path.GetTempPath(), "osuxaml-image-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);

                using (var picture = new Image<Rgba32>(8, 4))
                    picture.SaveAsPng(wide = Path.Combine(directory, "wide.png"));
                using (var picture = new Image<Rgba32>(3, 6))
                    picture.SaveAsPng(tall = Path.Combine(directory, "tall.png"));
            });

            AddStep("create", () =>
            {
                image = new Image { Source = wide };
                image.ImageOpened += () => opened++;
                Child = image;
            });
            AddUntilStep("file loaded", () => image.NaturalSize == new Vector2(8, 4) && opened == 1);

            AddStep("switch to another file", () => image.Source = tall);
            AddUntilStep("loaded in the background", () => image.NaturalSize == new Vector2(3, 6) && image.DrawSize == new Vector2(3, 6));
            AddAssert("opened twice", () => opened == 2);

            AddStep("clean up", () =>
            {
                Clear();
                Directory.Delete(directory, true);
            });
        }
    }

    /// <summary>Viewbox at runtime: scaling its child by Stretch and StretchDirection.</summary>
    [HeadlessTest]
    public partial class ViewboxTests : TestScene
    {
        private static osu.Framework.Graphics.Shapes.Box box(float width, float height) => new osu.Framework.Graphics.Shapes.Box { Width = width, Height = height };

        [Test]
        public void UniformScalesToFitAndCentres()
        {
            Viewbox viewbox = null;
            Drawable child = null;

            AddStep("create", () => Child = viewbox = new Viewbox { Size = new Vector2(200, 100), Child = child = box(50, 50) });

            AddUntilStep("scaled by two", () => viewbox.ChildScale == new Vector2(2));
            AddAssert("child fills the height, centred", () =>
            {
                var quad = child.ScreenSpaceDrawQuad;
                var topLeft = viewbox.ToLocalSpace(quad.TopLeft);
                var bottomRight = viewbox.ToLocalSpace(quad.BottomRight);
                return near(topLeft, new Vector2(50, 0)) && near(bottomRight, new Vector2(150, 100));
            });
        }

        [Test]
        public void FillAndDirection()
        {
            Viewbox fill = null, downOnly = null;

            AddStep("create", () => Children = new Drawable[]
            {
                fill = new Viewbox { Size = new Vector2(200, 100), Stretch = Stretch.Fill, Child = box(50, 50) },
                downOnly = new Viewbox { Y = 120, Size = new Vector2(200, 100), StretchDirection = StretchDirection.DownOnly, Child = box(50, 50) },
            });

            AddUntilStep("fill scales each axis", () => fill.ChildScale == new Vector2(4, 2));
            AddAssert("down only never enlarges", () => downOnly.ChildScale == Vector2.One);
        }

        [Test]
        public void WidthAloneKeepsTheChildProportions()
        {
            Viewbox viewbox = null;

            AddStep("create", () => Child = viewbox = new Viewbox { Width = 300, Child = box(60, 20) });
            AddUntilStep("height follows", () => viewbox.DrawSize == new Vector2(300, 100) && viewbox.ChildScale == new Vector2(5));
        }

        [Test]
        public void MeasuresInPanelsAndScalesText()
        {
            Viewbox viewbox = null;
            TextBlock text = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 400 };
                panel.Add(viewbox = new Viewbox { Child = text = new TextBlock { Text = "Hello" } });
                Child = panel;
            });

            AddUntilStep("text measured", () => text.DrawWidth > 0 && viewbox.ContentSize.X == text.DrawWidth);
            AddAssert("scaled to the panel width", () => osu.Framework.Utils.Precision.AlmostEquals(viewbox.ChildScale.X, 400 / text.DrawWidth, 0.001f));
            AddAssert("height in proportion", () => osu.Framework.Utils.Precision.AlmostEquals(viewbox.DrawHeight, text.DrawHeight * 400 / text.DrawWidth, 0.01f));
        }

        [Test]
        public void ReplacingTheChildKeepsTheOldOne()
        {
            Viewbox viewbox = null;
            Drawable first = null;

            AddStep("create", () => Child = viewbox = new Viewbox { Size = new Vector2(100), Child = first = box(10, 10) });
            AddUntilStep("scaled", () => viewbox.ChildScale == new Vector2(10));
            AddStep("replace", () => viewbox.Child = box(50, 25));
            AddUntilStep("new child scaled", () => viewbox.ChildScale == new Vector2(2));
            AddAssert("old child detached", () => first.Parent == null);
            AddStep("old child can be used elsewhere", () => Add(first));
            AddAssert("re-parented", () => first.Parent != null);
        }

        private static bool near(Vector2 a, Vector2 b) => osu.Framework.Utils.Precision.AlmostEquals(a, b, 0.01f);
    }
}
