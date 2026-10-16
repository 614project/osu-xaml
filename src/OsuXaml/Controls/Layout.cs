using System;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;

[assembly: OsuXaml.XamlGlobalProperties(typeof(OsuXaml.Controls.Layout))]

namespace OsuXaml.Controls
{
    public enum HorizontalAlignment
    {
        Stretch,
        Left,
        Center,
        Right,
    }

    public enum VerticalAlignment
    {
        Stretch,
        Top,
        Center,
        Bottom,
    }

    public enum Orientation
    {
        Vertical,
        Horizontal,
    }

    /// <summary>Like WPF: Hidden keeps its space in panels, Collapsed takes none.</summary>
    public enum Visibility
    {
        Visible,
        Hidden,
        Collapsed,
    }

    public enum Dock
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    /// <summary>
    /// A drawable that can report how much space it wants, like WPF's MeasureOverride.
    /// Plain osu!framework drawables don't need this: their auto-size or explicit size is used instead.
    /// </summary>
    public interface ILayoutElement
    {
        Vector2 Measure(Vector2 availableSize);
    }

    /// <summary>
    /// A container whose children can be inserted at an index, so ItemsSource keeps list order.
    /// </summary>
    public interface IItemsHost
    {
        void Insert(int index, Drawable drawable);

        bool Remove(Drawable drawable, bool disposeImmediately);
    }

    /// <summary>
    /// Attached layout properties that any drawable can carry (Grid.Row, DockPanel.Dock, HorizontalAlignment, ...).
    /// </summary>
    public sealed class LayoutProperties
    {
        public HorizontalAlignment HorizontalAlignment;
        public VerticalAlignment VerticalAlignment;
        public int Row;
        public int Column;
        public int RowSpan = 1;
        public int ColumnSpan = 1;
        public Dock Dock;
        public float Left = float.NaN;
        public float Top = float.NaN;
        public float Right = float.NaN;
        public float Bottom = float.NaN;
        public Visibility Visibility;
    }

    /// <summary>
    /// Storage for attached properties plus the measuring rules shared by every panel.
    /// <c>HorizontalAlignment="Center"</c> on any element in XAML compiles to <see cref="SetHorizontalAlignment"/>.
    /// </summary>
    public static class Layout
    {
        private static readonly ConditionalWeakTable<Drawable, LayoutProperties> properties = new ConditionalWeakTable<Drawable, LayoutProperties>();

        public static LayoutProperties Get(Drawable drawable) => properties.GetOrCreateValue(drawable);

        public static void SetHorizontalAlignment(Drawable drawable, HorizontalAlignment value) => Get(drawable).HorizontalAlignment = value;

        public static void SetVerticalAlignment(Drawable drawable, VerticalAlignment value) => Get(drawable).VerticalAlignment = value;

        /// <summary>The axes a drawable sizes from its own content. Sprite text always measures itself.</summary>
        public static Axes AutoSizeAxes(Drawable drawable) => drawable switch
        {
            CompositeDrawable c => c.AutoSizeAxes,
            SpriteText t => ~t.RelativeSizeAxes & Axes.Both,
            _ => Axes.None,
        };

        public static MarginPadding Add(MarginPadding a, MarginPadding b)
            => new MarginPadding { Left = a.Left + b.Left, Top = a.Top + b.Top, Right = a.Right + b.Right, Bottom = a.Bottom + b.Bottom };

        public static HorizontalAlignment GetHorizontalAlignment(Drawable drawable) => Get(drawable).HorizontalAlignment;

        public static VerticalAlignment GetVerticalAlignment(Drawable drawable) => Get(drawable).VerticalAlignment;

        /// <summary><c>Visibility="Collapsed"</c> hides the element and gives it no space in panels.</summary>
        public static void SetVisibility(Drawable drawable, Visibility value)
        {
            Get(drawable).Visibility = value;
            drawable.Alpha = value == Visibility.Visible ? 1 : 0;
        }

        public static Visibility GetVisibility(Drawable drawable) => Get(drawable).Visibility;

        public static bool IsCollapsed(Drawable drawable) => properties.TryGetValue(drawable, out var p) && p.Visibility == Visibility.Collapsed;

        /// <summary>
        /// The size <paramref name="content"/> would like, including its margin.
        /// Layout elements measure themselves; other drawables use their auto size or explicit size,
        /// and contribute nothing on axes where they stretch.
        /// </summary>
        public static Vector2 Measure(Drawable content, Vector2 available, MarginPadding margin, bool explicitX, bool explicitY)
        {
            if (IsCollapsed(content))
                return Vector2.Zero;

            var inner = new Vector2(
                Math.Max(0, available.X - margin.TotalHorizontal),
                Math.Max(0, available.Y - margin.TotalVertical));

            Vector2 desired;

            if (content is ILayoutElement element)
            {
                desired = element.Measure(inner);

                if (explicitX) desired.X = content.Width;
                if (explicitY) desired.Y = content.Height;
            }
            else
            {
                desired = new Vector2(
                    AutoSizeAxes(content).HasFlag(Axes.X) ? content.DrawWidth : explicitX ? content.Width : 0,
                    AutoSizeAxes(content).HasFlag(Axes.Y) ? content.DrawHeight : explicitY ? content.Height : 0);
            }

            return desired + new Vector2(margin.TotalHorizontal, margin.TotalVertical);
        }
    }
}
