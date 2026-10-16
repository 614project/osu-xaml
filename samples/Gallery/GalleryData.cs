using osu.Framework.Bindables;

namespace Gallery
{
    /// <summary>A row of the people table on the Lists page.</summary>
    public class Person
    {
        public Bindable<string> Name { get; } = new Bindable<string>();

        public BindableInt Age { get; } = new BindableInt();

        public string Role { get; set; }

        public BindableBool Active { get; } = new BindableBool();

        public Person(string name, int age, string role, bool active)
        {
            Name.Value = name;
            Age.Value = age;
            Role = role;
            Active.Value = active;
        }

        public override string ToString() => Name.Value;
    }

    /// <summary>A folder or file in the TreeView on the Lists page.</summary>
    public class FileNode
    {
        public string Name { get; set; }

        public BindableList<FileNode> Children { get; } = new BindableList<FileNode>();

        public BindableBool IsExpanded { get; } = new BindableBool();

        public FileNode(string name, params FileNode[] children)
        {
            Name = name;
            Children.AddRange(children);
        }

        public override string ToString() => Name;
    }
}
