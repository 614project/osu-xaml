using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>The input, date and scrolling controls written the way a WPF developer would, checked through the generator.</summary>
    [TestFixture]
    public class InputsGeneratorTests
    {
        private const string code_behind = @"
using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Name { get; } = new Bindable<string>(""Ann"");
        public Bindable<string> Password { get; } = new Bindable<string>("""");
        public Bindable<DateTime?> Birthday { get; } = new Bindable<DateTime?>();
        public Bindable<DateTime?> Due { get; } = new Bindable<DateTime?>();
        public BindableBool PickerOpen { get; } = new BindableBool();
        public BindableDouble Volume { get; } = new BindableDouble(5);
        public BindableDouble Progress { get; } = new BindableDouble();
        public BindableBool Busy { get; } = new BindableBool();
        public Bindable<Colour4> Tint { get; } = new Bindable<Colour4>(Colour4.White);
        public BindableList<TaskItem> Tasks { get; } = new BindableList<TaskItem>();

        public void PasswordEdited() { }
        public void DateChanged(DateTime? date) { }
        public void VolumeChanged(double value) { }
    }

    public class TaskItem
    {
        public string Title { get; set; }
        public Bindable<DateTime?> Due { get; } = new Bindable<DateTime?>();
        public BindableDouble Done { get; } = new BindableDouble();
    }

    public partial class View : CompositeDrawable
    {
        private void OnTextChanged(string text) { }
        private void OnScrolled() { }
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string page(string content) => header + "\n" + content + "\n</CompositeDrawable>";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, page(content));

        private static Microsoft.CodeAnalysis.Diagnostic error(string content) => GeneratorRunner.SingleError(code_behind, page(content));

        [Test]
        public void PasswordBoxAndTextBox()
        {
            string source = compiles(@"
  <StackPanel Spacing=""8"">
    <TextBox Text=""{Binding Name}"" PlaceholderText=""Your name"" MaxLength=""20"" IsReadOnly=""False"" TextChanged=""OnTextChanged"" />
    <PasswordBox Password=""{Binding Password}"" PasswordChar=""*"" MaxLength=""32"" PlaceholderText=""Password""
                 PasswordChanged=""{Bind PasswordEdited}"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e2.Password, dataContext.Password)"));
            Assert.That(source, Does.Contain("PasswordChar = char.Parse(\"*\")"));
            Assert.That(source, Does.Contain("MaxLength = 32"));
            Assert.That(source, Does.Contain("TextChanged += "));
            Assert.That(source, Does.Contain("PasswordChanged += "));
        }

        [Test]
        public void TextBlockWrappingAndTrimming()
        {
            string source = compiles(@"
  <StackPanel Width=""200"">
    <TextBlock Text=""{Bind Name}"" TextWrapping=""Wrap"" />
    <TextBlock Text=""A long line that should end in an ellipsis"" TextTrimming=""CharacterEllipsis"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("TextWrapping = global::OsuXaml.Controls.TextWrapping.Wrap"));
            Assert.That(source, Does.Contain("TextTrimming = global::OsuXaml.Controls.TextTrimming.CharacterEllipsis"));
        }

        [Test]
        public void CalendarAndDatePicker()
        {
            string source = compiles(@"
  <StackPanel Spacing=""12"">
    <Calendar SelectedDate=""{Binding Birthday}"" DisplayDate=""2026-10-01"" DisplayDateStart=""2026-01-01"" DisplayDateEnd=""2027-12-31""
              FirstDayOfWeek=""Monday"" IsTodayHighlighted=""True"" DisplayMode=""Month"" SelectedDatesChanged=""{Bind DateChanged}"" />
    <DatePicker SelectedDate=""{Binding Due}"" SelectedDateFormat=""Long"" PlaceholderText=""Pick a day"" Width=""220""
                IsDropDownOpen=""{Bind PickerOpen}"" SelectedDateChanged=""{Bind DateChanged}"" />
    <DatePicker DateFormat=""yyyy-MM-dd"" SelectedDate=""2026-12-24"" HorizontalAlignment=""Left"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e1.SelectedDate, dataContext.Birthday)"));
            Assert.That(source, Does.Contain("DisplayDate.Value = new global::System.DateTime(2026, 10, 1, 0, 0, 0)"));
            Assert.That(source, Does.Contain("DisplayDateStart = new global::System.DateTime(2026, 1, 1, 0, 0, 0)"));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e2.IsDropDownOpen, dataContext.PickerOpen)"));
            Assert.That(source, Does.Contain("SelectedDate.Value = new global::System.DateTime(2026, 12, 24, 0, 0, 0)"));
            Assert.That(source, Does.Contain("SelectedDatesChanged += "));
        }

        [Test]
        public void SliderProgressBarAndColorPicker()
        {
            string source = compiles(@"
  <StackPanel Orientation=""Horizontal"" Height=""160"" Spacing=""12"">
    <Slider Minimum=""0"" Maximum=""10"" Value=""{Binding Volume}"" TickFrequency=""0.5"" IsSnapToTickEnabled=""True"" TickPlacement=""BottomRight""
            Orientation=""Vertical"" SmallChange=""0.5"" LargeChange=""2"" ValueChanged=""{Bind VolumeChanged}"" />
    <ProgressBar Orientation=""Vertical"" Value=""{Bind Progress}"" Maximum=""1"" IsIndeterminate=""{Bind Busy}"" />
    <ColorPicker SelectedColor=""{Binding Tint}"" />
    <ColorPicker SelectedColor=""#89b4fa"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e1.Value, dataContext.Volume)"));
            Assert.That(source, Does.Contain("IsSnapToTickEnabled = true"));
            Assert.That(source, Does.Contain("Orientation = global::OsuXaml.Controls.Orientation.Vertical"));
            Assert.That(source, Does.Contain("ValueChanged += "));
            Assert.That(source, Does.Contain(".IsIndeterminate = "));
            Assert.That(source, Does.Contain("XamlBinding.TwoWay(e3.SelectedColor, dataContext.Tint)"));
            Assert.That(source, Does.Contain("SelectedColor.Value = global::osu.Framework.Graphics.Colour4.FromHex(\"89b4fa\")"));
        }

        [Test]
        public void ScrollViewerAxesAndContent()
        {
            string source = compiles(@"
  <StackPanel>
    <ScrollViewer Height=""120"" HorizontalScrollBarVisibility=""Auto"" VerticalScrollBarVisibility=""Disabled"" ScrollChanged=""OnScrolled"">
      <StackPanel Orientation=""Horizontal"" Spacing=""8"">
        <Border Width=""300"" Background=""#313244"" />
        <Border Width=""300"" Background=""#45475a"" />
      </StackPanel>
    </ScrollViewer>
    <ScrollViewer Height=""200"" HorizontalScrollBarVisibility=""Auto"" VerticalScrollBarVisibility=""Auto"">
      <ScrollViewer.Child>
        <Grid Width=""800"" Height=""600"" />
      </ScrollViewer.Child>
    </ScrollViewer>
  </StackPanel>");

            Assert.That(source, Does.Contain("HorizontalScrollBarVisibility = global::OsuXaml.Controls.ScrollBarVisibility.Auto"));
            Assert.That(source, Does.Contain("VerticalScrollBarVisibility = global::OsuXaml.Controls.ScrollBarVisibility.Disabled"));
            Assert.That(source, Does.Contain(".Child = e2;"));
            Assert.That(source, Does.Contain("ScrollChanged += "));
        }

        [Test]
        public void ControlsInsideItemTemplatesAndStyles()
        {
            string source = compiles(@"
  <StackPanel>
    <StackPanel.Resources>
      <Style TargetType=""Slider"">
        <Setter Property=""TickPlacement"" Value=""Both"" />
        <Setter Property=""Maximum"" Value=""1"" />
      </Style>
    </StackPanel.Resources>
    <ListBox ItemsSource=""{Bind Tasks}"">
      <ListBox.ItemTemplate>
        <DataTemplate>
          <StackPanel Orientation=""Horizontal"" Spacing=""8"">
            <TextBlock Text=""{Bind Title}"" Width=""120"" TextTrimming=""CharacterEllipsis"" />
            <DatePicker SelectedDate=""{Bind Due}"" Width=""160"" />
            <Slider Value=""{Bind Done}"" Width=""100"" />
          </StackPanel>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </StackPanel>");

            Assert.That(source, Does.Contain("new global::OsuXaml.DataTemplate<global::Demo.TaskItem>("));
            Assert.That(source, Does.Match(@"XamlBinding\.TwoWay\(e\d+\.SelectedDate, item\d*\.Due\)"), "two-way into each item");
            Assert.That(source, Does.Contain("TickPlacement = global::OsuXaml.Controls.TickPlacement.Both"), "implicit style applies");
        }

        [Test]
        public void MistakesAreBuildErrors()
        {
            Assert.That(error(@"<Slider Orientation=""Diagonal"" />").GetMessage(), Does.Contain("Horizontal"));
            Assert.That(error(@"<DatePicker SelectedDateFormat=""Medium"" />").GetMessage(), Does.Contain("Short"));
            Assert.That(error(@"<ScrollViewer VerticalScrollBarVisibility=""Sometimes"" />").GetMessage(), Does.Contain("Auto"));
            Assert.That(error(@"<Calendar SelectedDate=""{Binding Name}"" />").GetMessage(), Does.Contain("DateTime"));
            Assert.That(error(@"<Calendar DisplayDate=""not a date"" />").GetMessage(), Does.Contain("DateTime"));
        }
    }
}
