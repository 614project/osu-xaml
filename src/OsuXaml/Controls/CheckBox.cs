using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Like WPF's CheckBox: a rounded box that shows a check mark while <see cref="IsChecked"/>, followed by its
    /// <c>Content</c> (text or any element). Clicking it toggles <see cref="IsChecked"/>, which binds two-way, and Space
    /// toggles it while it has focus. Three-state check boxes aren't supported.
    /// </summary>
    public partial class CheckBox : ContentControl
    {
        private const float box_size = 18;
        private const float label_gap = 8;

        private readonly Container box;
        private readonly Box fill;
        private readonly SpriteIcon check;
        private bool isEnabled = true;
        private bool hovered;

        public CheckBox()
        {
            Padding = new MarginPadding { Left = box_size };
            VerticalContentAlignment = VerticalAlignment.Center;

            AddInternal(box = new Container
            {
                Size = new Vector2(box_size),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Masking = true,
                CornerRadius = 4,
                BorderThickness = 1.5f,
                BorderColour = Theme.Border,
                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Surface },
                    check = new SpriteIcon
                    {
                        Icon = FontAwesome.Solid.Check,
                        Size = new Vector2(11),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Theme.AccentText,
                        Alpha = 0,
                    },
                },
            });

            IsChecked.ValueChanged += e =>
            {
                updateState(true);

                if (e.NewValue)
                    Checked?.Invoke();
                else
                    Unchecked?.Invoke();
            };
        }

        /// <summary>Whether the box is checked. Binds two-way.</summary>
        public Bindable<bool> IsChecked { get; } = new Bindable<bool>();

        public event Action Checked;

        public event Action Unchecked;

        /// <summary>Raised after a click (or Space) toggled the box.</summary>
        public event Action Click;

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);
                updateState(true);
            }
        }

        public override bool AcceptsFocus => isEnabled;

        /// <summary>Toggles the box as a click would.</summary>
        public void Toggle()
        {
            if (!isEnabled)
                return;

            IsChecked.Value = !IsChecked.Value;
            Click?.Invoke();
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var size = base.Measure(availableSize);
            return new Vector2(size.X, Math.Max(size.Y, box_size));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateState(false);
        }

        protected override void Update()
        {
            base.Update();

            // Without content the control is just the box.
            float left = PresentedContent == null ? box_size : box_size + label_gap;
            if (Padding.Left != left)
                Padding = new MarginPadding { Left = left };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateState(true);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState(true);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (isEnabled && e.Button == MouseButton.Left)
                box.ScaleTo(0.88f, 80, Easing.OutQuint);

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            box.ScaleTo(1, 300, Easing.OutElastic);
            base.OnMouseUp(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return false;

            Toggle();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (HasFocus && e.Key == Key.Space && !e.Repeat)
            {
                Toggle();
                return true;
            }

            return base.OnKeyDown(e);
        }

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            updateState(true);
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            base.OnFocusLost(e);
            updateState(true);
        }

        private void updateState(bool animate)
        {
            if (!IsLoaded)
                return;

            double duration = animate ? 150 : 0;
            bool on = IsChecked.Value;
            bool highlight = isEnabled && (hovered || HasFocus);

            fill.FadeColour(on ? (highlight ? Theme.Accent.Lighten(0.15f) : Theme.Accent) : (highlight ? Theme.Hover : Theme.Surface), duration, Easing.OutQuint);
            box.TransformTo(nameof(BorderColour), (ColourInfo)(on ? Theme.Accent : highlight ? Theme.Accent : Theme.Border), duration, Easing.OutQuint);

            check.FadeTo(on ? 1 : 0, duration, Easing.OutQuint);
            check.ScaleTo(on ? 1 : 0.5f, animate ? 250 : 0, on ? Easing.OutBack : Easing.OutQuint);
        }
    }
}
