using System;
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
        public BindableList<DateTime> Days { get; } = new BindableList<DateTime>();
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

    /// <summary>A view whose root links its own list two-way (Calendar.SelectedDates), which can't be linked twice either.</summary>
    public partial class ReloadCalendar : OsuXaml.Controls.Calendar
    {
        [BackgroundDependencyLoader]
        private void load(ReloadViewModel viewModel) => InitializeComponent(viewModel);
    }

    /// <summary>A view whose root binds a bool? (CheckBox.IsChecked) to a view model's bool.</summary>
    public partial class ReloadThreeStateCheckBox : OsuXaml.Controls.CheckBox
    {
        [BackgroundDependencyLoader]
        private void load(ReloadViewModel viewModel) => InitializeComponent(viewModel);
    }
}
