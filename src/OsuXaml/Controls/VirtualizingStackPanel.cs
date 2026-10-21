using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>What a virtualizing list does with the container of an item that scrolls out of view, like WPF's VirtualizationMode.</summary>
    public enum VirtualizationMode
    {
        /// <summary>The container is thrown away, and a new one is made when the item comes back.</summary>
        Standard,

        /// <summary>The container is kept and shows the next item that scrolls into view. Its content is rebuilt for that item.</summary>
        Recycling,
    }

    /// <summary>
    /// Base for items panels that create containers only for the items that can be seen, like WPF's VirtualizingPanel.
    /// Its attached properties go on the list: <c>&lt;ListBox VirtualizingPanel.IsVirtualizing="False"&gt;</c> creates every
    /// container up front, and <c>VirtualizingPanel.VirtualizationMode="Recycling"</c> reuses containers. Both are read when
    /// the list loads. A TreeView, as in WPF, only virtualizes when <c>IsVirtualizing</c> is set to True on it.
    /// </summary>
    public abstract partial class VirtualizingPanel : Panel
    {
        private static readonly ConditionalWeakTable<Drawable, Settings> settings = new ConditionalWeakTable<Drawable, Settings>();

        /// <summary>Whether <paramref name="element"/> (a list, or its items panel) virtualizes. On unless turned off.</summary>
        public static bool GetIsVirtualizing(Drawable element) => IsVirtualizingSet(element) ?? true;

        public static void SetIsVirtualizing(Drawable element, bool value) => settings.GetOrCreateValue(element).IsVirtualizing = value;

        /// <summary>The value set with <see cref="SetIsVirtualizing"/>, or null when it was never set.</summary>
        internal static bool? IsVirtualizingSet(Drawable element) => settings.TryGetValue(element, out var s) ? s.IsVirtualizing : null;

        public static VirtualizationMode GetVirtualizationMode(Drawable element) => settings.TryGetValue(element, out var s) ? s.Mode : VirtualizationMode.Standard;

        public static void SetVirtualizationMode(Drawable element, VirtualizationMode value) => settings.GetOrCreateValue(element).Mode = value;

        /// <summary>The list whose containers this panel realizes, or null when it lays out ordinary children.</summary>
        internal ItemsControl Owner { get; private set; }

        /// <summary>The items that have a container in this panel, in item order.</summary>
        internal abstract IReadOnlyList<int> RealizedIndices { get; }

        /// <summary>Called by the list when it loads; from then on the panel decides which items get containers.</summary>
        internal void Attach(ItemsControl owner) => Owner = owner;

        internal abstract void OnItemsInserted(int index, int count);

        /// <summary>The items' containers have already been released by the list.</summary>
        internal abstract void OnItemsRemoved(int index, int count);

        internal abstract void OnItemMoved(int from, int to);

        /// <summary>The items were replaced; <paramref name="previousIndex"/> gives each new item's old index, or -1 for a new item.</summary>
        internal abstract void OnItemsReset(IReadOnlyList<int> previousIndex);

        /// <summary>Creates the container for <paramref name="index"/> now if needed, and scrolls until it (or <paramref name="part"/> of it) is in view.</summary>
        internal abstract void BringIndexIntoView(int index, Func<Drawable, Drawable> part);

        /// <summary>Creates the container for <paramref name="index"/> now if needed, without scrolling. It goes again once out of view.</summary>
        internal abstract void Realize(int index);

        private sealed class Settings
        {
            public bool? IsVirtualizing;
            public VirtualizationMode Mode;
        }
    }

    /// <summary>
    /// A StackPanel that, as a list's items panel, creates containers only for the items in view plus a margin either side,
    /// like WPF's VirtualizingStackPanel. ListBox, ListView and DataGrid use it by default, so a list of a hundred thousand
    /// items costs about as much as the rows on screen. Items that have been shown keep their measured length; the rest
    /// count as the average of those, so the scroll extent is exact for uniform rows and close for varying ones, and when
    /// a guess above the viewport is corrected the scroll position moves with it so the rows in view stay put.
    /// Given children directly (not as an items panel), or with <c>IsVirtualizing</c> off, it is a plain StackPanel.
    /// </summary>
    public partial class VirtualizingStackPanel : VirtualizingPanel
    {
        // Rows are realized this far past each edge of the viewport (a share of its length, at least min_cache pixels),
        // so the rows a scroll reveals already exist when it is drawn.
        private const float cache_length = 0.5f;
        private const float min_cache = 48;

        private readonly ItemSizeCache sizes = new ItemSizeCache();
        private readonly List<int> realized = new List<int>();

        private float widestItem;

        // The item at the top of the viewport and where it started before this frame's measuring, to keep it in place.
        private int anchorIndex = -1;
        private double anchorOffset;

        // The item being scrolled into view, kept realized until the scroll viewer's request settles.
        private int bringIntoView = -1;
        private ScrollViewer bringIntoViewViewer;
        private int bringIntoViewRequest;

        // The extent and widest item at the last arrange; when arranging changes them, the panel's desired size changed.
        private Vector2 arrangedExtent = new Vector2(float.NaN);

        private Orientation orientation;
        private float spacing;

        public Orientation Orientation
        {
            get => orientation;
            set => Layout.SetAffectsMeasure(this, ref orientation, value);
        }

        public float Spacing
        {
            get => spacing;
            set => Layout.SetAffectsMeasure(this, ref spacing, value);
        }

        /// <summary>How many items have a container right now.</summary>
        public int RealizedCount => Owner == null ? Slots.Count : realized.Count;

        internal override IReadOnlyList<int> RealizedIndices => realized;

        internal override void OnItemsInserted(int index, int count)
        {
            sizes.Insert(index, count);

            for (int i = 0; i < realized.Count; i++)
            {
                if (realized[i] >= index)
                    realized[i] += count;
            }

            if (bringIntoView >= index)
                bringIntoView += count;

            itemsChanged();
        }

        internal override void OnItemsRemoved(int index, int count)
        {
            sizes.Remove(index, count);

            for (int i = realized.Count - 1; i >= 0; i--)
            {
                if (realized[i] >= index + count)
                    realized[i] -= count;
                else if (realized[i] >= index)
                    realized.RemoveAt(i);
            }

            if (bringIntoView >= index + count)
                bringIntoView -= count;
            else if (bringIntoView >= index)
                bringIntoView = -1;

            itemsChanged();
        }

        internal override void OnItemMoved(int from, int to)
        {
            sizes.Move(from, to);

            for (int i = 0; i < realized.Count; i++)
                realized[i] = moved(realized[i], from, to);

            realized.Sort();

            if (bringIntoView >= 0)
                bringIntoView = moved(bringIntoView, from, to);

            itemsChanged();
        }

        internal override void OnItemsReset(IReadOnlyList<int> previousIndex)
        {
            sizes.Remap(previousIndex);

            int wanted = bringIntoView;
            bringIntoView = -1;
            realized.Clear();

            for (int i = 0; i < previousIndex.Count; i++)
            {
                if (Owner.ContainerAt(i) != null)
                    realized.Add(i);

                if (wanted >= 0 && previousIndex[i] == wanted)
                    bringIntoView = i;
            }

            // A list of different items starts over; a re-sort keeps the width it has seen.
            if (!previousIndex.Any(i => i >= 0))
                widestItem = 0;

            itemsChanged();
        }

        internal override void BringIndexIntoView(int index, Func<Drawable, Drawable> part)
        {
            if (Owner == null || index < 0 || index >= sizes.Count)
                return;

            // Created now, so the caller can use the container straight away; it is arranged, and scrolled to, this frame.
            realize(index);

            bringIntoViewViewer = scrollViewer();
            bringIntoView = bringIntoViewViewer == null ? -1 : index;

            // The row was placed by estimate, so the viewer keeps scrolling as the rows around it are measured. The index moves
            // with item changes, so the row is looked up each frame.
            if (bringIntoViewViewer != null)
            {
                bringIntoViewRequest = bringIntoViewViewer.BringIntoView(() =>
                {
                    var container = bringIntoView < 0 ? null : Owner?.ContainerAt(bringIntoView);
                    return (container == null ? null : part?.Invoke(container) ?? container, true);
                });
            }
        }

        internal override void Realize(int index)
        {
            if (Owner != null && index >= 0 && index < sizes.Count)
                realize(index);
        }

        protected override Vector2 MeasureOverride(Vector2 available)
        {
            if (Owner == null)
                return StackPanel.MeasureStack(Slots, available, Orientation, Spacing);

            sizes.Spacing = Spacing;
            return Make((float)sizes.Extent, widestItem, Orientation);
        }

        protected override void Update()
        {
            base.Update();

            if (Owner == null)
                return;

            updateRealized();

            // Bringing a row into view corrects the scroll position every frame until it settles.
            if (bringIntoView >= 0)
                InvalidateArrange();
        }

        protected override void ArrangeOverride(Vector2 size)
        {
            if (Owner == null)
            {
                StackPanel.ArrangeStack(Slots, size, Orientation, Spacing);
                return;
            }

            sizes.Spacing = Spacing;

            float cross = Cross(size, Orientation);
            var constraint = Make(float.PositiveInfinity, cross, Orientation);

            // Realized items replace the estimate with their real length.
            foreach (int index in realized)
            {
                var slot = slotAt(index);
                if (slot == null)
                    continue;

                if (Layout.IsCollapsed(slot.Content))
                {
                    sizes.Set(index, ItemSizeCache.Collapsed);
                    continue;
                }

                var desired = slot.Measure(constraint);
                float length = Axis(desired, Orientation);
                sizes.Set(index, length, Owner.TypicalLength(slot.Content, length));
                widestItem = Math.Max(widestItem, Cross(desired, Orientation));
            }

            keepAnchorInPlace();

            foreach (int index in realized)
            {
                var slot = slotAt(index);
                if (slot == null)
                    continue;

                float offset = (float)sizes.OffsetOf(index);

                if (Layout.IsCollapsed(slot.Content))
                    slot.Arrange(Make(offset, 0, Orientation), Vector2.Zero);
                else
                    slot.Arrange(Make(offset, 0, Orientation), Make(sizes.LengthOf(index), cross, Orientation));
            }

            // Measuring the rows in view replaces estimates, which changes the extent the panel asks for.
            var extent = Make((float)sizes.Extent, widestItem, Orientation);

            if (extent != arrangedExtent)
            {
                arrangedExtent = extent;
                InvalidateMeasure();
            }
        }

        private void updateRealized()
        {
            sizes.Spacing = Spacing;

            int first = 0, last = -1;
            anchorIndex = -1;

            if (visibleSpan(out double start, out double end) && sizes.Count > 0)
            {
                double extent = sizes.Extent;
                double margin = Math.Max((end - start) * cache_length, min_cache);
                double from = Math.Max(0, start - margin);
                double to = Math.Min(extent, end + margin);

                if (to >= from && end + margin >= 0 && start - margin <= extent)
                {
                    first = sizes.IndexAt(from);
                    last = sizes.IndexAt(to);
                }

                // Only when content is hidden above the viewport can a size change there move what is in view.
                if (start > 0 && start < extent)
                {
                    anchorIndex = sizes.IndexAt(start);
                    anchorOffset = sizes.OffsetOf(anchorIndex);
                }
            }

            var focused = focusedContainer();

            if (bringIntoView >= 0 && !bringIntoViewViewer.IsBringingIntoView(bringIntoViewRequest))
                bringIntoView = -1;

            for (int i = realized.Count - 1; i >= 0; i--)
            {
                int index = realized[i];

                if (index >= first && index <= last)
                    continue;

                // A row being typed in or edited stays, and so does a row that is being brought into view.
                var container = Owner.ContainerAt(index);
                if (index == bringIntoView || container == null || container == focused || Owner.KeepsContainerRealized(container))
                    continue;

                realized.RemoveAt(i);
                Owner.ReleaseContainer(index);
            }

            for (int index = first; index <= last; index++)
                realize(index);
        }

        private void realize(int index)
        {
            int at = realized.BinarySearch(index);
            if (at >= 0)
                return;

            realized.Insert(~at, index);
            Owner.RealizeContainer(index);
        }

        /// <summary>The part of the panel that can be seen (whatever clips it, usually the list's scroll viewer), along the panel's axis in child space.</summary>
        private bool visibleSpan(out double start, out double end)
        {
            start = end = 0;

            var bounds = ChildMaskingBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return false;

            var local = ToLocalSpace(Quad.FromRectangle(bounds)).AABBFloat;
            float padding = Orientation == Orientation.Horizontal ? Padding.Left : Padding.Top;

            start = (Orientation == Orientation.Horizontal ? local.Left : local.Top) - padding;
            end = (Orientation == Orientation.Horizontal ? local.Right : local.Bottom) - padding;
            return end > start;
        }

        /// <summary>The realized container that holds keyboard focus, if any.</summary>
        private Drawable focusedContainer()
        {
            for (var d = GetContainingInputManager()?.FocusedDrawable; d != null; d = d.Parent)
            {
                if (d.Parent == this)
                    return (d as Slot)?.Content;
            }

            return null;
        }

        private Slot slotAt(int index) => SlotOf(Owner.ContainerAt(index));

        private ScrollViewer scrollViewer()
        {
            for (var d = Parent; d != null; d = d.Parent)
            {
                if (d is ScrollViewer viewer)
                    return viewer;
            }

            return null;
        }

        /// <summary>
        /// When lengths above the viewport change (a row measured taller than its estimate, or a new average), scrolls by the same
        /// amount so the rows in view don't jump. Only the scroll bar moves.
        /// </summary>
        private void keepAnchorInPlace()
        {
            if (anchorIndex < 0 || anchorIndex >= sizes.Count)
                return;

            double delta = sizes.OffsetOf(anchorIndex) - anchorOffset;
            anchorIndex = -1;

            if (Math.Abs(delta) > 0.01)
                scrollViewer()?.OffsetScroll(Make((float)delta, 0, Orientation));
        }

        private void itemsChanged()
        {
            // Item changes move offsets for reasons other than measuring, so they aren't compensated.
            anchorIndex = -1;
            InvalidateMeasure();
        }

        private static int moved(int index, int from, int to)
        {
            if (index == from)
                return to;
            if (from < to && index > from && index <= to)
                return index - 1;
            if (to < from && index >= to && index < from)
                return index + 1;

            return index;
        }
    }

    /// <summary>
    /// The length of every item along a virtualizing panel's axis: measured lengths for items that have been shown, and the
    /// average of those for the rest (each measured item can say what it suggests for the others: an expanded tree node counts
    /// as its header). Offsets come from Fenwick trees (rebuilt after inserts and removals), so finding the items at a scroll
    /// position costs O(log² n) however many items there are.
    /// </summary>
    internal sealed class ItemSizeCache
    {
        /// <summary>The length of an item that takes no space (Visibility.Collapsed).</summary>
        public const float Collapsed = -1;

        /// <summary>Used for every item until one has been measured.</summary>
        public const float DefaultEstimate = 24;

        // Per item: NaN until measured, Collapsed, or the measured length.
        private readonly List<float> lengths = new List<float>();

        // Per measured item: the length it suggests for the items not measured yet; NaN otherwise.
        private readonly List<float> samples = new List<float>();

        // 1-based Fenwick trees over the measured lengths, the number measured and the number collapsed.
        private double[] lengthTree = Array.Empty<double>();
        private int[] measuredTree = Array.Empty<int>();
        private int[] collapsedTree = Array.Empty<int>();
        private bool treesValid;

        private double measuredTotal;
        private double sampleTotal;
        private int measuredCount;
        private int collapsedCount;

        /// <summary>The gap between two items that aren't collapsed.</summary>
        public float Spacing { get; set; }

        public int Count => lengths.Count;

        /// <summary>The length assumed for an item that hasn't been measured: the average of what the measured ones suggest.</summary>
        public float Estimate => measuredCount > 0 ? (float)(sampleTotal / measuredCount) : DefaultEstimate;

        /// <summary>The length of <paramref name="index"/>: measured, estimated, or 0 when collapsed.</summary>
        public float LengthOf(int index)
        {
            float length = lengths[index];
            return float.IsNaN(length) ? Estimate : Math.Max(0, length);
        }

        /// <summary>Whether <paramref name="index"/> has been measured (or found collapsed).</summary>
        public bool IsKnown(int index) => !float.IsNaN(lengths[index]);

        /// <summary>The total length: every item, and the spacing between those that aren't collapsed.</summary>
        public double Extent
        {
            get
            {
                int shown = lengths.Count - collapsedCount;
                return measuredTotal + (shown - measuredCount) * (double)Estimate + Math.Max(0, shown - 1) * (double)Spacing;
            }
        }

        /// <summary>
        /// Records a measured length, or <see cref="Collapsed"/>. <paramref name="sample"/> is the length it suggests for items
        /// not measured yet: the length itself unless given.
        /// </summary>
        public void Set(int index, float length, float sample = float.NaN)
        {
            if (length < 0)
                length = Collapsed;

            if (length < 0)
                sample = float.NaN;
            else if (float.IsNaN(sample))
                sample = length;

            float old = lengths[index];
            float oldSample = samples[index];
            if (old.Equals(length) && oldSample.Equals(sample))
                return;

            lengths[index] = length;
            samples[index] = sample;
            count(old, oldSample, -1);
            count(length, sample, 1);

            if (treesValid && !old.Equals(length))
            {
                double deltaLength = (length >= 0 ? length : 0) - (old >= 0 ? old : 0);
                int deltaMeasured = (length >= 0 ? 1 : 0) - (old >= 0 ? 1 : 0);
                int deltaCollapsed = (length < 0 ? 1 : 0) - (old < 0 ? 1 : 0);

                for (int i = index + 1; i < lengthTree.Length && i <= lengths.Count; i += i & -i)
                {
                    lengthTree[i] += deltaLength;
                    measuredTree[i] += deltaMeasured;
                    collapsedTree[i] += deltaCollapsed;
                }
            }
        }

        public void Insert(int index, int count)
        {
            lengths.InsertRange(index, Enumerable.Repeat(float.NaN, count));
            samples.InsertRange(index, Enumerable.Repeat(float.NaN, count));
            treesValid = false;
        }

        public void Remove(int index, int count)
        {
            for (int i = index; i < index + count; i++)
                this.count(lengths[i], samples[i], -1);

            lengths.RemoveRange(index, count);
            samples.RemoveRange(index, count);
            treesValid = false;
        }

        public void Move(int from, int to)
        {
            float length = lengths[from];
            float sample = samples[from];
            lengths.RemoveAt(from);
            samples.RemoveAt(from);
            lengths.Insert(to, length);
            samples.Insert(to, sample);
            treesValid = false;
        }

        /// <summary>Reorders after a reset: each new item takes the length of its old index, or starts unknown.</summary>
        public void Remap(IReadOnlyList<int> previousIndex)
        {
            var old = lengths.ToArray();
            var oldSamples = samples.ToArray();
            lengths.Clear();
            samples.Clear();

            foreach (int i in previousIndex)
            {
                lengths.Add(i >= 0 ? old[i] : float.NaN);
                samples.Add(i >= 0 ? oldSamples[i] : float.NaN);
            }

            treesValid = false;
            ensureTrees();
        }

        /// <summary>Where <paramref name="index"/> starts: the lengths of every item before it and the spacing after each.</summary>
        public double OffsetOf(int index)
        {
            ensureTrees();

            double length = 0;
            int measured = 0, collapsed = 0;

            for (int i = Math.Min(index, lengths.Count); i > 0; i -= i & -i)
            {
                length += lengthTree[i];
                measured += measuredTree[i];
                collapsed += collapsedTree[i];
            }

            int shown = index - collapsed;
            return length + (shown - measured) * (double)Estimate + shown * (double)Spacing;
        }

        /// <summary>The item at <paramref name="offset"/>: the last one that starts at or before it (the first item for offsets before the start).</summary>
        public int IndexAt(double offset)
        {
            if (lengths.Count == 0)
                return -1;

            int low = 0, high = lengths.Count - 1;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;

                if (OffsetOf(mid) <= offset)
                    low = mid;
                else
                    high = mid - 1;
            }

            return low;
        }

        private void count(float length, float sample, int sign)
        {
            if (length >= 0)
            {
                measuredTotal += sign * length;
                sampleTotal += sign * sample;
                measuredCount += sign;
            }
            else if (length < 0)
                collapsedCount += sign;
        }

        private void ensureTrees()
        {
            if (treesValid)
                return;

            int n = lengths.Count;

            if (lengthTree.Length < n + 1)
            {
                int capacity = Math.Max(n + 1, lengthTree.Length * 2);
                lengthTree = new double[capacity];
                measuredTree = new int[capacity];
                collapsedTree = new int[capacity];
            }
            else
            {
                Array.Clear(lengthTree, 0, n + 1);
                Array.Clear(measuredTree, 0, n + 1);
                Array.Clear(collapsedTree, 0, n + 1);
            }

            // Recounted from scratch, so rounding from many small updates doesn't build up.
            measuredTotal = 0;
            sampleTotal = 0;
            measuredCount = 0;
            collapsedCount = 0;

            for (int i = 1; i <= n; i++)
            {
                float length = lengths[i - 1];

                if (length >= 0)
                {
                    lengthTree[i] += length;
                    measuredTree[i]++;
                }
                else if (length < 0)
                    collapsedTree[i]++;

                count(length, samples[i - 1], 1);

                int parent = i + (i & -i);

                if (parent <= n)
                {
                    lengthTree[parent] += lengthTree[i];
                    measuredTree[parent] += measuredTree[i];
                    collapsedTree[parent] += collapsedTree[i];
                }
            }

            treesValid = true;
        }
    }
}
