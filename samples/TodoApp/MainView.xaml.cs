using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;

namespace TodoApp
{
    public partial class MainView : CompositeDrawable
    {
        [BackgroundDependencyLoader]
        private void load(TodoViewModel viewModel) => InitializeComponent(viewModel);

        // Converters are plain methods; the generator checks their signatures at build time.
        private static float DoneToAlpha(bool done) => done ? 0.35f : 1f;

        private static float VisibleWhen(bool visible) => visible ? 1f : 0f;
    }
}
