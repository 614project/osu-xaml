using System.Collections.Generic;
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
    /// <summary>Menu bars and their submenus with real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class MenuTests : ManualInputManagerTestScene
    {
        private Window window;
        private Menu menu;
        private MenuItem file, open, recent, recentA, exit, view, wrap, help, disabled;
        private List<string> clicks;
        private BindableBool wordWrap;

        [SetUpSteps]
        public void SetUpSteps()
        {
            // Built while the steps are being added, so the tests' step helpers capture these instances.
            clicks = new List<string>();
            wordWrap = new BindableBool();

            file = new MenuItem { Header = "_File" };
            file.Add(open = new MenuItem { Header = "_Open...", InputGestureText = "Ctrl+O" });
            file.Add(recent = new MenuItem { Header = "_Recent" });
            recent.Add(recentA = new MenuItem { Header = "a.txt" });
            recent.Add(new MenuItem { Header = "b.txt" });
            file.Add(disabled = new MenuItem { Header = "Disabled", IsEnabled = false });
            file.Add(new Separator());
            file.Add(exit = new MenuItem { Header = "E_xit" });

            view = new MenuItem { Header = "_View" };
            view.Add(wrap = new MenuItem { Header = "_Word wrap", IsCheckable = true });
            wrap.IsChecked.BindTo(wordWrap);

            help = new MenuItem { Header = "_Help" };

            open.Click += () => clicks.Add("open");
            recentA.Command = () => clicks.Add("a.txt");
            exit.Click += () => clicks.Add("exit");
            disabled.Click += () => clicks.Add("disabled");
            help.Click += () => clicks.Add("help");

            menu = new Menu();
            menu.Add(file);
            menu.Add(view);
            menu.Add(help);

            var dock = new DockPanel { RelativeSizeAxes = Axes.Both };
            DockPanel.SetDock(menu, Dock.Top);
            dock.Add(menu);
            dock.Add(new Border { Background = Theme.Base });

            window = new Window { Child = dock };

            AddStep("create", () => Child = window);

            AddUntilStep("loaded", () => menu.IsLoaded && file.DrawWidth > 0);
        }

        private static string headerText(MenuItem item)
            => item.ChildrenOfType<ContentPresenter>().Select(p => p.PresentedContent).OfType<TextBlock>().FirstOrDefault()?.Text.ToString();

        private void press(MenuItem item) => AddStep($"press {item.Header}", () =>
        {
            InputManager.MoveMouseTo(item);
            InputManager.Click(MouseButton.Left);
        });

        private void waitShown(MenuItem item) => AddUntilStep($"{item.Header} showing", () => item.IsLoaded && item.IsPresent && item.DrawWidth > 0 && item.Alpha > 0);

        [Test]
        public void BarLayoutAndHeaders()
        {
            AddAssert("access key markers stripped", () => headerText(file) == "File" && file.AccessKey == 'F' && exit.Header as string == "E_xit");
            AddAssert("headers in a row", () => view.ScreenSpaceDrawQuad.TopLeft.X > file.ScreenSpaceDrawQuad.TopRight.X - 1
                                               && view.ScreenSpaceDrawQuad.TopLeft.Y == file.ScreenSpaceDrawQuad.TopLeft.Y);
            AddAssert("bar sized to its items", () => menu.DrawHeight > 30 && menu.DrawHeight < 45 && menu.DrawWidth == window.DrawWidth);
            AddAssert("roles", () => file.Role == MenuItemRole.TopLevelHeader && help.Role == MenuItemRole.TopLevelItem);
        }

        [Test]
        public void PressingAHeaderOpensItsSubmenuAndClickingAnItemClosesEverything()
        {
            press(file);
            AddAssert("File open", () => file.IsSubmenuOpen.Value && menu.OpenItem == file);
            waitShown(open);
            AddAssert("submenu below the header", () => open.ScreenSpaceDrawQuad.TopLeft.Y >= file.ScreenSpaceDrawQuad.BottomLeft.Y);
            AddAssert("submenu roles and text", () => open.Role == MenuItemRole.SubmenuItem && recent.Role == MenuItemRole.SubmenuHeader && headerText(open) == "Open...");
            AddAssert("rows share the widest width", () => open.DrawWidth == exit.DrawWidth && open.DrawWidth >= 140);

            press(open);
            AddAssert("clicked and closed", () => clicks.SequenceEqual(new[] { "open" }) && !file.IsSubmenuOpen.Value && menu.OpenItem == null);
            AddAssert("no popups left", () => window.PopupLayer.OpenPopups.Count == 0);

            press(help);
            AddAssert("top-level item clicks directly", () => clicks.Last() == "help" && window.PopupLayer.OpenPopups.Count == 0);
        }

        [Test]
        public void HoveringAnotherHeaderSwitchesMenus()
        {
            AddStep("hover View first", () => InputManager.MoveMouseTo(view));
            AddAssert("nothing opens on hover alone", () => !view.IsSubmenuOpen.Value && view.IsHighlighted);

            press(file);
            AddStep("hover View", () => InputManager.MoveMouseTo(view));
            AddAssert("View open, File closed", () => view.IsSubmenuOpen.Value && !file.IsSubmenuOpen.Value && menu.OpenItem == view);
            AddAssert("one popup", () => window.PopupLayer.OpenPopups.Count == 1);

            AddStep("press View again", () => InputManager.Click(MouseButton.Left));
            AddAssert("closed", () => !view.IsSubmenuOpen.Value && window.PopupLayer.OpenPopups.Count == 0);
        }

        [Test]
        public void NestedSubmenuOpensOnHoverToTheRight()
        {
            press(file);
            waitShown(recent);
            AddStep("hover Recent", () => InputManager.MoveMouseTo(recent));
            AddUntilStep("Recent opens after a moment", () => recent.IsSubmenuOpen.Value);
            waitShown(recentA);
            AddAssert("to the right of Recent", () => recentA.ScreenSpaceDrawQuad.TopLeft.X >= recent.ScreenSpaceDrawQuad.TopRight.X - 1
                                                     && System.Math.Abs(recentA.ScreenSpaceDrawQuad.TopLeft.Y - recent.ScreenSpaceDrawQuad.TopLeft.Y) < 4);

            press(recentA);
            AddAssert("command ran and every level closed", () => clicks.SequenceEqual(new[] { "a.txt" }) && !recent.IsSubmenuOpen.Value && !file.IsSubmenuOpen.Value);
            AddAssert("no popups left", () => window.PopupLayer.OpenPopups.Count == 0);
        }

        [Test]
        public void HoveringASiblingClosesAnOpenSubmenu()
        {
            press(file);
            waitShown(recent);
            AddStep("open Recent", () => recent.PerformClick());
            AddAssert("Recent open", () => recent.IsSubmenuOpen.Value);
            AddStep("hover Open", () => InputManager.MoveMouseTo(open));
            AddUntilStep("Recent closes, File stays", () => !recent.IsSubmenuOpen.Value && file.IsSubmenuOpen.Value);
        }

        [Test]
        public void CheckableItemTogglesBoundValue()
        {
            press(view);
            waitShown(wrap);
            press(wrap);
            AddAssert("checked, bound value follows, menu closed", () => wrap.IsChecked.Value && wordWrap.Value && !view.IsSubmenuOpen.Value);

            AddStep("view model unchecks", () => wordWrap.Value = false);
            AddAssert("item follows", () => !wrap.IsChecked.Value);
        }

        [Test]
        public void DisabledItemDoesNothing()
        {
            press(file);
            waitShown(disabled);
            press(disabled);
            AddAssert("no click, menu still open", () => clicks.Count == 0 && file.IsSubmenuOpen.Value);
        }

        [Test]
        public void EscapeClosesTheDeepestLevelFirstAndOutsideClickClosesAll()
        {
            press(file);
            waitShown(recent);
            AddStep("open Recent", () => recent.PerformClick());
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("only Recent closed", () => !recent.IsSubmenuOpen.Value && file.IsSubmenuOpen.Value);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("File closed", () => !file.IsSubmenuOpen.Value);

            press(file);
            AddStep("open Recent", () => recent.PerformClick());
            AddStep("click outside", () =>
            {
                InputManager.MoveMouseTo(window, new Vector2(200, 200));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("everything closed", () => !file.IsSubmenuOpen.Value && !recent.IsSubmenuOpen.Value && window.PopupLayer.OpenPopups.Count == 0);
        }

        [Test]
        public void KeyboardNavigation()
        {
            AddStep("Alt+F", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.F);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("File open, first item highlighted", () => file.IsSubmenuOpen.Value && file.HighlightedItem == open);

            AddStep("Down", () => InputManager.Key(Key.Down));
            AddAssert("Recent highlighted", () => file.HighlightedItem == recent);
            AddStep("Right", () => InputManager.Key(Key.Right));
            AddAssert("Recent open at its first item", () => recent.IsSubmenuOpen.Value && recent.HighlightedItem == recentA);
            AddStep("Left", () => InputManager.Key(Key.Left));
            AddAssert("back in File", () => !recent.IsSubmenuOpen.Value && file.IsSubmenuOpen.Value && file.HighlightedItem == recent);

            AddStep("Down skips disabled items and separators", () => InputManager.Key(Key.Down));
            AddAssert("Exit highlighted", () => file.HighlightedItem == exit);
            AddStep("Down wraps", () => InputManager.Key(Key.Down));
            AddAssert("Open highlighted", () => file.HighlightedItem == open);

            AddStep("Right on a leaf", () => InputManager.Key(Key.Right));
            AddAssert("moves to View", () => view.IsSubmenuOpen.Value && !file.IsSubmenuOpen.Value && view.HighlightedItem == wrap);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("toggled and closed", () => wordWrap.Value && !view.IsSubmenuOpen.Value);

            AddStep("Alt+F then X", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.F);
                InputManager.ReleaseKey(Key.AltLeft);
                InputManager.Key(Key.X);
            });
            AddAssert("access key clicks Exit", () => clicks.SequenceEqual(new[] { "exit" }) && !file.IsSubmenuOpen.Value);
        }

        private sealed class Cmd
        {
            public string Title;
            public readonly BindableList<Cmd> Children = new BindableList<Cmd>();
        }

        [Test]
        public void DataBoundMenuWithHierarchicalTemplate()
        {
            Menu bound = null;
            object clicked = null;
            var scope = new BindingScope();
            var edit = new Cmd { Title = "Edit" };
            var undo = new Cmd { Title = "Undo" };

            AddStep("create", () =>
            {
                edit.Children.Add(undo);
                edit.Children.Add(new Cmd { Title = "Redo" });

                var roots = new BindableList<Cmd>(new[] { edit });

                bound = new Menu
                {
                    ItemTemplate = new HierarchicalDataTemplate<Cmd>((c, _) => new TextBlock { Text = c.Title }, (c, s) => XamlBinding.ListCopy(s, c.Children)),
                    ItemsSource = XamlBinding.ListCopy(scope, roots),
                };
                bound.ItemClick += item => clicked = item;

                var panel = new StackPanel();
                panel.Add(bound);
                Child = window = new Window { Child = panel };
            });

            AddUntilStep("loaded", () => bound.IsLoaded && bound.ContainerFromIndex(0)?.DrawWidth > 0);
            AddAssert("generated header", () => headerText((MenuItem)bound.ContainerFromIndex(0)) == "Edit");

            AddStep("open Edit", () =>
            {
                InputManager.MoveMouseTo(bound.ContainerFromIndex(0));
                InputManager.Click(MouseButton.Left);
            });

            MenuItem undoItem() => (MenuItem)((MenuItem)bound.ContainerFromIndex(0)).ContainerFromIndex(0);

            AddUntilStep("children generated", () => undoItem()?.IsLoaded == true && undoItem().DrawWidth > 0 && headerText(undoItem()) == "Undo");
            AddStep("click Undo", () =>
            {
                InputManager.MoveMouseTo(undoItem());
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("ItemClick reports the data item", () => clicked == undo && window.PopupLayer.OpenPopups.Count == 0);

            AddStep("add a child live", () => edit.Children.Add(new Cmd { Title = "Cut" }));
            AddAssert("submenu follows the list", () => ((MenuItem)bound.ContainerFromIndex(0)).Items.Count == 3);
        }
    }
}
