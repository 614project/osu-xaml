using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>The XAML samples shown in README.md, so the documentation keeps compiling.</summary>
    [TestFixture]
    public class ReadmeSampleTests
    {
        private const string people_code_behind = @"
using osu.Framework.Bindables;

namespace Demo
{
    public class Person
    {
        public Bindable<string> Name { get; } = new Bindable<string>();
        public BindableInt Age { get; } = new BindableInt();
    }

    public class PeopleViewModel
    {
        public BindableList<Person> People { get; } = new BindableList<Person>();
        public Bindable<Person> Selected { get; } = new Bindable<Person>();
    }

    public partial class MainWindow : OsuXaml.Controls.Window
    {
    }
}";

        [Test]
        public void TemplatesResourcesAndStyles()
        {
            GeneratorRunner.Compiles(people_code_behind, @"
<Window xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
        x:Class=""Demo.MainWindow"" x:DataType=""local:PeopleViewModel"" Title=""People"">
  <Window.Resources>
    <Colour4 x:Key=""Warm"">#fab387</Colour4>
    <Style TargetType=""Button"">
      <Setter Property=""CornerRadius"" Value=""12"" />
    </Style>
    <Style x:Key=""Danger"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""{StaticResource Warm}"" />
    </Style>
    <DataTemplate x:Key=""PersonCard"" x:DataType=""local:Person"">
      <StackPanel Orientation=""Horizontal"" Spacing=""8"">
        <TextBlock Text=""{Bind Name}"" />
        <TextBlock Text=""{Bind Age, StringFormat='({0})'}"" Foreground=""{StaticResource Warm}"" />
      </StackPanel>
    </DataTemplate>
  </Window.Resources>

  <ListBox ItemsSource=""{Bind People}"" SelectedItem=""{Bind Selected}"" ItemTemplate=""{StaticResource PersonCard}"" />
</Window>");
        }
    }
}
