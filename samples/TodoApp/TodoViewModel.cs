using System.Linq;
using osu.Framework.Bindables;

namespace TodoApp
{
    public class TodoItem
    {
        public string Text { get; }

        public BindableBool Done { get; } = new BindableBool();

        public TodoItem(string text)
        {
            Text = text;
        }
    }

    /// <summary>
    /// A view model with no UI references: state lives in osu!framework Bindables,
    /// which the XAML binds to without reflection or INotifyPropertyChanged.
    /// </summary>
    public class TodoViewModel
    {
        public BindableList<TodoItem> Items { get; } = new BindableList<TodoItem>();

        public Bindable<string> NewText { get; } = new Bindable<string>(string.Empty);

        public BindableInt RemainingCount { get; } = new BindableInt();

        public BindableBool HasCompleted { get; } = new BindableBool();

        public TodoViewModel()
        {
            Items.BindCollectionChanged((_, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (TodoItem item in e.NewItems)
                        item.Done.BindValueChanged(_ => refresh());
                }

                refresh();
            }, true);
        }

        public void Seed(params string[] texts)
        {
            foreach (string text in texts)
                Items.Add(new TodoItem(text));
        }

        public void AddItem()
        {
            string text = NewText.Value?.Trim();

            if (string.IsNullOrEmpty(text))
                return;

            Items.Add(new TodoItem(text));
            NewText.Value = string.Empty;
        }

        public void RemoveItem(TodoItem item) => Items.Remove(item);

        public void ClearCompleted() => Items.RemoveAll(i => i.Done.Value);

        private void refresh()
        {
            RemainingCount.Value = Items.Count(i => !i.Done.Value);
            HasCompleted.Value = Items.Any(i => i.Done.Value);
        }
    }
}
