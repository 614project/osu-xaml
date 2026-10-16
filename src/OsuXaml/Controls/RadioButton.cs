using System;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A round option button with a label, like WPF's RadioButton. Checking one unchecks the others in its group:
    /// buttons with the same <see cref="GroupName"/> in the same window, or, without a GroupName, the buttons that share
    /// its parent panel. Clicking a checked button leaves it checked. <see cref="IsChecked"/> binds two-way.
    /// </summary>
    public partial class RadioButton : ContentControl
    {
        private const float indicator_size = 20;
        private const float indicator_spacing = 8;

        // Every loaded radio button, to find group members wherever they are in the tree. Weak, so removed buttons can be collected.
        private static readonly ConditionalWeakTable<RadioButton, object> live = new ConditionalWeakTable<RadioButton, object>();

        private readonly CircularContainer indicator;
        private readonly Box fill;
        private readonly Circle dot;
        private MarginPadding padding;
        private bool isEnabled = true;
        private bool hovered;

        public RadioButton()
        {
            VerticalContentAlignment = VerticalAlignment.Center;
            Padding = default;

            Chrome.Add(indicator = new CircularContainer
            {
                Size = new Vector2(indicator_size),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Masking = true,
                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Surface },
                    dot = new Circle
                    {
                        Size = new Vector2(8),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Theme.AccentText,
                        Scale = Vector2.Zero,
                    },
                },
            });

            IsChecked.BindValueChanged(e =>
            {
                if (e.NewValue && IsLoaded)
                    uncheckGroup();

                (e.NewValue ? Checked : Unchecked)?.Invoke();
                updateState(IsLoaded);
            });
        }

        /// <summary>Whether this option is chosen. Binds two-way.</summary>
        public Bindable<bool> IsChecked { get; } = new Bindable<bool>();

        /// <summary>Buttons with the same name form one group anywhere in the window. Without one, siblings form the group.</summary>
        public string GroupName { get; set; }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);
            }
        }

        /// <summary>Raised when the button is clicked, after it has been checked.</summary>
        public event Action Click;

        public event Action Checked;

        public event Action Unchecked;

        /// <summary>Space around the label. The indicator always sits to the left of it.</summary>
        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                padding = value;
                base.Padding = new MarginPadding { Left = value.Left + indicator_size + indicator_spacing, Top = value.Top, Right = value.Right, Bottom = value.Bottom };
            }
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            var desired = base.Measure(availableSize);

            // Without content the indicator alone sets the size.
            if (Content == null)
                desired.X = indicator_size + padding.TotalHorizontal + BorderThickness * 2;

            return Vector2.ComponentMax(desired, new Vector2(0, indicator_size + padding.TotalVertical));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            live.AddOrUpdate(this, null);

            // A button checked before it joined the tree wins over its group, like the last one checked in WPF.
            if (IsChecked.Value)
                uncheckGroup();

            updateState(false);
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
            if (isEnabled)
                indicator.ScaleTo(0.85f, 100, Easing.OutQuint);
            return false;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            indicator.ScaleTo(1, 300, Easing.OutQuint);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return false;

            if (!IsChecked.Disabled)
                IsChecked.Value = true;

            Click?.Invoke();
            return true;
        }

        private void uncheckGroup()
        {
            object scope = groupScope();

            foreach (var other in live.Select(p => p.Key).ToArray())
            {
                if (other != this && other.IsChecked.Value && !other.IsChecked.Disabled && (other.GroupName ?? string.Empty) == (GroupName ?? string.Empty) && ReferenceEquals(other.groupScope(), scope))
                    other.IsChecked.Value = false;
            }
        }

        /// <summary>What a group is limited to: the window (root) for named groups, the parent element otherwise.</summary>
        private object groupScope()
        {
            if (!string.IsNullOrEmpty(GroupName))
            {
                Drawable root = this;
                while (root.Parent != null)
                    root = root.Parent;
                return root;
            }

            // A panel wraps each child in a slot; the panel is the parent that counts.
            var parent = Parent;
            return parent is Slot ? parent.Parent : parent;
        }

        private void updateState(bool animate)
        {
            if (!IsLoaded)
                return;

            double duration = animate ? 150 : 0;
            bool isChecked = IsChecked.Value;

            fill.FadeColour(isChecked ? (hovered && isEnabled ? Theme.Accent.Lighten(0.15f) : Theme.Accent) : hovered && isEnabled ? Theme.Hover : Theme.Surface, duration, Easing.OutQuint);
            dot.ScaleTo(isChecked ? 1 : 0, animate ? 250 : 0, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            live.Remove(this);
            base.Dispose(isDisposing);
        }
    }
}
