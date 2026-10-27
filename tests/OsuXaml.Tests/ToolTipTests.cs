using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Tooltips attached through ToolTipService, driven by real mouse movement.</summary>
    [HeadlessTest]
    public partial class ToolTipTests : ManualInputManagerTestScene
    {
        private Window window;
        private Button save;
        private Border card;
        private TextBlock inside;
        private TextBlock plain;
        private int saveClicks;

        [SetUpSteps]
        public void SetUpSteps()
        {
            // Built while the steps are being added, so the tests' step helpers capture these instances.
            saveClicks = 0;

            save = new Button { Content = "Save" };
            save.Click += () => saveClicks++;
            ToolTipService.SetToolTip(save, "Save the document");

            inside = new TextBlock { Text = "Inner text" };
            ToolTipService.SetToolTip(inside, "Inner tooltip");

            card = new Border { Padding = new MarginPadding(30), Background = Theme.Surface, Child = inside };
            ToolTipService.SetToolTip(card, "Card tooltip");

            plain = new TextBlock { Text = "No tooltip" };

            var panel = new StackPanel { Padding = new MarginPadding(40), Spacing = 20 };
            panel.Add(save);
            panel.Add(card);
            panel.Add(plain);

            window = new Window { Child = panel };

            AddStep("create", () => Child = window);

            AddUntilStep("loaded", () => save.IsLoaded && save.DrawWidth > 0);
            AddStep("mouse on plain text", () => InputManager.MoveMouseTo(plain));
        }

        private ToolTip shown => window.PopupLayer.ChildrenOfType<ToolTip>().SingleOrDefault(t => t.Parent != null);

        private string shownText => (shown?.PresentedContent as TextBlock)?.Text.ToString();

        [Test]
        public void ShowsAfterADelayNearTheMouseAndHidesOnLeave()
        {
            double hoveredAt = 0;

            AddStep("hover Save", () =>
            {
                InputManager.MoveMouseTo(save);
                hoveredAt = Time.Current;
            });
            AddAssert("not immediately", () => shown == null);
            AddUntilStep("shows", () => shownText == "Save the document");
            AddAssert("after about half a second", () => Time.Current - hoveredAt >= 450);
            AddUntilStep("below the pointer", () =>
            {
                var mouse = InputManager.CurrentState.Mouse.Position;
                var quad = shown.ScreenSpaceDrawQuad;
                return quad.TopLeft.Y > mouse.Y && quad.TopLeft.Y < mouse.Y + 40 && System.Math.Abs(quad.TopLeft.X - mouse.X) < 2;
            });
            AddAssert("styled like the theme", () => shown.Background == Theme.Surface && ((TextBlock)shown.PresentedContent).FontSize == 14);

            AddStep("leave", () => InputManager.MoveMouseTo(plain));
            AddAssert("hidden", () => shown == null);
        }

        [Test]
        public void DeepestElementWinsAndNeighboursShowAtOnce()
        {
            AddStep("hover inner text", () => InputManager.MoveMouseTo(inside));
            AddUntilStep("inner tooltip", () => shownText == "Inner tooltip");

            AddStep("move to the card's padding", () => InputManager.MoveMouseTo(card, new Vector2(0, -card.DrawHeight / 2 + 5)));
            AddAssert("card tooltip right away", () => shownText == "Card tooltip");
        }

        [Test]
        public void ClickHidesUntilTheMouseLeaves()
        {
            AddStep("hover Save", () => InputManager.MoveMouseTo(save));
            AddUntilStep("shows", () => shown != null);
            AddStep("click", () => InputManager.Click(MouseButton.Left));
            AddAssert("hidden and click went through", () => shown == null && saveClicks == 1);
            AddWaitStep("wait", 10);
            AddAssert("stays hidden while still hovered", () => shown == null);

            AddStep("leave", () => InputManager.MoveMouseTo(plain));
            AddStep("come back", () => InputManager.MoveMouseTo(save));
            AddUntilStep("shows again", () => shown != null);
        }

        [Test]
        public void TooltipNeverTakesInput()
        {
            AddStep("place over the element", () => ToolTipService.SetPlacement(save, PlacementMode.Center));
            AddStep("hover Save", () => InputManager.MoveMouseTo(save));
            AddUntilStep("shows over the button", () => shown != null && shown.ScreenSpaceDrawQuad.Contains(InputManager.CurrentState.Mouse.Position));
            AddStep("press and release", () =>
            {
                InputManager.PressButton(MouseButton.Left);
                InputManager.ReleaseButton(MouseButton.Left);
            });
            AddAssert("the button got the click", () => saveClicks == 1);
            AddAssert("hover reached the button", () => save.IsHovered);
        }

        [Test]
        public void RichContentAndLiveText()
        {
            AddStep("rich tooltip on the card", () =>
            {
                var stack = new StackPanel { Spacing = 4 };
                stack.Add(new TextBlock { Text = "Title", FontWeight = FontWeight.Bold });
                stack.Add(new TextBlock { Text = "Details" });
                ToolTipService.SetToolTip(card, new ToolTip { Content = stack });
                ToolTipService.SetToolTip(inside, (string)null);
                ToolTipService.SetInitialShowDelay(card, 100);
            });
            AddStep("hover the card", () => InputManager.MoveMouseTo(inside));
            AddUntilStep("rich tooltip", () => shown?.PresentedContent is StackPanel);

            AddStep("plain drawable as tooltip", () => ToolTipService.SetToolTip(save, new Box { Size = new Vector2(30), Colour = Theme.Danger }));
            AddStep("hover Save", () => InputManager.MoveMouseTo(save));
            AddUntilStep("wrapped in a tooltip", () => shown?.PresentedContent is Box);

            AddStep("text tooltip while showing", () => ToolTipService.SetToolTip(save, "First"));
            AddUntilStep("switches to the text", () => shownText == "First");
            AddStep("bound text changes", () => ToolTipService.SetToolTip(save, "Second"));
            AddUntilStep("follows while showing", () => shownText == "Second");
        }

        [Resolved]
        private osu.Framework.Game game { get; set; }

        [Test]
        public void WorksWithoutAWindow()
        {
            TextBlock bare = null;

            AddStep("element outside any window", () =>
            {
                Child = bare = new TextBlock { Text = "Bare" };
                ToolTipService.SetToolTip(bare, "Bare tooltip");
            });
            AddUntilStep("a popup layer joins the game", () => bare.IsLoaded && game.ChildrenOfType<PopupLayer>().Any());
        }

        [Test]
        public void TooltipKnowsTheElementItShowsFor()
        {
            RecordingToolTip recording = null;

            AddStep("own tooltip on Save", () => ToolTipService.SetToolTip(save, recording = new RecordingToolTip { Content = "Recorded" }));
            AddAssert("no target while hidden", () => recording.PlacementTarget == null);
            AddStep("hover Save", () => InputManager.MoveMouseTo(save));
            AddUntilStep("shows", () => shown == recording);
            AddAssert("target is Save, told before joining the layer", () => recording.PlacementTarget == save && recording.OpenedFor == save && !recording.WasInLayerWhenOpening);

            AddStep("leave", () => InputManager.MoveMouseTo(plain));
            AddAssert("target cleared on hide", () => shown == null && recording.PlacementTarget == null);

            AddStep("hover Save again", () => InputManager.MoveMouseTo(save));
            AddUntilStep("opened twice", () => recording.Openings == 2);
        }

        [Test]
        public void TextTooltipsKnowTheirTargetToo()
        {
            AddStep("hover inner text", () => InputManager.MoveMouseTo(inside));
            AddUntilStep("inner tooltip", () => shownText == "Inner tooltip");
            AddAssert("target is the text", () => shown.PlacementTarget == inside);
            AddStep("move to the card's padding", () => InputManager.MoveMouseTo(card, new Vector2(0, -card.DrawHeight / 2 + 5)));
            AddAssert("target follows", () => shownText == "Card tooltip" && shown.PlacementTarget == card);
        }

        private partial class RecordingToolTip : ToolTip
        {
            public Drawable OpenedFor;
            public bool WasInLayerWhenOpening;
            public int Openings;

            protected internal override void OnOpening()
            {
                Openings++;
                OpenedFor = PlacementTarget;
                WasInLayerWhenOpening = Parent != null;
            }
        }
    }
}
