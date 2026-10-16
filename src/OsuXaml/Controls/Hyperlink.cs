using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Clickable link text, like WPF's Hyperlink: <c>&lt;Hyperlink NavigateUri="https://..."&gt;osu!framework&lt;/Hyperlink&gt;</c>.
    /// Hovering underlines and brightens it. A click raises <see cref="Click"/>, then <see cref="RequestNavigate"/> if anything
    /// handles it, otherwise opens <see cref="NavigateUri"/> in the system browser. It sizes to its text and is left-aligned by default.
    /// </summary>
    public partial class Hyperlink : ContentControl
    {
        private readonly Box underline;
        private Colour4 hoverForeground = Theme.Accent.Lighten(0.25f);
        private bool isEnabled = true;
        private bool hovered;
        private TextBlock ownText;

        [Resolved(canBeNull: true)]
        private GameHost host { get; set; }

        public Hyperlink()
        {
            Foreground = Theme.Accent;
            Padding = new MarginPadding { Bottom = 2 };
            Layout.SetHorizontalAlignment(this, HorizontalAlignment.Left);

            Chrome.Add(underline = new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Height = 1,
                Colour = hoverForeground,
                Alpha = 0,
            });
        }

        /// <summary>The link text; the same as text <c>Content</c>.</summary>
        public string Text
        {
            get => Content?.ToString();
            set => Content = value;
        }

        /// <summary>The address opened on click.</summary>
        public string NavigateUri { get; set; }

        /// <summary>Text colour while hovered.</summary>
        public Colour4 HoverForeground
        {
            get => hoverForeground;
            set
            {
                hoverForeground = value;
                underline.Colour = value;
            }
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);
            }
        }

        /// <summary>Raised on every click.</summary>
        public event Action Click;

        /// <summary>Raised with <see cref="NavigateUri"/> on click. While anything handles it, the link doesn't open the browser itself.</summary>
        public event Action<string> RequestNavigate;

        /// <summary>Opens <paramref name="uri"/> when nothing handles <see cref="RequestNavigate"/>.</summary>
        protected virtual void Navigate(string uri) => host?.OpenUrlExternally(uri);

        protected override Drawable CreateTextContent(osu.Framework.Localisation.LocalisableString text)
        {
            ownText = new TextBlock { Text = text, Colour = hovered ? hoverForeground : Foreground };
            return ownText;
        }

        // Only the text itself is the link, even when a panel stretches the control wider.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
            => PresentedContent?.ReceivePositionalInputAt(screenSpacePos) ?? base.ReceivePositionalInputAt(screenSpacePos);

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateState();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState();
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (isEnabled)
                Chrome.FadeTo(0.7f, 60, Easing.OutQuint);
            return false;
        }

        protected override void OnMouseUp(MouseUpEvent e) => Chrome.FadeTo(1, 200, Easing.OutQuint);

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return false;

            Click?.Invoke();

            if (!string.IsNullOrEmpty(NavigateUri))
            {
                if (RequestNavigate != null)
                    RequestNavigate.Invoke(NavigateUri);
                else
                    Navigate(NavigateUri);
            }

            return true;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            // The underline follows the text, which may sit anywhere inside a stretched control.
            if (PresentedContent is Drawable text && text.Parent != null)
            {
                underline.X = text.ToSpaceOfOtherDrawable(Vector2.Zero, Chrome).X;
                underline.Width = text.DrawWidth;
            }
        }

        private void updateState()
        {
            bool active = hovered && isEnabled;

            underline.FadeTo(active ? 1 : 0, 100, Easing.OutQuint);

            if (ownText != null && ReferenceEquals(PresentedContent, ownText))
                ownText.FadeColour(active ? hoverForeground : Foreground, 100, Easing.OutQuint);
        }
    }
}
