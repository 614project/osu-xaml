using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Layout;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// The rectangle a panel gives one child. The panel positions and sizes the slot;
    /// the slot applies the child's margin and alignment inside it.
    /// When the child changes size or presence by itself, the slot tells the panels around it to lay out again.
    /// </summary>
    public partial class Slot : Container
    {
        public readonly Drawable Content;

        private readonly bool explicitX;
        private readonly bool explicitY;
        private readonly MarginPadding margin;

        // Reopened after each measure and arrange: osu!framework only passes on a child's size change while the last one has been handled.
        private readonly LayoutValue contentSize = new LayoutValue(Invalidation.DrawSize | Invalidation.Presence, InvalidationSource.Child);

        // Above zero while the slot measures or arranges its content; changes those cause are already accounted for.
        private int laying;

        public Slot(Drawable content)
        {
            Content = content;

            // A size set before the child joins a panel counts as explicit, like Width/Height in WPF.
            explicitX = !Layout.AutoSizeAxes(content).HasFlag(Axes.X) && !content.RelativeSizeAxes.HasFlag(Axes.X) && content.Width > 0;
            explicitY = !Layout.AutoSizeAxes(content).HasFlag(Axes.Y) && !content.RelativeSizeAxes.HasFlag(Axes.Y) && content.Height > 0;

            // osu!framework margins don't shrink relatively sized children, so the margin becomes slot padding.
            margin = content.Margin;
            content.Margin = default;
            Padding = margin;

            Child = content;

            AddLayout(contentSize);
        }

        /// <summary>Whether the content asks for the same size whatever happens inside it: it has a size of its own on both axes, or is collapsed.</summary>
        internal bool HasFixedSize => (explicitX && explicitY) || Layout.IsCollapsed(Content);

        /// <summary>Takes the content out of the slot without disposing it, restoring what the slot changed.</summary>
        public Drawable Detach()
        {
            unstretch(Axes.Both);
            Remove(Content, false);
            Content.Margin = margin;
            return Content;
        }

        public Vector2 Measure(Vector2 available)
        {
            laying++;

            try
            {
                return Layout.Measure(Content, available, margin, explicitX, explicitY);
            }
            finally
            {
                laying--;
                contentSize.Validate();
            }
        }

        /// <summary>Places the slot and applies alignment: Stretch fills the slot unless the child has its own size.</summary>
        public void Arrange(Vector2 position, Vector2 size)
        {
            laying++;

            try
            {
                arrange(position, size);
            }
            finally
            {
                laying--;
                contentSize.Validate();
            }
        }

        protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
        {
            bool result = base.OnInvalidate(invalidation, source);

            if (source == InvalidationSource.Child && laying == 0 && (invalidation & (Invalidation.DrawSize | Invalidation.Presence)) != 0)
                Layout.InvalidateMeasure(this);

            return result;
        }

        private void arrange(Vector2 position, Vector2 size)
        {
            Position = position;
            Size = Vector2.ComponentMax(Vector2.Zero, size);

            var props = Layout.Get(Content);
            bool canStretchX = !explicitX && !Layout.AutoSizeAxes(Content).HasFlag(Axes.X);
            bool canStretchY = !explicitY && !Layout.AutoSizeAxes(Content).HasFlag(Axes.Y);

            Anchor x;

            switch (props.HorizontalAlignment)
            {
                case HorizontalAlignment.Stretch when canStretchX:
                    stretch(Axes.X);
                    x = Anchor.x0;
                    break;

                case HorizontalAlignment.Stretch:
                    // WPF centres a child that has an explicit width; auto-sized content starts at the left.
                    x = explicitX ? Anchor.x1 : Anchor.x0;
                    break;

                case HorizontalAlignment.Center:
                    unstretch(Axes.X);
                    x = Anchor.x1;
                    break;

                case HorizontalAlignment.Right:
                    unstretch(Axes.X);
                    x = Anchor.x2;
                    break;

                default:
                    unstretch(Axes.X);
                    x = Anchor.x0;
                    break;
            }

            Anchor y;

            switch (props.VerticalAlignment)
            {
                case VerticalAlignment.Stretch when canStretchY:
                    stretch(Axes.Y);
                    y = Anchor.y0;
                    break;

                case VerticalAlignment.Stretch:
                    y = explicitY ? Anchor.y1 : Anchor.y0;
                    break;

                case VerticalAlignment.Center:
                    unstretch(Axes.Y);
                    y = Anchor.y1;
                    break;

                case VerticalAlignment.Bottom:
                    unstretch(Axes.Y);
                    y = Anchor.y2;
                    break;

                default:
                    unstretch(Axes.Y);
                    y = Anchor.y0;
                    break;
            }

            Content.Anchor = Content.Origin = x | y;

            // Layout elements (panels, borders) have no intrinsic size, so when they don't stretch they take their measured size.
            if (Content is ILayoutElement element)
            {
                bool stretchedX = Content.RelativeSizeAxes.HasFlag(Axes.X);
                bool stretchedY = Content.RelativeSizeAxes.HasFlag(Axes.Y);

                if ((!stretchedX && !explicitX) || (!stretchedY && !explicitY))
                {
                    var desired = Layout.MeasureElement(element, ChildSize);
                    if (!stretchedX && !explicitX) Content.Width = desired.X;
                    if (!stretchedY && !explicitY) Content.Height = desired.Y;
                }
            }
        }

        private Axes stretchedAxes;
        private Vector2 sizeBeforeStretch;

        /// <summary>Undoes a stretch this slot applied, when the alignment changes away from Stretch or the content leaves.</summary>
        private void unstretch(Axes axis)
        {
            var ours = stretchedAxes & axis;
            if (ours == Axes.None)
                return;

            stretchedAxes &= ~ours;
            Content.RelativeSizeAxes &= ~ours;

            // The size it had, not the stretched one, which would count as an explicit size in the next slot (a recycled container's, say).
            if (ours.HasFlag(Axes.X))
                Content.Width = sizeBeforeStretch.X;
            if (ours.HasFlag(Axes.Y))
                Content.Height = sizeBeforeStretch.Y;
        }

        private void stretch(Axes axis)
        {
            if (Content.RelativeSizeAxes.HasFlag(axis))
                return;

            if (axis == Axes.X)
                sizeBeforeStretch.X = Content.Width;
            else
                sizeBeforeStretch.Y = Content.Height;

            Content.RelativeSizeAxes |= axis;
            stretchedAxes |= axis;

            if (axis == Axes.X)
                Content.Width = 1;
            else
                Content.Height = 1;
        }
    }
}
