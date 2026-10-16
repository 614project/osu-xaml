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

        public BindableList<Person> People { get; } = new BindableList<Person>(new[]
        {
            new Person("Ann", 29, "Designer", true),
            new Person("Bob", 35, "Developer", false),
            new Person("Cid", 41, "Producer", true),
            new Person("Dee", 24, "Tester", true),
            new Person("Eve", 38, "Developer", false),
        });

        public Bindable<Person> SelectedPerson { get; } = new Bindable<Person>();

        public BindableList<FileNode> Files { get; } = new BindableList<FileNode>(new[]
        {
            new FileNode("src", new FileNode("Controls", new FileNode("Grid.cs"), new FileNode("ListBox.cs"), new FileNode("Menu.cs")), new FileNode("Templates.cs")),
            new FileNode("samples", new FileNode("Gallery", new FileNode("MainWindow.xaml")), new FileNode("TodoApp")),
            new FileNode("README.md"),
        });

        public Bindable<FileNode> SelectedFile { get; } = new Bindable<FileNode>();

        public BindableList<string> Themes { get; } = new BindableList<string>(new[] { "Mocha", "Macchiato", "Frappe", "Latte" });

        public Bindable<string> SelectedTheme { get; } = new Bindable<string>("Mocha");

        public BindableBool WordWrap { get; } = new BindableBool();

        public BindableBool ShowStatusBar { get; } = new BindableBool(true);

        private int nextTag = 1;
        private int nextPerson = 1;

        public GalleryViewModel()
        {
            Name.BindValueChanged(e => Greeting.Value = $"Hello, {e.NewValue}!", true);
            Notifications.BindValueChanged(e => Status.Value = e.NewValue ? "Notifications on" : "Notifications off");
            Volume.BindValueChanged(e => Status.Value = $"Volume {e.NewValue:0}%");
            SelectedPerson.BindValueChanged(e => Status.Value = e.NewValue == null ? "No selection" : $"Selected {e.NewValue.Name.Value}");
            SelectedFile.BindValueChanged(e => Status.Value = e.NewValue == null ? "No file" : $"File {e.NewValue.Name}");
            SelectedTheme.BindValueChanged(e => Status.Value = $"Theme {e.NewValue}");
            WordWrap.BindValueChanged(e => Status.Value = e.NewValue ? "Word wrap on" : "Word wrap off");
        }

        public void AddPerson()
        {
            var person = new Person($"New person {nextPerson++}", 20 + nextPerson, "Intern", false);
            People.Add(person);
            SelectedPerson.Value = person;
        }

        public void RemovePerson(Person person)
        {
            People.Remove(person);
            Status.Value = $"Removed {person.Name.Value}";
        }

        public void RemoveSelected()
        {
            if (SelectedPerson.Value != null)
                RemovePerson(SelectedPerson.Value);
        }

        public void Command(string name) => Status.Value = name;

        public void NewFile() => Command("New file");

        public void OpenFile() => Command("Open...");

        public void SaveFile() => Command("Saved");

        public void About() => Command("osu!xaml gallery: WPF-style controls on osu!framework");

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
