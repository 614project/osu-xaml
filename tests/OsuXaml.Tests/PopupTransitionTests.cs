using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using osu.Framework.Utils;
using OsuXaml.Controls;
using osuTK;

namespace OsuXaml.Tests
{
    /// <summary>The hooks a control library uses to place and animate popups: CustomPopupPlacementCallback and IPopupTransition.</summary>
    [HeadlessTest]
    public partial class PopupTransitionTests : TestScene
    {
        private static readonly Vector2 content_size = new Vector2(120, 60);
        private const double close_duration = 400;

        private Window window;
        private Border target;
        private Popup popup;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create", () =>
            {
                target = new Border { Size = new Vector2(100, 30), Background = Theme.Surface };
                popup = new Popup { PlacementTarget = target, Child = new Border { Size = content_size, Background = Theme.Accent } };

                var panel = new StackPanel { Padding = new MarginPadding { Left = 200, Top = 150 } };
                panel.Add(target);
                panel.Add(popup);
                Child = window = new Window { Child = panel };
            });
            AddUntilStep("loaded", () => popup.IsLoaded && target.DrawWidth > 0);
        }

        private Drawable roots => (Drawable)popup.Root.Parent;

        // The target's rectangle in the space popups are placed in.
        private RectangleF targetInLayer()
        {
            var quad = target.ScreenSpaceDrawQuad;
            var topLeft = roots.ToLocalSpace(quad.TopLeft);
            return new RectangleF(topLeft, roots.ToLocalSpace(quad.BottomRight) - topLeft);
        }

        private bool placedAt(Vector2 position) => popup.Root.DrawWidth > 0 && Precision.AlmostEquals(popup.Root.Position, position);

        [Test]
        public void TheCallbackGetsTheSizesInTheLayersSpace()
        {
            Vector2 size = default, bounds = default;
            RectangleF rectangle = default;
            int calls = 0;

            AddStep("open with a callback", () =>
            {
                popup.Placement = PlacementMode.Custom;
                popup.CustomPopupPlacementCallback = (s, t, b) =>
                {
                    calls++;
                    size = s;
                    rectangle = t;
                    bounds = b;
                    return new Vector2(t.Right + 4, t.Y);
                };
                popup.Open();
            });
            AddUntilStep("placed where it said", () => placedAt(new Vector2(targetInLayer().Right + 4, targetInLayer().Y)));

            AddAssert("the popup's own size", () => size == content_size);
            AddAssert("the target's rectangle", () => Precision.AlmostEquals(rectangle.TopLeft, targetInLayer().TopLeft) && Precision.AlmostEquals(rectangle.Size, new Vector2(100, 30)));
            AddAssert("the layer's size", () => bounds == roots.DrawSize && bounds.X > 0);
            AddAssert("asked again every frame, so it follows the target", () => calls > 1);
            AddAssert("the side is worked out from where it went", () => popup.Root.ActualPlacement == PlacementMode.Right);
        }

        [Test]
        public void ThePositionIsKeptInsideTheLayerAndOffsetsStillApply()
        {
            AddStep("open off the top-left", () =>
            {
                popup.Placement = PlacementMode.Custom;
                popup.CustomPopupPlacementCallback = (s, t, b) => new Vector2(-500, -500);
                popup.Open();
            });
            AddUntilStep("clamped to the corner", () => placedAt(Vector2.Zero));

            AddStep("off the bottom-right", () => popup.CustomPopupPlacementCallback = (s, t, b) => b + new Vector2(500));
            AddUntilStep("clamped to the other corner", () => placedAt(roots.DrawSize - content_size));

            AddStep("above the target, with offsets", () =>
            {
                popup.HorizontalOffset = 7;
                popup.VerticalOffset = -3;
                popup.CustomPopupPlacementCallback = (s, t, b) => new Vector2(t.X, t.Y - s.Y);
            });
            AddUntilStep("offsets are added after the callback", () => placedAt(new Vector2(targetInLayer().X + 7, targetInLayer().Y - content_size.Y - 3)));
            AddAssert("on top", () => popup.Root.ActualPlacement == PlacementMode.Top);
        }

        [Test]
        public void CustomWithoutACallbackOpensBelow()
        {
            AddStep("open", () =>
            {
                popup.Placement = PlacementMode.Custom;
                popup.Open();
            });
            AddUntilStep("below the target", () => placedAt(new Vector2(targetInLayer().X, targetInLayer().Bottom)));
            AddAssert("bottom", () => popup.Root.ActualPlacement == PlacementMode.Bottom);
        }

        [Test]
        public void TheOpeningAnimationStartsOnceTheContentHasASize()
        {
            var transition = new RecordingTransition();

            AddStep("open with a transition", () =>
            {
                popup.Transition = transition;
                popup.Open();
            });
            AddUntilStep("it started", () => transition.Opens.Count == 1);
            AddAssert("placed and sized already, on its side", () =>
                transition.Opens[0].Size == content_size && transition.Opens[0].Side == PlacementMode.Bottom
                && Precision.AlmostEquals(transition.Opens[0].Position, new Vector2(targetInLayer().X, targetInLayer().Bottom)));
            AddUntilStep("visible", () => popup.Root.IsPresent && popup.Root.Alpha == 1);
            AddWaitStep("a few frames", 3);
            AddAssert("only once", () => transition.Opens.Count == 1);
            AddAssert("its offset moves it", () => Precision.AlmostEquals(popup.Root.Position, new Vector2(targetInLayer().X, targetInLayer().Bottom) + transition.OpenOffset));
        }

        [Test]
        public void ContentThatSizesItselfInItsUpdateStillOpens()
        {
            var transition = new RecordingTransition();
            SizedInUpdate content = null;
            bool waited = false;

            AddStep("open content with no size until it updates", () =>
            {
                popup.Transition = transition;
                popup.Child = content = new SizedInUpdate();
                popup.Open();
                waited = popup.Root.IsWaitingForSize && popup.Root.IsPresent && popup.Root.Alpha == 0 && !popup.Root.PropagatePositionalInputSubTree;
            });
            AddAssert("it waited, present but invisible and taking no input", () => waited);
            AddUntilStep("the content updated and the animation started", () => content.Updates > 0 && transition.Opens.Count == 1);
            AddAssert("with the size it gave itself", () => transition.Opens[0].Size == content_size);
            AddAssert("no longer waiting, and visible", () => !popup.Root.IsWaitingForSize && popup.Root.Alpha == 1);
        }

        [Test]
        public void ContentCanTakeFocusAsItOpens()
        {
            FocusableBox box = null;
            bool presentAtZero = false;

            AddStep("open fading in from zero", () =>
            {
                popup.Transition = new FadeFromZeroTransition();
                popup.Child = box = new FocusableBox { Size = content_size };
                popup.Open();
                presentAtZero = popup.Root.Alpha == 0 && popup.Root.IsPresent;
            });
            AddAssert("present from the first frame, at alpha 0", () => presentAtZero);
            AddUntilStep("content loaded", () => box.IsLoaded);
            AddStep("focus while the popup starts to fade in", () => GetContainingFocusManager()?.ChangeFocus(box));
            AddWaitStep("a few frames", 3);
            AddAssert("focus kept", () => box.HasFocus);
            AddStep("close", () => popup.Close());
            AddAssert("closing: present only while visible", () => popup.Root.IsPresent == popup.Root.Alpha > 0);
        }

        [Test]
        public void TheSideIsSettledWhenTheAnimationStarts()
        {
            var transition = new RecordingTransition();
            bool above = false;

            AddStep("open with a transition", () =>
            {
                popup.Transition = transition;
                popup.Placement = PlacementMode.Custom;
                popup.CustomPopupPlacementCallback = (s, t, b) => above ? new Vector2(t.X, t.Y - s.Y) : new Vector2(t.X, t.Bottom);
                popup.Open();
            });
            AddUntilStep("started below", () => transition.Opens.Count == 1 && transition.Opens[0].Side == PlacementMode.Bottom);
            AddStep("the callback moves it above", () => above = true);
            AddUntilStep("moved", () => popup.Root.Y < targetInLayer().Y);
            AddAssert("still the side it started on", () => popup.Root.ActualPlacement == PlacementMode.Bottom);
        }

        [Test]
        public void ClosingKeepsTheContentForTheAnimationWithoutInput()
        {
            var transition = new RecordingTransition();
            double closedAt = 0;

            AddStep("open with a transition", () =>
            {
                popup.Transition = transition;
                popup.Open();
            });
            AddUntilStep("opened", () => transition.Opens.Count == 1);

            AddStep("close", () =>
            {
                popup.Close();
                closedAt = Time.Current;
            });
            AddAssert("the closing animation was asked for once", () => transition.Closes == 1);
            AddAssert("the content stays, taking no input", () =>
                popup.Root.Parent != null && popup.Root.IsClosing && !popup.Root.PropagatePositionalInputSubTree && !popup.Root.PropagateNonPositionalInputSubTree);
            AddAssert("the popup counts as closed", () => !window.PopupLayer.OpenPopups.Contains(popup));

            AddUntilStep("removed when it ends", () => popup.Root.Parent == null);
            AddAssert("not before", () => Time.Current - closedAt >= close_duration);
            AddAssert("no longer closing", () => !popup.Root.IsClosing);
        }

        [Test]
        public void ReopeningWhileClosingKeepsTheContent()
        {
            var transition = new RecordingTransition();
            double reopenedAt = 0;

            AddStep("open with a transition", () =>
            {
                popup.Transition = transition;
                popup.Open();
            });
            AddUntilStep("opened", () => transition.Opens.Count == 1);

            AddStep("close", () => popup.Close());
            AddStep("open again", () =>
            {
                popup.Open();
                reopenedAt = Time.Current;
            });
            AddUntilStep("the opening animation played again", () => transition.Opens.Count == 2);
            AddAssert("takes input again", () => !popup.Root.IsClosing && popup.Root.PropagatePositionalInputSubTree);

            AddUntilStep("the old closing time passes", () => Time.Current - reopenedAt > close_duration + 100);
            AddAssert("still there", () => popup.Root.Parent != null && window.PopupLayer.OpenPopups.Contains(popup));
        }

        [Test]
        public void ContentWithNoSizeYetLeavesAtOnce()
        {
            var transition = new RecordingTransition();

            AddStep("open empty content and close it in one frame", () =>
            {
                popup.Transition = transition;
                popup.Child = new Container();
                popup.Open();
                popup.Close();
            });
            AddAssert("no animation either way", () => transition.Opens.Count == 0 && transition.Closes == 0);
            AddAssert("gone", () => popup.Root.Parent == null);
        }

        [Test]
        public void WithoutATransitionItFadesInAndLeavesAtOnce()
        {
            AddStep("open", () => popup.Open());
            AddUntilStep("shown", () => popup.Root.IsPresent && popup.Root.DrawWidth > 0);
            AddStep("close", () => popup.Close());
            AddAssert("gone", () => popup.Root.Parent == null);
        }

        private partial class FocusableBox : Container
        {
            public override bool AcceptsFocus => true;
        }

        private class FadeFromZeroTransition : IPopupTransition
        {
            public void Open(PopupRoot root, PlacementMode side) => root.FadeInFromZero(300);

            public double Close(PopupRoot root)
            {
                root.FadeOut(100);
                return 100;
            }
        }

        // Content that, like a layout panel, works out its size in its own update.
        private partial class SizedInUpdate : Container
        {
            public int Updates;

            protected override void Update()
            {
                base.Update();
                Updates++;
                Size = content_size;
            }
        }

        private class RecordingTransition : IPopupTransition
        {
            public readonly List<(PlacementMode Side, Vector2 Size, Vector2 Position)> Opens = new List<(PlacementMode, Vector2, Vector2)>();
            public readonly Vector2 OpenOffset = new Vector2(0, 5);
            public int Closes;

            public void Open(PopupRoot root, PlacementMode side)
            {
                Opens.Add((side, root.DrawSize, root.Position));
                root.Offset = OpenOffset;
            }

            public double Close(PopupRoot root)
            {
                Closes++;
                root.FadeOut(close_duration);
                return close_duration;
            }
        }
    }
}
