using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using TodoApp;

namespace OsuXaml.Tests
{
    /// <summary>
    /// Loads the XAML-built MainView inside a headless osu!framework host and drives it through the view model,
    /// checking that bindings, item templates and list removal update the real drawable tree.
    /// </summary>
    [HeadlessTest]
    public partial class TodoAppTests : TestScene
    {
        [Cached]
        private readonly TodoViewModel viewModel = new TodoViewModel();

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create view", () =>
            {
                viewModel.Items.Clear();
                viewModel.NewText.Value = string.Empty;
                viewModel.Seed("one", "two");
                Child = new MainView();
            });
            AddUntilStep("view loaded", () => this.ChildrenOfType<MainView>().SingleOrDefault()?.IsLoaded == true);
        }

        [Test]
        public void AddingAnItemCreatesARowAndUpdatesTheCount()
        {
            AddAssert("two rows", () => this.ChildrenOfType<BasicCheckbox>().Count() == 2);
            AddAssert("count text", () => hasText("2 items left"));

            AddStep("type and add", () =>
            {
                viewModel.NewText.Value = "three";
                viewModel.AddItem();
            });

            AddAssert("three rows", () => this.ChildrenOfType<BasicCheckbox>().Count() == 3);
            AddAssert("new row text", () => hasText("three"));
            AddAssert("text box cleared through two-way binding", () => this.ChildrenOfType<BasicTextBox>().Single().Current.Value == string.Empty);
        }

        [Test]
        public void CheckboxWritesBackToTheViewModel()
        {
            AddStep("tick first checkbox", () => this.ChildrenOfType<BasicCheckbox>().First().Current.Value = true);
            AddAssert("item marked done", () => viewModel.Items[0].Done.Value);
            AddAssert("count text", () => hasText("1 items left"));
        }

        [Test]
        public void ClearCompletedRemovesRows()
        {
            AddStep("mark second done", () => viewModel.Items[1].Done.Value = true);
            AddStep("clear completed", () => viewModel.ClearCompleted());
            AddAssert("one row left", () => this.ChildrenOfType<BasicCheckbox>().Count() == 1);
            AddAssert("remaining row is first item", () => hasText("one") && !hasText("two"));
        }

        private bool hasText(string text) => this.ChildrenOfType<SpriteText>().Any(s => s.Text.ToString() == text && s.IsPresent);
    }
}
