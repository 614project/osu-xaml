using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>The Cursor property and the built-in cursors, written the way a WPF developer would, through the generator.</summary>
    [TestFixture]
    public class CursorGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using OsuXaml.Controls;

namespace Demo
{
    public class Vm
    {
        public Bindable<Cursor> BusyCursor { get; } = new Bindable<Cursor>(Cursors.Wait);
    }

    public partial class View : CompositeDrawable
    {
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        private static string compiles(string content) => GeneratorRunner.Compiles(code_behind, header + "\n" + content + "\n</CompositeDrawable>");

        private static Microsoft.CodeAnalysis.Diagnostic error(string content) => GeneratorRunner.SingleError(code_behind, header + "\n" + content + "\n</CompositeDrawable>");

        [Test]
        public void AnyElementTakesACursorByName()
        {
            string source = compiles(@"
  <StackPanel>
    <Border Cursor=""Hand""><TextBlock Text=""Card"" /></Border>
    <Button Content=""Busy"" Cursor=""{Bind BusyCursor}"" />
    <SpriteText Text=""osu!framework"" Cursor=""IBeam"" />
  </StackPanel>");

            Assert.That(source, Does.Contain("global::OsuXaml.Controls.CursorService.SetCursor("));
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.Cursors.Hand"));
            Assert.That(source, Does.Contain("global::OsuXaml.Controls.Cursors.IBeam"));
            Assert.That(source, Does.Contain("BusyCursor"));
        }

        [Test]
        public void MisspeltCursorIsABuildErrorListingTheShapes()
        {
            var diagnostic = error(@"<Border Cursor=""Hnad"" />");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0005"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("Hnad").And.Contain("Hand").And.Contain("SizeWE"));
        }
    }

    /// <summary>Which cursor the pointer gets over elements with and without one, while dragging, and under popups and dialogs.</summary>
    [HeadlessTest]
    public partial class CursorTests : ManualInputManagerTestScene
    {
        private Window window;
        private Hyperlink link;
        private Hyperlink disabledLink;
        private TextBox box;
        private Border card;
        private TextBlock cardText;
        private TextBox cardBox;
        private TextBlock plain;
        private Grid grid;
        private GridSplitter splitter;
        private Box right;
        private StackPanel panel;

        private Cursor cursor => window.PopupLayer.CurrentCursor;

        private Drawable linkText => (Drawable)link.PresentedContent;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create", () =>
            {
                CursorService.OverrideCursor = null;

                link = new Hyperlink { Text = "Open the docs" };
                Layout.SetHorizontalAlignment(link, HorizontalAlignment.Stretch);
                disabledLink = new Hyperlink { Text = "Unavailable", IsEnabled = false };
                box = new TextBox { Width = 200 };

                cardText = new TextBlock { Text = "Click the card" };
                cardBox = new TextBox { Width = 200 };
                var cardPanel = new StackPanel { Spacing = 10 };
                cardPanel.Add(cardText);
                cardPanel.Add(cardBox);
                card = new Border { Padding = new MarginPadding(20), Background = Theme.Surface, Child = cardPanel };
                CursorService.SetCursor(card, Cursors.Hand);

                plain = new TextBlock { Text = "Nothing special" };

                grid = new Grid { Width = 405, Height = 100, ColumnDefinitions = GridLengthCollection.Parse("*,Auto,*") };
                grid.Add(new Box());
                grid.Add(splitter = new GridSplitter { ShowsPreview = true });
                Grid.SetColumn(splitter, 1);
                grid.Add(right = new Box());
                Grid.SetColumn(right, 2);

                panel = new StackPanel { Padding = new MarginPadding(40), Spacing = 20 };
                panel.Add(link);
                panel.Add(disabledLink);
                panel.Add(box);
                panel.Add(card);
                panel.Add(plain);
                panel.Add(grid);

                Child = window = new Window { Child = panel };
            });

            AddUntilStep("loaded", () => grid.ColumnDefinitions[1].ActualLength == 5 && box.DrawWidth == 200);
            AddStep("mouse on plain text", () => InputManager.MoveMouseTo(plain));
        }

        [TearDownSteps]
        public void TearDownSteps() => AddStep("no override", () => CursorService.OverrideCursor = null);

        [Test]
        public void ArrowWhereNothingAsksForACursor()
        {
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);
        }

        [Test]
        public void HyperlinkShowsTheHandUnlessDisabled()
        {
            AddStep("hover the link", () => InputManager.MoveMouseTo(linkText));
            AddUntilStep("hand", () => cursor == Cursors.Hand);

            AddStep("hover the disabled link", () => InputManager.MoveMouseTo(disabledLink));
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);

            // Only the text is the link, even when the control is stretched wider.
            AddStep("hover beside the link text", () => InputManager.MoveMouseTo(link, new Vector2(link.DrawWidth / 2 - 5, 0)));
            AddAssert("stretched wider than its text", () => link.DrawWidth > linkText.DrawWidth + 20);
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);
        }

        [Test]
        public void TextBoxShowsTheTextCursor()
        {
            AddStep("hover the text box", () => InputManager.MoveMouseTo(box));
            AddUntilStep("text cursor", () => cursor == Cursors.IBeam);
        }

        [Test]
        public void ElementCursorCoversItsContentButNotControlsWithTheirOwn()
        {
            AddStep("hover the card's text", () => InputManager.MoveMouseTo(cardText));
            AddUntilStep("hand", () => cursor == Cursors.Hand);

            AddStep("hover the card's padding", () => InputManager.MoveMouseTo(card, new Vector2(0, -card.DrawHeight / 2 + 5)));
            AddUntilStep("hand", () => cursor == Cursors.Hand);

            AddStep("hover the text box in the card", () => InputManager.MoveMouseTo(cardBox));
            AddUntilStep("text cursor", () => cursor == Cursors.IBeam);
        }

        [Test]
        public void ElementCursorWinsOverTheBuiltInOne()
        {
            AddStep("arrow on the link", () => CursorService.SetCursor(link, Cursors.Arrow));
            AddStep("hover the link", () => InputManager.MoveMouseTo(linkText));
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);

            AddStep("clear it", () => CursorService.SetCursor(link, null));
            AddUntilStep("hand again", () => cursor == Cursors.Hand);
            AddAssert("nothing set", () => CursorService.GetCursor(link) == null);
        }

        [Test]
        public void GridSplitterShowsResizeArrowsAndKeepsThemWhileDragging()
        {
            AddStep("hover the splitter", () => InputManager.MoveMouseTo(splitter));
            AddUntilStep("left-right arrows", () => cursor == Cursors.SizeWE);

            AddStep("press", () => InputManager.PressButton(MouseButton.Left));
            AddStep("drag onto the right column", () => InputManager.MoveMouseTo(right));
            AddAssert("dragging", () => splitter.IsDragging);
            AddUntilStep("still left-right arrows", () => cursor == Cursors.SizeWE);

            // With ShowsPreview the splitter catches up with the pointer on release, so it is still under it.
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("resized", () => grid.ColumnDefinitions[0].ActualLength > 250);
            AddStep("move away", () => InputManager.MoveMouseTo(plain));
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);
        }

        [Test]
        public void SplitterCursorFollowsItsDirectionAndState()
        {
            AddAssert("rows get up-down arrows", () => defaultCursor(new GridSplitter { ResizeDirection = GridResizeDirection.Rows }) == Cursors.SizeNS);
            AddAssert("columns get left-right arrows", () => defaultCursor(new GridSplitter { ResizeDirection = GridResizeDirection.Columns }) == Cursors.SizeWE);
            AddAssert("disabled gets none", () => defaultCursor(new GridSplitter { IsEnabled = false }) == null);
        }

        [Test]
        public void OverrideCursorWinsEverywhere()
        {
            AddStep("override with wait", () => CursorService.OverrideCursor = Cursors.Wait);
            AddStep("hover the link", () => InputManager.MoveMouseTo(linkText));
            AddUntilStep("wait", () => cursor == Cursors.Wait);

            AddStep("no override", () => CursorService.OverrideCursor = null);
            AddUntilStep("hand", () => cursor == Cursors.Hand);
        }

        [Test]
        public void OpenPopupCoversTheCursorsBelowIt()
        {
            Popup popup = null;

            AddStep("open a popup over the link", () =>
            {
                popup = new Popup { PlacementTarget = linkText, Placement = PlacementMode.Center, Child = new Border { Width = 300, Height = 60, Background = Theme.Surface } };
                panel.Add(popup);
                popup.IsOpen.Value = true;
            });
            AddUntilStep("shown", () => popup.Root.IsPresent && popup.Root.DrawWidth > 0);

            AddStep("hover the link under it", () => InputManager.MoveMouseTo(linkText));
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);

            AddStep("close", () => popup.IsOpen.Value = false);
            AddUntilStep("hand", () => cursor == Cursors.Hand);
        }

        [Test]
        public void ModalDialogBlocksCursorsBelowItButNotItsOwn()
        {
            Dialog dialog = null;
            Hyperlink inside = null;

            AddStep("show a dialog", () =>
            {
                inside = new Hyperlink { Text = "Inside the dialog" };
                dialog = new Dialog { Title = "Links", Content = inside };
                dialog.ShowDialog(window);
            });
            AddUntilStep("shown", () => inside.IsLoaded && inside.DrawWidth > 0 && dialog.Alpha == 1);

            AddStep("hover the link inside", () => InputManager.MoveMouseTo(inside.PresentedContent as Drawable));
            AddUntilStep("hand", () => cursor == Cursors.Hand);

            AddStep("hover the link below the dialog", () => InputManager.MoveMouseTo(linkText));
            AddUntilStep("arrow", () => cursor == Cursors.Arrow);

            AddStep("close", () => dialog.Close());
            AddUntilStep("hand below", () => cursor == Cursors.Hand);
        }

        private static Cursor defaultCursor(Drawable control) => ((IHasDefaultCursor)control).DefaultCursor;
    }
}
