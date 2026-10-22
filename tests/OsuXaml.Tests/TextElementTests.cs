using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using OsuXaml.Controls;
using Icon = OsuXaml.Controls.Icon;

namespace OsuXaml.Tests
{
    /// <summary>TextElement's inherited Foreground and font properties, as they reach TextBlocks, inlines, icons and control captions.</summary>
    [HeadlessTest]
    public partial class TextElementTests : TestScene
    {
        private const string sans = "TextElementTests.Sans";

        private static readonly Colour4 red = Colour4.FromHex("f38ba8");
        private static readonly Colour4 green = Colour4.FromHex("a6e3a1");
        private static readonly Colour4 blue = Colour4.FromHex("89b4fa");

        private StackPanel outer;
        private StackPanel inner;
        private TextBlock outerText;
        private TextBlock innerText;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("register a family", () =>
            {
                FontFamilies.Register(sans, 1.25f, FontWeight.Light, FontWeight.Regular, FontWeight.SemiBold);
                FontFamilies.AddAlias("Text Element Sans", sans);
            });

            AddStep("create", () =>
            {
                outer = new StackPanel();
                inner = new StackPanel();
                outer.Add(outerText = new TextBlock { Text = "outer" });
                outer.Add(inner);
                inner.Add(innerText = new TextBlock { Text = "inner" });
                Child = outer;
            });
            AddUntilStep("loaded", () => innerText.IsLoaded);
        }

        private void add(Drawable drawable, StackPanel panel = null) => AddStep($"add {drawable.GetType().Name}", () => (panel ?? inner).Add(drawable));

        private void loaded(params Drawable[] drawables) => AddUntilStep("loaded", () => drawables.All(d => d.IsLoaded));

        private void loaded(Func<Drawable> drawable) => AddUntilStep("loaded", () => drawable()?.IsLoaded == true);

        [Test]
        public void TheNearestElementWinsAndChangesFollow()
        {
            AddStep("set both panels", () =>
            {
                TextElement.SetForeground(outer, red);
                TextElement.SetForeground(inner, green);
            });
            AddAssert("each text takes the nearest", () => outerText.Colour == red && innerText.Colour == green);

            AddStep("change the inner one", () => TextElement.SetForeground(inner, blue));
            AddAssert("followed", () => innerText.Colour == blue && outerText.Colour == red);

            AddStep("clear the inner one", () => TextElement.ClearForeground(inner));
            AddAssert("the outer one reaches it", () => innerText.Colour == red);

            AddStep("clear the outer one", () => TextElement.ClearForeground(outer));
            AddAssert("back to the theme's text colour", () => innerText.Colour == Theme.Text && outerText.Colour == Theme.Text);

            AddStep("set a size through the properties object", () =>
            {
                TextElement.SetFontSize(outer, 13);
                TextElement.Get(outer).FontSize = 20;
            });
            AddAssert("followed", () => innerText.FontSize == 20 && outerText.FontSize == 20);
            AddAssert("clearing something never set adds nothing", () =>
            {
                TextElement.ClearFontWeight(innerText);
                return TextElement.Get(innerText) == null;
            });
        }

        [Test]
        public void ValuesSetOnTheTextWin()
        {
            TextBlock own = null;

            add(own = new TextBlock { Text = "own", Foreground = blue, FontSize = 30, FontWeight = FontWeight.Bold, FontFamily = "RobotoCondensed" });
            loaded(own);

            AddStep("set everything on the panel", () =>
            {
                TextElement.SetForeground(inner, red);
                TextElement.SetFontSize(inner, 12);
                TextElement.SetFontWeight(inner, FontWeight.Regular);
                TextElement.SetFontFamily(inner, "Roboto");
            });
            AddAssert("own values stay", () => own.Colour == blue && own.FontSize == 30 && own.Font.Weight == "Bold" && own.Font.Family == "RobotoCondensed");
            AddAssert("others take the panel's", () => innerText.Colour == red && innerText.FontSize == 12 && innerText.Font.Family == "Roboto");

            AddStep("hand the colour back", () => own.HasOwnForeground = false);
            AddAssert("inherited", () => own.Colour == red && !own.HasOwnForeground);
        }

        [Test]
        public void AColourOrFontSetDirectlyStaysAndComesBack()
        {
            TextBlock direct = null;

            add(direct = new TextBlock { Text = "direct", Colour = green, Font = FrameworkFont.Condensed.With(size: 22) });
            loaded(direct);
            AddAssert("kept while nothing is inherited", () => direct.Colour == green && direct.Font.Family == "RobotoCondensed" && direct.Font.Size == 22);

            AddStep("inherit colour and size", () =>
            {
                TextElement.SetForeground(outer, red);
                TextElement.SetFontSize(outer, 13);
            });
            AddAssert("inherited win", () => direct.Colour == red && direct.Font.Size == 13 && direct.Font.Family == "RobotoCondensed");

            AddStep("clear them", () =>
            {
                TextElement.ClearForeground(outer);
                TextElement.ClearFontSize(outer);
            });
            AddAssert("the direct ones come back", () => direct.Colour == green && direct.Font.Size == 22);

            AddStep("inherit again", () => TextElement.SetForeground(outer, red));
            AddStep("set a colour and font directly meanwhile", () =>
            {
                direct.Colour = blue;
                direct.Font = FrameworkFont.Regular.With(size: 18, weight: "Bold");
            });
            AddStep("change and clear what is inherited", () =>
            {
                TextElement.SetForeground(outer, Colour4.White);
                TextElement.SetFontSize(outer, 11);
            });
            AddAssert("inherited still wins", () => direct.Colour == Colour4.White && direct.Font.Size == 11 && direct.Font.Family == "Roboto");
            AddStep("clear", () =>
            {
                TextElement.ClearForeground(outer);
                TextElement.ClearFontSize(outer);
            });
            AddAssert("the latest direct ones come back", () => direct.Colour == blue && direct.Font.Size == 18 && direct.Font.Weight == "Bold");
        }

        [Test]
        public void TextThatSetsItOnItselfTakesIt()
        {
            AddStep("set on the text itself and on its panel", () =>
            {
                TextElement.SetForeground(innerText, blue);
                TextElement.SetForeground(inner, red);
                TextElement.SetFontWeight(innerText, FontWeight.Bold);
            });
            AddAssert("its own is the nearest", () => innerText.Colour == blue && innerText.Font.Weight == "Bold");

            AddStep("clear its own", () => TextElement.ClearForeground(innerText));
            AddAssert("the panel's", () => innerText.Colour == red);
        }

        [Test]
        public void IconsTakeTheForeground()
        {
            Icon plain = null, own = null, direct = null;

            add(plain = new Icon { Kind = "Solid.Check" });
            add(own = new Icon { Kind = "Solid.Check", Foreground = blue });
            add(direct = new Icon { Kind = "Solid.Check", Colour = green });
            loaded(plain, own, direct);

            AddStep("set on the panel", () => TextElement.SetForeground(outer, red));
            AddAssert("icons without their own take it", () => plain.Colour == red && direct.Colour == red && own.Colour == blue);

            AddStep("clear", () => TextElement.ClearForeground(outer));
            AddAssert("back", () => plain.Colour == Theme.Text && direct.Colour == green && own.Colour == blue);

            AddStep("set on an icon itself", () => TextElement.SetForeground(plain, blue));
            AddAssert("taken", () => plain.Colour == blue);
        }

        [Test]
        public void AnElementThatLoadedBeforeSettingAnythingStillReachesItsText()
        {
            Container middle = null;
            TextBlock deep = null;

            AddStep("add text below a container", () =>
            {
                middle = new Container { AutoSizeAxes = Axes.Both };
                middle.Add(deep = new TextBlock { Text = "deep" });
                inner.Add(middle);
            });
            loaded(() => deep);

            AddStep("set on the loaded outer panel", () => TextElement.SetForeground(outer, red));
            AddAssert("everything below follows", () => outerText.Colour == red && innerText.Colour == red && deep.Colour == red);

            AddStep("set on the loaded container in between", () => TextElement.SetForeground(middle, green));
            AddAssert("the nearer one wins", () => deep.Colour == green && innerText.Colour == red);

            AddStep("change the outer one", () => TextElement.SetForeground(outer, blue));
            AddAssert("the text below the container keeps listening to both", () => deep.Colour == green && innerText.Colour == blue);
            AddStep("clear the container", () => TextElement.ClearForeground(middle));
            AddAssert("the outer one again", () => deep.Colour == blue);
        }

        [Test]
        public void SetOwnFontKeepsTheWholeFont()
        {
            TextBlock fixedFont = null;

            add(fixedFont = new TextBlock { Text = "fixed" });
            AddStep("set its own font", () => fixedFont.SetOwnFont(FrameworkFont.Condensed.With(size: 18)));
            loaded(fixedFont);

            AddStep("set fonts on the panel", () =>
            {
                TextElement.SetFontFamily(inner, sans);
                TextElement.SetFontSize(inner, 10);
                TextElement.SetFontWeight(inner, FontWeight.Bold);
            });
            AddAssert("untouched", () => fixedFont.Font.Family == "RobotoCondensed" && fixedFont.Font.Size == 18 && fixedFont.Font.Weight == "Regular");
        }

        [Test]
        public void InheritedFontsAreEmSizesWithTheNearestWeight()
        {
            AddStep("set a registered family", () =>
            {
                TextElement.SetFontFamily(outer, "Text Element Sans, Segoe UI");
                TextElement.SetFontSize(outer, 12);
                TextElement.SetFontWeight(outer, FontWeight.Bold);
            });
            AddAssert("the alias resolves, the em size scales, Bold is the nearest the family has", () =>
                innerText.Font.Family == sans && innerText.Font.Size == 15 && innerText.FontSize == 12 && innerText.Font.Weight == "SemiBold");

            AddStep("clear them", () =>
            {
                TextElement.SetFontFamily(outer, null);
                TextElement.ClearFontSize(outer);
                TextElement.ClearFontWeight(outer);
            });
            AddAssert("back to the TextBlock's own font", () => innerText.Font.Family == "Roboto" && innerText.Font.Size == 16 && innerText.Font.Weight == "Regular");
        }

        [Test]
        public void InlineRunsUseTheInheritedFont()
        {
            TextBlock rich = null;

            AddStep("add rich text", () =>
            {
                rich = new TextBlock();
                rich.Inlines.Add(new Run("a"));
                rich.Inlines.Add(new Run("b") { FontWeight = FontWeight.Light });
                rich.Inlines.Add(new Run("c") { FontFamily = "Roboto", FontSize = 10 });
                inner.Add(rich);
            });
            loaded(() => rich);

            AddStep("set a registered family", () =>
            {
                TextElement.SetFontFamily(outer, sans);
                TextElement.SetFontSize(outer, 12);
            });
            AddUntilStep("laid out", () => rich.LaidOut?.GlyphStyles.Count == 3 && rich.LaidOut.GlyphStyles[0].Font.Family == sans);
            AddAssert("a plain run takes the TextBlock's font", () => rich.LaidOut.GlyphStyles[0].Font.Size == 15);
            AddAssert("a run's weight is the nearest the family has, at the same em size", () =>
            {
                var font = rich.LaidOut.GlyphStyles[1].Font;
                return font.Family == sans && font.Weight == "Light" && font.Size == 15;
            });
            AddAssert("a run's own family and size, in that family's scale", () =>
            {
                var font = rich.LaidOut.GlyphStyles[2].Font;
                return font.Family == "Roboto" && font.Size == 10;
            });
        }

        [Test]
        public void ControlCaptionsKeepTheControlsOwnForeground()
        {
            Button button = null;
            Button withContent = null;
            TextBlock contentText = null;
            Label label = null;
            Expander expander = null;

            AddStep("add controls", () =>
            {
                button = new Button { Content = "Save" };

                var stack = new StackPanel();
                stack.Add(contentText = new TextBlock { Text = "drawable content" });
                withContent = new Button { Content = stack };

                label = new Label { Content = "Name" };
                expander = new Expander { Header = "Details", Foreground = blue };

                inner.Add(button);
                inner.Add(withContent);
                inner.Add(label);
                inner.Add(expander);
            });
            AddUntilStep("loaded", () => button.PresentedContent?.IsLoaded == true && label.PresentedContent?.IsLoaded == true
                                         && expander.PresentedHeader?.IsLoaded == true && contentText.IsLoaded);

            AddStep("set a foreground around them", () => TextElement.SetForeground(outer, red));
            AddAssert("the button's caption keeps the button's colour", () => button.PresentedContent.Colour == Theme.AccentText);
            AddAssert("so does the header of a control with its own Foreground", () => expander.PresentedHeader.Colour == blue);
            AddAssert("a label without one takes it", () => label.PresentedContent.Colour == red);
            AddAssert("drawable content takes it as any text does", () => contentText.Colour == red);

            AddStep("set it on the button itself", () => TextElement.SetForeground(button, green));
            AddAssert("the caption follows", () => button.PresentedContent.Colour == green);
            AddStep("clear it on the button", () => TextElement.ClearForeground(button));
            AddAssert("the button's own again", () => button.PresentedContent.Colour == Theme.AccentText);

            AddStep("give the label a Foreground", () => label.Foreground = blue);
            AddAssert("its caption takes it", () => label.PresentedContent.Colour == blue);
            AddStep("change the outer one", () => TextElement.SetForeground(outer, green));
            AddAssert("the label keeps its own", () => label.PresentedContent.Colour == blue);
        }
    }

    /// <summary>FontFamilies' registry and CSS weight matching.</summary>
    [TestFixture]
    public class FontFamiliesTests
    {
        private static readonly FontWeight[] every =
        {
            FontWeight.Thin, FontWeight.ExtraLight, FontWeight.Light, FontWeight.SemiLight, FontWeight.Regular, FontWeight.Medium,
            FontWeight.SemiBold, FontWeight.Bold, FontWeight.ExtraBold, FontWeight.Black,
        };

        [TestCase(FontWeight.Thin, FontWeight.Light)]
        [TestCase(FontWeight.ExtraLight, FontWeight.Light)]
        [TestCase(FontWeight.Light, FontWeight.Light)]
        [TestCase(FontWeight.SemiLight, FontWeight.Light)]
        [TestCase(FontWeight.Regular, FontWeight.Regular)]
        [TestCase(FontWeight.Medium, FontWeight.Regular)]
        [TestCase(FontWeight.SemiBold, FontWeight.SemiBold)]
        [TestCase(FontWeight.Bold, FontWeight.Bold)]
        [TestCase(FontWeight.ExtraBold, FontWeight.Bold)]
        [TestCase(FontWeight.Black, FontWeight.Bold)]
        public void NearestWeightAsCssMatchesIt(FontWeight wanted, FontWeight expected)
            => Assert.That(FontFamilies.Nearest(wanted, new[] { FontWeight.Light, FontWeight.Regular, FontWeight.SemiBold, FontWeight.Bold }), Is.EqualTo(expected));

        [TestCase(FontWeight.Thin, FontWeight.Regular)]
        [TestCase(FontWeight.Light, FontWeight.Regular)]
        [TestCase(FontWeight.SemiLight, FontWeight.Regular)]
        [TestCase(FontWeight.Medium, FontWeight.Regular)]
        [TestCase(FontWeight.SemiBold, FontWeight.Bold)]
        [TestCase(FontWeight.Black, FontWeight.Bold)]
        public void RobotoHasRegularAndBold(FontWeight wanted, FontWeight expected)
            => Assert.That(FontFamilies.WeightName("Roboto", wanted), Is.EqualTo(FontFamilies.Name(expected)));

        [Test]
        public void TheEdgesOfCssMatching()
        {
            // 400 to 500 look up to 500 first, then lighter, then heavier.
            Assert.That(FontFamilies.Nearest(FontWeight.Regular, new[] { FontWeight.Medium, FontWeight.Light, FontWeight.SemiBold }), Is.EqualTo(FontWeight.Medium));
            Assert.That(FontFamilies.Nearest(FontWeight.Regular, new[] { FontWeight.Light, FontWeight.SemiBold }), Is.EqualTo(FontWeight.Light));
            Assert.That(FontFamilies.Nearest(FontWeight.Medium, new[] { FontWeight.SemiBold, FontWeight.Black }), Is.EqualTo(FontWeight.SemiBold));

            // Lighter weights look lighter first, heavier ones heavier first.
            Assert.That(FontFamilies.Nearest(FontWeight.Light, new[] { FontWeight.Thin, FontWeight.Regular }), Is.EqualTo(FontWeight.Thin));
            Assert.That(FontFamilies.Nearest(FontWeight.SemiBold, new[] { FontWeight.Regular, FontWeight.Black }), Is.EqualTo(FontWeight.Black));
            Assert.That(FontFamilies.Nearest(FontWeight.Black, new[] { FontWeight.Thin }), Is.EqualTo(FontWeight.Thin));
            Assert.That(FontFamilies.Nearest(FontWeight.Bold, new FontWeight[0]), Is.EqualTo(FontWeight.Bold));
        }

        [Test]
        public void UnregisteredFamiliesTakeTheWeightAsWritten()
        {
            foreach (var weight in every)
                Assert.That(FontFamilies.WeightName("FontFamiliesTests.Unknown", weight), Is.EqualTo(FontFamilies.Name(weight)));

            Assert.That(FontFamilies.SizeScale("FontFamiliesTests.Unknown"), Is.EqualTo(1));
            Assert.That(FontFamilies.SizeScale(null), Is.EqualTo(1));
        }

        [Test]
        public void NamesNumbersAndParsing()
        {
            foreach (var weight in every)
            {
                Assert.That(FontFamilies.Parse(FontFamilies.Name(weight)), Is.EqualTo(weight));
                Assert.That(FontFamilies.Parse(FontFamilies.Name(weight).ToLowerInvariant()), Is.EqualTo(weight));
            }

            Assert.That(every.Select(FontFamilies.Number), Is.Ordered.And.Unique);
            Assert.That(FontFamilies.Number(FontWeight.Regular), Is.EqualTo(400));
            Assert.That(FontFamilies.Number(FontWeight.Bold), Is.EqualTo(700));
            Assert.That(FontWeight.Normal, Is.EqualTo(FontWeight.Regular));
            Assert.That(FontFamilies.Parse(null), Is.EqualTo(FontWeight.Regular));
            Assert.That(FontFamilies.Parse("Heavyish"), Is.EqualTo(FontWeight.Regular));
        }

        [Test]
        public void RegisteringAliasesAndScales()
        {
            const string family = "FontFamiliesTests.Selawik";

            FontFamilies.Register(family, FontFamilies.LineBoxPerEm(2457, 2048), FontWeight.Light, FontWeight.Regular, FontWeight.Light);
            FontFamilies.AddAlias("FontFamiliesTests Segoe UI", family);

            Assert.That(FontFamilies.SizeScale(family), Is.EqualTo(2457f / 2048).Within(1e-6));
            Assert.That(FontFamilies.Resolve("fontfamiliestests segoe ui"), Is.EqualTo(family));
            Assert.That(FontFamilies.Resolve("FontFamiliesTests.Other"), Is.EqualTo("FontFamiliesTests.Other"));
            Assert.That(FontFamilies.Resolve(null), Is.Null);
            Assert.That(FontFamilies.WeightName(family, FontWeight.Bold), Is.EqualTo("Regular"));

            // Registering again replaces it; a scale that isn't positive means 1.
            FontFamilies.Register(family, 0, FontWeight.Bold);
            Assert.That(FontFamilies.SizeScale(family), Is.EqualTo(1));
            Assert.That(FontFamilies.WeightName(family, FontWeight.Regular), Is.EqualTo("Bold"));

            Assert.Throws<System.ArgumentException>(() => FontFamilies.Register("", 1));
        }

        [Test]
        public void TextBlockFontSizeIsTheEmSizeOfARegisteredFamily()
        {
            const string family = "FontFamiliesTests.Sans";
            FontFamilies.Register(family, 1.25f, FontWeight.Light, FontWeight.Regular, FontWeight.SemiBold);
            FontFamilies.AddAlias("FontFamiliesTests Sans Alias", family);

            // osu!framework falls back to a font it has for glyphs a family lacks, so the font asked for is what is checked.
            var text = new TextBlock { FontSize = 14 };
            Assert.That(text.Font.Size, Is.EqualTo(14), "Roboto keeps osu!framework's size");

            text.FontFamily = "FontFamiliesTests Sans Alias, Segoe UI";
            Assert.That(text.Font.Family, Is.EqualTo(family));
            Assert.That(text.Font.Size, Is.EqualTo(17.5f), "the em size stays when the family changes");
            Assert.That(text.FontSize, Is.EqualTo(14));

            text.FontSize = 16;
            Assert.That(text.Font.Size, Is.EqualTo(20));

            text.FontWeight = FontWeight.Bold;
            Assert.That(text.Font.Weight, Is.EqualTo("SemiBold"));
            Assert.That(text.FontWeight, Is.EqualTo(FontWeight.Bold), "the weight asked for is kept");

            text.FontFamily = "Roboto";
            Assert.That(text.Font.Weight, Is.EqualTo("Bold"), "a family with the weight asked for takes it");
            Assert.That(text.Font.Size, Is.EqualTo(16));
        }

        [Test]
        public void InlinesUseTheSameRules()
        {
            const string family = "FontFamiliesTests.InlineSans";
            FontFamilies.Register(family, 2, FontWeight.Regular, FontWeight.Black);

            var outer = new TextStyle(FrameworkFont.Regular.With(size: 16), null, TextDecorations.None);

            var run = new Run("x") { FontFamily = family, FontWeight = FontWeight.Bold };
            var font = run.StyleWithin(outer).Font;
            Assert.That(font.Family, Is.EqualTo(family));
            Assert.That(font.Size, Is.EqualTo(32), "the em size of the text around, in this family's scale");
            Assert.That(font.Weight, Is.EqualTo("Black"));

            var sized = new Run("y") { FontSize = 10, FontWeight = FontWeight.SemiBold };
            font = sized.StyleWithin(outer).Font;
            Assert.That(font.Family, Is.EqualTo("Roboto"));
            Assert.That(font.Size, Is.EqualTo(10));
            Assert.That(font.Weight, Is.EqualTo("Bold"));
        }
    }
}
