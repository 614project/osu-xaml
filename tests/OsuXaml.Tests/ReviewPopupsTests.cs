using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
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
        public void MenuBarKeyboardWhileATextBoxIsFocused()
        {
            focusTextBox();
            AddStep("Alt+F", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.F);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("File open at Open", () => file.IsSubmenuOpen.Value && file.HighlightedItem == open);
            AddStep("Down, Down, Enter", () =>
            {
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Enter);
            });
            AddAssert("Exit clicked", () => clicks.SequenceEqual(new[] { "exit" }) && !file.IsSubmenuOpen.Value);
            AddAssert("text box focused again", () => textBox.HasFocus);
        }

        [Test]
        public void ContextMenuKeyboardWhileATextBoxIsFocused()
        {
            focusTextBox();
            rightClickArea();
            AddStep("Down, Down, Enter", () =>
            {
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Enter);
            });
            AddAssert("Paste clicked", () => clicks.SequenceEqual(new[] { "paste" }) && !areaMenu.IsOpen.Value);
            AddAssert("text box focused again", () => textBox.HasFocus);
        }

        [Test]
        public void ContextMenuEscapeWhileATextBoxIsFocused()
        {
            focusTextBox();
            rightClickArea();
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("menu closed", () => !areaMenu.IsOpen.Value);
            AddAssert("text box focused again", () => textBox.HasFocus);
        }

        [Test]
        public void ContextMenuArrowsDontChangeAFocusedComboBox()
        {
            ComboBox combo = null;

            AddStep("combo and area", () =>
            {
                combo = new ComboBox { Width = 200 };
                combo.Add("Apple");
                combo.Add("Banana");
                area = new Border { Size = new Vector2(200, 100), Background = Theme.Surface };
                ContextMenuService.SetContextMenu(area, areaMenu);

                var panel = new StackPanel { Padding = new MarginPadding(20), Spacing = 10 };
                panel.Add(combo);
                panel.Add(area);
                window.Child = panel;
            });
            AddUntilStep("loaded", () => combo.IsLoaded && combo.DrawWidth > 0 && area.DrawWidth > 0);
            AddStep("focus the combo", () =>
            {
                InputManager.MoveMouseTo(combo);
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.Escape);
            });
            AddAssert("combo focused, closed", () => combo.HasFocus && !combo.IsDropDownOpen.Value && combo.SelectedIndex.Value == -1);
            AddStep("right-click the area", () =>
            {
                InputManager.MoveMouseTo(area);
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("menu showing", () => areaMenu.IsOpen.Value && copy.IsLoaded && copy.IsPresent && copy.DrawWidth > 0);
            AddStep("Down", () => InputManager.Key(Key.Down));
            AddAssert("the menu moved, the combo didn't", () => areaMenu.HighlightedItem == copy && combo.SelectedIndex.Value == -1);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("combo focused again", () => !areaMenu.IsOpen.Value && combo.HasFocus);
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

        [Test]
        public void ClickingASeparatorKeepsTheMenuOnTheKeyboard()
        {
            Separator separator = null;

            AddStep("add a separator", () => areaMenu.Add(separator = new Separator()));
            focusTextBox();
            rightClickArea();
            AddUntilStep("separator showing", () => separator.IsLoaded && separator.DrawWidth > 0);
            AddStep("click the separator", () =>
            {
                InputManager.MoveMouseTo(separator);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("menu still open", () => areaMenu.IsOpen.Value);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("closed and the text box has focus again", () => !areaMenu.IsOpen.Value && textBox.HasFocus);
        }
    }
}
