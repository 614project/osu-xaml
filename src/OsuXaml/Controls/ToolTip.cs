using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Localisation;
using osuTK;

[assembly: OsuXaml.XamlGlobalProperties(typeof(OsuXaml.Controls.ToolTipService))]

namespace OsuXaml.Controls
{
    /// <summary>
    /// The small box shown near the mouse while an element with a tooltip is hovered, like WPF's ToolTip.
    /// Text tooltips (<c>ToolTip="Save the file"</c>) use one automatically; declare one yourself for rich content:
    /// <c>&lt;ToolTipService.ToolTip&gt;&lt;ToolTip&gt;&lt;StackPanel&gt;...&lt;/StackPanel&gt;&lt;/ToolTip&gt;&lt;/ToolTipService.ToolTip&gt;</c>.
    /// A tooltip never takes mouse or keyboard input.
    /// </summary>
    public partial class ToolTip : ContentControl
    {
        public ToolTip()
        {
            Background = Theme.Surface;
            BorderBrush = Theme.Border;
            BorderThickness = 1;
            CornerRadius = 4;
            Padding = new MarginPadding { Horizontal = 8, Vertical = 4 };

            Chrome.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Colour4.Black.Opacity(0.3f),
                Radius = 6,
                Offset = new Vector2(0, 2),
            };
        }

        public override bool PropagatePositionalInputSubTree => false;

        public override bool PropagateNonPositionalInputSubTree => false;

        /// <summary>
        /// The element the tooltip is showing for, as WPF's <c>ToolTip.PlacementTarget</c>: set just before it opens (before
        /// <see cref="OnOpening"/>), null again once it has closed.
        /// </summary>
        public Drawable PlacementTarget { get; internal set; }

        /// <summary>
        /// Called each time the tooltip is about to show for <see cref="PlacementTarget"/>, before it joins the popup layer. A tooltip
        /// that takes something from the element it belongs to (its theme, say, as it opens outside that element) takes it here.
        /// </summary>
        protected internal virtual void OnOpening()
        {
        }

        protected override Drawable CreateTextContent(LocalisableString text) => new TextBlock { Text = text, FontSize = 14, Colour = Foreground };

        /// <summary>
        /// Where the tooltip goes for <see cref="PlacementMode.Mouse"/>, in the layer's space: below the pointer like desktop
        /// tooltips, or above it when there is no room. It is kept inside the layer afterwards.
        /// </summary>
        public virtual Vector2 PlaceNearPointer(Vector2 pointer, Vector2 size, Vector2 bounds)
        {
            var position = pointer + new Vector2(0, 20);

            if (position.Y + size.Y > bounds.Y)
                position.Y = pointer.Y - size.Y - 4;

            return position;
        }
    }

    /// <summary>
    /// The <c>ToolTip</c> property every XAML element accepts, like WPF's ToolTipService:
    /// <c>ToolTip="Save the file"</c>, <c>ToolTip="{Bind Status}"</c>, or an element (usually a <see cref="ToolTip"/>)
    /// for rich content. The tooltip appears near the mouse after <see cref="InitialShowDelay"/> of hovering
    /// and hides when the mouse leaves the element or a button is pressed.
    /// </summary>
    public static class ToolTipService
    {
        private sealed class Entry
        {
            public object Content;
            public double? InitialShowDelay;
            public PlacementMode Placement = PlacementMode.Mouse;
        }

        private static readonly ConditionalWeakTable<Drawable, Entry> entries = new ConditionalWeakTable<Drawable, Entry>();

        /// <summary>How long the mouse rests on an element before its tooltip shows, in milliseconds.</summary>
        public static double InitialShowDelay = 500;

        /// <summary>Moving from one tooltip element to another within this many milliseconds shows the next tooltip at once.</summary>
        public static double BetweenShowDelay = 100;

        /// <summary>
        /// Makes the tooltips that show text tooltips and rich content that isn't a <see cref="ToolTip"/> itself. A theme library
        /// sets it to its own ToolTip subclass; tooltips made before it changed are replaced the next time one shows.
        /// </summary>
        public static Func<ToolTip> ToolTipFactory { get; set; } = () => new ToolTip();

        public static void SetToolTip(Drawable element, LocalisableString text)
        {
            if (string.IsNullOrEmpty(text.ToString()))
                clear(element);
            else
                entry(element).Content = text;
        }

        public static void SetToolTip(Drawable element, string text) => SetToolTip(element, (LocalisableString)(text ?? string.Empty));

        /// <summary>Rich tooltip content. A <see cref="ToolTip"/> is shown as is; any other drawable is shown inside one.</summary>
        public static void SetToolTip(Drawable element, Drawable content)
        {
            if (content == null)
                clear(element);
            else
                entry(element).Content = content;
        }

        /// <summary>The tooltip of <paramref name="element"/>: a LocalisableString, a drawable, or null.</summary>
        public static object GetToolTip(Drawable element) => entries.TryGetValue(element, out var e) ? e.Content : null;

        /// <summary><c>ToolTipService.InitialShowDelay="1000"</c>: this element's delay in milliseconds.</summary>
        public static void SetInitialShowDelay(Drawable element, int milliseconds) => entry(element).InitialShowDelay = milliseconds;

        public static double GetInitialShowDelay(Drawable element)
            => entries.TryGetValue(element, out var e) && e.InitialShowDelay.HasValue ? e.InitialShowDelay.Value : InitialShowDelay;

        /// <summary>
        /// <c>ToolTipService.Placement="Bottom"</c>: Mouse (the default) shows the tooltip below the pointer;
        /// Bottom, Top, Left, Right and Center place it against the element.
        /// </summary>
        public static void SetPlacement(Drawable element, PlacementMode placement) => entry(element).Placement = placement;

        public static PlacementMode GetPlacement(Drawable element) => entries.TryGetValue(element, out var e) ? e.Placement : PlacementMode.Mouse;

        /// <summary>Elements with tooltip content, for the popup layer's hit test.</summary>
        internal static IEnumerable<Drawable> Targets
        {
            get
            {
                foreach (var pair in (IEnumerable<KeyValuePair<Drawable, Entry>>)entries)
                {
                    if (pair.Value.Content != null)
                        yield return pair.Key;
                }
            }
        }

        private static Entry entry(Drawable element)
        {
            if (entries.TryGetValue(element, out var existing))
                return existing;

            var created = new Entry();
            entries.Add(element, created);
            PopupLayer.EnsureFor(element);
            return created;
        }

        private static void clear(Drawable element)
        {
            if (entries.TryGetValue(element, out var e))
                e.Content = null;
        }
    }

    /// <summary>
    /// The part of a <see cref="PopupLayer"/> that shows tooltips. It polls the mouse instead of handling hover events,
    /// because most elements with a tooltip (text, borders) don't take input at all.
    /// </summary>
    internal partial class ToolTipHost : CompositeDrawable
    {
        private readonly PopupLayer layer;

        // The tooltips for text and for wrapped content, from the factory that made them.
        private ToolTip textToolTip;
        private ToolTip wrapper;
        private Func<ToolTip> madeBy;

        private Drawable hovered;
        private double hoveredSince;
        private double hiddenAt = double.NegativeInfinity;

        // An element clicked while hovered shows no tooltip until the mouse leaves it.
        private Drawable suppressed;

        private ToolTip shown;
        private object shownContent;
        private Drawable shownFor;
        private Vector2 anchorPosition;

        public ToolTipHost(PopupLayer layer)
        {
            this.layer = layer;
            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>The tooltip currently showing, if any.</summary>
        public ToolTip Current => shown;

        public override bool PropagatePositionalInputSubTree => false;

        public override bool PropagateNonPositionalInputSubTree => false;

        protected override void Update()
        {
            base.Update();

            var input = GetContainingInputManager();
            if (input == null)
                return;

            var mouse = input.CurrentState.Mouse;
            var target = mouse.IsPositionValid ? layer.FindTarget(ToolTipService.Targets, mouse.Position) : null;

            if (target != hovered)
            {
                hovered = target;
                hoveredSince = Time.Current;
                suppressed = null;
                hide();
            }

            if (target == null)
                return;

            if (mouse.Buttons.HasAnyButtonPressed)
            {
                suppressed = target;
                hide();
                return;
            }

            if (target == suppressed)
                return;

            if (shown != null)
            {
                // Bound text (or the whole tooltip) can change while it is showing.
                object content = ToolTipService.GetToolTip(target);

                if (Equals(content, shownContent))
                    return;

                if (shown == textToolTip && content is LocalisableString text)
                {
                    textToolTip.Content = shownContent = text;
                    return;
                }

                hide();
                show(target, ToScreenSpace(anchorPosition));
                return;
            }

            double delay = Time.Current - hiddenAt <= ToolTipService.BetweenShowDelay ? 0 : ToolTipService.GetInitialShowDelay(target);

            if (Time.Current - hoveredSince >= delay)
                show(target, mouse.Position);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (shown != null)
                place();
        }

        /// <summary>Hides the tooltip of the element under the mouse until the mouse leaves it, as a click does.</summary>
        public void SuppressHovered()
        {
            if (hovered == null)
                return;

            suppressed = hovered;
            hide();
        }

        private void ensureToolTips()
        {
            var factory = ToolTipService.ToolTipFactory ?? (() => new ToolTip());

            if (factory == madeBy && textToolTip != null)
                return;

            textToolTip?.Dispose();
            wrapper?.Dispose();

            madeBy = factory;
            textToolTip = factory() ?? new ToolTip();
            wrapper = factory() ?? new ToolTip();
        }

        private void show(Drawable target, Vector2 mouseScreenSpace)
        {
            object content = ToolTipService.GetToolTip(target);

            // Only replaced while none is showing, so a factory set in the meantime takes effect from the next tooltip.
            if (!(content is ToolTip))
                ensureToolTips();

            switch (content)
            {
                case ToolTip own:
                    // Content the app declared belongs to it; it is removed again (not disposed) on hide.
                    if (own.Parent != null)
                        return;

                    shown = own;
                    break;

                case Drawable drawable:
                    if (drawable.Parent != null)
                        return;

                    wrapper.Content = drawable;
                    shown = wrapper;
                    break;

                case LocalisableString text:
                    textToolTip.Content = text;
                    shown = textToolTip;
                    break;

                default:
                    return;
            }

            shownFor = target;
            shownContent = content;
            anchorPosition = ToLocalSpace(mouseScreenSpace);

            shown.PlacementTarget = target;
            shown.OnOpening();

            AddInternal(shown);
            shown.ClearTransforms();
            shown.Alpha = 0;
            shown.FadeIn(150, Easing.OutQuint);
            place();
        }

        private void hide()
        {
            if (shown == null)
                return;

            RemoveInternal(shown, false);
            shown.PlacementTarget = null;

            // Hand rich content back so it can be shown again (or disposed by its owner).
            if (shown == wrapper)
                wrapper.Content = null;

            shown = null;
            shownFor = null;
            shownContent = null;
            hiddenAt = Time.Current;
        }

        private void place()
        {
            var size = shown.DrawSize;
            var bounds = DrawSize;
            Vector2 position;

            var quad = shownFor.ScreenSpaceDrawQuad;
            var topLeft = ToLocalSpace(quad.TopLeft);
            var bottomRight = ToLocalSpace(quad.BottomRight);

            switch (ToolTipService.GetPlacement(shownFor))
            {
                case PlacementMode.Bottom:
                    position = new Vector2(topLeft.X, bottomRight.Y + 4);
                    break;

                case PlacementMode.Top:
                    position = new Vector2(topLeft.X, topLeft.Y - size.Y - 4);
                    break;

                case PlacementMode.Right:
                    position = new Vector2(bottomRight.X + 4, topLeft.Y);
                    break;

                case PlacementMode.Left:
                    position = new Vector2(topLeft.X - size.X - 4, topLeft.Y);
                    break;

                case PlacementMode.Center:
                    position = (topLeft + bottomRight) / 2 - size / 2;
                    break;

                default:
                    position = shown.PlaceNearPointer(anchorPosition, size, bounds);
                    break;
            }

            position.X = Math.Clamp(position.X, 0, Math.Max(0, bounds.X - size.X));
            position.Y = Math.Clamp(position.Y, 0, Math.Max(0, bounds.Y - size.Y));
            shown.Position = position;
        }

        protected override void Dispose(bool isDisposing)
        {
            // The shared tooltips are only children while showing, so the hidden ones aren't disposed with the host.
            if (textToolTip?.Parent == null)
                textToolTip?.Dispose();
            if (wrapper?.Parent == null)
                wrapper?.Dispose();

            base.Dispose(isDisposing);
        }
    }
}
