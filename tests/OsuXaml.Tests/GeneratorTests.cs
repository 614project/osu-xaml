using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>
    /// Runs the source generator in memory against real osu!framework references and checks
    /// both the generated code and the build errors reported for broken XAML.
    /// </summary>
    [TestFixture]
    public class GeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
        public BindableBool Enabled { get; } = new BindableBool();
        public BindableList<Row> Rows { get; } = new BindableList<Row>();
        public void Save() { }
        public void Remove(Row row) { }
    }

    public class Row
    {
        public string Name { get; set; }
        public BindableFloat Progress { get; } = new BindableFloat();
    }

    public partial class View : CompositeDrawable
    {
        private static float Opacity(bool enabled) => enabled ? 1 : 0.5f;
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        [Test]
        public void ValidXamlGeneratesCompilingCode()
        {
            var (diagnostics, compileErrors, source) = run(header + @"
  <FillFlowContainer RelativeSizeAxes=""Both"" Direction=""Vertical"" Spacing=""0,4"" Padding=""8,4"">
    <SpriteText Text=""{Bind Title}"" Font=""Bold,20"" Colour=""#ff8800"" />
    <SpriteText>Static text</SpriteText>
    <BasicCheckbox Current=""{Bind Enabled}"" Alpha=""{Bind Enabled, Mode=OneWay, Converter=Opacity, Animate=150, Easing=OutQuad}"" />
    <BasicButton Text=""Save"" Size=""80,30"" Action=""{Bind Save}"" />
    <FillFlowContainer ItemsSource=""{Bind Rows}"" RelativeSizeAxes=""X"" AutoSizeAxes=""Y"">
      <FillFlowContainer.ItemTemplate>
        <Container RelativeSizeAxes=""X"" Height=""20"">
          <SpriteText Text=""{Bind Name}"" />
          <SpriteText Text=""{Bind Progress, StringFormat='{0:P0}'}"" Anchor=""CentreRight"" Origin=""CentreRight"" />
          <BasicButton Text=""x"" Action=""{Bind Remove, Source=Root}"" />
        </Container>
      </FillFlowContainer.ItemTemplate>
    </FillFlowContainer>
  </FillFlowContainer>
</CompositeDrawable>");

            Assert.That(diagnostics, Is.Empty);
            Assert.That(compileErrors, Is.Empty, source);
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e"));
            Assert.That(source, Does.Contain("dataContext.Remove(item"));
            Assert.That(source, Does.Contain("Easing.OutQuad"));
        }

        [Test]
        public void UnknownPropertyIsReportedOnItsLine()
        {
            var diagnostic = single(header + @"
  <Box Colour=""White"" Wibble=""3"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0004"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Wibble"));
            Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line, Is.EqualTo(2));
        }

        [Test]
        public void UnknownBindingPathIsReported()
        {
            var diagnostic = single(header + @"
  <SpriteText Text=""{Bind Titel}"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0006"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Titel"));
        }

        [Test]
        public void BindingTypeMismatchNeedsConverter()
        {
            var diagnostic = single(header + @"
  <Box Alpha=""{Bind Title}"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Converter"));
        }

        [Test]
        public void TwoWayBindingRequiresMatchingBindable()
        {
            var diagnostic = single(header + @"
  <BasicCheckbox Current=""{Bind Title}"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
        }

        [Test]
        public void InvalidEnumListsValidValues()
        {
            var diagnostic = single(header + @"
  <Box Anchor=""Middle"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0005"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Centre"));
        }

        [Test]
        public void UnknownElementIsReported()
        {
            var diagnostic = single(header + @"
  <NoSuchControl />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0003"));
        }

        [Test]
        public void WpfStyleControlsAndAttachedPropertiesCompile()
        {
            var (diagnostics, compileErrors, source) = run(header + @"
  <DockPanel RelativeSizeAxes=""Both"">
    <Border DockPanel.Dock=""Top"" Background=""#181825"" Padding=""8,4"">
      <TextBlock Text=""{Bind Title}"" FontSize=""20"" FontWeight=""Bold"" />
    </Border>
    <Grid RowDefinitions=""Auto,*"" ColumnDefinitions=""120,*"" RowSpacing=""8"">
      <TextBlock Grid.Row=""0"" Text=""Enabled"" VerticalAlignment=""Center"" />
      <CheckBox Grid.Column=""1"" IsChecked=""{Bind Enabled}"" Content=""On"" />
      <TextBox Grid.Row=""1"" Grid.ColumnSpan=""2"" Text=""{Bind Title}"" HorizontalAlignment=""Stretch"" />
      <Button Grid.Row=""1"" Content=""Save"" Click=""{Bind Save}"" HorizontalAlignment=""Right"" />
    </Grid>
    <StackPanel ItemsSource=""{Bind Rows}"" Spacing=""4"">
      <StackPanel.ItemTemplate>
        <TextBlock Text=""{Bind Name}"" />
      </StackPanel.ItemTemplate>
    </StackPanel>
  </DockPanel>
</CompositeDrawable>");

            Assert.That(diagnostics, Is.Empty);
            Assert.That(compileErrors, Is.Empty, source);
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.Grid.SetColumnSpan(e"));
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.Layout.SetHorizontalAlignment(e"));
            Assert.That(source, Does.Contain("GridLengthCollection.Parse(\"Auto,*\")"));
            Assert.That(source, Does.Contain(".Child = e"));
        }

        [Test]
        public void InvalidRowDefinitionsAreABuildError()
        {
            var diagnostic = single(header + @"
  <Grid RowDefinitions=""Auto,big,*"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0005"));
        }

        [Test]
        public void UnknownAttachedPropertyIsReported()
        {
            var diagnostic = single(header + @"
  <Grid><Box Grid.Rowz=""1"" /></Grid>
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0004"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Rowz"));
        }

        private static Diagnostic single(string xaml) => GeneratorRunner.SingleError(code_behind, xaml);

        private static (ImmutableArray<Diagnostic> Diagnostics, Diagnostic[] CompileErrors, string Source) run(string xaml)
        {
            var result = GeneratorRunner.Run(code_behind, xaml);
            return (result.Diagnostics, result.CompileErrors, result.Source);
        }
    }
}
