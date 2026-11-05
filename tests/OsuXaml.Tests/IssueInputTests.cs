using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Regression tests for reported issues that need pointer or keyboard input, one region per issue.</summary>
    [HeadlessTest]
    public partial class IssueInputTests : ManualInputManagerTestScene
    {
        private void click(Drawable drawable)
        {
            InputManager.MoveMouseTo(drawable);
            InputManager.Click(MouseButton.Left);
        }

        #region #19: selections bound to locked bindables

        private sealed class Person
        {
            public string Name;
            public override string ToString() => Name;
        }

        private static readonly Person ann = new Person { Name = "Ann" }, bob = new Person { Name = "Bob" }, cid = new Person { Name = "Cid" };

        [Test]
        public void ALockedSelectedItemLocksTheListBox()
        {
            var selected = new Bindable<Person>(ann);
            ListBox list = null;

            AddStep("create, then lock the view model's selection", () =>
            {
                list = new ListBox { Size = new Vector2(200, 200), ItemsSource = new[] { ann, bob, cid } };
                XamlBinding.TwoWayObject(new BindingScope(), list.SelectedItem, selected);
                selected.Disabled = true;
                Child = list;
            });
            AddUntilStep("loaded", () => list.ContainerFromIndex(2) != null);
            AddAssert("the lock reaches the control", () => list.SelectedItem.Disabled);

            AddStep("click another item", () => click(list.ContainerFromIndex(1)));
            AddAssert("unchanged", () => selected.Value == ann && list.SelectedIndex.Value == 0);
            AddStep("press down", () => InputManager.Key(Key.Down));
            AddAssert("still unchanged", () => selected.Value == ann && list.SelectedIndex.Value == 0);

            AddStep("unlock", () => selected.Disabled = false);
            AddStep("click another item", () => click(list.ContainerFromIndex(1)));
            AddAssert("selects again", () => selected.Value == bob && list.SelectedIndex.Value == 1);
        }

        [Test]
        public void ALockedSelectedIndexKeepsTheItem()
        {
            var index = new BindableInt(0);
            ListBox list = null;

            AddStep("create with a locked index", () =>
            {
                list = new ListBox { Size = new Vector2(200, 200), ItemsSource = new[] { ann, bob, cid } };
                XamlBinding.TwoWay(list.SelectedIndex, index);
                index.Disabled = true;
                Child = list;
            });
            AddUntilStep("loaded", () => list.ContainerFromIndex(2) != null && list.SelectedItem.Value == ann);

            AddStep("select an item in code", () => list.Select(cid));
            AddAssert("the locked index wins", () => list.SelectedItem.Value == ann && index.Value == 0);
            AddStep("click another item", () => click(list.ContainerFromIndex(2)));
            AddAssert("unchanged", () => list.SelectedItem.Value == ann && index.Value == 0);
        }

        [Test]
        public void ALockedSelectedItemLocksTheTabControl()
        {
            var selected = new Bindable<object>();
            TabControl tabs = null;
            TabItem second = null;

            AddStep("create", () =>
            {
                tabs = new TabControl { Size = new Vector2(300, 200) };
                tabs.Add(new TabItem { Header = "One" });
                tabs.Add(second = new TabItem { Header = "Two" });
                Child = tabs;
            });
            AddUntilStep("first tab selected", () => tabs.SelectedIndex.Value == 0 && second.DrawWidth > 0);
            AddStep("lock the item", () =>
            {
                selected.Value = tabs.SelectedItem.Value;
                XamlBinding.TwoWay(tabs.SelectedItem, selected);
                selected.Disabled = true;
            });
            AddStep("click the second tab", () => click(second));
            AddAssert("unchanged", () => tabs.SelectedIndex.Value == 0);
        }

        [Test]
        public void ALockedCalendarDateIgnoresClicksAndKeys()
        {
            var date = new Bindable<DateTime?>(new DateTime(2026, 10, 16));
            Calendar calendar = null;

            AddStep("create locked", () =>
            {
                calendar = new Calendar { DisplayDate = { Value = new DateTime(2026, 10, 16) } };
                XamlBinding.TwoWay(calendar.SelectedDate, date);
                date.Disabled = true;
                Child = calendar;
            });
            AddStep("click the 20th", () => click(calendar.ChildrenOfType<CalendarDayButton>().Single(d => d.Date == new DateTime(2026, 10, 20))));
            AddAssert("unchanged", () => date.Value == new DateTime(2026, 10, 16) && calendar.SelectedDates.Single() == new DateTime(2026, 10, 16));
            AddStep("press right", () => InputManager.Key(Key.Right));
            AddAssert("still unchanged", () => date.Value == new DateTime(2026, 10, 16));
        }

        [Test]
        public void ALockedDatePickerPutsItsTextBack()
        {
            var date = new Bindable<DateTime?>(new DateTime(2026, 10, 16));
            DatePicker picker = null;
            OsuXaml.Controls.TextBox box = null;

            AddStep("create locked", () =>
            {
                picker = new DatePicker { Width = 250, DateFormat = "yyyy-MM-dd" };
                XamlBinding.TwoWay(picker.SelectedDate, date);
                date.Disabled = true;
                Child = new Window { Child = picker };
                box = picker.ChildrenOfType<OsuXaml.Controls.TextBox>().Single();
            });
            AddStep("focus the box", () => click(box));
            AddStep("type a date and press enter", () =>
            {
                box.Text.Value = "2027-01-05";
                InputManager.Key(Key.Enter);
            });
            AddAssert("unchanged, text put back", () => date.Value == new DateTime(2026, 10, 16) && picker.Text == "2026-10-16");
        }

        #endregion
    }
}
