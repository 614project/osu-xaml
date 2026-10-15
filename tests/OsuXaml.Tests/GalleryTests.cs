using System.Linq;
using Gallery;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using OsuXaml.Controls;
using TextBox = OsuXaml.Controls.TextBox;

namespace OsuXaml.Tests
{
    [HeadlessTest]
    public partial class GalleryTests : TestScene
    {
        [Cached]
        private readonly GalleryViewModel viewModel = new GalleryViewModel();

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create window", () => Child = new MainWindow());
            AddUntilStep("loaded", () => this.ChildrenOfType<MainWindow>().SingleOrDefault()?.IsLoaded == true);
        }

        [Test]
        public void TextBoxShowsAndWritesBoundValue()
        {
            AddAssert("shows initial name", () => ((BasicTextBox)this.ChildrenOfType<TextBox>().Single()).Text == viewModel.Name.Value);
            AddStep("edit text box", () => this.ChildrenOfType<TextBox>().Single().Text.Value = "Claude");
            AddAssert("greeting follows", () => viewModel.Greeting.Value == "Hello, Claude!");
        }
    }
}
