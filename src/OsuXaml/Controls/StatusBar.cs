using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A bar of status fields, usually docked at the bottom of a window, like WPF's StatusBar. Items are laid out by a DockPanel:
    /// they line up from the left, <c>DockPanel.Dock="Right"</c> puts one at the right edge, and the last item fills what is left.
    /// Children other than <see cref="StatusBarItem"/> and <see cref="Separator"/> are wrapped in a StatusBarItem;
    /// separators turn vertical.
    /// </summary>
    public partial class StatusBar : ItemsControl
    {
        private readonly BarChrome chrome;

        public StatusBar()
        {
            chrome = new BarChrome
            {
                Background = Theme.Mantle,
                Padding = new MarginPadding { Horizontal = 6, Vertical = 1 },
            };
        }

        public Colour4 Background
        {
            get => chrome.Background;
            set => chrome.Background = value;
        }

        /// <summary>The colour of the line along the top edge.</summary>
        public Colour4 BorderBrush
        {
            get => chrome.Line.Colour;
            set => chrome.Line.Colour = value;
        }

        public new MarginPadding Padding
        {
            get => chrome.Padding;
            set => chrome.Padding = value;
        }

        protected override Panel CreateDefaultItemsPanel() => new DockPanel();

        protected override Drawable CreateItemsHost(Panel panel)
        {
            chrome.Child = panel;
            return chrome;
        }

        protected override bool IsItemItsOwnContainer(object item) => item is StatusBarItem || item is Separator;

        protected override Drawable CreateContainer() => new StatusBarItem();

        protected override void PrepareContainer(Drawable container, object item)
        {
            if (item is Separator separator)
            {
                BarLayout.MakeVertical(separator);
                return;
            }

            base.PrepareContainer(container, item);

            // DockPanel.Dock written on a wrapped child belongs on the item that the panel actually holds.
            if (!ReferenceEquals(container, item) && item is Drawable drawable)
                DockPanel.SetDock(container, DockPanel.GetDock(drawable));
        }

        /// <summary>The bar background with a hairline along its top edge.</summary>
        private partial class BarChrome : Border
        {
            public readonly Box Line;

            public BarChrome()
            {
                Chrome.Add(Line = new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = Theme.Border });
            }
        }
    }

    /// <summary>One field of a <see cref="StatusBar"/>. Text content is drawn small and subdued, as status text usually is.</summary>
    public partial class StatusBarItem : ContentControl
    {
        public StatusBarItem()
        {
            Padding = new MarginPadding { Horizontal = 8, Vertical = 4 };
            Foreground = Theme.SubtleText;
            VerticalContentAlignment = VerticalAlignment.Center;
        }

        protected override Drawable CreateTextContent(LocalisableString text) => new TextBlock { Text = text, Colour = Foreground, FontSize = 14 };
    }

    /// <summary>Layout touches shared by bars of controls (status bars, toolbars).</summary>
    internal static class BarLayout
    {
        /// <summary>Turns a horizontal <see cref="Separator"/> into a vertical line for a horizontal bar, or back.</summary>
        public static void MakeVertical(Separator separator, bool vertical = true)
        {
            if (vertical)
            {
                separator.RelativeSizeAxes = Axes.Y;
                separator.Height = 1;
                separator.Width = 1;
                separator.Margin = new MarginPadding { Horizontal = 4, Vertical = 5 };
            }
            else
            {
                separator.RelativeSizeAxes = Axes.X;
                separator.Width = 1;
                separator.Height = 1;
                separator.Margin = new MarginPadding { Horizontal = 5, Vertical = 4 };
            }
        }
    }
}
