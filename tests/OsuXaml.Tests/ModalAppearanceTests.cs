using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>A dialog that implements IModalAppearance brings its own backdrop and animations to the ModalHost showing it.</summary>
    [HeadlessTest]
    public partial class ModalAppearanceTests : TestScene
    {
        private static readonly Colour4 smoke = Colour4.FromHex("202040").Opacity(0.3f);

        private Window left, right;

        /// <summary>What a theme library's dialog looks like: a tinted backdrop, a slide in and a fade out.</summary>
        private sealed partial class ThemedDialog : Dialog, IModalAppearance
        {
            public Colour4 BackdropColour => smoke;
            public double OpenDuration => 100;
            public double CloseDuration => 300;

            public int Opens;
            public int Closes;
            public double OpenedAt;
            public double ClosedAt;

            // What the host had done when it asked for each animation.
            public bool DefaultAnimationPlayed;
            public double[] BackdropFadeIn;
            public double[] BackdropFadeOut;

            public ThemedDialog()
            {
                Title = "Themed";
                Content = new TextBlock { Text = "Hello" };
            }

            public void AnimateOpen()
            {
                Opens++;
                OpenedAt = Time.Current;
                DefaultAnimationPlayed = Transforms.Any() || Scale != Vector2.One || Alpha != 1;
                BackdropFadeIn = durations(Host);
                this.MoveToY(20).MoveToY(0, OpenDuration, Easing.OutQuint);
            }

            public void AnimateClose()
            {
                Closes++;
                ClosedAt = Time.Current;
                DefaultAnimationPlayed |= Transforms.Any();
                BackdropFadeOut = durations(Host);
                this.FadeOut(CloseDuration, Easing.OutQuint);
            }
        }

        /// <summary>Records what the host does to a plain dialog as it opens.</summary>
        private sealed partial class PlainDialog : Dialog
        {
            public bool ScaledIn;
            public double[] BackdropFadeIn;

            public PlainDialog()
            {
                Title = "Plain";
                Content = new TextBlock { Text = "Hi" };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // The host has loaded just before its dialog.
                ScaledIn = Scale.X < 1 && Transforms.Any();
                BackdropFadeIn = durations(Host);
            }
        }

        private static double[] durations(ModalHost host) => durations(backdropOf(host));

        private static double[] durations(Drawable drawable) => drawable.Transforms.Select(t => t.EndTime - t.StartTime).ToArray();

        private static bool has(IEnumerable<double> lengths, double length) => lengths != null && lengths.Any(l => Precision.AlmostEquals(l, length));

        // Every length of animation the right window's cover has played, gathered each frame so none is missed between steps.
        private readonly List<double> coverAnimations = new List<double>();

        protected override void Update()
        {
            base.Update();

            if (right?.PopupLayer?.IsLoaded == true)
                coverAnimations.AddRange(durations(coverOf(right)));
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create two windows", () =>
            {
                coverAnimations.Clear();
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        left = new Window { Width = 0.5f, Child = new TextBlock { Text = "left" } },
                        right = new Window { Width = 0.5f, X = 0.5f, RelativePositionAxes = Axes.X, Child = new TextBlock { Text = "right" } },
                    },
                };
            });
            AddUntilStep("loaded", () => left.IsLoaded && right.IsLoaded);
        }

        private static Box backdropOf(ModalHost host) => host.ChildrenOfType<Box>().First();

        // The window's own cover; each dialog's popup layer has one too.
        private static Box coverOf(Window window) =>
            window.PopupLayer.ChildrenOfType<PopupLayer.ModalCover>().Single(c => c.Parent == window.PopupLayer).ChildrenOfType<Box>().Single();

        private ThemedDialog show()
        {
            var dialog = new ThemedDialog();
            AddStep("show a themed dialog in the left window", () => dialog.ShowDialog(left));
            AddUntilStep("opened", () => dialog.Opens == 1);
            return dialog;
        }

        [Test]
        public void TheBackdropTakesTheDialogsColourAndDuration()
        {
            var dialog = show();

            AddAssert("only the dialog's own opening animation", () => dialog.Opens == 1 && !dialog.DefaultAnimationPlayed);
            AddAssert("the backdrop's colour without its transparency", () => backdropOf(dialog.Host).Colour == smoke.Opacity(1f));
            AddAssert("fading in for the dialog's time", () => has(dialog.BackdropFadeIn, 100) && dialog.BackdropFadeIn.Length == 1);
            AddUntilStep("to the dialog's transparency", () => Precision.AlmostEquals(backdropOf(dialog.Host).Alpha, smoke.A));
        }

        [Test]
        public void ClosingWaitsForTheDialogsCloseDuration()
        {
            var dialog = show();
            ModalHost host = null;

            bool shownAfterClosing = false;
            double removedAt = 0;

            AddStep("close", () =>
            {
                host = dialog.Host;
                dialog.Close();
                shownAfterClosing = host.Parent != null;
            });
            AddAssert("the dialog's closing animation, not the default one", () => dialog.Closes == 1 && !dialog.DefaultAnimationPlayed);
            AddAssert("closing, taking no input", () => host.IsClosing && !host.PropagatePositionalInputSubTree && !left.PopupLayer.OpenModals.Contains(host));
            AddAssert("the backdrop fades out for as long", () => has(dialog.BackdropFadeOut, 300));
            AddAssert("still shown while it plays", () => shownAfterClosing);

            // Headless frames are far apart in game time, so the removal time is noted on the frame it is first seen.
            AddUntilStep("removed afterwards", () =>
            {
                if (host.Parent != null)
                    return false;

                removedAt = Time.Current;
                return true;
            });
            AddAssert("not before", () => removedAt - dialog.ClosedAt >= 300);
        }

        [Test]
        public void TheOtherWindowsDimInTheDialogsColour()
        {
            var dialog = show();

            AddUntilStep("the right window dims like the backdrop", () =>
                coverOf(right).Colour == smoke.Opacity(1f) && Precision.AlmostEquals(coverOf(right).Alpha, smoke.A));
            AddAssert("in the dialog's time", () => has(coverAnimations, 100));
            AddAssert("the dialog's own window isn't covered twice", () => coverOf(left).Alpha == 0);

            AddStep("close", () => dialog.Close());
            AddUntilStep("gone", () => coverOf(right).Alpha == 0);
            AddAssert("in the dialog's close time", () => has(coverAnimations, 300));
        }

        [Test]
        public void DialogsWithoutOneKeepTheDefaultLook()
        {
            PlainDialog dialog = null;

            AddStep("show a plain dialog", () =>
            {
                dialog = new PlainDialog();
                dialog.ShowDialog(left);
            });
            AddUntilStep("opened", () => dialog.IsLoaded);
            AddAssert("scaled in", () => dialog.ScaledIn);
            AddAssert("the backdrop fades in for the default time", () => has(dialog.BackdropFadeIn, ModalHost.OpenDuration));
            AddUntilStep("half-transparent black", () => backdropOf(dialog.Host).Colour == Colour4.Black && Precision.AlmostEquals(backdropOf(dialog.Host).Alpha, ModalHost.Dim));
            AddUntilStep("the other window too", () => coverOf(right).Colour == Colour4.Black && Precision.AlmostEquals(coverOf(right).Alpha, ModalHost.Dim));
            AddAssert("in the default time", () => has(coverAnimations, ModalHost.OpenDuration));

            AddStep("close", () => dialog.Close());
            AddUntilStep("the cover goes", () => coverOf(right).Alpha == 0);
            AddAssert("in the default time", () => has(coverAnimations, ModalHost.CloseDuration));
        }
    }
}
