using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using OsuXaml.Controls;
using OsuXaml.Tests.Views;
using osuTK.Graphics;

namespace OsuXaml.Tests
{
    /// <summary>A part of the tree with a theme of its own, as a theme library's scope (WinUI's RequestedTheme) would be.</summary>
    public partial class ThemeScope : Container
    {
        public ThemeScope()
        {
            RelativeSizeAxes = Axes.Both;
        }

        public string Theme { get; set; }
    }

    /// <summary>Themes by name: an element takes the theme of the nearest <see cref="ThemeScope"/> around it, or the default one.</summary>
    public sealed class TestThemeProvider : IThemeResourceProvider
    {
        public readonly Dictionary<string, Dictionary<string, object>> Themes = new Dictionary<string, Dictionary<string, object>>();

        public string DefaultTheme = "Dark";

        // Weak, so counting lookups never keeps an element alive.
        private readonly ConditionalWeakTable<Drawable, Dictionary<string, int>> lookups = new ConditionalWeakTable<Drawable, Dictionary<string, int>>();

        public event Action ResourcesChanged;

        public TestThemeProvider Set(string theme, string key, object value)
        {
            if (!Themes.TryGetValue(theme, out var resources))
                Themes[theme] = resources = new Dictionary<string, object>();

            resources[key] = value;
            return this;
        }

        public bool TryGetResource(Drawable element, string key, out object value)
        {
            lock (lookups)
            {
                var counts = lookups.GetOrCreateValue(element);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            string theme = DefaultTheme;

            for (var d = element; d != null; d = d.Parent)
            {
                if (d is ThemeScope scope && scope.Theme != null)
                {
                    theme = scope.Theme;
                    break;
                }
            }

            value = null;
            return Themes.TryGetValue(theme, out var resources) && resources.TryGetValue(key, out value);
        }

        public void Raise() => ResourcesChanged?.Invoke();

        public int LookupsOf(Drawable element, string key)
        {
            lock (lookups)
                return lookups.TryGetValue(element, out var counts) ? counts.GetValueOrDefault(key) : 0;
        }

        public void ClearLookups()
        {
            lock (lookups)
                lookups.Clear();
        }
    }

    /// <summary>{ThemeResource} at run time: values from the nearest theme scope, following changes, and the generated view.</summary>
    [HeadlessTest]
    public partial class ThemeResourceTests : TestScene
    {
        private static readonly Colour4 dark_card = Colour4.FromHex("2b2b2b");
        private static readonly Colour4 light_card = Colour4.FromHex("fbfbfb");
        private static readonly Colour4 dark_text = Colour4.FromHex("ffffff");
        private static readonly Colour4 light_text = Colour4.FromHex("1b1b1b");

        private TestThemeProvider provider;

        [SetUpSteps]
        public void SetUpSteps() => AddStep("add the theme", () =>
        {
            ThemeResources.RemoveProvider(provider);

            provider = new TestThemeProvider()
                       .Set("Dark", "Card", dark_card).Set("Light", "Card", light_card)
                       .Set("Dark", "Text", new SolidColorBrush(dark_text)).Set("Light", "Text", new SolidColorBrush(light_text))
                       .Set("Dark", "NotAColour", "#ff0000").Set("Light", "NotAColour", "#ff0000");

            ThemeResources.AddProvider(provider);
        });

        [TearDownSteps]
        public void TearDownSteps() => AddStep("remove the theme", () => ThemeResources.RemoveProvider(provider));

        [Test]
        public void TheValueComesFromTheNearestScopeOnceLoaded()
        {
            Border border = null;

            AddStep("bind before the tree", () =>
            {
                border = new Border { Width = 50, Height = 50 };
                ThemeResources.Bind<Colour4>(border, "Card", c => border.Background = c);
            });
            AddAssert("the default theme's value at once", () => border.Background == dark_card);

            AddStep("add inside a light scope", () => Child = new ThemeScope { Theme = "Dark", Child = new ThemeScope { Theme = "Light", Child = border } });
            AddUntilStep("loaded", () => border.IsLoaded);
            AddAssert("the nearest scope's value", () => border.Background == light_card);
        }

        [Test]
        public void LoadedElementsFollowChanges()
        {
            Border border = null, other = null;
            ThemeScope scope = null, otherScope = null;

            AddStep("create", () =>
            {
                border = new Border { Width = 50, Height = 50 };
                other = new Border { Width = 50, Height = 50 };
                ThemeResources.Bind<Colour4>(border, "Card", c => border.Background = c);
                ThemeResources.Bind<Colour4>(other, "Card", c => other.Background = c);

                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        scope = new ThemeScope { Theme = "Light", Child = border },
                        otherScope = new ThemeScope { Theme = "Light", Child = other },
                    },
                };
            });
            AddUntilStep("loaded", () => border.IsLoaded && other.IsLoaded);

            AddStep("change the light card", () =>
            {
                provider.Set("Light", "Card", Colour4.Red);
                provider.Raise();
            });
            AddAssert("both follow at once", () => border.Background == Colour4.Red && other.Background == Colour4.Red);

            AddStep("one scope turns dark", () =>
            {
                scope.Theme = otherScope.Theme = "Dark";
                ThemeResources.Refresh(scope);
            });
            AddAssert("only inside it", () => border.Background == dark_card && other.Background == Colour4.Red);

            AddStep("refresh everything", () => ThemeResources.Refresh());
            AddAssert("the other one too", () => other.Background == dark_card);
        }

        [Test]
        public void ChangesFromAnotherThreadApplyOnTheUpdateThread()
        {
            Border border = null;
            var threads = new List<bool>();

            AddStep("create", () =>
            {
                border = new Border { Width = 50, Height = 50 };
                ThemeResources.Bind<Colour4>(border, "Card", c =>
                {
                    lock (threads)
                        threads.Add(ThreadSafety.IsUpdateThread);
                    border.Background = c;
                });
                Child = new ThemeScope { Theme = "Light", Child = border };
            });
            AddUntilStep("loaded", () => border.IsLoaded && border.Background == light_card);

            AddStep("raise on another thread", () =>
            {
                lock (threads)
                    threads.Clear();

                Task.Run(() =>
                {
                    provider.Set("Light", "Card", Colour4.Green);
                    provider.Raise();
                    provider.Raise();
                }).Wait();
            });
            AddUntilStep("applied", () => border.Background == Colour4.Green);
            AddAssert("on the update thread, once for both changes", () =>
            {
                lock (threads)
                    return threads.Count == 1 && threads.All(t => t);
            });
        }

        [Test]
        public void UnknownKeysAndValuesThatDontConvertLeaveThePropertyAlone()
        {
            Border border = null;
            int applied = 0;

            AddStep("create", () =>
            {
                border = new Border { Width = 50, Height = 50, Background = Colour4.Pink };
                ThemeResources.Bind<Colour4>(border, "NoSuchKey", c => applied++);
                ThemeResources.Bind<Colour4>(border, "NotAColour", c => applied++);
                Child = new ThemeScope { Theme = "Light", Child = border };
            });
            AddUntilStep("loaded", () => border.IsLoaded);
            AddStep("raise", () => provider.Raise());
            AddAssert("never applied", () => applied == 0 && border.Background == Colour4.Pink);
        }

        [Test]
        public void LaterProvidersWinAndRemovingOneFallsBack()
        {
            Border border = null;
            var accent = new TestThemeProvider().Set("Light", "Card", Colour4.Orange);

            AddStep("create", () =>
            {
                border = new Border { Width = 50, Height = 50 };
                ThemeResources.Bind<Colour4>(border, "Card", c => border.Background = c);
                Child = new ThemeScope { Theme = "Light", Child = border };
            });
            AddUntilStep("loaded", () => border.IsLoaded && border.Background == light_card);

            AddStep("add another provider", () => ThemeResources.AddProvider(accent));
            AddAssert("it wins", () => border.Background == Colour4.Orange && ThemeResources.Providers[^1] == accent);

            AddStep("move the first one ahead again", () => ThemeResources.AddProvider(provider));
            AddAssert("the first wins", () => border.Background == light_card && ThemeResources.Providers.Count(p => p == provider) == 1);

            AddStep("remove the first", () => ThemeResources.RemoveProvider(provider));
            AddAssert("the other one's value", () => border.Background == Colour4.Orange);

            AddStep("remove the other", () => ThemeResources.RemoveProvider(accent));
            AddStep("its changes are no longer heard", () =>
            {
                accent.Set("Light", "Card", Colour4.Purple);
                accent.Raise();
            });
            AddAssert("kept", () => border.Background == Colour4.Orange);
            AddStep("put the theme back", () => ThemeResources.AddProvider(provider));
        }

        [Test]
        public void StoppedBindingsAndDisposedElementsAreDropped()
        {
            Border stopped = null, removed = null;
            ThemeScope scope = null;
            IDisposable binding = null;
            int stoppedApplied = 0, removedApplied = 0, tracked = 0;

            AddStep("create", () =>
            {
                stopped = new Border { Width = 50, Height = 50 };
                removed = new Border { Width = 50, Height = 50 };
                binding = ThemeResources.Bind<Colour4>(stopped, "Card", c => stoppedApplied++);
                ThemeResources.Bind<Colour4>(removed, "Card", c => removedApplied++);

                var flow = new FillFlowContainer { RelativeSizeAxes = Axes.Both };
                flow.Add(stopped);
                flow.Add(removed);
                Child = scope = new ThemeScope { Theme = "Light", Child = flow };
            });
            AddUntilStep("loaded", () => stopped.IsLoaded && removed.IsLoaded);
            AddAssert("applied on creation and on load", () => stoppedApplied == 2 && removedApplied == 2);

            AddStep("stop one binding, dispose the other element", () =>
            {
                tracked = ThemeResources.TrackedElementCount;
                binding.Dispose();
                ((FillFlowContainer)removed.Parent).Remove(removed, true);
            });
            AddStep("raise", () => provider.Raise());
            AddAssert("neither applied again", () => stoppedApplied == 2 && removedApplied == 2);
            AddAssert("the disposed element is no longer tracked", () => ThemeResources.TrackedElementCount < tracked);
        }

        [Test]
        public void AttachedTextForegroundReachesTheText()
        {
            StackPanel panel = null;
            TextBlock text = null, own = null;
            Icon icon = null;

            AddStep("create", () =>
            {
                panel = new StackPanel();
                panel.Add(text = new TextBlock { Text = "inherits" });
                panel.Add(own = new TextBlock { Text = "own" });
                panel.Add(icon = new Icon { Kind = "Solid.Check" });

                // A brush converts to the colour the setter takes.
                ThemeResources.Bind<Colour4>(panel, "Text", c => TextElement.SetForeground(panel, c));
                ThemeResources.Bind<Colour4>(own, "Card", c => TextElement.SetForeground(own, c));

                Child = new ThemeScope { Theme = "Light", Child = panel };
            });
            AddUntilStep("loaded", () => text.IsLoaded && icon.IsLoaded);
            AddAssert("text and icon take the panel's", () => text.Colour == light_text && icon.Colour == light_text);
            AddAssert("a TextBlock's own TextElement.Foreground wins", () => own.Colour == light_card);
        }

        [Test]
        public void TheGeneratedViewFollowsTheTheme()
        {
            ThemedView view = null;
            ThemeScope scope = null;

            AddStep("add view resources", () => provider
                                                 .Set("Dark", "SolidBackgroundFillColorBaseBrush", new SolidColorBrush(Colour4.FromHex("202020")))
                                                 .Set("Light", "SolidBackgroundFillColorBaseBrush", new SolidColorBrush(Colour4.FromHex("f3f3f3")))
                                                 .Set("Dark", "TextFillColorPrimaryBrush", new SolidColorBrush(dark_text))
                                                 .Set("Light", "TextFillColorPrimaryBrush", new SolidColorBrush(light_text))
                                                 .Set("Dark", "TextFillColorSecondaryBrush", new SolidColorBrush(Colour4.White) { Opacity = 0.75f })
                                                 .Set("Light", "TextFillColorSecondaryBrush", new SolidColorBrush(Colour4.Black) { Opacity = 0.6f })
                                                 .Set("Dark", "CardBackgroundFillColorDefaultBrush", dark_card)
                                                 .Set("Light", "CardBackgroundFillColorDefaultBrush", light_card)
                                                 .Set("Dark", "AccentFillColorDefaultBrush", Colour4.FromHex("60cdff"))
                                                 .Set("Light", "AccentFillColorDefaultBrush", Colour4.FromHex("005fb8"))
                                                 .Set("Dark", "ControlCornerRadius", 4.0)
                                                 .Set("Light", "ControlCornerRadius", 8)
                                                 .Set("Dark", "CardPadding", 12)
                                                 .Set("Light", "CardPadding", 16f));

            AddStep("create in a light scope", () => Child = scope = new ThemeScope { Theme = "Light", Child = view = new ThemedView() });
            AddUntilStep("loaded", () => view.Primary.IsLoaded && view.Swatch.IsLoaded);

            AddAssert("root background", () => view.Background == Colour4.FromHex("f3f3f3"));
            AddAssert("inherited text colour", () => view.Primary.Colour == light_text);
            AddAssert("own TextElement.Foreground, with the brush's opacity", () => view.Secondary.Colour == Colour4.Black.Opacity(0.6f));
            AddAssert("card", () => view.Card.Background == light_card && view.Card.Padding.Equals(new MarginPadding(16)) && view.Card.CornerRadius == 8);
            AddAssert("brush", () => ((SolidColorBrush)view.Swatch.Fill).Color == Colour4.FromHex("005fb8"));
            AddAssert("no theme has the key: untouched", () => view.Missing.Colour == light_text);

            AddStep("switch to dark", () =>
            {
                scope.Theme = "Dark";
                provider.Raise();
            });
            AddAssert("root background", () => view.Background == Colour4.FromHex("202020"));
            AddAssert("text", () => view.Primary.Colour == dark_text && view.Secondary.Colour == Colour4.White.Opacity(0.75f));
            AddAssert("card", () => view.Card.Background == dark_card && view.Card.Padding.Equals(new MarginPadding(12)) && view.Card.CornerRadius == 4);
            AddAssert("brush", () => ((SolidColorBrush)view.Swatch.Fill).Color == Colour4.FromHex("60cdff"));
        }

        [Test]
        public void HotReloadRebindsTheViewOnce()
        {
            ThemedView view = null;
            Border oldCard = null;
            int reloaded = 0;

            void countReload(Drawable _) => reloaded++;

            AddStep("create with hot reload on", () =>
            {
                XamlHotReload.IsEnabled = true;
                XamlHotReload.ViewReloaded += countReload;
                provider.Set("Light", "SolidBackgroundFillColorBaseBrush", Colour4.Gray).Set("Light", "CardBackgroundFillColorDefaultBrush", light_card);
                Child = new ThemeScope { Theme = "Light", Child = view = new ThemedView() };
            });
            AddUntilStep("loaded", () => view.Card.IsLoaded);
            AddStep("remember the card", () => oldCard = view.Card);

            AddStep("hot reload", () => XamlHotReload.UpdateApplication(new[] { typeof(ThemedView) }));
            AddUntilStep("rebuilt", () => reloaded == 1 && view.Card != oldCard && view.Card.IsLoaded);
            AddAssert("themed again", () => view.Background == Colour4.Gray && view.Card.Background == light_card);

            AddStep("raise", () =>
            {
                provider.ClearLookups();
                provider.Raise();
            });
            AddAssert("the root's binding asks once", () => provider.LookupsOf(view, "SolidBackgroundFillColorBaseBrush") == 1);
            AddAssert("the new card asks once, the old one not at all", () => provider.LookupsOf(view.Card, "CardBackgroundFillColorDefaultBrush") == 1
                                                                              && provider.LookupsOf(oldCard, "CardBackgroundFillColorDefaultBrush") == 0);

            AddStep("hot reload off", () =>
            {
                XamlHotReload.IsEnabled = false;
                XamlHotReload.ViewReloaded -= countReload;
            });
        }
    }

    /// <summary>The conversions a theme value goes through, and that a binding never keeps its element alive.</summary>
    [TestFixture]
    public class ThemeResourceConversionTests
    {
        private static T convert<T>(object value)
        {
            Assert.That(ThemeResources.TryConvert(value, out T result), Is.True, $"{value} to {typeof(T).Name}");
            return result;
        }

        private static void fails<T>(object value) => Assert.That(ThemeResources.TryConvert(value, out T _), Is.False, $"{value} to {typeof(T).Name}");

        [Test]
        public void ColoursAndBrushes()
        {
            var colour = new Colour4(0.2f, 0.4f, 0.6f, 1);

            Assert.That(convert<Colour4>(colour), Is.EqualTo(colour));
            Assert.That(convert<Colour4>(new SolidColorBrush(colour) { Opacity = 0.5f }), Is.EqualTo(colour.Opacity(0.5f)));
            Assert.That(convert<Colour4>((Color4)colour), Is.EqualTo(colour));
            Assert.That(convert<Color4>(colour), Is.EqualTo((Color4)colour));
            Assert.That(convert<ColourInfo>(colour), Is.EqualTo(ColourInfo.SingleColour(colour)));
            Assert.That(convert<Colour4>(ColourInfo.SingleColour(colour)), Is.EqualTo(colour));
            Assert.That(convert<SolidColorBrush>(colour).Color, Is.EqualTo(colour));
            Assert.That(((SolidColorBrush)convert<Brush>(colour)).Color, Is.EqualTo(colour));

            var gradient = new LinearGradientBrush();
            Assert.That(convert<Brush>(gradient), Is.SameAs(gradient));
            fails<Colour4>(gradient);
            fails<Colour4>(ColourInfo.GradientVertical(Colour4.Red, Colour4.Blue));
            fails<Colour4>("#ff0000");
            fails<Colour4>(3);
        }

        [Test]
        public void NumbersAndSpacing()
        {
            Assert.That(convert<float>(4.5), Is.EqualTo(4.5f));
            Assert.That(convert<double>(3), Is.EqualTo(3.0));
            Assert.That(convert<int>(2.0), Is.EqualTo(2));
            Assert.That(convert<long>(7f), Is.EqualTo(7L));
            Assert.That(convert<MarginPadding>(12), Is.EqualTo(new MarginPadding(12)));
            Assert.That(convert<MarginPadding>(6.5), Is.EqualTo(new MarginPadding(6.5f)));
            fails<int>(1e20);
            fails<float>("12");
            fails<float>(true);
        }

        [Test]
        public void NullableTargetsTextEnumsAndNull()
        {
            Assert.That(convert<Colour4?>(Colour4.Red), Is.EqualTo(Colour4.Red));
            Assert.That(convert<Colour4?>(new SolidColorBrush(Colour4.Blue)), Is.EqualTo(Colour4.Blue));
            Assert.That(convert<float?>(2.0), Is.EqualTo(2f));
            Assert.That(convert<bool?>(true), Is.EqualTo(true));

            Assert.That(convert<Colour4?>(null), Is.Null);
            Assert.That(convert<string>(null), Is.Null);
            fails<Colour4>(null);

            Assert.That(convert<LocalisableString>("Save").ToString(), Is.EqualTo("Save"));
            Assert.That(convert<FontWeight>("semibold"), Is.EqualTo(FontWeight.SemiBold));
            Assert.That(convert<object>(Colour4.Red), Is.EqualTo(Colour4.Red));
            fails<FontWeight>("Heavyish");
            fails<string>(Colour4.Red);
        }

        [Test]
        public void ABoundElementCanBeCollected()
        {
            var weak = bindUnreferenced();

            for (int i = 0; i < 5 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.That(weak.IsAlive, Is.False);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference bindUnreferenced()
        {
            var border = new Border();

            // A binding whose action captures the element, and one made the way generated code makes them, kept by a scope.
            ThemeResources.Bind<Colour4>(border, "Card", c => border.Background = c);
            ThemeResources.Bind(new BindingScope(), border, border, "Card", (Border b, Colour4 c) => b.Background = c);

            return new WeakReference(border);
        }
    }
}
