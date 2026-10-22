using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>What <c>{ThemeResource Key}</c> compiles to: a binding through ThemeResources that the theme's providers fill at run time.</summary>
    [TestFixture]
    public class ThemeResourceGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using OsuXaml.Controls;

namespace Demo
{
    public class Vm
    {
        public BindableList<string> Names { get; } = new BindableList<string>();
    }

    public partial class View : CompositeDrawable
    {
        private void Clicked() { }
    }

    public partial class Page1 : UserControl
    {
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string page(string content) => header + "\n" + content + "\n</CompositeDrawable>";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, page(content));

        private const string bind = "global::OsuXaml.ThemeResources.Bind<";
        private const string colour = "global::osu.Framework.Graphics.Colour4";

        [Test]
        public void PropertiesBindAtRunTimeWithTheirOwnType()
        {
            string source = compiles(@"<Border Background=""{ThemeResource CardBackgroundFillColorDefaultBrush}"" CornerRadius=""{ThemeResource ControlCornerRadius}"" />");

            Assert.That(source, Does.Contain(bind + $"global::OsuXaml.Controls.Border, {colour}>(this.xamlBindings, e0, e0, \"CardBackgroundFillColorDefaultBrush\", "
                                             + "(element, value) => element.Background = value);"));
            Assert.That(source, Does.Contain(bind + "global::OsuXaml.Controls.Border, float>(this.xamlBindings, e0, e0, \"ControlCornerRadius\", "
                                             + "(element, value) => element.CornerRadius = value);"));
        }

        [Test]
        public void AttachedAndGlobalPropertiesCallTheirSetters()
        {
            string source = compiles(@"<TextBlock Text=""Caption"" TextElement.Foreground=""{ThemeResource TextFillColorSecondaryBrush}"" ToolTip=""{ThemeResource HelpText}"" />");

            Assert.That(source, Does.Contain(bind + $"global::OsuXaml.Controls.TextBlock, {colour}>(this.xamlBindings, e0, e0, \"TextFillColorSecondaryBrush\", "
                                             + "(element, value) => global::OsuXaml.Controls.TextElement.SetForeground(element, value));"));

            // ToolTip takes text or a drawable; the string overload is the last choice.
            Assert.That(source, Does.Contain(bind + "global::OsuXaml.Controls.TextBlock, global::osu.Framework.Localisation.LocalisableString>(this.xamlBindings, e0, e0, \"HelpText\", "
                                             + "(element, value) => global::OsuXaml.Controls.ToolTipService.SetToolTip(element, value));"));
        }

        [Test]
        public void BindableMembersTakeTheValue()
        {
            string source = compiles(@"<CheckBox IsChecked=""{ThemeResource DefaultChecked}"" />");

            Assert.That(source, Does.Contain("\"DefaultChecked\", (element, value) => element.IsChecked.Value = value);"));
        }

        [Test]
        public void StyleSettersBindAndTheElementsOwnValueWins()
        {
            string source = compiles(@"
  <CompositeDrawable.Resources>
    <Style TargetType=""Button"">
      <Setter Property=""Background"" Value=""{ThemeResource AccentFillColorDefaultBrush}"" />
      <Setter Property=""Foreground"" Value=""{ThemeResource TextOnAccentFillColorPrimaryBrush}"" />
      <Setter Property=""TextElement.FontSize"" Value=""{ThemeResource ControlContentThemeFontSize}"" />
    </Style>
  </CompositeDrawable.Resources>
  <StackPanel>
    <Button Content=""Themed"" />
    <Button Content=""Own"" Foreground=""#ffffff"" />
  </StackPanel>");

            Assert.That(Regex.Matches(source, Regex.Escape("\"AccentFillColorDefaultBrush\", (element, value) => element.Background = value);")).Count, Is.EqualTo(2), source);
            Assert.That(Regex.Matches(source, Regex.Escape("\"TextOnAccentFillColorPrimaryBrush\"")).Count, Is.EqualTo(1), "the own Foreground replaces the setter");
            Assert.That(source, Does.Contain("(element, value) => global::OsuXaml.Controls.TextElement.SetFontSize(element, value));"));
        }

        [Test]
        public void PropertyElementsBindToo()
        {
            string source = compiles(@"
  <Border>
    <Border.Background>{ThemeResource LayerFillColorDefaultBrush}</Border.Background>
  </Border>");

            Assert.That(source, Does.Contain("\"LayerFillColorDefaultBrush\", (element, value) => element.Background = value);"));
        }

        [Test]
        public void BrushesAndInlinesFollowTheDrawableAroundThem()
        {
            string source = compiles(@"
  <StackPanel>
    <Rectangle Width=""10"" Height=""10"">
      <Rectangle.Fill>
        <SolidColorBrush Color=""{ThemeResource AccentFillColorDefaultBrush}"" />
      </Rectangle.Fill>
    </Rectangle>
    <TextBlock>Hello <Run Text=""world"" Foreground=""{ThemeResource AccentTextFillColorPrimaryBrush}"" /></TextBlock>
  </StackPanel>");

            Assert.That(source, Does.Contain(bind + $"global::OsuXaml.Controls.SolidColorBrush, {colour}>(this.xamlBindings, e1, e2, \"AccentFillColorDefaultBrush\", "
                                             + "(element, value) => element.Color = value);"));
            Assert.That(source, Does.Contain(bind + $"global::OsuXaml.Controls.Run, {colour}>(this.xamlBindings, e3, e4, \"AccentTextFillColorPrimaryBrush\", "
                                             + "(element, value) => element.Foreground = value);"));
        }

        [Test]
        public void TemplatesBindIntoTheItemsScopeAndFollowTheirOwnDrawables()
        {
            string source = compiles(@"
  <StackPanel>
    <ItemsControl ItemsSource=""{Bind Names}"">
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <TextBlock Text=""{Bind}"" TextElement.Foreground=""{ThemeResource TextFillColorPrimaryBrush}"" />
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <Button Content=""x"">
      <Button.Template>
        <ControlTemplate TargetType=""Button"">
          <Rectangle>
            <Rectangle.Fill>
              <SolidColorBrush Color=""{ThemeResource ControlFillColorDefaultBrush}"" />
            </Rectangle.Fill>
          </Rectangle>
        </ControlTemplate>
      </Button.Template>
    </Button>
  </StackPanel>");

            var item = Regex.Match(source, @"new global::OsuXaml\.DataTemplate<string>\(\(item\d+, (scope\d+)\) =>\s*\{\s*var (e\d+) = new global::OsuXaml\.Controls\.TextBlock\(\);");
            Assert.That(item.Success, source);
            Assert.That(source, Does.Contain($"({item.Groups[1].Value}, {item.Groups[2].Value}, {item.Groups[2].Value}, \"TextFillColorPrimaryBrush\""));

            // The brush inside the template follows the template's rectangle, not the button outside the template.
            var rectangle = Regex.Match(source, @"var (e\d+) = new global::OsuXaml\.Controls\.Rectangle\(\);");
            Assert.That(rectangle.Success, source);
            Assert.That(source, Does.Match($@"Bind<global::OsuXaml\.Controls\.SolidColorBrush, {Regex.Escape(colour)}>\(scope\d+, {rectangle.Groups[1].Value}, e\d+, ""ControlFillColorDefaultBrush"""));
        }

        [Test]
        public void TheRootRegistersInTheViewsScopeSoHotReloadRebindsIt()
        {
            string source = GeneratorRunner.Compiles(code_behind, @"<UserControl xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""Demo.Page1""
    Background=""{ThemeResource SolidBackgroundFillColorBaseBrush}"">
  <TextBlock Text=""Hi"" />
</UserControl>");

            // A rebuild unbinds the view's scope first, which stops this binding; the new build binds again, so no guard is needed.
            Assert.That(source, Does.Contain("            " + bind + $"global::Demo.Page1, {colour}>(this.xamlBindings, this, this, \"SolidBackgroundFillColorBaseBrush\", "
                                             + "(element, value) => element.Background = value);"));
            Assert.That(source, Does.Not.Contain("IsRebuilding) global::OsuXaml.ThemeResources"));
        }

        [Test]
        public void TheKeyIsNotCheckedAtBuildTime()
        {
            // No resource with this key exists anywhere in the XAML; that is for the theme to supply.
            string source = compiles(@"<TextBlock Text=""x"" Foreground=""{ThemeResource NoSuchKeyAnywhere}"" />");

            Assert.That(source, Does.Contain("\"NoSuchKeyAnywhere\""));
        }

        [Test]
        public void MistakesAreBuildErrors()
        {
            Assert.That(GeneratorRunner.SingleError(code_behind, page(@"<Border Background=""{ThemeResource}"" />")).GetMessage(), Does.Contain("{ThemeResource} needs a value"));

            Assert.That(GeneratorRunner.SingleError(code_behind, page(@"<Button Click=""{ThemeResource Clicked}"" />")).Id, Is.EqualTo("OX0004"));

            Assert.That(GeneratorRunner.SingleError(code_behind, page(@"<ListBox ItemTemplate=""{ThemeResource RowTemplate}"" />")).GetMessage(),
                Does.Contain("{ThemeResource} can't be used for 'ItemTemplate'"));

            Assert.That(GeneratorRunner.SingleError(code_behind, page(@"<Border Background=""{DynamicThing Key}"" />")).GetMessage(), Does.Contain("{ThemeResource}"));
        }
    }
}
