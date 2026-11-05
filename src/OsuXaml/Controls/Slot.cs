using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
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

        // Whether the content has a size of its own on each axis, like Width/Height in WPF. Sizes set before the content loads
        // count (in its constructor, or by bindings, ElementName ones included, which run once every element exists), and so do
        // sizes set from outside later: once the content has loaded, the slot remembers the size it last left the content at and
        // looks again when that changes.
        private bool explicitX;
        private bool explicitY;
        private Vector2 sizeBeforeLoad;
        private Vector2 knownSize;
        private bool tracking;

        // The content's margin, which stays on the content (so setting it, back to zero too, is always seen). The slot's padding
        // makes the area the content is placed in smaller by the margin, as osu!framework margins don't shrink relatively sized
        // children; the padding is on the side away from the anchor, as the margin itself already offsets the content from it.
        private MarginPadding margin;

        // Reopened after each measure and arrange: osu!framework only passes on a child's size change while the last one has been handled.
        private readonly LayoutValue contentSize = new LayoutValue(Invalidation.DrawSize | Invalidation.Presence, InvalidationSource.Child);

        // Above zero while the slot measures or arranges its content; changes those cause are already accounted for.
        private int laying;

        public Slot(Drawable content)
        {
            Content = content;

            explicitX = hasOwnSize(Axes.X);
            explicitY = hasOwnSize(Axes.Y);

            margin = content.Margin;
            applyPadding();

            Child = content;

            AddLayout(contentSize);
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            // Runs as the slot loads, just before the content does.
            if (followsSize)
            {
                explicitX = hasOwnSize(Axes.X);
                explicitY = hasOwnSize(Axes.Y);
                sizeBeforeLoad = Content.Size;
            }

            return base.CreateChildDependencies(parent);
        }

        protected override void Update()
        {
            base.Update();
            followContent();
        }

        /// <summary>Whether the content's size on <paramref name="axis"/> is its own: set, and neither automatic nor relative (other than the slot's stretch).</summary>
        private bool hasOwnSize(Axes axis)
        {
            if (Layout.AutoSizeAxes(Content).HasFlag(axis) || Content.RelativeSizeAxes.HasFlag(axis))
                return false;

            return (axis == Axes.X ? Content.Width : Content.Height) > 0;
        }

        /// <summary>
        /// Whether the slot watches the content's size for changes from outside. Not for content that sizes itself on an axis:
        /// reading the size of an auto-sizing container computes it, which must not happen before its children have loaded or
        /// updated; those keep the size they had when they joined.
        /// </summary>
        private bool followsSize => Layout.AutoSizeAxes(Content) == Axes.None;

        /// <summary>Remembers the size the slot left the content at, so only later changes count as set from outside.</summary>
        private void rememberSize()
        {
            if (!followsSize)
                return;

            if (tracking)
                knownSize = Content.Size;
            else
                sizeBeforeLoad = Content.Size;
        }

        /// <summary>Takes in a margin or size set on the content since the slot last laid it out.</summary>
        private void followContent()
        {
            bool changed = false;

            if (!Content.Margin.Equals(margin))
            {
                margin = Content.Margin;
                applyPadding();
                changed = true;
            }

            if (!followsSize)
            {
                if (changed)
                    Layout.InvalidateMeasure(this);
                return;
            }

            if (!tracking)
            {
                if (!Content.IsLoaded)
                {
                    if (changed)
                        Layout.InvalidateMeasure(this);
                    return;
                }

                // A size set between the content loading and now is its own too, except a sprite's: a sprite takes its texture's
                // size as it loads.
                tracking = true;
                knownSize = Content is Sprite ? Content.Size : sizeBeforeLoad;
            }

            var size = Content.Size;

            if (size != knownSize)
            {
                if (size.X != knownSize.X && setFromOutside(Axes.X))
                    changed |= takeOwnSize(Axes.X);
                if (size.Y != knownSize.Y && setFromOutside(Axes.Y))
                    changed |= takeOwnSize(Axes.Y);

                knownSize = Content.Size;
            }

            if (changed)
                Layout.InvalidateMeasure(this);
        }

        /// <summary>
        /// Whether a change to the content's size on <paramref name="axis"/> since the slot last laid it out was made from outside
        /// (a binding, code) rather than by layout. That is certain when the size was the content's own, when the slot had
        /// stretched it, and for content that isn't a layout element, whose size layout never sets. A layout element the slot
        /// sizes to its desired size can also size itself (a panel or content control that sizes to its content), so a change
        /// there is left to layout.
        /// </summary>
        private bool setFromOutside(Axes axis)
            => (axis == Axes.X ? explicitX : explicitY) || stretchedAxes.HasFlag(axis) || !(Content is ILayoutElement);

        /// <summary>A size set from outside on <paramref name="axis"/>: it replaces a stretch the slot applied, and decides again whether the size is explicit.</summary>
        private bool takeOwnSize(Axes axis)
        {
            if (stretchedAxes.HasFlag(axis))
            {
                // Set while the slot stretched the content, so it was taken as relative; it is a size of its own. osu!framework
                // converts the size when the axis stops being relative, so the value set is put back.
                float value = axis == Axes.X ? Content.Width : Content.Height;
                stretchedAxes &= ~axis;
                Content.RelativeSizeAxes &= ~axis;

                if (axis == Axes.X)
                    Content.Width = value;
                else
                    Content.Height = value;
            }

            bool own = hasOwnSize(axis);

            if (axis == Axes.X)
            {
                bool was = explicitX;
                explicitX = own;
                return was != own || own;
            }
            else
            {
                bool was = explicitY;
                explicitY = own;
                return was != own || own;
            }
        }

        private void applyPadding()
        {
            var anchor = Content.Anchor;
            float h = margin.TotalHorizontal;
            float v = margin.TotalVertical;

            Padding = new MarginPadding
            {
                Left = anchor.HasFlag(Anchor.x2) ? h : anchor.HasFlag(Anchor.x1) ? h / 2 : 0,
                Right = anchor.HasFlag(Anchor.x2) ? 0 : anchor.HasFlag(Anchor.x1) ? h / 2 : h,
                Top = anchor.HasFlag(Anchor.y2) ? v : anchor.HasFlag(Anchor.y1) ? v / 2 : 0,
                Bottom = anchor.HasFlag(Anchor.y2) ? 0 : anchor.HasFlag(Anchor.y1) ? v / 2 : v,
            };
        }

        /// <summary>Whether the content asks for the same size whatever happens inside it: it has a size of its own on both axes, or is collapsed.</summary>
        internal bool HasFixedSize => (explicitX && explicitY) || Layout.IsCollapsed(Content);

        /// <summary>Takes the content out of the slot without disposing it, restoring what the slot changed.</summary>
        public Drawable Detach()
        {
            unstretch(Axes.Both);
            Remove(Content, false);
            return Content;
        }

        public Vector2 Measure(Vector2 available)
        {
            followContent();
            laying++;

            try
            {
                return Layout.Measure(Content, available, margin, explicitX, explicitY);
            }
            finally
            {
                laying--;
                rememberSize();
                contentSize.Validate();
            }
        }

        /// <summary>Places the slot and applies alignment: Stretch fills the slot unless the child has its own size.</summary>
        public void Arrange(Vector2 position, Vector2 size)
        {
            followContent();
            laying++;

            try
            {
                arrange(position, size);
            }
            finally
            {
                laying--;
                rememberSize();
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

            if (Content.Anchor != (x | y))
            {
                Content.Anchor = Content.Origin = x | y;
                applyPadding();
            }
            else
                Content.Origin = x | y;

            // Layout elements (panels, borders) have no intrinsic size, so when they don't stretch they take their measured size.
            if (Content is ILayoutElement element)
            {
                bool stretchedX = Content.RelativeSizeAxes.HasFlag(Axes.X);
                bool stretchedY = Content.RelativeSizeAxes.HasFlag(Axes.Y);

                if ((!stretchedX && !explicitX) || (!stretchedY && !explicitY))
                {
                    // An element with a size of its own on one axis is measured within it, as Layout.Measure does, so text in a
                    // fixed-width element gets the height of its wrapped lines rather than of the slot's width.
                    var available = new Vector2(explicitX ? Content.Width : ChildSize.X, explicitY ? Content.Height : ChildSize.Y);
                    var desired = Layout.MeasureElement(element, available);
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
