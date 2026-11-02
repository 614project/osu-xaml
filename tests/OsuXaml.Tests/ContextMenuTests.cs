using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Context menus attached through ContextMenuService, opened by real right-clicks.</summary>
    [HeadlessTest]
    public partial class ContextMenuTests : ManualInputManagerTestScene
    {
        private Window window;
        private Border outer;
        private Box inner;
        private ContextMenu outerMenu, innerMenu;
        private MenuItem copy, more, nested, innerItem;
        private List<string> clicks;

        [SetUpSteps]
        public void SetUpSteps()
        {
            // Built while the steps are being added, so the tests' step helpers capture these instances.
            clicks = new List<string>();

            outerMenu = new ContextMenu();
            outerMenu.Add(copy = new MenuItem { Header = "_Copy", InputGestureText = "Ctrl+C" });
            outerMenu.Add(new Separator());
            outerMenu.Add(more = new MenuItem { Header = "More" });
            more.Add(nested = new MenuItem { Header = "Nested" });
            copy.Click += () => clicks.Add("copy");
            nested.Click += () => clicks.Add("nested");

            innerMenu = new ContextMenu();
            innerMenu.Add(innerItem = new MenuItem { Header = "Inner action" });
            innerItem.Click += () => clicks.Add("inner");

            inner = new Box { Size = new Vector2(60), Colour = Theme.Accent };
            ContextMenuService.SetContextMenu(inner, innerMenu);

            var content = new Container { RelativeSizeAxes = Axes.Both, Children = new Drawable[] { new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Surface }, inner } };
            outer = new Border { Size = new Vector2(300, 200), Child = content };
            ContextMenuService.SetContextMenu(outer, outerMenu);

            var panel = new StackPanel { Padding = new MarginPadding(40) };
            panel.Add(outer);

            window = new Window { Child = panel };

            AddStep("create", () => Child = window);

            AddUntilStep("loaded", () => outer.IsLoaded && outer.DrawWidth > 0);
        }

        private void rightClick(Drawable target, Vector2 offset) => AddStep("right-click", () =>
        {
            InputManager.MoveMouseTo(target, offset);
            InputManager.Click(MouseButton.Right);
        });

        private void waitShown(MenuItem item) => AddUntilStep($"{item.Header} showing", () => item.IsLoaded && item.IsPresent && item.DrawWidth > 0);

        [Test]
        public void RightClickOpensAtTheMouseAndPickingClosesIt()
        {
            rightClick(outer, new Vector2(100, 50));
            AddAssert("outer menu open", () => outerMenu.IsOpen.Value && !innerMenu.IsOpen.Value && outerMenu.PlacementTarget == outer);
            waitShown(copy);
            AddAssert("opens at the mouse", () =>
            {
                var mouse = InputManager.CurrentState.Mouse.Position;
                var topLeft = window.PopupLayer.OpenPopups[0].Child.ScreenSpaceDrawQuad.TopLeft;
                return System.Math.Abs(topLeft.X - mouse.X) < 2 && System.Math.Abs(topLeft.Y - mouse.Y) < 2;
            });
            AddAssert("items are submenu rows", () => copy.Role == MenuItemRole.SubmenuItem && more.Role == MenuItemRole.SubmenuHeader);

            AddStep("click Copy", () =>
            {
                InputManager.MoveMouseTo(copy);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("clicked and closed", () => clicks.SequenceEqual(new[] { "copy" }) && !outerMenu.IsOpen.Value && window.PopupLayer.OpenPopups.Count == 0);
            AddUntilStep("menu leaves the layer once closed", () => outerMenu.Parent == null);
        }

        [Test]
        public void DeepestElementWins()
        {
            rightClick(inner, Vector2.Zero);
            AddAssert("inner menu", () => innerMenu.IsOpen.Value && !outerMenu.IsOpen.Value);

            AddStep("right-click the outer area while open", () =>
            {
                InputManager.MoveMouseTo(outer, new Vector2(100, 50));
                InputManager.Click(MouseButton.Right);
            });
            AddAssert("switches to the outer menu", () => outerMenu.IsOpen.Value && !innerMenu.IsOpen.Value);
        }

        [Test]
        public void NestedSubmenuEscapeAndOutsideClick()
        {
            rightClick(outer, new Vector2(100, 50));
            waitShown(more);
            AddStep("hover More", () => InputManager.MoveMouseTo(more));
            AddUntilStep("submenu opens", () => more.IsSubmenuOpen.Value);
            waitShown(nested);
            AddAssert("to the right", () => nested.ScreenSpaceDrawQuad.TopLeft.X >= more.ScreenSpaceDrawQuad.TopRight.X - 1);

            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("submenu closed, menu open", () => !more.IsSubmenuOpen.Value && outerMenu.IsOpen.Value);
            AddStep("Escape", () => InputManager.Key(Key.Escape));
            AddAssert("menu closed", () => !outerMenu.IsOpen.Value);

            rightClick(outer, new Vector2(100, 50));
            AddStep("click outside", () =>
            {
                InputManager.MoveMouseTo(outer, new Vector2(0, 300));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("closed", () => !outerMenu.IsOpen.Value);
        }

        [Test]
        public void KeyboardPicksItems()
        {
            rightClick(outer, new Vector2(100, 50));
            waitShown(copy);
            AddStep("Down, Down, Right", () =>
            {
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Down);
                InputManager.Key(Key.Right);
            });
            AddAssert("More open at Nested", () => more.IsSubmenuOpen.Value && more.HighlightedItem == nested);
            AddStep("Enter", () => InputManager.Key(Key.Enter));
            AddAssert("Nested clicked, all closed", () => clicks.SequenceEqual(new[] { "nested" }) && !outerMenu.IsOpen.Value && !more.IsSubmenuOpen.Value);
        }

        [Test]
        public void OpensFromCode()
        {
            AddStep("open from code", () =>
            {
                innerMenu.PlacementTarget = inner;
                innerMenu.Placement = PlacementMode.Bottom;
                innerMenu.IsOpen.Value = true;
            });
            AddUntilStep("open below the element", () => innerItem.IsLoaded && innerItem.IsPresent
                                                       && window.PopupLayer.OpenPopups.Count == 1
                                                       && window.PopupLayer.OpenPopups[0].Child.ScreenSpaceDrawQuad.TopLeft.Y >= inner.ScreenSpaceDrawQuad.BottomLeft.Y - 1);
            AddStep("close from code", () => innerMenu.IsOpen.Value = false);
            AddAssert("closed", () => window.PopupLayer.OpenPopups.Count == 0);
        }

        [Test]
        public void ElementsWithoutMenusDontConsumeRightClicks()
        {
            int rightDowns = 0;
            var plain = new RightClickBox(() => rightDowns++) { Size = new Vector2(80) };

            AddStep("add a plain element", () =>
            {
                var panel = new StackPanel();
                panel.Add(plain);
                window.Child = panel;
            });
            AddUntilStep("loaded", () => plain.IsLoaded);
            rightClick(plain, Vector2.Zero);
            AddAssert("event reaches it", () => rightDowns == 1 && window.PopupLayer.OpenPopups.Count == 0);
        }

        private partial class RightClickBox : Box
        {
            private readonly System.Action onRightDown;

            public RightClickBox(System.Action onRightDown)
            {
                this.onRightDown = onRightDown;
            }

            protected override bool OnMouseDown(osu.Framework.Input.Events.MouseDownEvent e)
            {
                if (e.Button == MouseButton.Right)
                    onRightDown();
                return true;
            }
        }

        [Test]
        public void LayerHostsDetachedElementsSoTheirPopupsOpen()
        {
            Container holder = null;
            Popup popup = null;

            AddStep("host a detached element", () =>
            {
                holder = new Container { Child = popup = new Popup { PlacementTarget = inner, Child = new Box { Size = new Vector2(50) } } };
                window.PopupLayer.Host(holder);
            });
            AddUntilStep("loaded in the layer", () => holder.IsLoaded && window.PopupLayer.IsHosting(holder));
            AddAssert("hosting twice keeps one", () =>
            {
                window.PopupLayer.Host(holder);
                return window.PopupLayer.IsHosting(holder);
            });
            AddStep("open its popup", () => popup.Open());
            AddAssert("the popup opened in this layer", () => popup.IsOpen.Value && popup.Layer == window.PopupLayer && window.PopupLayer.OpenPopups.Contains(popup));
            AddStep("close and release", () =>
            {
                popup.Close();
                window.PopupLayer.ReleaseHosted(holder);
            });
            AddAssert("out of the layer", () => holder.Parent == null && !window.PopupLayer.IsHosting(holder));
            AddStep("host it again", () => window.PopupLayer.Host(holder));
            AddAssert("not disposed: back in the layer", () => window.PopupLayer.IsHosting(holder) && holder.IsLoaded);
            AddStep("an element with a parent stays put", () => window.PopupLayer.Host(inner));
            AddAssert("still in its own parent", () => !window.PopupLayer.IsHosting(inner));
        }
    }
}
