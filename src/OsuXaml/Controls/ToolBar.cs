using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using Visibility = OsuXaml.Controls.Visibility;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A strip of commands, like WPF's ToolBar: list Buttons, ToggleButtons, Separators, ComboBoxes or any other element as children.
    /// Buttons that keep their default look turn flat (no accent fill, a hover highlight, tighter padding), separators turn
    /// vertical and small elements are centred across the strip. An optional <see cref="Header"/> is shown before the items.
    /// Put several in a <see cref="ToolBarTray"/> to arrange them in bands.
    /// </summary>
    public partial class ToolBar : ItemsControl
    {
        // What Button's constructor sets; anything else was chosen on purpose and is kept.
        private static readonly MarginPadding default_button_padding = new MarginPadding { Horizontal = 16, Vertical = 8 };

        private readonly Border chrome;
        private readonly StackPanel strip;
        private readonly Grip grip;
        private readonly ContentPresenter header;
        private Orientation orientation = Orientation.Horizontal;
        private int band;
        private int bandIndex;

        public ToolBar()
        {
            chrome = new Border
            {
                Background = Theme.Mantle,
                CornerRadius = 6,
                Padding = new MarginPadding(4),
                Child = strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 },
            };

            strip.Add(grip = new Grip());
            strip.Add(header = new ContentPresenter { Foreground = Theme.SubtleText, Margin = new MarginPadding { Horizontal = 6 } });
            Layout.SetVerticalAlignment(header, VerticalAlignment.Center);
            Layout.SetVisibility(header, Visibility.Collapsed);
        }

        /// <summary>A title shown before the items.</summary>
        public object Header
        {
            get => header.Content;
            set
            {
                header.Content = value;
                Layout.SetVisibility(header, value == null ? Visibility.Collapsed : Visibility.Visible);
            }
        }

        /// <summary>
        /// Horizontal or vertical. Inside a <see cref="ToolBarTray"/> the tray's orientation is used instead. Set before the toolbar loads.
        /// </summary>
        public Orientation Orientation
        {
            get => orientation;
            set => orientation = value;
        }

        /// <summary>The row (or column) of a <see cref="ToolBarTray"/> this toolbar goes in.</summary>
        public int Band
        {
            get => band;
            set => Layout.SetAffectsMeasure(this, ref band, value);
        }

        /// <summary>The position within its band, lowest first.</summary>
        public int BandIndex
        {
            get => bandIndex;
            set => Layout.SetAffectsMeasure(this, ref bandIndex, value);
        }

        public Colour4 Background
        {
            get => chrome.Background;
            set => chrome.Background = value;
        }

        public Colour4 BorderBrush
        {
            get => chrome.BorderBrush;
            set => chrome.BorderBrush = value;
        }

        public new float BorderThickness
        {
            get => chrome.BorderThickness;
            set => chrome.BorderThickness = value;
        }

        public new float CornerRadius
        {
            get => chrome.CornerRadius;
            set => chrome.CornerRadius = value;
        }

        public new MarginPadding Padding
        {
            get => chrome.Padding;
            set => chrome.Padding = value;
        }

        /// <summary>Whether the grip at the start is shown.</summary>
        public bool ShowGrip
        {
            get => Layout.GetVisibility(grip) != Visibility.Collapsed;
            set => Layout.SetVisibility(grip, value ? Visibility.Visible : Visibility.Collapsed);
        }

        private Orientation effectiveOrientation => Parent is Slot slot && slot.Parent is ToolBarTray tray ? tray.Orientation : orientation;

        protected override Panel CreateDefaultItemsPanel() => new StackPanel { Spacing = 2 };

        protected override Drawable CreateItemsHost(Panel panel)
        {
            var o = effectiveOrientation;

            strip.Orientation = o;
            grip.Orientation = o;

            if (panel is StackPanel stack)
                stack.Orientation = o;

            // The items panel takes its measured size in the strip, instead of filling the control.
            panel.RelativeSizeAxes = Axes.None;
            panel.Size = Vector2.Zero;
            strip.Add(panel);
            return chrome;
        }

        protected override void PrepareContainer(Drawable container, object item)
        {
            base.PrepareContainer(container, item);

            bool horizontal = effectiveOrientation == Orientation.Horizontal;

            switch (container)
            {
                case Separator separator:
                    BarLayout.MakeVertical(separator, horizontal);
                    break;

                case Button button:
                    flatten(button);
                    break;

                default:
                    // Text and fixed-size elements, which can't fill the strip's thickness, sit in its middle rather than at its start.
                    var across = horizontal ? Axes.Y : Axes.X;
                    float size = horizontal ? container.Height : container.Width;
                    bool fillsAcross = !Layout.AutoSizeAxes(container).HasFlag(across) && (container.RelativeSizeAxes.HasFlag(across) || size == 0);

                    if (!fillsAcross)
                    {
                        var props = Layout.Get(container);

                        if (horizontal && props.VerticalAlignment == VerticalAlignment.Stretch)
                            props.VerticalAlignment = VerticalAlignment.Center;
                        else if (!horizontal && props.HorizontalAlignment == HorizontalAlignment.Stretch)
                            props.HorizontalAlignment = HorizontalAlignment.Center;
                    }

                    break;
            }
        }

        /// <summary>Gives a button the toolbar look, leaving anything that was set to a non-default value alone.</summary>
        private static void flatten(Button button)
        {
            if (button.Background == Theme.Accent || button.Background == Theme.Surface)
                button.Background = Colour4.Transparent;

            if (button.Foreground == Theme.AccentText)
                button.Foreground = Theme.Text;

            if (button.Padding.Equals(default_button_padding))
                button.Padding = new MarginPadding { Horizontal = 10, Vertical = 5 };

            if (button.CornerRadius == 6)
                button.CornerRadius = 4;

            if (button is ToggleButton toggle && toggle.CheckedBackground == Theme.Accent)
            {
                toggle.CheckedBackground = Theme.Accent.Opacity(0.25f);
                toggle.CheckedForeground = Theme.Accent;
            }
        }

        /// <summary>The dotted handle at the start of a toolbar.</summary>
        private partial class Grip : CompositeDrawable
        {
            private Orientation orientation = Orientation.Horizontal;

            public Grip()
            {
                Margin = new MarginPadding(2);
                Layout.SetHorizontalAlignment(this, HorizontalAlignment.Center);
                Layout.SetVerticalAlignment(this, VerticalAlignment.Center);
                Orientation = Orientation.Horizontal;
            }

            public Orientation Orientation
            {
                get => orientation;
                set
                {
                    orientation = value;
                    bool horizontal = value == Orientation.Horizontal;

                    Size = horizontal ? new Vector2(6, 16) : new Vector2(16, 6);
                    ClearInternal();

                    for (int i = 0; i < 8; i++)
                    {
                        int along = i / 2, across = i % 2;

                        AddInternal(new Circle
                        {
                            Size = new Vector2(2),
                            Colour = Theme.Overlay,
                            Position = horizontal ? new Vector2(across * 4, along * 4.5f) : new Vector2(along * 4.5f, across * 4),
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Holds toolbars in bands, like WPF's ToolBarTray: each <see cref="ToolBar.Band"/> is a row (a column when vertical),
    /// ordered by <see cref="ToolBar.BandIndex"/>. Toolbars keep their own length.
    /// </summary>
    public partial class ToolBarTray : Panel
    {
        private Orientation orientation = Orientation.Horizontal;
        private float spacing = 4;

        public ToolBarTray()
        {
            Background = Theme.Crust;
            Padding = new MarginPadding(4);
        }

        public Orientation Orientation
        {
            get => orientation;
            set => Layout.SetAffectsMeasure(this, ref orientation, value);
        }

        /// <summary>The gap between toolbars and between bands.</summary>
        public float Spacing
        {
            get => spacing;
            set => Layout.SetAffectsMeasure(this, ref spacing, value);
        }

        private List<List<Slot>> bands()
            => Slots.Where(s => !Layout.IsCollapsed(s.Content))
                    .GroupBy(s => (s.Content as ToolBar)?.Band ?? 0)
                    .OrderBy(g => g.Key)
                    .Select(g => g.OrderBy(s => (s.Content as ToolBar)?.BandIndex ?? 0).ToList())
                    .ToList();

        protected override Vector2 MeasureOverride(Vector2 available)
        {
            float along = 0, cross = 0;
            var bandList = bands();

            foreach (var band in bandList)
            {
                float length = 0, thickness = 0;

                foreach (var slot in band)
                {
                    var d = slot.Measure(new Vector2(float.PositiveInfinity));
                    length += Axis(d, Orientation);
                    thickness = Math.Max(thickness, Cross(d, Orientation));
                }

                length += Math.Max(0, band.Count - 1) * Spacing;
                along = Math.Max(along, length);
                cross += thickness;
            }

            cross += Math.Max(0, bandList.Count - 1) * Spacing;
            return Make(along, cross, Orientation);
        }

        protected override void ArrangeOverride(Vector2 size)
        {
            float crossOffset = 0;

            foreach (var slot in Slots.Where(s => Layout.IsCollapsed(s.Content)))
                slot.Arrange(Vector2.Zero, Vector2.Zero);

            foreach (var band in bands())
            {
                var sizes = band.Select(s => s.Measure(new Vector2(float.PositiveInfinity))).ToList();
                float thickness = sizes.Max(d => Cross(d, Orientation));
                float position = 0;

                for (int i = 0; i < band.Count; i++)
                {
                    float length = Axis(sizes[i], Orientation);
                    band[i].Arrange(Make(position, crossOffset, Orientation), Make(length, thickness, Orientation));
                    position += length + Spacing;
                }

                crossOffset += thickness + Spacing;
            }
        }
    }
}
