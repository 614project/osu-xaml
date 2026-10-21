using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>TabControl and TabItem: selection, page switching, templates, placement and keyboard, in a headless host.</summary>
    [HeadlessTest]
    public partial class TabControlTests : ManualInputManagerTestScene
    {
        private sealed class Page
        {
            public string Title;
            public readonly Bindable<string> Body = new Bindable<string>();
        }

        private static readonly DataTemplate<Page> header_template = new DataTemplate<Page>((p, _) => new TextBlock { Text = p.Title });

        private static readonly DataTemplate<Page> page_template = new DataTemplate<Page>((p, scope) =>
        {
            var text = new TextBlock();
            XamlBinding.OneWay(scope, p.Body, (v, _) => text.Text = v ?? string.Empty);
            return text;
        });

        private static Container page(float height = 50) => new Container { RelativeSizeAxes = Axes.X, Height = height };

        /// <summary>Whether a page drawable is in the tree and shown (its page presenter is visible).</summary>
        private static bool shown(Drawable content) => content.Parent != null && content.FindClosestParent<ContentPresenter>()?.IsPresent == true;

        [Test]
        public void TabItemsShowOnlyTheSelectedPage()
        {
            TabControl tabs = null;
            TabItem[] items = null;
            Container[] pages = null;
            var index = new BindableInt();

            AddStep("create", () =>
            {
                pages = new[] { page(), page(80), page() };
                items = new[]
                {
                    new TabItem { Header = "General", Content = pages[0] },
                    new TabItem { Header = "Advanced", Content = pages[1] },
                    new TabItem { Header = "About", Content = pages[2] },
                };

                tabs = new TabControl { Size = new Vector2(400, 300) };
                foreach (var item in items)
                    tabs.Add(item);

                XamlBinding.TwoWay(tabs.SelectedIndex, index);
                Child = tabs;
            });

            AddAssert("first tab selected by default", () => tabs.SelectedIndex.Value == 0 && tabs.SelectedItem.Value == items[0] && items[0].IsSelected);
            AddAssert("only the first page is in the tree", () => shown(pages[0]) && pages[1].Parent == null && pages[2].Parent == null);
            AddAssert("tabs line up left to right", () => items[1].ScreenSpaceDrawQuad.TopLeft.X > items[0].ScreenSpaceDrawQuad.TopRight.X - 1);
            AddAssert("page below the strip", () => pages[0].ScreenSpaceDrawQuad.TopLeft.Y >= items[0].ScreenSpaceDrawQuad.BottomLeft.Y);

            AddStep("click Advanced", () =>
            {
                InputManager.MoveMouseTo(items[1]);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("selection and view model follow", () => tabs.SelectedIndex.Value == 1 && index.Value == 1 && items[1].IsSelected && !items[0].IsSelected);
            AddUntilStep("second page shown, first hidden but kept", () => shown(pages[1]) && !shown(pages[0]) && pages[0].Parent != null && pages[0].LoadState == LoadState.Loaded);
            AddAssert("selected content", () => tabs.SelectedContent == pages[1]);

            AddStep("view model selects About", () => index.Value = 2);
            AddUntilStep("third page shown", () => shown(pages[2]) && !shown(pages[1]));

            AddUntilStep("marker under the selected tab", () =>
            {
                var marker = tabs.ChildrenOfType<Circle>().Single();
                var tab = items[2].ScreenSpaceDrawQuad;
                var m = marker.ScreenSpaceDrawQuad;
                return marker.Alpha > 0.9f && m.TopLeft.X >= tab.TopLeft.X && m.TopRight.X <= tab.TopRight.X && System.Math.Abs(m.BottomLeft.Y - tab.BottomLeft.Y) < 1;
            });

            AddStep("swap the page content", () => items[2].Content = "Plain text page");
            AddAssert("page shows the new content", () => visiblePageText(tabs) == "Plain text page");
        }

        [Test]
        public void ItemsSourceUsesHeaderAndContentTemplates()
        {
            var source = new BindableList<Page>();
            var selected = new Bindable<Page>();
            var scope = new BindingScope();
            TabControl tabs = null;

            AddStep("create", () =>
            {
                source.Clear();
                source.AddRange(new[]
                {
                    new Page { Title = "One", Body = { Value = "first body" } },
                    new Page { Title = "Two", Body = { Value = "second body" } },
                });

                tabs = new TabControl
                {
                    Size = new Vector2(400, 200),
                    ItemTemplate = header_template,
                    ContentTemplate = page_template,
                    ItemsSource = XamlBinding.ListCopy(scope, source),
                };
                XamlBinding.TwoWayObject(scope, tabs.SelectedItem, selected);
                Child = tabs;
            });

            AddAssert("headers come from the item template", () => tabs.ChildrenOfType<TabItem>().Select(t => ((TextBlock)t.ChildrenOfType<ContentPresenter>().First().PresentedContent).Text.ToString()).SequenceEqual(new[] { "One", "Two" }));
            AddAssert("first item selected and written back", () => selected.Value == source[0]);
            AddAssert("page built from the content template", () => visiblePageText(tabs) == "first body");

            AddStep("view model selects Two", () => selected.Value = source[1]);
            AddUntilStep("second page", () => visiblePageText(tabs) == "second body");
            AddStep("edit the body", () => source[1].Body.Value = "edited");
            AddAssert("page binding is live", () => visiblePageText(tabs) == "edited");

            AddStep("remove the selected item", () => source.RemoveAt(1));
            AddAssert("falls back to the first tab", () => tabs.SelectedIndex.Value == 0 && selected.Value == source[0]);
            AddUntilStep("first page shown again", () => visiblePageText(tabs) == "first body");

            AddStep("add a tab", () => source.Add(new Page { Title = "Three", Body = { Value = "third" } }));
            AddAssert("two tabs", () => tabs.ChildrenOfType<TabItem>().Count() == 2);

            AddStep("clear", () => source.Clear());
            AddAssert("nothing selected", () => tabs.SelectedIndex.Value == -1 && selected.Value == null && visiblePageText(tabs) == null);
        }

        private static string visiblePageText(TabControl tabs)
            => tabs.ChildrenOfType<ContentPresenter>()
                   .Where(p => p.RelativeSizeAxes == Axes.Both && p.IsPresent && p.Parent != null)
                   .Select(p => (p.PresentedContent as TextBlock)?.Text.ToString())
                   .SingleOrDefault();

        [Test]
        public void SideStripsStackTabsVertically()
        {
            TabControl tabs = null;
            TabItem a = null, b = null;
            Container content = null;

            AddStep("create with tabs on the left", () =>
            {
                tabs = new TabControl { Size = new Vector2(400, 300), TabStripPlacement = Dock.Left };
                tabs.Add(a = new TabItem { Header = "Short", Content = content = page() });
                tabs.Add(b = new TabItem { Header = "A longer header" });
                Child = tabs;
            });

            AddAssert("tabs stacked", () => b.ScreenSpaceDrawQuad.TopLeft.Y >= a.ScreenSpaceDrawQuad.BottomLeft.Y - 1);
            AddAssert("tabs share the strip width", () => System.Math.Abs(a.DrawWidth - b.DrawWidth) < 0.5f);
            AddAssert("page to the right of the strip", () => content.ScreenSpaceDrawQuad.TopLeft.X >= a.ScreenSpaceDrawQuad.TopRight.X);

            AddStep("move the strip to the bottom", () => tabs.TabStripPlacement = Dock.Bottom);
            AddAssert("tabs side by side at the bottom", () => b.ScreenSpaceDrawQuad.TopLeft.X >= a.ScreenSpaceDrawQuad.TopRight.X - 1
                                                             && a.ScreenSpaceDrawQuad.TopLeft.Y > content.ScreenSpaceDrawQuad.BottomLeft.Y);
        }

        [Test]
        public void IsSelectedDisabledTabsAndKeyboard()
        {
            TabControl tabs = null;
            TabItem[] items = null;

            AddStep("create", () =>
            {
                items = new[]
                {
                    new TabItem { Header = "A" },
                    new TabItem { Header = "B", IsEnabled = false },
                    new TabItem { Header = "C", IsSelected = true },
                    new TabItem { Header = "D" },
                };

                tabs = new TabControl { Size = new Vector2(400, 200) };
                foreach (var item in items)
                    tabs.Add(item);
                Child = tabs;
            });

            AddAssert("IsSelected picks the first tab shown", () => tabs.SelectedIndex.Value == 2);

            AddStep("click the disabled tab", () =>
            {
                InputManager.MoveMouseTo(items[1]);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("still C", () => tabs.SelectedIndex.Value == 2);

            AddStep("click C", () =>
            {
                InputManager.MoveMouseTo(items[2]);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("the tab control took focus", () => tabs.HasFocus);

            AddStep("press Left", () => InputManager.Key(Key.Left));
            AddAssert("skips the disabled tab", () => tabs.SelectedIndex.Value == 0);
            AddStep("press Left again", () => InputManager.Key(Key.Left));
            AddAssert("wraps to the end", () => tabs.SelectedIndex.Value == 3);
            AddStep("press Home", () => InputManager.Key(Key.Home));
            AddAssert("first", () => tabs.SelectedIndex.Value == 0);

            AddStep("press Ctrl+Tab", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.Tab);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("next enabled tab", () => tabs.SelectedIndex.Value == 2);

            AddStep("select with IsSelected after load", () => items[3].IsSelected = true);
            AddAssert("D", () => tabs.SelectedIndex.Value == 3);
        }

        [Test]
        public void FocusLeavesAPageWhenItIsHidden()
        {
            TabControl tabs = null;
            TextBox box = null;

            AddStep("create", () =>
            {
                var form = new StackPanel();
                form.Add(box = new TextBox());

                tabs = new TabControl { Size = new Vector2(400, 200) };
                tabs.Add(new TabItem { Header = "Form", Content = form });
                tabs.Add(new TabItem { Header = "Other" });
                Child = tabs;
            });

            AddStep("click into the text box", () =>
            {
                InputManager.MoveMouseTo(box);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("text box focused", () => box.HasFocus);

            AddStep("Ctrl+Tab from inside the page", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.Tab);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("switched tab", () => tabs.SelectedIndex.Value == 1);
            AddAssert("focus moved to the tab control", () => !box.HasFocus && tabs.HasFocus);
        }

        [Test]
        public void SizesToTheSelectedPageWhenUnsized()
        {
            TabControl tabs = null;

            AddStep("create", () =>
            {
                tabs = new TabControl { Padding = new MarginPadding(10) };
                tabs.Add(new TabItem { Header = "Small", Content = new Container { Size = new Vector2(100, 40) } });
                tabs.Add(new TabItem { Header = "Big", Content = new Container { Size = new Vector2(300, 120) } });
                Child = tabs;
            });

            float stripHeight = 0;
            AddAssert("small page plus strip", () =>
            {
                stripHeight = tabs.ChildrenOfType<TabItem>().First().DrawHeight;
                return tabs.DrawWidth >= 120 && System.Math.Abs(tabs.DrawHeight - (stripHeight + 60)) < 1;
            });
            AddStep("select Big", () => tabs.SelectedIndex.Value = 1);
            AddUntilStep("grows to the big page", () => System.Math.Abs(tabs.DrawWidth - 320) < 1 && System.Math.Abs(tabs.DrawHeight - (stripHeight + 140)) < 1);
        }
    

        private static TabControl.TabStrip stripOf(TabControl tabs) => tabs.ChildrenOfType<TabControl.TabStrip>().Single();

        private static TabControl.TabStripScrollButton scrollButton(TabControl tabs, int direction)
            => tabs.ChildrenOfType<TabControl.TabStripScrollButton>().Single(b => b.Direction == direction);

        /// <summary>Whether all of <paramref name="tab"/> is inside the part of the strip between the scroll buttons.</summary>
        private static bool fullyShown(TabControl tabs, TabItem tab)
        {
            var strip = stripOf(tabs).ScreenSpaceDrawQuad;
            var quad = tab.ScreenSpaceDrawQuad;
            float buttons = stripOf(tabs).Overflows ? 24 * strip.Width / stripOf(tabs).DrawWidth : 0;
            return quad.TopLeft.X >= strip.TopLeft.X + buttons - 1 && quad.TopRight.X <= strip.TopRight.X - buttons + 1;
        }

        [Test]
        public void TabsThatDontFitScroll()
        {
            TabControl tabs = null;
            TabItem[] items = null;

            AddStep("create ten tabs in a narrow control", () =>
            {
                items = Enumerable.Range(1, 10).Select(i => new TabItem { Header = "Section " + i, Content = page() }).ToArray();
                tabs = new TabControl { Size = new Vector2(320, 200) };
                foreach (var item in items)
                    tabs.Add(item);
                Child = tabs;
            });
            AddUntilStep("laid out", () => items[9].DrawWidth > 0);

            AddAssert("overflows, with arrows", () => stripOf(tabs).Overflows && scrollButton(tabs, -1).Alpha > 0 && scrollButton(tabs, 1).IsPresent);
            AddAssert("strip no wider than the control", () => stripOf(tabs).DrawWidth <= tabs.DrawWidth + 0.5f);
            AddAssert("starts at the first tab", () => stripOf(tabs).ScrollTarget == 0 && fullyShown(tabs, items[0]) && !fullyShown(tabs, items[9]));
            AddAssert("back arrow dimmed at the start", () => !scrollButton(tabs, -1).IsEnabled && scrollButton(tabs, 1).IsEnabled);

            AddStep("select the last tab", () => tabs.SelectedIndex.Value = 9);
            AddUntilStep("scrolled to show it", () => fullyShown(tabs, items[9]) && stripOf(tabs).ScrollOffset == stripOf(tabs).MaxScroll);
            AddUntilStep("marker under it", () =>
            {
                var marker = stripOf(tabs).Indicator.ScreenSpaceDrawQuad;
                var tab = items[9].ScreenSpaceDrawQuad;
                return marker.TopLeft.X > tab.TopLeft.X && marker.TopRight.X < tab.TopRight.X;
            });
            AddAssert("forward arrow dimmed at the end", () => !scrollButton(tabs, 1).IsEnabled);

            AddStep("Home", () =>
            {
                InputManager.MoveMouseTo(items[9]);
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.Home);
            });
            AddUntilStep("back at the first tab", () => tabs.SelectedIndex.Value == 0 && stripOf(tabs).ScrollOffset == 0);

            AddStep("wheel down over the strip", () =>
            {
                InputManager.MoveMouseTo(items[1]);
                InputManager.ScrollVerticalBy(-1);
            });
            AddUntilStep("tabs move along", () => stripOf(tabs).ScrollOffset > 40);

            float before = 0;
            AddStep("click the back arrow", () =>
            {
                before = stripOf(tabs).ScrollTarget;
                InputManager.MoveMouseTo(scrollButton(tabs, -1));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("scrolled back, selection kept", () => stripOf(tabs).ScrollTarget < before && tabs.SelectedIndex.Value == 0);

            AddStep("click the forward arrow", () =>
            {
                InputManager.MoveMouseTo(scrollButton(tabs, 1));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("scrolled forward", () => stripOf(tabs).ScrollTarget > 0 && tabs.SelectedIndex.Value == 0);

            AddStep("widen the control", () => tabs.Width = 1200);
            AddUntilStep("everything fits, arrows gone", () => !stripOf(tabs).Overflows && stripOf(tabs).ScrollOffset == 0
                                                              && fullyShown(tabs, items[0]) && fullyShown(tabs, items[9]) && scrollButton(tabs, 1).Alpha == 0);
        }

        [Test]
        public void SideStripsScrollVertically()
        {
            TabControl tabs = null;
            TabItem[] items = null;

            AddStep("create", () =>
            {
                items = Enumerable.Range(1, 12).Select(i => new TabItem { Header = "Item " + i, Content = page() }).ToArray();
                tabs = new TabControl { Size = new Vector2(400, 220), TabStripPlacement = Dock.Left };
                foreach (var item in items)
                    tabs.Add(item);
                Child = tabs;
            });
            AddUntilStep("laid out", () => items[11].DrawHeight > 0);
            AddAssert("overflows", () => stripOf(tabs).Overflows && stripOf(tabs).DrawHeight <= tabs.DrawHeight + 0.5f);

            AddStep("press End", () =>
            {
                InputManager.MoveMouseTo(items[0]);
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.End);
            });
            AddUntilStep("last tab shown", () =>
            {
                var strip = stripOf(tabs).ScreenSpaceDrawQuad;
                var tab = items[11].ScreenSpaceDrawQuad;
                return tabs.SelectedIndex.Value == 11 && tab.BottomLeft.Y <= strip.BottomLeft.Y - 23 && stripOf(tabs).ScrollOffset == stripOf(tabs).MaxScroll;
            });

            AddStep("wheel up", () =>
            {
                InputManager.MoveMouseTo(items[11]);
                InputManager.ScrollVerticalBy(1);
            });
            AddUntilStep("scrolled up", () => stripOf(tabs).ScrollOffset < stripOf(tabs).MaxScroll - 40);
        }
    }
}
