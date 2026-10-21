using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A header that shows or hides its content when clicked, like WPF's Expander. <see cref="IsExpanded"/> binds two-way;
    /// the chevron turns and the height animates. Space or Enter toggles it while it has focus.
    /// </summary>
    public partial class Expander : HeaderedContentControl
    {
        private readonly BindableFloat expansion = new BindableFloat();
        private readonly SpriteIcon chevron;
        private readonly HeaderButton headerButton;

        public Expander()
        {
            MinHeaderHeight = 34;
            HeaderPadding = new MarginPadding { Left = 32, Right = 10, Vertical = 7 };
            Padding = new MarginPadding { Left = 32, Right = 10, Top = 2, Bottom = 10 };
            CornerRadius = 6;

            HeaderArea.AddRange(new Drawable[]
            {
                headerButton = new HeaderButton(this) { RelativeSizeAxes = Axes.Both, Depth = 1 },
                chevron = new SpriteIcon
                {
                    Icon = FontAwesome.Solid.ChevronRight,
                    Size = new Vector2(11),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = 16,
                    Colour = Theme.SubtleText,
                },
            });

            // The panels around measure the expander again as its height animates.
            expansion.BindValueChanged(_ => Layout.InvalidateMeasure(this));

            IsExpanded.BindValueChanged(e =>
            {
                double duration = IsLoaded ? 250 : 0;

                if (duration > 0)
                    this.TransformBindableTo(expansion, e.NewValue ? 1 : 0, duration, Easing.OutQuint);
                else
                    expansion.Value = e.NewValue ? 1 : 0;

                chevron.RotateTo(e.NewValue ? 90 : 0, duration, Easing.OutQuint);
                (e.NewValue ? Expanded : Collapsed)?.Invoke();
            });
        }

        /// <summary>Whether the content is shown. Binds two-way.</summary>
        public Bindable<bool> IsExpanded { get; } = new Bindable<bool>();

        /// <summary>Raised when <see cref="IsExpanded"/> becomes true.</summary>
        public event Action Expanded;

        /// <summary>Raised when <see cref="IsExpanded"/> becomes false.</summary>
        public event Action Collapsed;

        // Only a click on the header focuses the expander; clicks on buttons in the content pass focus on to the parents as usual.
        public override bool AcceptsFocus => headerButton.IsHovered;

        protected override float ContentFraction => expansion.Value;

        protected override Drawable CreateHeaderTextContent(LocalisableString text) => new TextBlock { Text = text, Colour = Foreground, FontWeight = FontWeight.Bold };

        /// <summary>Flips <see cref="IsExpanded"/>, as a click on the header does.</summary>
        public void Toggle()
        {
            if (!IsExpanded.Disabled)
                IsExpanded.Value = !IsExpanded.Value;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!HasFocus || e.Repeat || (e.Key != Key.Space && e.Key != Key.Enter && e.Key != Key.KeypadEnter))
                return base.OnKeyDown(e);

            Toggle();
            return true;
        }

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            headerButton.Focused = true;
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            base.OnFocusLost(e);
            headerButton.Focused = false;
        }

        /// <summary>The clickable header background, behind the header content so controls in the header keep their own clicks.</summary>
        private partial class HeaderButton : CompositeDrawable
        {
            private readonly Expander owner;
            private readonly Box highlight;
            private bool hovered;
            private bool focused;

            public HeaderButton(Expander owner)
            {
                this.owner = owner;
                Masking = true;
                CornerRadius = 6;
                InternalChild = highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 };
            }

            public bool Focused
            {
                set
                {
                    focused = value;
                    updateState();
                }
            }

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
                highlight.FadeTo(0.12f, 80, Easing.OutQuint);
                return false;
            }

            protected override void OnMouseUp(MouseUpEvent e) => updateState();

            protected override bool OnClick(ClickEvent e)
            {
                owner.Toggle();
                return true;
            }

            private void updateState() => highlight.FadeTo(hovered ? 0.07f : focused ? 0.04f : 0, 150, Easing.OutQuint);
        }
    }
}
