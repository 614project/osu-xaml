using osu.Framework;
using osu.Framework.Allocation;

namespace TodoApp
{
    public partial class TodoGame : Game
    {
        [Cached]
        private readonly TodoViewModel viewModel = new TodoViewModel();

        [BackgroundDependencyLoader]
        private void load()
        {
            viewModel.Seed("Try osu!framework", "Write a XAML compiler", "Bind it to Bindables");
            viewModel.Items[0].Done.Value = true;

            Add(new MainView());
        }
    }
}
