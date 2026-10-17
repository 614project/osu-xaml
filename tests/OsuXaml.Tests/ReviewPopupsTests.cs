using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;
using Menu = OsuXaml.Controls.Menu;
using MenuItem = OsuXaml.Controls.MenuItem;
using TextBox = OsuXaml.Controls.TextBox;

namespace OsuXaml.Tests
{
    /// <summary>Review tests for menus, context menus, tooltips, combo boxes and popups.</summary>
    [HeadlessTest]
    public partial class ReviewPopupsTests : ManualInputManagerTestScene
    {
        private Window window;
        private Menu menu;
        private MenuItem file, open, recent, exit;
        private TextBox textBox;
        private Border area;
        private ContextMenu areaMenu;
        private MenuItem copy, paste;
        private List<string> clicks;

        [SetUpSteps]
        public void SetUpSteps()
        {
            // Built while the steps are being added, so the tests' step helpers capture these instances.
            clicks = new List<string>();

            file = new MenuItem { Header = "_File" };
            file.Add(open = new MenuItem { Header = "_Open" });
            file.Add(recent = new MenuItem { Header = "_Recent" });
            recent.Add(new MenuItem { Header = "a.txt" });
            file.Add(exit = new MenuItem { Header = "E_xit" });
            open.Click += () => clicks.Add("open");
            exit.Click += () => clicks.Add("exit");

            menu = new Menu();
            menu.Add(file);

            areaMenu = new ContextMenu();
            areaMenu.Add(copy = new MenuItem { Header = "_Copy" });
            areaMenu.Add(paste = new MenuItem { Header = "_Paste" });
            copy.Click += () => clicks.Add("copy");
            paste.Click += () => clicks.Add("paste");

            textBox = new TextBox { Width = 200 };
            area = new Border { Size = new Vector2(200, 100), Background = Theme.Surface };
            ContextMenuService.SetContextMenu(area, areaMenu);

            var panel = new StackPanel { Padding = new MarginPadding(20), Spacing = 10 };
            panel.Add(textBox);
            panel.Add(area);

            var dock = new DockPanel { RelativeSizeAxes = Axes.Both };
            DockPanel.SetDock(menu, Dock.Top);
            dock.Add(menu);
            dock.Add(panel);

            window = new Window { Child = dock };

            AddStep("create", () => Child = window);
            AddUntilStep("loaded", () => menu.IsLoaded && file.DrawWidth > 0 && textBox.DrawWidth > 0);
        }

        private void focusTextBox()
        {
            AddStep("focus the text box", () =>
            {
                InputManager.MoveMouseTo(textBox);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("text box focused", () => textBox.HasFocus);
        }

        private void rightClickArea()
        {
            AddStep("right-click the area", () =>
            {
                InputManager.MoveMouseTo(area);
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("menu showing", () => areaMenu.IsOpen.Value && copy.IsLoaded && copy.IsPresent && copy.DrawWidth > 0);
        }

        [Test]
        public void PickingFromTheMenuBarKeepsTheFocusedControl()
        {
            focusTextBox();
            AddStep("press File", () =>
            {
                InputManager.MoveMouseTo(file);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("Open showing", () => open.IsLoaded && open.IsPresent && open.DrawWidth > 0);
            AddStep("click Open", () =>
            {
                InputManager.MoveMouseTo(open);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("clicked", () => clicks.SequenceEqual(new[] { "open" }));
            AddAssert("text box still focused", () => textBox.HasFocus);
        }

        [Test]
        public void PickingFromAContextMenuKeepsTheFocusedControl()
        {
            focusTextBox();
            rightClickArea();
            AddStep("click Paste", () =>
            {
                InputManager.MoveMouseTo(paste);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("clicked", () => clicks.SequenceEqual(new[] { "paste" }));
            AddAssert("text box still focused", () => textBox.HasFocus);
        }

        [Test]
        public void ContextMenuAtTheBottomRightCornerDoesNotCoverThePointer()
        {
            Border full = null;

            AddStep("full-window element with the menu", () =>
            {
                full = new Border { RelativeSizeAxes = Axes.Both, Background = Theme.Surface };
                ContextMenuService.SetContextMenu(full, areaMenu);
                window.Child = full;
            });
            AddUntilStep("loaded", () => full.IsLoaded && full.DrawWidth > 0);
            AddStep("right-click near the bottom right corner", () =>
            {
                InputManager.MoveMouseTo(window.ScreenSpaceDrawQuad.BottomRight - new Vector2(10));
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("menu showing", () => areaMenu.IsOpen.Value && copy.IsLoaded && copy.IsPresent && copy.DrawWidth > 0);
            AddWaitStep("settle", 3);
            AddAssert("opens above and to the left of the pointer", () =>
            {
                var mouse = InputManager.CurrentState.Mouse.Position;
                var quad = window.PopupLayer.OpenPopups[0].Child.ScreenSpaceDrawQuad;
                return quad.BottomRight.X <= mouse.X + 1 && quad.BottomRight.Y <= mouse.Y + 1
                       && quad.BottomRight.X >= mouse.X - 2 && quad.BottomRight.Y >= mouse.Y - 2;
            });
        }

        private ComboBox createCombo(bool editable, BindableList<string> source = null)
        {
            var combo = new ComboBox { Width = 200, IsEditable = editable };

            if (source != null)
                combo.ItemsSource = source;
            else
            {
                combo.Add("Apple");
                combo.Add("Banana");
                combo.Add("Cherry");
            }

            var panel = new StackPanel { Padding = new MarginPadding(20), Spacing = 10 };
            panel.Add(combo);
            window.Child = panel;
            return combo;
        }

        [Test]
        public void ComboHighlightFollowsItsItemWhenTheListChanges()
        {
            ComboBox combo = null;
            var source = new BindableList<string>(new[] { "Apple", "Banana", "Cherry" });

            int highlightedCount() => Enumerable.Range(0, combo.Items.Count).Count(i => ((ComboBoxItem)combo.ContainerFromIndex(i)).IsHighlighted);

            AddStep("combo", () => combo = createCombo(false, source));
            AddUntilStep("loaded", () => combo.IsLoaded && combo.DrawWidth > 0);
            AddStep("focus and open", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("list showing", () => combo.ContainerFromIndex(0)?.IsLoaded == true);
            AddStep("highlight Banana", () => combo.HighlightContainer(combo.ContainerFromIndex(1)));
            AddStep("insert at the top", () => source.Insert(0, "Avocado"));
            AddAssert("Banana still highlighted, and only it", () => combo.HighlightedIndex == 2 && ((ComboBoxItem)combo.ContainerFromIndex(2)).IsHighlighted
                                                                  && highlightedCount() == 1);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("picks the highlighted Banana", () => (string)combo.SelectedItem.Value == "Banana");

            AddStep("open and highlight Cherry", () =>
            {
                combo.IsDropDownOpen.Value = true;
                combo.HighlightContainer(combo.ContainerFromIndex(3));
            });
            AddStep("remove Cherry", () => source.Remove("Cherry"));
            AddAssert("one item highlighted, the one the index names", () => highlightedCount() == 1 && ((ComboBoxItem)combo.ContainerFromIndex(combo.HighlightedIndex)).IsHighlighted);
        }

        [Test]
        public void PopupMovedToAnotherWindowOpensThere()
        {
            Window second = null;
            ComboBox combo = null;

            AddStep("combo in the first window", () => combo = createCombo(false));
            AddUntilStep("loaded", () => combo.IsLoaded && combo.DrawWidth > 0);
            AddStep("open and close", () =>
            {
                combo.IsDropDownOpen.Value = true;
                combo.IsDropDownOpen.Value = false;
            });
            AddStep("move the combo to a new window", () =>
            {
                ((StackPanel)window.Child).Remove(combo, false);
                var panel = new StackPanel { Padding = new MarginPadding(20) };
                panel.Add(combo);
                Child = second = new Window { Child = panel };
            });
            AddUntilStep("loaded again", () => combo.Parent != null && second.IsLoaded && combo.DrawWidth > 0);
            AddStep("open", () => combo.IsDropDownOpen.Value = true);
            AddUntilStep("opens in the new window's layer", () => second.PopupLayer.OpenPopups.Count == 1 && combo.ContainerFromIndex(0).IsPresent);
        }

        private partial class TrackedCombo : ComboBox
        {
            public bool Disposed;

            protected override void Dispose(bool isDisposing)
            {
                Disposed = true;
                base.Dispose(isDisposing);
            }
        }

        /// <summary>An item bound to a view model value, the way generated item templates are.</summary>
        private partial class TrackedItem : ComboBoxItem
        {
            public bool Disposed;
            public int Updates;

            // A field, so disposal unbinds it.
            private readonly Bindable<string> label = new Bindable<string>();

            public TrackedItem(Bindable<string> source)
            {
                label.BindTo(source);
                label.BindValueChanged(e =>
                {
                    Content = e.NewValue;
                    Updates++;
                }, true);
            }

            protected override void Dispose(bool isDisposing)
            {
                Disposed = true;
                base.Dispose(isDisposing);
            }
        }

        [Test]
        public void ClosedDropDownIsDisposedWithItsWindow()
        {
            var name = new Bindable<string>("Tracked");
            TrackedCombo combo = null;
            TrackedItem item = null;
            int updates = 0;

            AddStep("combo", () =>
            {
                combo = new TrackedCombo { Width = 200 };
                combo.Add(item = new TrackedItem(name));
                combo.Add("Other");
                var panel = new StackPanel { Padding = new MarginPadding(20) };
                panel.Add(combo);
                window.Child = panel;
            });
            AddUntilStep("loaded", () => combo.IsLoaded && combo.DrawWidth > 0);
            AddStep("open", () => combo.IsDropDownOpen.Value = true);
            AddUntilStep("showing", () => item.IsLoaded && item.IsPresent);
            AddStep("close", () => combo.IsDropDownOpen.Value = false);
            AddStep("replace the window", () => Child = new Container());
            AddUntilStep("combo disposed", () => combo.Disposed);
            AddUntilStep("its drop-down content disposed too", () => item.Disposed);
            AddStep("view model changes", () =>
            {
                updates = item.Updates;
                name.Value = "Changed";
            });
            AddAssert("the binding was cut", () => item.Updates == updates);
        }

        [Test]
        public void HidingAnOpenComboBoxClosesItsList()
        {
            ComboBox combo = null;
            StackPanel panel = null;

            AddStep("combo", () =>
            {
                combo = createCombo(false);
                panel = (StackPanel)window.Child;
            });
            AddUntilStep("loaded", () => combo.IsLoaded && combo.DrawWidth > 0);
            AddStep("click the box", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("showing", () => combo.ContainerFromIndex(0)?.IsLoaded == true && combo.IsDropDownOpen.Value);
            AddStep("collapse the combo", () => Layout.SetVisibility(combo, OsuXaml.Controls.Visibility.Collapsed));
            AddUntilStep("list closed", () => !combo.IsDropDownOpen.Value && window.PopupLayer.OpenPopups.Count == 0);

            AddStep("show it and open again", () =>
            {
                Layout.SetVisibility(combo, OsuXaml.Controls.Visibility.Visible);
                combo.IsDropDownOpen.Value = true;
            });
            AddUntilStep("showing", () => combo.ContainerFromIndex(0)?.IsPresent == true && window.PopupLayer.OpenPopups.Count == 1);
            AddStep("hide the panel around it", () => panel.Alpha = 0);
            AddUntilStep("list closed", () => !combo.IsDropDownOpen.Value && window.PopupLayer.OpenPopups.Count == 0);
        }

        [Test]
        public void TopPlacementFlipsBelowAtTheTopEdge()
        {
            Border target = null;
            Popup popup = null;
            Border content = null;

            AddStep("popup above an element at the top", () =>
            {
                target = new Border { Size = new Vector2(120, 30), Background = Theme.Surface };
                content = new Border { Size = new Vector2(100, 60), Background = Theme.Accent };
                popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Top, Child = content };

                var panel = new StackPanel();
                panel.Add(target);
                panel.Add(popup);
                window.Child = panel;
            });
            AddUntilStep("loaded", () => popup.IsLoaded && target.DrawWidth > 0);
            AddStep("open", () => popup.IsOpen.Value = true);
            AddUntilStep("showing", () => content.IsLoaded && content.DrawHeight > 0);
            AddWaitStep("settle", 3);
            AddAssert("below the element instead of over it", () => content.ScreenSpaceDrawQuad.TopLeft.Y >= target.ScreenSpaceDrawQuad.BottomLeft.Y - 1);
        }
    }
}
