using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>ComboBox behaviour with real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class ComboBoxTests : ManualInputManagerTestScene
    {
        private sealed class Fruit
        {
            public int Id;
            public string Name;
            public override string ToString() => Name;
        }

        private static readonly DataTemplate<Fruit> fruit_template = new DataTemplate<Fruit>((f, _) => new TextBlock { Text = f.Name });

        private static readonly ItemBinding<Fruit, int> id_path = new ItemBinding<Fruit, int>("Id", f => f.Id, f => f.Id);

        private Window window;
        private ComboBox combo;
        private BindableList<Fruit> fruits;
        private BindingScope scope;

        private void create(Action<ComboBox> setup, float width = 240)
        {
            AddStep("create", () =>
            {
                scope = new BindingScope();
                fruits = new BindableList<Fruit>(new[]
                {
                    new Fruit { Id = 1, Name = "Apple" },
                    new Fruit { Id = 2, Name = "Banana" },
                    new Fruit { Id = 3, Name = "Cherry" },
                });

                combo = new ComboBox { Width = width };
                setup(combo);

                var panel = new StackPanel { Padding = new MarginPadding(40), Spacing = 10 };
                panel.Add(combo);
                panel.Add(new TextBlock { Text = "below" });

                Child = window = new Window { Child = panel };
            });

            AddUntilStep("loaded", () => combo.IsLoaded && combo.DrawWidth > 0);
        }

        private void createFruits(Action<ComboBox> extra = null) => create(c =>
        {
            c.ItemTemplate = fruit_template;
            c.ItemsSource = XamlBinding.ListCopy(scope, fruits);
            extra?.Invoke(c);
        });

        private string selectionText => (combo.ChildrenOfType<ContentPresenter>().Single().PresentedContent as TextBlock)?.Text.ToString();

        private Drawable item(int index) => combo.ContainerFromIndex(index);

        private void waitForList() => AddUntilStep("list showing", () => item(0)?.IsLoaded == true && item(0).IsPresent && item(0).DrawHeight > 0 && window.PopupLayer.OpenPopups.Count == 1);

        [Test]
        public void SelectionBindsTwoWayAndShowsInTheBox()
        {
            var selected = new Bindable<Fruit>();
            var selectedId = new BindableInt();

            createFruits(c =>
            {
                c.SelectedValuePath = id_path;
                XamlBinding.TwoWayObject(scope, c.SelectedItem, selected);
                XamlBinding.TwoWayObject(scope, c.SelectedValue, selectedId);
            });

            AddAssert("nothing selected", () => combo.SelectedIndex.Value == -1 && selectionText == null);

            AddStep("view model selects Banana", () => selected.Value = fruits[1]);
            AddAssert("index, text and value follow", () => combo.SelectedIndex.Value == 1 && combo.Text.Value == "Banana" && selectedId.Value == 2);
            AddUntilStep("box shows Banana through the template", () => selectionText == "Banana");

            AddStep("view model selects id 3", () => selectedId.Value = 3);
            AddAssert("Cherry selected", () => selected.Value == fruits[2] && selectionText == "Cherry");

            AddStep("set Text", () => combo.Text.Value = "apple");
            AddAssert("matching item selected", () => combo.SelectedIndex.Value == 0 && selected.Value == fruits[0]);

            AddStep("remove selected item", () => fruits.RemoveAt(0));
            AddAssert("selection cleared", () => combo.SelectedItem.Value == null && selected.Value == null && selectionText == null);
        }

        [Test]
        public void ValueBoundBeforeItemsSelectsOnLoad()
        {
            var selectedId = new BindableInt(2);

            createFruits(c =>
            {
                XamlBinding.TwoWayObject(scope, c.SelectedValue, selectedId);
                c.SelectedValuePath = id_path;
            });

            AddAssert("Banana selected", () => combo.SelectedItem.Value == fruits[1] && selectedId.Value == 2);
            AddUntilStep("shown", () => selectionText == "Banana");
        }

        [Test]
        public void ClickOpensListBelowAndPickingAnItemSelectsAndCloses()
        {
            createFruits();

            AddStep("click the box", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("open", () => combo.IsDropDownOpen.Value);
            waitForList();
            AddUntilStep("list is below the box and as wide", () =>
            {
                var list = window.PopupLayer.OpenPopups[0].Child.ScreenSpaceDrawQuad;
                var box = combo.ScreenSpaceDrawQuad;
                return list.TopLeft.Y >= box.BottomLeft.Y - 1 && Math.Abs(list.Width - box.Width) < 1;
            });

            AddStep("hover Cherry", () => InputManager.MoveMouseTo(item(2)));
            AddAssert("Cherry highlighted", () => combo.HighlightedIndex == 2 && ((ComboBoxItem)item(2)).IsHighlighted);

            AddStep("click Cherry", () => InputManager.Click(MouseButton.Left));
            AddAssert("selected and closed", () => combo.SelectedIndex.Value == 2 && !combo.IsDropDownOpen.Value && window.PopupLayer.OpenPopups.Count == 0);
            AddAssert("combo keeps focus", () => combo.HasFocus);
            AddUntilStep("box shows Cherry", () => selectionText == "Cherry");

            AddStep("open again", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
            });
            waitForList();
            AddAssert("selected item marked", () => ((ComboBoxItem)item(2)).IsSelected && !((ComboBoxItem)item(0)).IsSelected);

            AddStep("click outside", () =>
            {
                InputManager.MoveMouseTo(combo, new Vector2(0, 300));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("closed, selection kept", () => !combo.IsDropDownOpen.Value && combo.SelectedIndex.Value == 2);

            AddStep("open and click the box again", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("open", () => combo.IsDropDownOpen.Value);
            AddStep("click the box", () => InputManager.Click(MouseButton.Left));
            AddAssert("toggled closed", () => !combo.IsDropDownOpen.Value);
        }

        [Test]
        public void KeyboardChangesSelectionAndDrivesTheList()
        {
            createFruits();

            AddStep("focus by clicking, then Escape", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.Escape);
            });
            AddAssert("closed and focused", () => !combo.IsDropDownOpen.Value && combo.HasFocus);

            AddStep("Down", () => InputManager.Key(Key.Down));
            AddAssert("first item", () => combo.SelectedIndex.Value == 0);
            AddStep("Down, Down, Down", () =>
            {
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Down);
            });
            AddAssert("stops at the last item", () => combo.SelectedIndex.Value == 2);
            AddStep("Up", () => InputManager.Key(Key.Up));
            AddAssert("second item", () => combo.SelectedIndex.Value == 1);

            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("open with selection highlighted", () => combo.IsDropDownOpen.Value && combo.HighlightedIndex == 1);
            waitForList();
            AddStep("Down", () => InputManager.Key(Key.Down));
            AddAssert("highlight moves, selection doesn't", () => combo.HighlightedIndex == 2 && combo.SelectedIndex.Value == 1);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("picked and closed", () => combo.SelectedIndex.Value == 2 && !combo.IsDropDownOpen.Value);

            AddStep("Alt+Down", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.Down);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("open", () => combo.IsDropDownOpen.Value);
            AddStep("Up then Escape", () =>
            {
                InputManager.Key(Key.Up);
                InputManager.Key(Key.Escape);
            });
            AddAssert("closed without changing the selection", () => !combo.IsDropDownOpen.Value && combo.SelectedIndex.Value == 2);

            AddStep("Space", () => InputManager.Key(Key.Space));
            AddAssert("Space opens", () => combo.IsDropDownOpen.Value);
        }

        [Test]
        public void LongListScrollsWithinMaxDropDownHeight()
        {
            create(c =>
            {
                c.MaxDropDownHeight = 200;
                for (int i = 0; i < 40; i++)
                    c.Add($"Item {i}");
                c.SelectedIndex.Value = 39;
            });

            AddStep("open", () => combo.IsDropDownOpen.Value = true);
            waitForList();
            AddUntilStep("list height capped", () =>
            {
                float height = window.PopupLayer.OpenPopups[0].Child.DrawHeight;
                return height > 150 && height <= 200 + 6;
            });
            AddUntilStep("selected item scrolled into view", () =>
            {
                var list = window.PopupLayer.OpenPopups[0].Child.ScreenSpaceDrawQuad;
                var last = item(39).ScreenSpaceDrawQuad;
                return last.TopLeft.Y >= list.TopLeft.Y && last.BottomLeft.Y <= list.BottomLeft.Y + 1;
            });
        }

        [Test]
        public void ItemChildrenAndStrings()
        {
            create(c =>
            {
                c.Add(new ComboBoxItem { Content = "Small" });
                c.Add("Medium");
                c.Add(new ComboBoxItem { Content = new TextBlock { Text = "Large" } });
                c.PlaceholderText = "Size";
            });

            AddAssert("placeholder while empty", () => combo.ChildrenOfType<TextBlock>().Any(t => t.Text.ToString() == "Size" && t.Alpha > 0));
            AddStep("select Medium", () => combo.SelectedIndex.Value = 1);
            AddUntilStep("string item shown", () => selectionText == "Medium" && combo.Text.Value == "Medium");
            AddAssert("placeholder hidden", () => combo.ChildrenOfType<TextBlock>().Single(t => t.Text.ToString() == "Size").Alpha == 0);
            AddStep("select the drawable item", () => combo.SelectedIndex.Value = 2);
            AddUntilStep("its text is shown", () => selectionText == "Large" && combo.Text.Value == "Large");
            AddStep("select Small", () => combo.SelectedIndex.Value = 0);
            AddUntilStep("ComboBoxItem content shown", () => selectionText == "Small");
        }

        [Test]
        public void EditableTextSelectsMatchingItems()
        {
            create(c =>
            {
                c.IsEditable = true;
                c.Add("Apple");
                c.Add("Banana");
            });

            AddStep("type banana", () => combo.ChildrenOfType<BasicTextBox>().Single().Current.Value = "banana");
            AddAssert("Banana selected", () => (string)combo.SelectedItem.Value == "Banana" && combo.Text.Value == "banana");

            AddStep("type something else", () => combo.ChildrenOfType<BasicTextBox>().Single().Current.Value = "Cherry");
            AddAssert("selection cleared, text kept", () => combo.SelectedItem.Value == null && combo.Text.Value == "Cherry");

            AddStep("pick Apple from code", () => combo.SelectedIndex.Value = 0);
            AddAssert("text box shows it", () => combo.ChildrenOfType<BasicTextBox>().Single().Current.Value == "Apple");
        }

        [Test]
        public void SizesLikeAFormControl()
        {
            create(c => c.Add("Short"), width: 0);

            AddAssert("stretches across the panel", () => Math.Abs(combo.DrawWidth - window.DrawWidth + 80) < 1);
            AddAssert("standard height", () => combo.DrawHeight == 36);

            AddStep("free-standing combo", () => Child = combo = new ComboBox { ItemsSource = new[] { "One", "Two" } });
            AddUntilStep("sizes to content with a minimum width", () => combo.IsLoaded && combo.DrawWidth >= 120 && combo.DrawHeight == 36);
        }
    }
}
