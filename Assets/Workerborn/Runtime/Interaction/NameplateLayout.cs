using System;
using System.Collections.Generic;

namespace Workerborn
{
    public sealed class NameplateSlot
    {
        public long Order;
        public float X, Y, Width, Height, PlacedX, PlacedY;
        public float? ObjectY;
        public float Scale = 1f;
        public bool Visible = true;
        internal float AnchorY;
        internal float HalfWidth => Width * Scale * .5f;
        internal float HalfHeight => Height * Scale * .5f;
    }

    // Canvas coordinates, positive Y upwards. Only the safe area can change the projected X.
    public static class NameplateLayout
    {
        private const float Epsilon = .001f;

        public static float Present(List<NameplateSlot> labels, bool alt, float bottom, float top, float gap,
            float overflowScroll = 0f, long? scrollOrder = null, float minimumScale = 1f,
            float left = float.NegativeInfinity, float right = float.PositiveInfinity)
        {
            if (alt)
                return Arrange(labels, bottom, top, gap, overflowScroll, scrollOrder, minimumScale, left, right);
            foreach (var label in labels)
            {
                label.PlacedX = label.X;
                label.PlacedY = label.Y;
                label.Scale = 1f;
                label.Visible = true;
            }
            return 0f;
        }

        public static float Arrange(List<NameplateSlot> labels, float bottom, float top, float gap,
            float overflowScroll = 0f, long? scrollOrder = null, float minimumScale = 1f,
            float left = float.NegativeInfinity, float right = float.PositiveInfinity)
        {
            foreach (var label in labels)
            {
                label.Scale = 1f;
                label.Visible = true;
                SetAnchor(label, bottom, top, left, right);
            }

            if (minimumScale < 1f)
            {
                CompactOverlaps(labels, gap, minimumScale);
                foreach (var label in labels)
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

        private static void CompactOverlaps(List<NameplateSlot> labels, float gap, float minimumScale)
        {
            for (int i = 0; i < labels.Count; i++)
            {
                var a = labels[i];
                for (int j = 0; j < i; j++)
                {
                    var b = labels[j];
                    float dx = Math.Abs(a.PlacedX - b.PlacedX), dy = Math.Abs(a.AnchorY - b.AnchorY);
                    float width = (a.Width + b.Width) * .5f, height = (a.Height + b.Height) * .5f;
                    if (width <= 0f || height <= 0f || dx >= width + gap || dy >= height + gap)
                        continue;
                    // Shrinking coincident boxes gives them no separation.
                    if (Math.Abs(a.X - b.X) < .5f && Math.Abs((a.ObjectY ?? a.Y) - (b.ObjectY ?? b.Y)) < .5f)
                        continue;
                    float scale = Math.Max(minimumScale, Math.Max((dx - gap) / width, (dy - gap) / height));
                    a.Scale = Math.Min(a.Scale, scale);
                    b.Scale = Math.Min(b.Scale, scale);
                }
            }
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
            float displacement = 0f, highest = float.MinValue, lowest = float.MaxValue;
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
                highest = Math.Max(highest, current.PlacedY + current.HalfHeight);
                lowest = Math.Min(lowest, current.PlacedY - current.HalfHeight);
            }

            float minimum = bottom - lowest, maximum = top - highest;
            float preferred = displacement / group.Count;
            float clamped = Math.Max(minimum, Math.Min(maximum, preferred));
            if (minimum <= maximum && TryFit(group, occupied, clamped, minimum, maximum, gap, out float shift))
            {
                Commit(group, occupied, shift, bottom, top);
                return 0f;
            }

            // If the stack cannot fit, scroll it inside the nearest largest free band.
            (bottom, top) = FreeBand(group, occupied, bottom, top, gap, (highest + lowest) * .5f + preferred);
            float overflow = Math.Max(0f, highest - lowest - (top - bottom));
            shift = top - highest + Math.Min(Math.Max(0f, scroll), overflow);
            Commit(group, occupied, shift, bottom, top);
            return overflow;
        }

        private static bool TryFit(List<NameplateSlot> group, List<NameplateSlot> occupied,
            float preferred, float minimum, float maximum, float gap, out float shift)
        {
            float down = ClearObstacles(group, occupied, preferred, gap, -1);
            float up = ClearObstacles(group, occupied, preferred, gap, 1);
            bool canMoveDown = down >= minimum - Epsilon, canMoveUp = up <= maximum + Epsilon;
            shift = canMoveDown && (!canMoveUp || preferred - down <= up - preferred) ? down : up;
            return canMoveDown || canMoveUp;
        }

        private static float ClearObstacles(List<NameplateSlot> group, List<NameplateSlot> occupied, float shift, float gap, int direction)
        {
            while (true)
            {
                float next = shift;
                foreach (var label in group)
                    foreach (var obstacle in occupied)
                    {
                        if (!HorizontalOverlap(label, obstacle, gap))
                            continue;
                        float centered = obstacle.PlacedY - label.PlacedY;
                        float separation = label.HalfHeight + obstacle.HalfHeight + gap;
                        if (Math.Abs(shift - centered) < separation - Epsilon)
                            next = direction > 0 ? Math.Max(next, centered + separation) : Math.Min(next, centered - separation);
                    }
                if (next == shift)
                    return shift;
                shift = next;
            }
        }

        private static (float Bottom, float Top) FreeBand(List<NameplateSlot> group, List<NameplateSlot> occupied,
            float bottom, float top, float gap, float center)
        {
            var blocked = new List<(float Bottom, float Top)>();
            foreach (var obstacle in occupied)
            {
                if (group.Exists(label => HorizontalOverlap(label, obstacle, gap)))
                    blocked.Add((Math.Max(bottom, obstacle.PlacedY - obstacle.HalfHeight - gap),
                        Math.Min(top, obstacle.PlacedY + obstacle.HalfHeight + gap)));
            }
            blocked.Add((top, top));
            blocked.Sort((a, b) => a.Bottom.CompareTo(b.Bottom));
            float bestBottom = bottom, bestTop = bottom, bestDistance = float.MaxValue, start = bottom;
            foreach (var interval in blocked)
            {
                float height = interval.Bottom - start;
                float distance = Math.Abs((start + interval.Bottom) * .5f - center);
                if (height > bestTop - bestBottom || height == bestTop - bestBottom && distance < bestDistance)
                {
                    bestBottom = start;
                    bestTop = interval.Bottom;
                    bestDistance = distance;
                }
                start = Math.Max(start, interval.Top);
            }
            return (bestBottom, bestTop);
        }

        private static void Commit(List<NameplateSlot> group, List<NameplateSlot> occupied, float shift, float bottom, float top)
        {
            foreach (var label in group)
            {
                label.PlacedY += shift;
                label.Visible = label.PlacedY - label.HalfHeight >= bottom - Epsilon &&
                    label.PlacedY + label.HalfHeight <= top + Epsilon;
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
