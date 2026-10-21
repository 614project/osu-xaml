using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using Icon = OsuXaml.Controls.Icon;

namespace OsuXaml.Tests
{
    /// <summary>Icon names, through the generator and at runtime.</summary>
    [TestFixture]
    public class IconGeneratorTests
    {
        [Test]
        public void IconsWithLiteralAndBoundKinds()
        {
            string source = ShapesTestData.Compiles(@"
  <StackPanel Orientation=""Horizontal"" Spacing=""6"">
    <Icon Kind=""Solid.Check"" Size=""20"" Foreground=""#a6e3a1"" VerticalAlignment=""Center"" />
    <Icon Kind=""{Bind IconName}"" />
    <Button>
      <StackPanel Orientation=""Horizontal"" Spacing=""4"">
        <Icon Kind=""save"" Foreground=""#1e1e2e"" />
        <TextBlock Text=""Save"" />
      </StackPanel>
    </Button>
  </StackPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.Icon()"));
            Assert.That(source, Does.Contain("Kind = \"Solid.Check\""));
            Assert.That(source, Does.Contain("Size = new global::osuTK.Vector2(20f)"));
        }

        [Test]
        public void IconAsAMenuItemIcon()
        {
            string source = ShapesTestData.Compiles(@"
  <Menu>
    <MenuItem Header=""_File"">
      <MenuItem Header=""_Save"">
        <MenuItem.Icon>
          <Icon Kind=""Solid.Save"" />
        </MenuItem.Icon>
      </MenuItem>
    </MenuItem>
  </Menu>");

            Assert.That(source, Does.Contain(".Icon = e"));
        }

        [Test]
        public void EveryNameIconFindTakesCompiles()
        {
            ShapesTestData.Compiles(@"
  <StackPanel>
    <Icon Kind=""check"" />
    <Icon Kind=""solid.CHECK"" />
    <Icon Kind=""fa-check-circle"" />
    <Icon Kind=""FontAwesome.Solid.Times"" />
    <Icon Kind=""Regular.Bell"" />
    <Icon Kind=""Brands.Github"" />
    <Icon Kind="" Solid.Star "" />
    <Icon>
      <Icon.Kind>Solid.Heart</Icon.Kind>
    </Icon>
  </StackPanel>");
        }

        [TestCase("Solid.Chek", "did you mean Solid.Check?")]
        [TestCase("chek", "did you mean Solid.Check?")]
        [TestCase("fa-check-circel", "did you mean Solid.CheckCircle?")]
        [TestCase("Solid.Github", "did you mean Brands.Github?")]
        [TestCase("Light.Check", "did you mean Solid.Check?")]
        [TestCase("Light.Xyzzy", "the styles are Solid, Regular and Brands")]
        [TestCase("Solid.Xyzzyplugh", "has no icon by that name")]
        [TestCase("", "has no icon by that name")]
        public void UnknownKindIsABuildError(string kind, string hint)
        {
            var error = ShapesTestData.Error($@"<Icon Kind=""{kind}"" />");

            Assert.That(error.Id, Is.EqualTo("OX0005"));
            Assert.That(error.GetMessage(), Does.Contain($"\"{kind}\" to a FontAwesome icon").And.Contain(hint));
            Assert.That(error.Location.GetLineSpan().StartLinePosition.Line, Is.EqualTo(2), "points at the Icon's line");
        }

        [Test]
        public void UnknownKindFromAStyleOrResourceIsABuildError()
        {
            var fromStyle = ShapesTestData.Error(@"
  <StackPanel>
    <StackPanel.Resources>
      <Style TargetType=""Icon"">
        <Setter Property=""Kind"" Value=""Solid.Sav"" />
      </Style>
    </StackPanel.Resources>
    <Icon />
  </StackPanel>");

            Assert.That(fromStyle.GetMessage(), Does.Contain("did you mean Solid.Save?"));

            var fromResource = ShapesTestData.Error(@"
  <StackPanel>
    <StackPanel.Resources>
      <x:String x:Key=""SaveIcon"">Solid.Sav</x:String>
    </StackPanel.Resources>
    <Icon Kind=""{StaticResource SaveIcon}"" />
  </StackPanel>");

            Assert.That(fromResource.GetMessage(), Does.Contain("did you mean Solid.Save?"));
        }

        [Test]
        public void IconNameWorksOnYourOwnControls()
        {
            const string code_behind = @"
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm { }

    public class IconButton : Container
    {
        [OsuXaml.IconName]
        public string IconKind { get; set; }

        public string Caption { get; set; }
    }

    public partial class View : CompositeDrawable { }
}";

            string Page(string content) => @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">
" + content + @"
</CompositeDrawable>";

            GeneratorRunner.Compiles(code_behind, Page(@"<local:IconButton IconKind=""Solid.Save"" Caption=""Solid.Sav"" />"));

            var error = GeneratorRunner.SingleError(code_behind, Page(@"<local:IconButton IconKind=""Solid.Sav"" />"));
            Assert.That(error.GetMessage(), Does.Contain("did you mean Solid.Save?"));
        }

        [Test]
        public void BoundKindsAreLeftToRuntime()
        {
            ShapesTestData.Compiles(@"<Icon Kind=""{Bind IconName}"" />");
        }

        [Test]
        public void NamesFindFontAwesomeIcons()
        {
            Assert.That(Icon.Find("Solid.Check"), Is.EqualTo(FontAwesome.Solid.Check));
            Assert.That(Icon.Find("check"), Is.EqualTo(FontAwesome.Solid.Check), "no style looks in Solid first");
            Assert.That(Icon.Find("Regular.Bell"), Is.EqualTo(FontAwesome.Regular.Bell));
            Assert.That(Icon.Find("Brands.Github"), Is.EqualTo(FontAwesome.Brands.Github));
            Assert.That(Icon.Find("fa-check-circle"), Is.EqualTo(FontAwesome.Solid.CheckCircle), "CSS names work too");
            Assert.That(Icon.Find("FontAwesome.Solid.Times"), Is.EqualTo(FontAwesome.Solid.Times));
            Assert.That(Icon.Find("Regular.NoSuchIcon"), Is.Null);
            Assert.That(Icon.Find(""), Is.Null);
        }
    }

    /// <summary>Icon at runtime: size, colour and the fallback for unknown names.</summary>
    [HeadlessTest]
    public partial class IconTests : TestScene
    {
        [Test]
        public void IconKeepsItsSizeInAPanel()
        {
            Icon check = null, unknown = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 200 };
                panel.Add(check = new Icon { Kind = "Solid.Check", Foreground = Colour4.Pink });
                panel.Add(unknown = new Icon { Kind = "nope", Size = new Vector2(24) });
                Child = panel;
            });

            AddUntilStep("default size, centred like other sized elements", () => check.DrawSize == new Vector2(16) && check.ToSpaceOfOtherDrawable(Vector2.Zero, check.Parent.Parent).X == 92);
            AddAssert("shows the icon", () => check.Icon.Equals(FontAwesome.Solid.Check) && check.IsKnownKind);
            AddAssert("unknown kinds show a question mark", () => unknown.Icon.Equals(FontAwesome.Solid.Question) && !unknown.IsKnownKind && unknown.DrawSize == new Vector2(24));
            AddStep("change kind", () => check.Kind = "Solid.Times");
            AddAssert("updated", () => check.Icon.Equals(FontAwesome.Solid.Times));
        }
    }
}
