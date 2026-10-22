using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Whether this element tells the panels around it when what <see cref="Measure"/> returns may have changed, by calling
        /// <see cref="Layout.InvalidateMeasure"/> or by changing its own size (which its slot passes on). Panels keep the
        /// measurement of an element that does until it says otherwise; one that doesn't is measured again every frame, and
        /// so are the panels around it.
        /// </summary>
        bool ReportsMeasureChanges => false;
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
    /// Changing one makes the panels around the drawable lay out again.
    /// </summary>
    public sealed class LayoutProperties
    {
        private readonly Drawable owner;

        private HorizontalAlignment horizontalAlignment;
        private VerticalAlignment verticalAlignment;
        private int row;
        private int column;
        private int rowSpan = 1;
        private int columnSpan = 1;
        private Dock dock;
        private float left = float.NaN;
        private float top = float.NaN;
        private float right = float.NaN;
        private float bottom = float.NaN;
        private Visibility visibility;

        internal LayoutProperties(Drawable owner)
        {
            this.owner = owner;
        }

        public HorizontalAlignment HorizontalAlignment
        {
            get => horizontalAlignment;
            set => set(ref horizontalAlignment, value);
        }

        public VerticalAlignment VerticalAlignment
        {
            get => verticalAlignment;
            set => set(ref verticalAlignment, value);
        }

        public int Row
        {
            get => row;
            set => set(ref row, value);
        }

        public int Column
        {
            get => column;
            set => set(ref column, value);
        }

        public int RowSpan
        {
            get => rowSpan;
            set => set(ref rowSpan, value);
        }

        public int ColumnSpan
        {
            get => columnSpan;
            set => set(ref columnSpan, value);
        }

        public Dock Dock
        {
            get => dock;
            set => set(ref dock, value);
        }

        public float Left
        {
            get => left;
            set => set(ref left, value);
        }

        public float Top
        {
            get => top;
            set => set(ref top, value);
        }

        public float Right
        {
            get => right;
            set => set(ref right, value);
        }

        public float Bottom
        {
            get => bottom;
            set => set(ref bottom, value);
        }

        public Visibility Visibility
        {
            get => visibility;
            set => set(ref visibility, value);
        }

        private void set<T>(ref T field, T value) => Layout.SetAffectsMeasure(owner, ref field, value);
    }

    /// <summary>
    /// Storage for attached properties plus the measuring rules shared by every panel.
    /// <c>HorizontalAlignment="Center"</c> on any element in XAML compiles to <see cref="SetHorizontalAlignment"/>.
    /// </summary>
    public static class Layout
    {
        private static readonly ConditionalWeakTable<Drawable, LayoutProperties> properties = new ConditionalWeakTable<Drawable, LayoutProperties>();

        public static LayoutProperties Get(Drawable drawable) => properties.GetValue(drawable, d => new LayoutProperties(d));

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
        /// Tells the panels around <paramref name="drawable"/> that its desired size may have changed, like WPF's InvalidateMeasure:
        /// every panel it is in (itself included) measures and arranges again on the next frame, and the others keep what they have.
        /// Panels already notice their children being added, removed or resized, and attached properties changing;
        /// a layout element calls this when something else that <see cref="ILayoutElement.Measure"/> reads changes.
        /// </summary>
        public static void InvalidateMeasure(Drawable drawable)
        {
            Drawable from = null;

            for (var d = drawable; d != null; from = d, d = d.Parent)
            {
                switch (d)
                {
                    case Panel panel:
                        panel.InvalidateLayout();
                        break;

                    // Whatever changed inside content of a fixed size (or collapsed) can't change the size it asks for.
                    // A change to the content itself (an attached property, say) still goes on to its panel.
                    case Slot slot when from != null && from != drawable && from == slot.Content && slot.HasFixedSize:
                        return;
                }
            }
        }

        /// <summary>
        /// Sets a property's backing field and, when the value changes, calls <see cref="InvalidateMeasure"/> on <paramref name="owner"/>:
        /// for properties that change the size an element asks for, like WPF's AffectsMeasure. Returns whether it changed.
        /// </summary>
        public static bool SetAffectsMeasure<T>(Drawable owner, ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            InvalidateMeasure(owner);
            return true;
        }

        // Set when an element that doesn't report its own changes (ILayoutElement.ReportsMeasureChanges) is measured,
        // so the panel measuring it knows it can't keep the result.
        [ThreadStatic]
        private static bool measuredUnreported;

        /// <summary>Starts watching for measurements of elements that don't report their changes; returns the outer state for <see cref="EndWatch"/>.</summary>
        internal static bool BeginWatch()
        {
            bool outer = measuredUnreported;
            measuredUnreported = false;
            return outer;
        }

        /// <summary>Whether such an element was measured since <see cref="BeginWatch"/>. The outer watch sees it too.</summary>
        internal static bool EndWatch(bool outer)
        {
            bool inner = measuredUnreported;
            measuredUnreported = outer || inner;
            return inner;
        }

        /// <summary>Records that a measurement can't be kept past this frame.</summary>
        internal static void MeasuredUnreported() => measuredUnreported = true;

        /// <summary>Measures a layout element that may not report its own changes, recording it when it doesn't.</summary>
        internal static Vector2 MeasureElement(ILayoutElement element, Vector2 availableSize)
        {
            if (!element.ReportsMeasureChanges)
                measuredUnreported = true;

            return element.Measure(availableSize);
        }

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
                // An element with a size of its own lays its content out within that size, as WPF measures it, so text in a
                // fixed-width panel wraps at that width when measured as well as when arranged.
                if (explicitX) inner.X = content.Width;
                if (explicitY) inner.Y = content.Height;

                desired = MeasureElement(element, inner);

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
