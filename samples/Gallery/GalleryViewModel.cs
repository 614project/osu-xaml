using osu.Framework.Bindables;

namespace Gallery
{
    public class GalleryViewModel
    {
        public Bindable<string> Name { get; } = new Bindable<string>("osu!xaml");

        public Bindable<string> Greeting { get; } = new Bindable<string>();

        public BindableDouble Volume { get; } = new BindableDouble(65) { MinValue = 0, MaxValue = 100 };

        public BindableBool Notifications { get; } = new BindableBool(true);

        public BindableList<string> Tags { get; } = new BindableList<string>(new[] { "Grid", "StackPanel", "DockPanel", "WrapPanel" });

        public BindableInt Clicks { get; } = new BindableInt();

        public Bindable<string> Status { get; } = new Bindable<string>("Ready");

        private int nextTag = 1;

        public GalleryViewModel()
        {
            Name.BindValueChanged(e => Greeting.Value = $"Hello, {e.NewValue}!", true);
            Notifications.BindValueChanged(e => Status.Value = e.NewValue ? "Notifications on" : "Notifications off");
            Volume.BindValueChanged(e => Status.Value = $"Volume {e.NewValue:0}%");
        }

        public void AddTag()
        {
            Tags.Add($"Tag {nextTag++}");
            Status.Value = $"{Tags.Count} tags";
        }

        public void RemoveTag(string tag)
        {
            Tags.Remove(tag);
            Status.Value = $"Removed {tag}";
        }

        public void Click()
        {
            Clicks.Value++;
            Status.Value = $"Clicked {Clicks.Value} times";
        }
    }
}
