using System;
using System.Collections.Generic;

namespace Workerborn
{
    public sealed class NameplateSlot
    {
        private static long nextOrder;

        public long Order;
        public float X, Y, Width, Height, PlacedX, PlacedY;
        public float? ObjectY;
        internal float AnchorX, AnchorY;
        internal float HalfWidth => Width * .5f;
        internal float HalfHeight => Height * .5f;
        internal static long NextOrder() => ++nextOrder;
    }

    // Canvas coordinates, positive Y upwards.
    public static class NameplateLayout
    {
        private const float Epsilon = .001f;
        private const float MaximumDisplacementInRows = 2f;
        private const float MaximumDisplacementInColumns = 1.5f;

        public static void Present(List<NameplateSlot> labels, bool alt, float bottom, float top, float gap,
            float left = float.NegativeInfinity, float right = float.PositiveInfinity)
        {
            if (alt)
            {
                Arrange(labels, bottom, top, gap, left, right);
                return;
            }
            foreach (var label in labels)
            {
                label.PlacedX = label.X;
                label.PlacedY = label.Y;
            }
        }

        public static void Arrange(List<NameplateSlot> labels, float bottom, float top, float gap,
            float left = float.NegativeInfinity, float right = float.PositiveInfinity)
        {
            foreach (var label in labels)
            {
                label.AnchorX = label.PlacedX = ClampCenter(label.X, left, right, label.HalfWidth);
                label.AnchorY = label.PlacedY = ClampCenter(label.Y, bottom, top, label.HalfHeight);
            }

            labels.Sort(Compare);
            if (!HasOverlaps(labels, gap))
                return;

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

            foreach (var group in groups)
            {
                if (group.Count == 1)
                    continue;
                group.Sort(Compare);
                if (TryStack(group, occupied, bottom, top, gap))
                    occupied.AddRange(group);
                else
                    PlaceNearAnchors(group, occupied, bottom, top, left, right, gap);
            }
        }

        private static float ClampCenter(float value, float low, float high, float halfSize) =>
            high - low < halfSize * 2 ? (low + high) * .5f : Math.Max(low + halfSize, Math.Min(high - halfSize, value));

        private static bool TryStack(List<NameplateSlot> group, List<NameplateSlot> occupied, float bottom, float top, float gap)
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
                var (low, high) = VerticalRange(current, bottom, top, gap);
                minimum = Math.Max(minimum, low - current.PlacedY);
                maximum = Math.Min(maximum, high - current.PlacedY);
                if (minimum > maximum)
                    return false;
            }

            float preferred = displacement / group.Count;
            float clamped = Math.Max(minimum, Math.Min(maximum, preferred));
            float down = ClearObstacles(group, occupied, clamped, gap, -1);
            float up = ClearObstacles(group, occupied, clamped, gap, 1);
            bool canMoveDown = down >= minimum - Epsilon, canMoveUp = up <= maximum + Epsilon;
            if (!canMoveDown && !canMoveUp)
                return false;
            float shift = canMoveDown && (!canMoveUp || clamped - down <= up - clamped) ? down : up;
            foreach (var label in group)
                label.PlacedY += shift;
            return true;
        }

        private static (float Low, float High) VerticalRange(NameplateSlot label, float bottom, float top, float gap)
        {
            float distance = (label.Height + gap) * MaximumDisplacementInRows;
            return (ClampCenter(label.AnchorY - distance, bottom, top, label.HalfHeight),
                ClampCenter(label.AnchorY + distance, bottom, top, label.HalfHeight));
        }

        private static float ClearObstacles(List<NameplateSlot> group, List<NameplateSlot> occupied, float shift, float gap, int direction)
        {
            while (true)
            {
                float next = shift;
                foreach (var label in group)
                {
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

        private static void PlaceNearAnchors(List<NameplateSlot> group, List<NameplateSlot> occupied,
            float bottom, float top, float left, float right, float gap)
        {
            var columns = new List<float>();
            var rows = new List<float>();
            var neighbors = new List<(NameplateSlot Slot, float XSeparation, float YSeparation, bool Above)>();
            foreach (var label in group)
            {
                columns.Clear();
                columns.Add(label.AnchorX);
                rows.Clear();
                rows.Add(label.AnchorY);
                neighbors.Clear();
                float xRange = label.Width * MaximumDisplacementInColumns;
                float minX = ClampCenter(label.AnchorX - xRange, left, right, label.HalfWidth);
                float maxX = ClampCenter(label.AnchorX + xRange, left, right, label.HalfWidth);
                var (minY, maxY) = VerticalRange(label, bottom, top, gap);
                foreach (var other in occupied)
                {
                    float width = label.HalfWidth + other.HalfWidth + gap;
                    float height = label.HalfHeight + other.HalfHeight + gap;
                    if (other.PlacedX + width < minX || other.PlacedX - width > maxX ||
                        other.PlacedY + height < minY || other.PlacedY - height > maxY)
                        continue;

                    bool orderedAbove = Math.Abs(label.AnchorX - other.AnchorX) < width - Epsilon &&
                        Math.Abs(label.AnchorY - other.AnchorY) < height - Epsilon;
                    neighbors.Add((other, width, height, orderedAbove));
                    AddCandidate(columns, other.PlacedX - width, minX, maxX);
                    AddCandidate(columns, other.PlacedX + width, minX, maxX);
                    AddCandidate(rows, other.PlacedY - height, minY, maxY);
                    AddCandidate(rows, other.PlacedY + height, minY, maxY);
                }

                // Try the anchor and edges of nearby boxes. In a packed area keep all
                // labels placed, favoring the positions with the least overlap.
                var best = (Overlap: float.PositiveInfinity, Reversed: int.MaxValue, Distance: float.PositiveInfinity);
                foreach (float x in columns)
                {
                    float dx = (x - label.AnchorX) * .85f;
                    foreach (float y in rows)
                    {
                        float dy = y - label.AnchorY;
                        var score = (Overlap: 0f, Reversed: 0, Distance: dx * dx + dy * dy);
                        foreach (var (other, xSeparation, ySeparation, above) in neighbors)
                        {
                            float across = xSeparation - Math.Abs(x - other.PlacedX);
                            if (across <= Epsilon)
                                continue;
                            float vertical = y - other.PlacedY;
                            float overlap = ySeparation - Math.Abs(vertical);
                            if (overlap > Epsilon)
                            {
                                score.Overlap += across * overlap;
                                if (score.Overlap > best.Overlap)
                                    break;
                            }
                            if (above && vertical >= -Epsilon)
                                score.Reversed++;
                        }
                        if (score.CompareTo(best) >= 0)
                            continue;
                        best = score;
                        label.PlacedX = x;
                        label.PlacedY = y;
                    }
                }
                occupied.Add(label);
            }
        }

        private static void AddCandidate(List<float> positions, float value, float low, float high)
        {
            float clamped = Math.Max(low, Math.Min(high, value));
            if (!positions.Contains(clamped))
                positions.Add(clamped);
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
