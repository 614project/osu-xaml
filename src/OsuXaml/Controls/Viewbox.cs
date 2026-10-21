using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Scales its one child to fit the space it gets, like WPF's Viewbox:
    /// <c>&lt;Viewbox Width="200"&gt;&lt;TextBlock Text="Big" /&gt;&lt;/Viewbox&gt;</c>.
    /// The child is laid out at its natural (measured) size and then scaled as a whole, so text, borders and panels
    /// keep their proportions. <see cref="Stretch"/> is Uniform by default; <see cref="StretchDirection"/> can stop it
    /// enlarging or shrinking. Without a size of its own the viewbox takes the child's size.
    /// </summary>
    public partial class Viewbox : CompositeDrawable, ILayoutElement
    {
        private readonly Container scaler;
        private Drawable child;
        private Slot slot;

        private bool sizeToContentX;
        private bool sizeToContentY;
        private bool? explicitWidth;
        private bool? explicitHeight;

        public Viewbox()
        {
            InternalChild = scaler = new Container { Anchor = Anchor.Centre, Origin = Anchor.Centre };
        }

        /// <summary>The content to scale; a XAML child element lands here.</summary>
        public Drawable Child
        {
            get => child;
            set
            {
                if (child == value)
                    return;

                if (slot != null)
                {
                    // The old child belongs to whoever set it, so it leaves without being disposed.
                    slot.Detach();
                    scaler.Remove(slot, true);
                    slot = null;
                }

                child = value;

                if (value != null)
                    scaler.Add(slot = new Slot(value));

                Layout.InvalidateMeasure(this);
            }
        }

        private Stretch stretch = Stretch.Uniform;
        private StretchDirection stretchDirection = StretchDirection.Both;

        public Stretch Stretch
        {
            get => stretch;
            set => Layout.SetAffectsMeasure(this, ref stretch, value);
        }

        public StretchDirection StretchDirection
        {
            get => stretchDirection;
            set => Layout.SetAffectsMeasure(this, ref stretchDirection, value);
        }

        /// <summary>True: the child's slot passes on its changes, and so do the stretch settings.</summary>
        public bool ReportsMeasureChanges => true;

        /// <summary>The child's natural size, including its margin.</summary>
        public Vector2 ContentSize => slot?.Measure(new Vector2(float.PositiveInfinity)) ?? Vector2.Zero;

        /// <summary>The scale applied to the child in the last layout pass.</summary>
        public Vector2 ChildScale => scaler.Scale;

        private void decideExplicitSize()
        {
            explicitWidth ??= !RelativeSizeAxes.HasFlag(Axes.X) && Width > 0;
            explicitHeight ??= !RelativeSizeAxes.HasFlag(Axes.Y) && Height > 0;
        }

        public Vector2 Measure(Vector2 availableSize)
        {
            decideExplicitSize();

            var constraint = new Vector2(
                explicitWidth == true ? Width : availableSize.X,
                explicitHeight == true ? Height : availableSize.Y);

            return StretchMath.ComputeSize(constraint, ContentSize, Stretch, StretchDirection);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            decideExplicitSize();

            sizeToContentX = !(Parent is Slot) && explicitWidth == false && !RelativeSizeAxes.HasFlag(Axes.X);
            sizeToContentY = !(Parent is Slot) && explicitHeight == false && !RelativeSizeAxes.HasFlag(Axes.Y);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (sizeToContentX || sizeToContentY)
            {
                var desired = Measure(new Vector2(sizeToContentX ? float.PositiveInfinity : DrawWidth, sizeToContentY ? float.PositiveInfinity : DrawHeight));
                if (sizeToContentX) Width = desired.X;
                if (sizeToContentY) Height = desired.Y;
            }

            var content = ContentSize;

            scaler.Size = content;
            slot?.Arrange(Vector2.Zero, content);
            scaler.Scale = StretchMath.ComputeScale(DrawSize, content, Stretch, StretchDirection);
        }
    }
}
