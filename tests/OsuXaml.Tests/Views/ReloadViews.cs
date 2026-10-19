using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;

namespace OsuXaml.Tests.Views
{
    public class ReloadViewModel
    {
        public BindableList<string> Items { get; } = new BindableList<string>();
        public BindableBool Flag { get; } = new BindableBool();
        public Bindable<string> Title { get; } = new Bindable<string>("title");
    }

    /// <summary>A view whose root itself has ItemsSource, so a rebuild must take the old rows down.</summary>
    public partial class ReloadListView : FillFlowContainer
    {
        public int Reloads { get; private set; }

        [BackgroundDependencyLoader]
        private void load(ReloadViewModel viewModel) => InitializeComponent(viewModel);

        partial void OnXamlReloaded() => Reloads++;
    }

    /// <summary>A view whose root has a two-way binding on its own bindable, which can't be bound twice.</summary>
    public partial class ReloadCheckbox : BasicCheckbox
    {
        [BackgroundDependencyLoader]
        private void load(ReloadViewModel viewModel) => InitializeComponent(viewModel);
    }
}
