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
            AddStep("reset", () =>
            {
                viewModel.CurrentTab.Value = 0;
                viewModel.ShowStatusBar.Value = true;
            });
            AddStep("create window", () => Child = new MainWindow());
            AddUntilStep("loaded", () => this.ChildrenOfType<MainWindow>().SingleOrDefault()?.IsLoaded == true);
        }

        private TextBox layoutNameBox => this.ChildrenOfType<LayoutPage>().Single().ChildrenOfType<TextBox>().Single();

        [Test]
        public void TextBoxShowsAndWritesBoundValue()
        {
            AddUntilStep("shows initial name", () => ((BasicTextBox)layoutNameBox).Text == viewModel.Name.Value);
            AddStep("edit text box", () => layoutNameBox.Text.Value = "Claude");
            AddAssert("greeting follows", () => viewModel.Greeting.Value == "Hello, Claude!");
        }

        [Test]
        public void TabsFollowTheViewModel()
        {
            AddAssert("pages created on first visit", () => !this.ChildrenOfType<InputsPage>().Any());
            AddStep("select Inputs", () => viewModel.CurrentTab.Value = 2);
            AddUntilStep("Inputs page shown", () => this.ChildrenOfType<InputsPage>().SingleOrDefault() is InputsPage page && isShown(page));
            AddAssert("Layout page hidden", () => !isShown(this.ChildrenOfType<LayoutPage>().Single()));
            AddAssert("both inputs bind the same name", () => this.ChildrenOfType<InputsPage>().Single().ChildrenOfType<TextBox>().First().Text.Value == viewModel.Name.Value);
        }

        private static bool isShown(Drawable drawable)
        {
            for (var d = drawable; d != null; d = d.Parent)
            {
                if (!d.IsPresent)
                    return false;
            }

            return true;
        }

        [Test]
        public void StatusBarFollowsTheViewMenu()
        {
            AddAssert("status bar visible", () => this.ChildrenOfType<StatusBar>().Single().IsPresent);
            AddStep("uncheck View > Status bar", () => viewModel.ShowStatusBar.Value = false);
            AddUntilStep("status bar hidden", () => !this.ChildrenOfType<StatusBar>().Single().IsPresent);
        }
    }
}
