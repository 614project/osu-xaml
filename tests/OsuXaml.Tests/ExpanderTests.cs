using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;
using TextBox = OsuXaml.Controls.TextBox;

namespace OsuXaml.Tests
{
    /// <summary>Expander, GroupBox, Label and Hyperlink in a headless host with real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class ExpanderTests : ManualInputManagerTestScene
    {
        private static bool near(float a, float b) => System.Math.Abs(a - b) < 1;

        [Test]
        public void ExpanderTogglesAndAnimatesItsHeight()
        {
            var expanded = new BindableBool();
            Expander expander = null;
            Container body = null, below = null;
            var events = new List<string>();

            AddStep("create", () =>
            {
                events.Clear();
                expander = new Expander { Header = "Details", Child = body = new Container { RelativeSizeAxes = Axes.X, Height = 100 } };
                XamlBinding.TwoWay(expander.IsExpanded, expanded);
                expander.Expanded += () => events.Add("expanded");
                expander.Collapsed += () => events.Add("collapsed");

                var panel = new StackPanel { Width = 300 };
                panel.Add(expander);
                panel.Add(below = new Container { Height = 20 });
                Child = panel;
            });

            float headerHeight = 0;
            AddAssert("collapsed: only the header", () =>
            {
                headerHeight = expander.DrawHeight;
                return headerHeight >= 30 && headerHeight < 50 && !body.Parent.IsPresent;
            });
            AddAssert("sibling right below the header", () => near(below.ScreenSpaceDrawQuad.TopLeft.Y, expander.ScreenSpaceDrawQuad.BottomLeft.Y));

            AddStep("click the header", () =>
            {
                InputManager.MoveMouseTo(expander.PresentedHeader);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("expanded and view model follows", () => expander.IsExpanded.Value && expanded.Value && events.SequenceEqual(new[] { "expanded" }));
            AddUntilStep("reaches header + content + padding", () => near(expander.DrawHeight, headerHeight + 100 + expander.Padding.TotalVertical));
            AddAssert("content visible below the header", () => body.Parent.IsPresent && body.ScreenSpaceDrawQuad.TopLeft.Y >= expander.PresentedHeader.ScreenSpaceDrawQuad.BottomLeft.Y);
            AddAssert("content keeps its height", () => near(body.DrawHeight, 100));
            AddUntilStep("chevron turned", () => near(expander.ChildrenOfType<SpriteIcon>().Single().Rotation, 90));
            AddAssert("sibling moved down", () => near(below.ScreenSpaceDrawQuad.TopLeft.Y, expander.ScreenSpaceDrawQuad.BottomLeft.Y));

            AddStep("view model collapses", () => expanded.Value = false);
            AddAssert("collapsed event", () => events.SequenceEqual(new[] { "expanded", "collapsed" }));
            AddUntilStep("back to the header height", () => near(expander.DrawHeight, headerHeight) && !body.Parent.IsPresent);

            AddStep("press Space while focused", () =>
            {
                InputManager.MoveMouseTo(expander.PresentedHeader);
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.Space);
            });
            AddAssert("click opened, Space closed", () => !expander.IsExpanded.Value && events.Count == 4);
        }

        [Test]
        public void ExpanderSizesToItsHeaderWhenCollapsed()
        {
            Expander expander = null;

            AddStep("create unsized", () => Child = expander = new Expander { Header = "Header", Child = new Container { Size = new Vector2(400, 60) } });
            AddAssert("header width only", () => expander.DrawWidth < 200 && expander.DrawWidth > 50);
            AddStep("expand", () => expander.IsExpanded.Value = true);
            AddUntilStep("content width", () => near(expander.DrawWidth, 400 + expander.Padding.TotalHorizontal));
        }

        [Test]
        public void GroupBoxPutsTheHeaderAboveBorderedContent()
        {
            GroupBox box = null;
            Container content = null;

            AddStep("create", () =>
            {
                box = new GroupBox { Header = "Account", Width = 300, Child = content = new Container { RelativeSizeAxes = Axes.X, Height = 50 } };
                var panel = new StackPanel { Width = 300 };
                panel.Add(box);
                Child = panel;
            });

            AddAssert("height = header row + border + padding + content", () =>
            {
                float headerRow = box.PresentedHeader.DrawHeight + 6; // header padding
                return near(box.DrawHeight, headerRow + 2 + box.Padding.TotalVertical + 50);
            });
            AddAssert("header above the bordered box", () =>
            {
                float headerRow = box.PresentedHeader.DrawHeight + 6;
                float boxTop = box.ScreenSpaceDrawQuad.TopLeft.Y + headerRow;
                return box.PresentedHeader.ScreenSpaceDrawQuad.BottomLeft.Y <= boxTop
                       && near(content.ScreenSpaceDrawQuad.TopLeft.Y, boxTop + 1 + box.Padding.Top);
            });
            AddAssert("content inset by padding and border", () => near(content.DrawWidth, 300 - 2 - box.Padding.TotalHorizontal));
            AddAssert("header is subdued text", () => box.PresentedHeader is TextBlock t && t.Colour == Theme.SubtleText);
        }

        [Test]
        public void LabelFocusesItsTargetOnClickAndAccessKey()
        {
            Label label = null;
            TextBox box = null, other = null;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300, Spacing = 8 };
                panel.Add(label = new Label { Content = "_Name:" });
                panel.Add(box = new TextBox());
                panel.Add(other = new TextBox());
                label.Target = box;
                Child = panel;
            });

            AddAssert("underscore hidden, key parsed", () => ((TextBlock)label.PresentedContent).Text == "Name:" && label.AccessKey == Key.N);

            AddStep("click the label", () =>
            {
                InputManager.MoveMouseTo(label);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("target focused", () => box.HasFocus);

            AddStep("focus the other box", () =>
            {
                InputManager.MoveMouseTo(other);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("other focused", () => other.HasFocus);

            AddStep("press Alt+N", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.N);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("access key focuses the target", () => box.HasFocus);
        }

        [Test]
        public void AccessTextParsing()
        {
            AddAssert("first underscore marks the key", () => Label.ParseAccessText("_Save as", out char? k) == "Save as" && k == 'S');
            AddAssert("double underscore is literal", () => Label.ParseAccessText("snake__case _x", out char? k) == "snake_case x" && k == 'x');
            AddAssert("no underscore", () => Label.ParseAccessText("Plain", out char? k) == "Plain" && k == null);
        }

        [Test]
        public void HyperlinkClicksNavigatesAndUnderlinesOnHover()
        {
            TestHyperlink link = null, handled = null, wide = null;
            var requested = new List<string>();
            int clicks = 0;

            AddStep("create", () =>
            {
                requested.Clear();
                clicks = 0;

                var panel = new StackPanel { Width = 400, Spacing = 10 };
                panel.Add(link = new TestHyperlink { Text = "osu!framework", NavigateUri = "https://github.com/ppy/osu-framework" });
                panel.Add(handled = new TestHyperlink { Text = "handled", NavigateUri = "https://example.com" });
                panel.Add(wide = new TestHyperlink { Text = "stretched", NavigateUri = "https://example.org" });
                Layout.SetHorizontalAlignment(wide, HorizontalAlignment.Stretch);
                link.Click += () => clicks++;
                wide.Click += () => clicks++;
                handled.RequestNavigate += requested.Add;
                Child = panel;
            });

            AddAssert("sized to its text, not the panel", () => link.DrawWidth < 200 && near(link.DrawWidth, link.PresentedContent.DrawWidth));

            AddStep("hover", () => InputManager.MoveMouseTo(link));
            AddUntilStep("underline and hover colour", () => ((TextBlock)link.PresentedContent).Colour == link.HoverForeground && link.Underline.Alpha > 0.99f);
            AddAssert("underline spans the text", () => near(link.Underline.DrawWidth, link.PresentedContent.DrawWidth));

            AddStep("click", () => InputManager.Click(MouseButton.Left));
            AddAssert("click raised and uri opened", () => clicks == 1 && link.Navigated.SequenceEqual(new[] { "https://github.com/ppy/osu-framework" }));

            AddStep("click the handled link", () =>
            {
                InputManager.MoveMouseTo(handled);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("RequestNavigate handles it instead", () => requested.SequenceEqual(new[] { "https://example.com" }) && handled.Navigated.Count == 0);
            AddUntilStep("first link back to normal", () => ((TextBlock)link.PresentedContent).Colour == Theme.Accent && link.Underline.Alpha < 0.01f);

            AddAssert("stretched link fills the row", () => near(wide.DrawWidth, 400));
            AddStep("click the stretched link right of its text", () =>
            {
                InputManager.MoveMouseTo(wide, new Vector2(150, 0));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("only the text is the link", () => clicks == 1 && wide.Navigated.Count == 0);
            AddStep("hover its text", () => InputManager.MoveMouseTo(wide.PresentedContent));
            AddUntilStep("underline spans the text, not the row", () => wide.Underline.Alpha > 0.99f && near(wide.Underline.DrawWidth, wide.PresentedContent.DrawWidth));
        }

        private partial class TestHyperlink : Hyperlink
        {
            public readonly List<string> Navigated = new List<string>();

            public Drawable Underline => Chrome.Children.OfType<osu.Framework.Graphics.Shapes.Box>().Last();

            protected override void Navigate(string uri) => Navigated.Add(uri);
        }
    }
}
