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
    }
}
