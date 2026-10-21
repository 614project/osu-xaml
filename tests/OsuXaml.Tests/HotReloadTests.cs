using System;
using System.Linq;
using Gallery;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using OsuXaml.Controls;
using OsuXaml.Tests.Views;
using TodoApp;
using Menu = OsuXaml.Controls.Menu;

namespace OsuXaml.Tests
{
    /// <summary>
    /// Drives <see cref="XamlHotReload.UpdateApplication"/> the way .NET hot reload does after a XAML edit, and checks that
    /// live views rebuild in place: one copy of everything, new drawables, and bindings that still reach the same view model.
    /// </summary>
    [HeadlessTest]
    public partial class HotReloadTests : TestScene
    {
        [Cached]
        private readonly TodoViewModel todo = new TodoViewModel();

        [Cached]
        private readonly GalleryViewModel gallery = new GalleryViewModel();

        [Cached]
        private readonly ReloadViewModel reload = new ReloadViewModel();

        private int reloaded;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            XamlHotReload.IsEnabled = true;
            XamlHotReload.ViewReloaded += countReload;
            reloaded = 0;
        });

        [TearDown]
        public void TearDown()
        {
            XamlHotReload.IsEnabled = false;
            XamlHotReload.ViewReloaded -= countReload;
        }

        private void countReload(Drawable view) => reloaded++;

        [Test]
        public void TodoViewRebuildsInPlace()
        {
            MainView view = null;
            BasicTextBox oldInput = null;

            AddStep("create view", () =>
            {
                todo.Items.Clear();
                todo.NewText.Value = string.Empty;
                todo.Seed("one", "two");
                Child = view = new MainView();
            });
            AddUntilStep("loaded", () => view.IsLoaded);
            AddStep("remember input", () => oldInput = view.ChildrenOfType<BasicTextBox>().Single());

            AddStep("hot reload MainView", () => XamlHotReload.UpdateApplication(new[] { typeof(MainView) }));
            AddUntilStep("rebuilt", () => reloaded == 1);

            AddAssert("same view instance", () => Child == view);
            AddAssert("one new input", () => view.ChildrenOfType<BasicTextBox>().Single() != oldInput);
            AddAssert("old input removed", () => oldInput.Parent == null);
            AddAssert("rows not duplicated", () => view.ChildrenOfType<BasicCheckbox>().Count() == 2);
            AddAssert("count text shown once", () => texts(view, "2 items left") == 1);

            AddStep("add item through view model", () =>
            {
                todo.NewText.Value = "three";
                todo.AddItem();
            });
            AddAssert("one row added", () => view.ChildrenOfType<BasicCheckbox>().Count() == 3);
            AddAssert("count follows", () => texts(view, "3 items left") == 1);

            AddStep("type into the new input", () => view.ChildrenOfType<BasicTextBox>().Single().Current.Value = "typed");
            AddAssert("two-way binding reaches the view model", () => todo.NewText.Value == "typed");
            AddStep("write to the old input", () => oldInput.Current.Value = "stale");
            AddAssert("old input is disconnected", () => todo.NewText.Value == "typed");

            AddStep("hot reload again", () => XamlHotReload.UpdateApplication(new[] { typeof(MainView) }));
            AddUntilStep("rebuilt twice", () => reloaded == 2);
            AddAssert("still one copy", () => view.ChildrenOfType<BasicTextBox>().Count() == 1 && view.ChildrenOfType<BasicCheckbox>().Count() == 3);
        }

        [Test]
        public void OtherTypesDontRebuildTheView()
        {
            MainView view = null;
            BasicTextBox input = null;

            AddStep("create view", () => Child = view = new MainView());
            AddUntilStep("loaded", () => view.IsLoaded);
            AddStep("remember input", () => input = view.ChildrenOfType<BasicTextBox>().Single());

            AddStep("update an unrelated type", () => XamlHotReload.UpdateApplication(new[] { typeof(TodoViewModel) }));
            AddWaitStep("let scheduled work run", 2);
            AddAssert("not rebuilt", () => reloaded == 0 && view.ChildrenOfType<BasicTextBox>().Single() == input);

            AddStep("update with unknown types", () => XamlHotReload.UpdateApplication(null));
            AddUntilStep("rebuilt", () => reloaded == 1 && view.ChildrenOfType<BasicTextBox>().Single() != input);
        }

        [Test]
        public void ViewsBuiltWithoutHotReloadAreLeftAlone()
        {
            MainView view = null;
            BasicTextBox input = null;

            AddStep("create view without hot reload", () =>
            {
                XamlHotReload.IsEnabled = false;
                Child = view = new MainView();
            });
            AddUntilStep("loaded", () => view.IsLoaded);
            AddStep("remember input", () => input = view.ChildrenOfType<BasicTextBox>().Single());

            AddStep("hot reload MainView", () => XamlHotReload.UpdateApplication(new[] { typeof(MainView) }));
            AddWaitStep("let scheduled work run", 2);
            AddAssert("not rebuilt", () => reloaded == 0 && view.ChildrenOfType<BasicTextBox>().Single() == input);
        }

        [Test]
        public void WindowContentIsReplaced()
        {
            MainWindow window = null;
            Menu oldMenu = null;

            AddStep("create window", () =>
            {
                gallery.CurrentTab.Value = 0;
                Child = window = new MainWindow();
            });
            AddUntilStep("loaded", () => window.IsLoaded);
            AddUntilStep("layout page shown", () => window.ChildrenOfType<LayoutPage>().Any());
            AddStep("remember menu", () => oldMenu = window.ChildrenOfType<Menu>().Single());

            AddStep("hot reload MainWindow", () => XamlHotReload.UpdateApplication(new[] { typeof(MainWindow) }));
            AddUntilStep("rebuilt", () => reloaded == 1);
            AddAssert("one new menu", () => window.ChildrenOfType<Menu>().Single() != oldMenu);
            AddAssert("old content removed", () => oldMenu.Parent == null);
            AddUntilStep("one layout page", () => window.ChildrenOfType<LayoutPage>().Count() == 1);

            AddStep("switch tab in the view model", () => gallery.CurrentTab.Value = 2);
            AddUntilStep("tabs still bound", () => window.ChildrenOfType<InputsPage>().Any());
        }

        [Test]
        public void ItemsOnTheRootAreRebuiltNotDuplicated()
        {
            ReloadListView view = null;

            AddStep("create view", () =>
            {
                reload.Items.Clear();
                reload.Items.AddRange(new[] { "a", "b" });
                Child = view = new ReloadListView();
            });
            AddUntilStep("loaded", () => view.IsLoaded);
            AddAssert("two rows", () => view.ChildrenOfType<SpriteText>().Count() == 2);

            AddStep("hot reload", () => XamlHotReload.UpdateApplication(new[] { typeof(ReloadListView) }));
            AddUntilStep("rebuilt", () => view.Reloads == 1);
            AddAssert("still two rows", () => view.ChildrenOfType<SpriteText>().Select(s => s.Text.ToString()).SequenceEqual(new[] { "a", "b" }));

            AddStep("add an item", () => reload.Items.Add("c"));
            AddAssert("rows follow the list", () => view.ChildrenOfType<SpriteText>().Count() == 3);
        }

        [Test]
        public void TwoWayBindingOnTheRootSurvivesARebuild()
        {
            ReloadCheckbox view = null;

            AddStep("create view", () =>
            {
                reload.Flag.Value = false;
                Child = view = new ReloadCheckbox();
            });
            AddUntilStep("loaded", () => view.IsLoaded);

            AddStep("hot reload", () => XamlHotReload.UpdateApplication(new[] { typeof(ReloadCheckbox) }));
            AddUntilStep("rebuilt", () => reloaded == 1);

            AddStep("tick", () => view.Current.Value = true);
            AddAssert("view model follows", () => reload.Flag.Value);
            AddStep("change view model", () => reload.Flag.Value = false);
            AddAssert("checkbox follows", () => !view.Current.Value);
            AddStep("change title", () => reload.Title.Value = "renamed");
            AddAssert("one-way binding on the root still works", () => view.LabelText.ToString() == "renamed");
        }

        [Test]
        public void RootListLinkSurvivesRebuild()
        {
            ReloadCalendar view = null;
            var day = new DateTime(2026, 3, 14);

            AddStep("create view", () =>
            {
                reload.Days.Clear();
                Child = view = new ReloadCalendar();
            });
            AddUntilStep("loaded", () => view.IsLoaded);

            AddStep("hot reload", () => XamlHotReload.UpdateApplication(new[] { typeof(ReloadCalendar) }));
            AddUntilStep("rebuilt", () => reloaded == 1);

            AddStep("select through the view model", () => reload.Days.Add(day));
            AddAssert("calendar follows", () => view.SelectedDates.SequenceEqual(new[] { day }) && view.SelectedDate.Value == day);
            AddStep("clear through the calendar", () => view.SelectedDate.Value = null);
            AddAssert("view model follows", () => reload.Days.Count == 0);
        }

        [Test]
        public void RootNullableLinkSurvivesRebuild()
        {
            ReloadThreeStateCheckBox view = null;

            AddStep("create view", () =>
            {
                reload.Flag.Value = false;
                Child = view = new ReloadThreeStateCheckBox();
            });
            AddUntilStep("loaded", () => view.IsLoaded);

            AddStep("hot reload", () => XamlHotReload.UpdateApplication(new[] { typeof(ReloadThreeStateCheckBox) }));
            AddUntilStep("rebuilt", () => reloaded == 1);

            AddStep("tick", () => view.Toggle());
            AddAssert("view model follows", () => reload.Flag.Value);
            AddStep("change view model", () => reload.Flag.Value = false);
            AddAssert("check box follows", () => view.IsChecked.Value == false);
        }

        private static int texts(Drawable root, string text) => root.ChildrenOfType<SpriteText>().Count(s => s.Text.ToString() == text);
    }
}
