using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>StatusBar, ToolBar and ToolBarTray layout and styling in a headless host.</summary>
    [HeadlessTest]
    public partial class StatusBarTests : ManualInputManagerTestScene
    {
        private static bool near(float a, float b) => System.Math.Abs(a - b) < 1;

        [Test]
        public void StatusBarDocksAtTheBottomAndLaysItemsOut()
        {
            StatusBar bar = null;
            StatusBarItem ready = null, right = null;
            Separator separator = null;
            TextBlock line = null;
            Container body = null;

            AddStep("create", () =>
            {
                bar = new StatusBar();
                bar.Add(ready = new StatusBarItem { Content = "Ready" });
                bar.Add(separator = new Separator());
                bar.Add(line = new TextBlock { Text = "Ln 1" });
                bar.Add(right = new StatusBarItem { Content = new Container { Size = new Vector2(80, 8) } });
                DockPanel.SetDock(right, Dock.Right);
                bar.Add(new StatusBarItem());

                var dock = new DockPanel { Size = new Vector2(500, 300) };
                dock.Add(bar);
                DockPanel.SetDock(bar, Dock.Bottom);
                dock.Add(body = new Container());
                Child = dock;
            });

            AddAssert("bar at the bottom, full width", () => near(bar.DrawWidth, 500) && near(bar.ScreenSpaceDrawQuad.BottomLeft.Y, body.ScreenSpaceDrawQuad.BottomLeft.Y + bar.DrawHeight));
            AddAssert("compact height", () => bar.DrawHeight > 20 && bar.DrawHeight < 40);
            AddAssert("items left to right", () => separator.ScreenSpaceDrawQuad.TopLeft.X > ready.ScreenSpaceDrawQuad.TopRight.X - 1
                                                   && line.ScreenSpaceDrawQuad.TopLeft.X > separator.ScreenSpaceDrawQuad.TopRight.X);
            AddAssert("separator is vertical", () => near(separator.DrawWidth, 1) && separator.DrawHeight > 8);
            AddAssert("plain child is wrapped in a StatusBarItem", () => line.FindClosestParent<StatusBarItem>() != null);
            AddAssert("docked item at the right edge", () => near(right.ScreenSpaceDrawQuad.TopRight.X, bar.ScreenSpaceDrawQuad.TopRight.X - bar.Padding.Right));
            AddAssert("status text is small and subdued", () => ready.PresentedContent is TextBlock t && t.Colour == Theme.SubtleText && t.FontSize == 14);
        }

        [Test]
        public void ToolBarFlattensButtonsAndTurnsSeparators()
        {
            ToolBar toolbar = null;
            Button plain = null, custom = null;
            ToggleButton toggle = null;
            Separator separator = null;
            TextBlock label = null;
            int saves = 0;

            AddStep("create", () =>
            {
                saves = 0;
                toolbar = new ToolBar { Header = "File" };
                toolbar.Add(plain = new Button { Content = "Save" });
                toolbar.Add(custom = new Button { Content = "Delete", Background = Theme.Danger });
                toolbar.Add(separator = new Separator());
                toolbar.Add(toggle = new ToggleButton { Content = "B" });
                toolbar.Add(label = new TextBlock { Text = "Zoom" });
                plain.Click += () => saves++;
                Child = toolbar;
            });

            AddAssert("default button turns flat", () => plain.Background == Colour4.Transparent && plain.Foreground == Theme.Text && plain.Padding.Left == 10);
            AddAssert("explicit background kept", () => custom.Background == Theme.Danger);
            AddAssert("toggle loses its surface fill", () => toggle.Background == Colour4.Transparent && toggle.CheckedBackground.A < 0.5f);
            AddAssert("separator is vertical", () => near(separator.DrawWidth, 1) && separator.DrawHeight > 10);
            AddAssert("header shown first", () => toolbar.ChildrenOfType<ContentPresenter>().Any(p => p.PresentedContent is TextBlock t && t.Text == "File" && p.ScreenSpaceDrawQuad.TopRight.X <= plain.ScreenSpaceDrawQuad.TopLeft.X));
            AddAssert("text centred across the strip", () => near(label.ScreenSpaceDrawQuad.Centre.Y, plain.ScreenSpaceDrawQuad.Centre.Y));
            AddAssert("buttons share the strip height", () => near(plain.DrawHeight, toggle.DrawHeight));

            AddStep("click Save", () =>
            {
                InputManager.MoveMouseTo(plain);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("buttons still work", () => saves == 1);

            AddStep("click B", () =>
            {
                InputManager.MoveMouseTo(toggle);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("toggle checked with accent text", () => toggle.IsChecked.Value && ((TextBlock)toggle.PresentedContent).Colour == Theme.Accent);
        }

        [Test]
        public void ToolBarTrayArrangesBands()
        {
            ToolBarTray tray = null;
            ToolBar file = null, edit = null, help = null;

            AddStep("create", () =>
            {
                tray = new ToolBarTray();
                tray.Add(edit = toolbar("Cut", "Copy", "Paste"));
                tray.Add(help = toolbar("Help"));
                tray.Add(file = toolbar("New", "Open"));
                edit.BandIndex = 1;
                help.Band = 1;

                var dock = new DockPanel { Size = new Vector2(600, 300) };
                dock.Add(tray);
                DockPanel.SetDock(tray, Dock.Top);
                dock.Add(new Container());
                Child = dock;
            });

            AddAssert("band 0 in BandIndex order", () => edit.ScreenSpaceDrawQuad.TopLeft.X > file.ScreenSpaceDrawQuad.TopRight.X && near(edit.ScreenSpaceDrawQuad.TopLeft.Y, file.ScreenSpaceDrawQuad.TopLeft.Y));
            AddAssert("band 1 below", () => help.ScreenSpaceDrawQuad.TopLeft.Y >= file.ScreenSpaceDrawQuad.BottomLeft.Y && near(help.ScreenSpaceDrawQuad.TopLeft.X, file.ScreenSpaceDrawQuad.TopLeft.X));
            AddAssert("toolbars keep their own width", () => file.DrawWidth < edit.DrawWidth && edit.DrawWidth < 400);
            AddAssert("tray spans the window and fits its bands", () => near(tray.DrawWidth, 600) && near(tray.DrawHeight, file.DrawHeight + help.DrawHeight + tray.Spacing + tray.Padding.TotalVertical));
        }

        private static ToolBar toolbar(params string[] buttons)
        {
            var bar = new ToolBar();
            foreach (string b in buttons)
                bar.Add(new Button { Content = b });
            return bar;
        }
    }
}
