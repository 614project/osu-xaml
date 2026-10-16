using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;

namespace OsuXaml.Tests
{
    [HeadlessTest]
    public partial class ColorPickerTests : TestScene
    {
        [Test]
        public void SelectedColorBindsTwoWay()
        {
            var tint = new Bindable<Colour4>(Colour4.White);
            ColorPicker picker = null;
            Colour4? reported = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 320 };
                panel.Add(picker = new ColorPicker());
                XamlBinding.TwoWay(picker.SelectedColor, tint);
                picker.SelectedColorChanged += c => reported = c;
                Child = panel;
            });

            AddUntilStep("sized to its parts", () => picker.IsLoaded && picker.DrawWidth == 300 && picker.DrawHeight > 100);
            AddStep("view model picks blue", () => tint.Value = Colour4.FromHex("89b4fa"));
            AddAssert("picker follows", () => picker.SelectedColor.Value == Colour4.FromHex("89b4fa") && reported == tint.Value);
            AddStep("picker changes", () => picker.SelectedColor.Value = Colour4.Red);
            AddAssert("view model follows", () => tint.Value == Colour4.Red);
        }
    }
}
