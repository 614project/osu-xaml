using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using Visibility = OsuXaml.Controls.Visibility;

namespace OsuXaml.Tests
{
    /// <summary>The runtime side of templates: items controls, selection, content presentation and popups.</summary>
    [HeadlessTest]
    public partial class ItemsControlTests : TestScene
    {
        private sealed class Person
        {
            public string Name;
            public override string ToString() => Name;
        }

        private static readonly DataTemplate<Person> person_template = new DataTemplate<Person>((p, _) => new TextBlock { Text = p.Name });

        private static string textOf(Drawable container) => ((container as ContentControl)?.PresentedContent as TextBlock)?.Text.ToString();

        [Test]
        public void ItemsFollowAnObservableList()
        {
            var people = new BindableList<Person>();
            var scope = new BindingScope();
            ListBox list = null;

            AddStep("create", () =>
            {
                people.AddRange(new[] { new Person { Name = "Ann" }, new Person { Name = "Bob" } });
                Child = list = new ListBox
                {
                    Size = new Vector2(200, 300),
                    ItemTemplate = person_template,
                    ItemsSource = XamlBinding.ListCopy(scope, people),
                };
            });
            AddAssert("two rows", () => list.Items.Count == 2 && textOf(list.ContainerFromIndex(1)) == "Bob");

            AddStep("insert at 1", () => people.Insert(1, new Person { Name = "Cid" }));
            AddAssert("inserted in order", () => string.Join(",", Enumerable.Range(0, 3).Select(i => textOf(list.ContainerFromIndex(i)))) == "Ann,Cid,Bob");

            AddStep("move last to first", () => people.Move(2, 0));
            AddAssert("moved", () => textOf(list.ContainerFromIndex(0)) == "Bob");

            AddStep("remove all starting with A", () => people.RemoveAll(p => p.Name.StartsWith('A')));
            AddAssert("removed", () => list.Items.Count == 2 && list.Items.All(p => ((Person)p).Name != "Ann"));

            AddStep("clear", () => people.Clear());
            AddAssert("empty", () => list.Items.Count == 0);
        }

        [Test]
        public void SelectionStaysInSyncAndFollowsRemovals()
        {
            var people = new BindableList<Person>(new[] { new Person { Name = "Ann" }, new Person { Name = "Bob" }, new Person { Name = "Cid" } });
            var selected = new Bindable<Person>();
            var scope = new BindingScope();
            ListBox list = null;

            AddStep("create", () =>
            {
                list = new ListBox { Size = new Vector2(200, 300), ItemsSource = XamlBinding.ListCopy(scope, people), ItemTemplate = person_template };
                XamlBinding.TwoWayObject(scope, list.SelectedItem, selected);
                Child = list;
            });

            AddStep("select index 1", () => list.SelectedIndex.Value = 1);
            AddAssert("item and view model follow", () => list.SelectedItem.Value == people[1] && selected.Value == people[1]);
            AddAssert("container shows selection", () => ((ListBoxItem)list.ContainerFromIndex(1)).IsSelected && !((ListBoxItem)list.ContainerFromIndex(0)).IsSelected);

            AddStep("view model selects Cid", () => selected.Value = people[2]);
            AddAssert("index follows", () => list.SelectedIndex.Value == 2);

            AddStep("insert before selection", () => people.Insert(0, new Person { Name = "Zed" }));
            AddAssert("index shifts", () => list.SelectedIndex.Value == 3 && list.SelectedItem.Value == selected.Value);

            AddStep("remove selected", () => people.Remove(selected.Value));
            AddAssert("selection cleared", () => list.SelectedIndex.Value == -1 && list.SelectedItem.Value == null && selected.Value == null);
        }

        [Test]
        public void ExtendedSelectionTracksSelectedItems()
        {
            ListBox list = null;

            AddStep("create", () =>
            {
                list = new ListBox { Size = new Vector2(200, 300), SelectionMode = SelectionMode.Multiple };
                foreach (string s in new[] { "a", "b", "c" })
                    list.Add(s);
                Child = list;
            });

            AddStep("click a and c", () =>
            {
                list.SelectContainer(list.ContainerFromIndex(0));
                list.SelectContainer(list.ContainerFromIndex(2));
            });
            AddAssert("both selected", () => list.SelectedItems.SequenceEqual(new object[] { "a", "c" }));
            AddAssert("containers show it", () => ((ListBoxItem)list.ContainerFromIndex(2)).IsSelected && !((ListBoxItem)list.ContainerFromIndex(1)).IsSelected);

            AddStep("click a again", () => list.SelectContainer(list.ContainerFromIndex(0)));
            AddAssert("a toggled off", () => list.SelectedItems.SequenceEqual(new object[] { "c" }) && (string)list.SelectedItem.Value == "c");
        }

        [Test]
        public void ContentIsPresentedAsTextTemplateOrDrawable()
        {
            Border border = null;
            var box = new Container { Size = new Vector2(10) };

            AddStep("text", () => Child = border = new Border { Content = "hello" });
            AddAssert("text block", () => (border.PresentedContent as TextBlock)?.Text.ToString() == "hello");

            AddStep("templated object", () => border.ContentTemplate = person_template);
            AddStep("person", () => border.Content = new Person { Name = "Dee" });
            AddAssert("template used", () => (border.PresentedContent as TextBlock)?.Text.ToString() == "Dee");

            AddStep("drawable", () => border.Content = box);
            AddAssert("drawable shown", () => border.PresentedContent == box && box.Parent != null);

            AddStep("swap away", () => border.Content = "bye");
            AddAssert("caller's drawable is detached, not disposed", () => box.Parent == null && box.LoadState == LoadState.Loaded);
        }

        [Test]
        public void ControlTemplateReplacesTheLookAndPresentsContent()
        {
            Button button = null;

            AddStep("create", () =>
            {
                button = new Button
                {
                    Content = "Go",
                    Template = new ControlTemplate<Button>((b, _) =>
                    {
                        var presenter = new ContentPresenter();
                        var border = new Border { Background = Colour4.Red, Padding = new MarginPadding(10), Child = presenter };
                        b.AttachPresenter(presenter);
                        return border;
                    }),
                };
                Child = button;
            });

            AddAssert("content inside template", () => (button.PresentedContent as TextBlock)?.Text.ToString() == "Go"
                                                     && button.ChildrenOfType<Border>().Any(b => b.Background == Colour4.Red));
            AddStep("change content", () => button.Content = "Stop");
            AddAssert("presenter follows", () => (button.PresentedContent as TextBlock)?.Text.ToString() == "Stop");
            AddAssert("sized to template", () => button.DrawWidth > 20 && button.DrawHeight >= 20);
        }

        [Test]
        public void PopupOpensInTheWindowLayerAndClosesOnDismiss()
        {
            Window window = null;
            Popup popup = null;
            Button target = null;

            AddStep("create", () =>
            {
                popup = new Popup { Child = new Border { Background = Colour4.Blue, Size = new Vector2(80, 40) } };
                var panel = new StackPanel();
                panel.Add(target = new Button { Content = "Open" });
                panel.Add(popup);
                popup.PlacementTarget = target;
                Child = window = new Window { Child = panel };
            });

            AddStep("open", () => popup.IsOpen.Value = true);
            AddAssert("in window layer", () => window.PopupLayer.OpenPopups.Contains(popup) && popup.Child.IsPresent);
            AddUntilStep("placed below target", () =>
            {
                var popupTop = popup.Child.ScreenSpaceDrawQuad.TopLeft;
                var targetBottom = target.ScreenSpaceDrawQuad.BottomLeft;
                return Precision(popupTop.Y, targetBottom.Y) && Precision(popupTop.X, targetBottom.X);
            });

            AddStep("dismiss", () => window.PopupLayer.DismissAll());
            AddAssert("closed", () => !popup.IsOpen.Value && window.PopupLayer.OpenPopups.Count == 0);

            AddStep("stays-open popup", () =>
            {
                popup.StaysOpen = true;
                popup.IsOpen.Value = true;
                window.PopupLayer.DismissAll();
            });
            AddAssert("still open", () => popup.IsOpen.Value);

            AddStep("remove owner", () => window.Child = null);
            AddUntilStep("popup closes with its owner", () => window.PopupLayer.OpenPopups.Count == 0 && !popup.IsOpen.Value);
        }

        [Test]
        public void CollapsedChildrenTakeNoSpace()
        {
            StackPanel panel = null;
            Container a = null, b = null;

            AddStep("create", () =>
            {
                panel = new StackPanel { Width = 100, Spacing = 10 };
                panel.Add(a = new Container { Height = 20 });
                panel.Add(b = new Container { Height = 30 });
                Child = panel;
            });
            AddAssert("both", () => panel.DrawHeight == 60);
            AddStep("collapse a", () => Layout.SetVisibility(a, Visibility.Collapsed));
            AddAssert("only b, no spacing", () => panel.DrawHeight == 30 && a.Alpha == 0);
            AddStep("hide b", () =>
            {
                Layout.SetVisibility(a, Visibility.Visible);
                Layout.SetVisibility(b, Visibility.Hidden);
            });
            AddAssert("hidden keeps space", () => panel.DrawHeight == 60 && b.Alpha == 0);
        }

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 1;
    }

    [TestFixture]
    public class ItemBindingTests
    {
        private sealed class Todo
        {
            public string Title = "a";
            public readonly BindableBool Done = new BindableBool();
        }

        [Test]
        public void ObservesBindableMembersAndReadsPlainOnes()
        {
            var todo = new Todo();
            var scope = new BindingScope();
            var done = new ItemBinding<Todo, bool>("Done", t => t.Done, t => t.Done.Value);
            var title = new ItemBinding<Todo, string>("Title", t => t.Title, t => t.Title);

            var seen = new System.Collections.Generic.List<object>();
            done.Observe(todo, scope, v => seen.Add(v));
            todo.Done.Value = true;

            Assert.That(seen, Is.EqualTo(new object[] { false, true }));
            Assert.That(done.GetSource(todo), Is.SameAs(todo.Done));
            Assert.That(title.GetValue(todo), Is.EqualTo("a"));
            Assert.That(done.ValueType, Is.EqualTo(typeof(bool)));

            scope.UnbindAll();
            todo.Done.Value = false;
            Assert.That(seen, Has.Count.EqualTo(2), "unbound with its scope");
        }
    }
}
