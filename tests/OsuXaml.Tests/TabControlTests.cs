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
    }
}
