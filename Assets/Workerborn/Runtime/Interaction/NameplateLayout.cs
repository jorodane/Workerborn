using System;
using System.Collections.Generic;

namespace Workerborn
{
    public sealed class NameplateSlot
    {
        public long Order;
        public float X, Y, Width, Height, PlacedX, PlacedY;
        public float? ObjectY;
        public bool Visible = true;
        internal float AnchorY;
        internal float HalfWidth => Width * .5f;
        internal float HalfHeight => Height * .5f;
    }

    // Canvas coordinates, positive Y upwards. Only the safe area can change the projected X.
    public static class NameplateLayout
    {
        private const float Epsilon = .001f;
        private const float MaximumDisplacementInRows = 2f;

        public static float Present(List<NameplateSlot> labels, bool alt, float bottom, float top, float gap,
            float overflowScroll = 0f, long? scrollOrder = null,
            float left = float.NegativeInfinity, float right = float.PositiveInfinity)
        {
            if (alt)
                return Arrange(labels, bottom, top, gap, overflowScroll, scrollOrder, left, right);
            foreach (var label in labels)
            {
                label.PlacedX = label.X;
                label.PlacedY = label.Y;
                label.Visible = true;
            }
            return 0f;
        }

        public static float Arrange(List<NameplateSlot> labels, float bottom, float top, float gap,
            float overflowScroll = 0f, long? scrollOrder = null,
            float left = float.NegativeInfinity, float right = float.PositiveInfinity)
        {
            foreach (var label in labels)
            {
                label.Visible = true;
                SetAnchor(label, bottom, top, left, right);
            }

            labels.Sort(Compare);
            if (!HasOverlaps(labels, gap))
                return 0f;

            var groups = new List<List<NameplateSlot>>();
            foreach (var label in labels)
            {
                List<NameplateSlot> joined = null;
                for (int i = groups.Count - 1; i >= 0; i--)
                {
                    if (!OverlapsGroup(label, groups[i], gap))
                        continue;
                    if (joined == null)
                        joined = groups[i];
                    else
                        joined.AddRange(groups[i]);
                    groups.RemoveAt(i);
                }
                joined ??= new List<NameplateSlot>();
                joined.Add(label);
                groups.Add(joined);
            }

            // Reserve free anchors first. A moved box must make room for them.
            var occupied = new List<NameplateSlot>(labels.Count);
            foreach (var group in groups)
                if (group.Count == 1)
                    occupied.Add(group[0]);

            float maxOverflow = 0f;
            foreach (var group in groups)
            {
                if (group.Count == 1)
                    continue;
                group.Sort(Compare);
                bool scrollThisGroup = !scrollOrder.HasValue || group.Exists(label => label.Order == scrollOrder.Value);
                float overflow = Place(group, occupied, bottom, top, gap, scrollThisGroup ? overflowScroll : 0f);
                if (scrollThisGroup)
                    maxOverflow = Math.Max(maxOverflow, overflow);
            }
            return maxOverflow;
        }

        private static void SetAnchor(NameplateSlot label, float bottom, float top, float left, float right)
        {
            label.PlacedX = ClampCenter(label.X, left, right, label.HalfWidth);
            label.AnchorY = label.PlacedY = ClampCenter(label.Y, bottom, top, label.HalfHeight);
        }

        private static float ClampCenter(float value, float low, float high, float halfSize) =>
            high - low < halfSize * 2 ? (low + high) * .5f : Math.Max(low + halfSize, Math.Min(high - halfSize, value));

        private static float Place(List<NameplateSlot> group, List<NameplateSlot> occupied,
            float bottom, float top, float gap, float scroll)
        {
            float displacement = 0f;
            float minimum = float.NegativeInfinity, maximum = float.PositiveInfinity;
            for (int i = 0; i < group.Count; i++)
            {
                var current = group[i];
                for (int j = 0; j < i; j++)
                {
                    var earlier = group[j];
                    float separation = earlier.HalfHeight + current.HalfHeight + gap;
                    if (HorizontalOverlap(current, earlier, gap) && Math.Abs(current.AnchorY - earlier.AnchorY) < separation - Epsilon)
                        current.PlacedY = Math.Min(current.PlacedY, earlier.PlacedY - separation);
                }
                // Only a box in the chosen position can force an additional move.
                for (int j = 0; j < i;)
                {
                    var earlier = group[j];
                    if (Overlap(current, earlier, gap))
                    {
                        current.PlacedY = earlier.PlacedY - earlier.HalfHeight - current.HalfHeight - gap;
                        j = 0;
                    }
                    else
                        j++;
                }
                displacement += current.AnchorY - current.PlacedY;
                var bounds = AllowedShift(current, bottom, top);
                minimum = Math.Max(minimum, bounds.Minimum);
                maximum = Math.Min(maximum, bounds.Maximum);
            }

            float preferred = displacement / group.Count;
            float clamped = Math.Max(minimum, Math.Min(maximum, preferred));
            if (minimum <= maximum && TryFit(group, occupied, clamped, minimum, maximum, gap, out float shift))
            {
                Commit(group, occupied, shift, bottom, top, gap);
                return 0f;
            }

            // Scroll through crowded names, keeping each one inside its own visible range.
            var (start, end) = ScrollRange(group, occupied, bottom, top, gap);
            if (float.IsPositiveInfinity(start))
            {
                foreach (var label in group)
                    label.Visible = false;
                return 0f;
            }
            float overflow = Math.Max(0f, end - start);
            shift = overflow > 0f ? start + Math.Min(Math.Max(0f, scroll), overflow) : Math.Max(end, Math.Min(start, preferred));
            Commit(group, occupied, shift, bottom, top, gap);
            return overflow;
        }

        private static (float Minimum, float Maximum) AllowedShift(NameplateSlot label, float bottom, float top)
        {
            float distance = label.Height * MaximumDisplacementInRows;
            return (Math.Max(bottom + label.HalfHeight, label.AnchorY - distance) - label.PlacedY,
                Math.Min(top - label.HalfHeight, label.AnchorY + distance) - label.PlacedY);
        }

        private static bool TryFit(List<NameplateSlot> group, List<NameplateSlot> occupied,
            float preferred, float minimum, float maximum, float gap, out float shift)
        {
            float down = ClearObstacles(group, 0, group.Count, occupied, preferred, gap, -1);
            float up = ClearObstacles(group, 0, group.Count, occupied, preferred, gap, 1);
            bool canMoveDown = down >= minimum - Epsilon, canMoveUp = up <= maximum + Epsilon;
            shift = canMoveDown && (!canMoveUp || preferred - down <= up - preferred) ? down : up;
            return canMoveDown || canMoveUp;
        }

        private static float ClearObstacles(List<NameplateSlot> group, int first, int end, List<NameplateSlot> occupied,
            float shift, float gap, int direction)
        {
            while (true)
            {
                float next = shift;
                for (int i = first; i < end; i++)
                {
                    var label = group[i];
                    foreach (var obstacle in occupied)
                    {
                        if (!HorizontalOverlap(label, obstacle, gap))
                            continue;
                        float centered = obstacle.PlacedY - label.PlacedY;
                        float separation = label.HalfHeight + obstacle.HalfHeight + gap;
                        if (Math.Abs(shift - centered) < separation - Epsilon)
                            next = direction > 0 ? Math.Max(next, centered + separation) : Math.Min(next, centered - separation);
                    }
                }
                if (next == shift)
                    return shift;
                shift = next;
            }
        }

        private static (float Start, float End) ScrollRange(List<NameplateSlot> group, List<NameplateSlot> occupied,
            float bottom, float top, float gap)
        {
            float start = float.PositiveInfinity, end = float.NegativeInfinity;
            for (int i = 0; i < group.Count; i++)
            {
                var bounds = AllowedShift(group[i], bottom, top);
                float firstVisible = ClearObstacles(group, i, i + 1, occupied, bounds.Minimum, gap, 1);
                float lastVisible = ClearObstacles(group, i, i + 1, occupied, bounds.Maximum, gap, -1);
                if (firstVisible > lastVisible + Epsilon)
                    continue;
                start = Math.Min(start, lastVisible);
                end = Math.Max(end, firstVisible);
            }
            return (start, end);
        }

        private static void Commit(List<NameplateSlot> group, List<NameplateSlot> occupied, float shift, float bottom, float top, float gap)
        {
            foreach (var label in group)
            {
                var bounds = AllowedShift(label, bottom, top);
                label.PlacedY += shift;
                label.Visible = shift >= bounds.Minimum - Epsilon && shift <= bounds.Maximum + Epsilon &&
                    !OverlapsGroup(label, occupied, gap);
                if (label.Visible)
                    occupied.Add(label);
            }
        }

        private static int Compare(NameplateSlot a, NameplateSlot b)
        {
            int height = Math.Round((b.ObjectY ?? b.Y) * 2).CompareTo(Math.Round((a.ObjectY ?? a.Y) * 2));
            return height != 0 ? height : a.Order.CompareTo(b.Order);
        }

        private static bool HasOverlaps(List<NameplateSlot> labels, float gap)
        {
            for (int i = 1; i < labels.Count; i++)
                for (int j = 0; j < i; j++)
                    if (Overlap(labels[i], labels[j], gap))
                        return true;
            return false;
        }

        private static bool OverlapsGroup(NameplateSlot label, List<NameplateSlot> group, float gap)
        {
            foreach (var other in group)
                if (Overlap(label, other, gap))
                    return true;
            return false;
        }

        private static bool Overlap(NameplateSlot a, NameplateSlot b, float gap) =>
            HorizontalOverlap(a, b, gap) && Math.Abs(a.PlacedY - b.PlacedY) < a.HalfHeight + b.HalfHeight + gap - Epsilon;

        private static bool HorizontalOverlap(NameplateSlot a, NameplateSlot b, float gap) =>
            Math.Abs(a.PlacedX - b.PlacedX) < a.HalfWidth + b.HalfWidth + gap - Epsilon;
    }
}
