using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Frame navigation with real input: history, kept-alive pages, disposal, transitions, templates, mouse and keys.</summary>
    [HeadlessTest]
    public partial class FrameTests : ManualInputManagerTestScene
    {
        private Window window;
        private Frame frame;
        private Button back;
        private BindingScope scope;
        private List<NavigationEventArgs> navigated;

        private TestPage home, details, settings;

        /// <summary>What a XAML page with a button compiles to, with a way to tell whether it was disposed.</summary>
        private sealed partial class TestPage : Page
        {
            public readonly Button Button;
            public readonly TextBox TextBox;
            public int Clicks;

            public TestPage(string title)
            {
                Title = title;
                Button = new Button { Content = title };
                Button.Click += () => Clicks++;
                TextBox = new TextBox { Width = 200 };

                var panel = new StackPanel { Spacing = 8 };
                panel.Add(Button);
                panel.Add(TextBox);
                Content = panel;
            }

            public bool Disposed => IsDisposed;
        }

        private sealed class Item
        {
            public string Name;
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create shell", () =>
            {
                scope?.UnbindAll();
                scope = new BindingScope();
                navigated = new List<NavigationEventArgs>();

                home = new TestPage("Home");
                details = new TestPage("Details");
                settings = new TestPage("Settings");

                back = new Button { Content = "Back" };
                frame = new Frame { Child = home };
                frame.Navigated += e => navigated.Add(e);

                // What IsEnabled="{Bind CanGoBack, ElementName=frame}" compiles to.
                XamlBinding.OneWay(scope, frame.CanGoBack, (v, _) => back.IsEnabled = v);
                back.Click += () => frame.GoBack();

                var bar = new StackPanel { Orientation = Orientation.Horizontal, Padding = new MarginPadding(8) };
                DockPanel.SetDock(bar, Dock.Top);
                bar.Add(back);

                var dock = new DockPanel();
                dock.Add(bar);
                dock.Add(frame);
                Child = window = new Window { Child = dock };
            });
            AddUntilStep("loaded", () => home.IsLoaded && frame.DrawWidth > 0);
        }

        /// <summary>The frame's wrapper around a page: page, slot, content area, chrome, presenter.</summary>
        private static Drawable presenterOf(Page page) => page.Parent?.Parent?.Parent?.Parent;

        private void navigate(System.Func<object> content) => AddStep("navigate", () => frame.Navigate(content()));

        private void settle() => AddUntilStep("transition done", () => frame.ChildrenOfType<ContentPresenter>().Count(p => p.Parent?.Parent == frame) == 1
                                                                       && frame.PresentedContent?.Parent != null && ((Drawable)frame.PresentedContent).IsLoaded);

        [Test]
        public void HistoryKeepsPagesAliveAndDropsTheForwardStack()
        {
            AddAssert("initial page from the child element", () => frame.Content == home && !frame.CanGoBack.Value && !frame.CanGoForward.Value);
            AddAssert("the page gets the frame's NavigationService", () => home.NavigationService == frame.NavigationService && home.NavigationService.Frame == frame);
            AddAssert("Back disabled", () => !back.IsEnabled);

            AddStep("the page navigates its own frame", () => home.NavigationService.Navigate(details));
            AddAssert("details shown, home behind it", () => frame.Content == details && frame.BackStack.SequenceEqual(new object[] { home })
                                                             && frame.CanGoBack.Value && back.IsEnabled && !frame.CanGoForward.Value);
            AddAssert("Navigated raised", () => navigated.Count == 1 && navigated[0].Content == details && navigated[0].NavigationMode == NavigationMode.New
                                                && navigated[0].Navigator == frame);
            settle();
            AddAssert("home left the tree but stays alive", () => presenterOf(home).Parent == null && !home.Disposed);

            AddStep("type in details", () => details.TextBox.Text.Value = "kept");
            AddStep("click Back", () =>
            {
                InputManager.MoveMouseTo(back);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("same home instance back", () => frame.Content == home && frame.ForwardStack.SequenceEqual(new object[] { details })
                                                       && frame.CanGoForward.Value && !frame.CanGoBack.Value && navigated.Last().NavigationMode == NavigationMode.Back);
            settle();
            AddAssert("details kept for going forward", () => !details.Disposed);

            AddStep("forward", () => frame.GoForward());
            AddAssert("details again, with its state", () => frame.Content == details && details.TextBox.Text.Value == "kept"
                                                             && navigated.Last().NavigationMode == NavigationMode.Forward);
            settle();

            AddStep("back", () => frame.GoBack());
            settle();
            navigate(() => settings);
            AddAssert("forward stack dropped", () => frame.Content == settings && !frame.CanGoForward.Value && frame.ForwardStack.Count == 0
                                                     && frame.BackStack.SequenceEqual(new object[] { home }));
            AddAssert("details disposed", () => details.Disposed && details.NavigationService == null);
            AddAssert("home alive", () => !home.Disposed);

            AddStep("back past the start does nothing", () =>
            {
                frame.GoBack();
                frame.GoBack();
            });
            AddAssert("at home", () => frame.Content == home && !frame.CanGoBack.Value);
        }

        [Test]
        public void PagesSlideAndOnlyTheCurrentOneTakesInput()
        {
            bool startsAside = false, onlyTheNewPageTakesInput = false;
            AddStep("navigate", () =>
            {
                frame.Navigate(details);
                var incoming = presenterOf(details);
                var leaving = presenterOf(home);
                startsAside = incoming.X > 0 && incoming.Alpha < 1 && incoming.LatestTransformEndTime > incoming.Time.Current;
                onlyTheNewPageTakesInput = leaving.Parent != null && leaving.IsPresent
                                                                  && !leaving.PropagatePositionalInputSubTree && !leaving.PropagateNonPositionalInputSubTree;
            });
            AddAssert("the new page starts off to the side and faded", () => startsAside);
            AddAssert("the leaving page, still drawn, takes no input", () => onlyTheNewPageTakesInput);
            AddUntilStep("slid into place", () =>
            {
                var incoming = presenterOf(details);
                return incoming.X == 0 && incoming.Alpha == 1 && incoming.PropagatePositionalInputSubTree && incoming.PropagateNonPositionalInputSubTree;
            });
            settle();

            AddStep("click the new page's button", () =>
            {
                InputManager.MoveMouseTo(details.Button);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("it works", () => details.Clicks == 1);

            AddStep("focus the text box", () =>
            {
                InputManager.MoveMouseTo(details.TextBox);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("focused", () => details.TextBox.HasFocus);
            bool slidFromTheLeft = false;
            AddStep("back", () =>
            {
                frame.GoBack();
                var incoming = presenterOf(home);
                slidFromTheLeft = incoming.X < 0 && incoming.LatestTransformEndTime > incoming.Time.Current;
            });
            AddAssert("focus left the page that left", () => !details.TextBox.HasFocus);
            AddAssert("going back slides in from the other side", () => slidFromTheLeft);
        }

        [Test]
        public void ObjectsShowThroughTheContentTemplate()
        {
            Item first = null;

            AddStep("set a template and navigate to items", () =>
            {
                frame.ContentTemplate = new DataTemplate<Item>((item, _) => new TextBlock { Text = item.Name });
                frame.Navigate(first = new Item { Name = "First" });
            });
            AddAssert("built by the template", () => frame.PresentedContent is TextBlock t && t.Text.ToString() == "First");
            AddStep("navigate to plain text", () => frame.Navigate("Just text"));
            AddAssert("shown as text", () => frame.PresentedContent is TextBlock t && t.Text.ToString() == "Just text");
            settle();

            Drawable built = null;
            AddStep("back", () => frame.GoBack());
            AddAssert("the item's view is kept, not rebuilt", () =>
            {
                built = frame.PresentedContent;
                return frame.Content == first && built is TextBlock t && t.Text.ToString() == "First";
            });
            AddStep("forward and back", () =>
            {
                frame.GoForward();
                frame.GoBack();
            });
            AddAssert("same drawable", () => frame.PresentedContent == built);
        }

        [Test]
        public void MouseSideButtonsAndAltArrowsNavigate()
        {
            navigate(() => details);
            settle();

            AddStep("press the mouse's back button over the frame", () =>
            {
                InputManager.MoveMouseTo(frame);
                InputManager.Click(MouseButton.Button1);
            });
            AddAssert("back", () => frame.Content == home);
            AddStep("press the mouse's forward button", () => InputManager.Click(MouseButton.Button2));
            AddAssert("forward", () => frame.Content == details);
            settle();

            AddStep("Alt+Left", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.Left);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("back", () => frame.Content == home);
            AddStep("Alt+Right", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.Right);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("forward", () => frame.Content == details);
        }

        [Test]
        public void NavigatingCanCancelAndBackEntriesCanBeRemoved()
        {
            AddStep("cancel navigations to settings", () => frame.Navigating += e => e.Cancel = e.Content == settings);
            AddAssert("cancelled", () => !frame.Navigate(settings) && frame.Content == home && navigated.Count == 0);
            AddAssert("navigating to the current content changes nothing", () => !frame.Navigate(home));

            navigate(() => details);
            AddStep("remove the back entry", () => Assert.That(frame.RemoveBackEntry(), Is.SameAs(home)));
            AddAssert("no way back, home disposed", () => !frame.CanGoBack.Value && home.Disposed);
            AddAssert("nothing more to remove", () => frame.RemoveBackEntry() == null);
        }

        [Test]
        public void ElementsFindTheServiceAndBindingsCantFakeHistory()
        {
            AddAssert("an element in a page finds its frame's service", () => NavigationService.GetNavigationService(home.Button) == frame.NavigationService);
            AddAssert("outside a frame there is none", () => NavigationService.GetNavigationService(back) == null);

            var source = new osu.Framework.Bindables.BindableBool(true);
            AddStep("two-way bind CanGoBack to a view model that says true", () => XamlBinding.TwoWay(frame.CanGoBack, source));
            AddUntilStep("the frame keeps the truth and tells the view model", () => !frame.CanGoBack.Value && !source.Value);
            AddStep("the view model writes true", () => source.Value = true);
            AddUntilStep("put right again", () => !frame.CanGoBack.Value && !source.Value);
            navigate(() => details);
            AddAssert("view model follows", () => source.Value);
        }

        [Test]
        public void TheOptionalNavigationBarShowsTitleAndButtons()
        {
            SpriteIcon arrow(IconUsage icon) => frame.ChildrenOfType<SpriteIcon>().Single(i => i.Icon.Equals(icon));

            bool shown(Drawable d)
            {
                for (; d != null; d = d.Parent)
                {
                    if (!d.IsPresent)
                        return false;
                }

                return true;
            }

            AddAssert("hidden by default", () => frame.ChildrenOfType<SpriteIcon>().All(i => !shown(i)) && presenterOf(home).ScreenSpaceDrawQuad.TopLeft.Y == frame.ScreenSpaceDrawQuad.TopLeft.Y);
            AddStep("Automatic", () => frame.NavigationUIVisibility = NavigationUIVisibility.Automatic);
            AddAssert("still hidden without history", () => !shown(arrow(FontAwesome.Solid.ArrowLeft)));
            navigate(() => details);
            settle();
            AddAssert("shown once there is history, above the page", () => shown(arrow(FontAwesome.Solid.ArrowLeft))
                                                                            && presenterOf(details).ScreenSpaceDrawQuad.TopLeft.Y >= frame.ScreenSpaceDrawQuad.TopLeft.Y + 39);
            AddUntilStep("with the page title", () => frame.ChildrenOfType<TextBlock>().Any(t => t.Text.ToString() == "Details" && shown(t) && !(t.Parent?.Parent?.Parent?.Parent is Button)));

            AddStep("click the bar's back arrow", () =>
            {
                InputManager.MoveMouseTo(arrow(FontAwesome.Solid.ArrowLeft));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("back", () => frame.Content == home);
            AddStep("click the bar's forward arrow", () =>
            {
                InputManager.MoveMouseTo(arrow(FontAwesome.Solid.ArrowRight));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("forward", () => frame.Content == details);
            AddStep("Hidden", () => frame.NavigationUIVisibility = NavigationUIVisibility.Hidden);
            AddAssert("gone", () => !shown(arrow(FontAwesome.Solid.ArrowLeft)));
        }

        [Test]
        public void RemovingTheFrameDisposesKeptPages()
        {
            navigate(() => details);
            navigate(() => settings);
            settle();
            AddStep("remove the window", () => Clear());
            AddUntilStep("every page disposed", () => home.Disposed && details.Disposed && settings.Disposed);
        }
    }
}
