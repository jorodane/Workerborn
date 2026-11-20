using System;
using System.Collections.Generic;

namespace Workerborn
{
    public sealed class NameplateSlot
    {
        public long Order;
        public float X, Y, Width, Height, PlacedY;
        public float? ObjectY;
    }

    // Canvas coordinates, positive Y upwards. Horizontal positions never change.
    public static class NameplateLayout
    {
        public static float Present(List<NameplateSlot> labels, bool alt, float bottom, float top, float gap,
            float overflowScroll = 0f, long? scrollOrder = null)
        {
            if (alt)
                return Arrange(labels, bottom, top, gap, overflowScroll, scrollOrder);
            foreach (var label in labels)
                label.PlacedY = label.Y;
            return 0;
        }

        public static float Arrange(List<NameplateSlot> labels, float bottom, float top, float gap,
            float overflowScroll = 0f, long? scrollOrder = null)
        {
            labels.Sort(Compare);
            var groups = new List<List<NameplateSlot>>();
            foreach (var label in labels)
            {
                float half = label.Height * .5f;
                label.PlacedY = Math.Max(bottom + half, Math.Min(top - half, label.Y));
                var joined = new List<NameplateSlot> { label };
                for (int i = groups.Count - 1; i >= 0; i--)
                {
                    if (!groups[i].Exists(other => Collides(label, other, gap)))
                        continue;
                    joined.AddRange(groups[i]);
                    groups.RemoveAt(i);
                }
                joined.Sort(Compare);
                groups.Add(joined);
            }
            foreach (var group in groups)
                Place(group, bottom, top, gap, 0f);

            // Moving a stack can cause a new collision. Merge it only with boxes it actually touches.
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                for (int j = i + 1; j < groups.Count; j++)
                {
                    if (!Collides(group, groups[j], gap))
                        continue;
                    group.AddRange(groups[j]);
                    groups.RemoveAt(j);
                    group.Sort(Compare);
                    Place(group, bottom, top, gap, 0f);
                    i = -1;
                    break;
                }
            }

            float maxOverflow = 0f;
            foreach (var group in groups)
            {
                if (!scrollOrder.HasValue || group.Exists(label => label.Order == scrollOrder.Value))
                    maxOverflow = Math.Max(maxOverflow, Place(group, bottom, top, gap, overflowScroll));
            }

            return maxOverflow;
        }

        private static float Place(List<NameplateSlot> group, float bottom, float top, float gap, float scroll)
        {
            float displacement = 0f, highest = float.MinValue, lowest = float.MaxValue;
            for (int i = 0; i < group.Count; i++)
            {
                var current = group[i];
                current.PlacedY = current.Y;
                for (int j = 0; j < i; j++)
                {
                    var earlier = group[j];
                    if (HorizontalOverlap(current, earlier, gap))
                        current.PlacedY = Math.Min(current.PlacedY, earlier.PlacedY - (earlier.Height + current.Height) * .5f - gap);
                }
                displacement += current.Y - current.PlacedY;
                highest = Math.Max(highest, current.PlacedY + current.Height * .5f);
                lowest = Math.Min(lowest, current.PlacedY - current.Height * .5f);
            }

            float overflow = Math.Max(0f, highest - lowest - (top - bottom));
            float shift = overflow > 0f ? top - highest + Math.Min(Math.Max(scroll, 0f), overflow)
                : Math.Max(bottom - lowest, Math.Min(top - highest, displacement / group.Count));
            foreach (var label in group)
                label.PlacedY += shift;
            return overflow;
        }

        private static int Compare(NameplateSlot a, NameplateSlot b)
        {
            int height = Math.Round((b.ObjectY ?? b.Y) * 2).CompareTo(Math.Round((a.ObjectY ?? a.Y) * 2));
            return height != 0 ? height : a.Order.CompareTo(b.Order);
        }

        private static bool Collides(List<NameplateSlot> a, List<NameplateSlot> b, float gap)
        {
            foreach (var first in a)
                foreach (var second in b)
                    if (Collides(first, second, gap))
                        return true;
            return false;
        }

        private static bool Collides(NameplateSlot a, NameplateSlot b, float gap) =>
            HorizontalOverlap(a, b, gap) && Math.Abs(a.PlacedY - b.PlacedY) < (a.Height + b.Height) * .5f + gap;

        private static bool HorizontalOverlap(NameplateSlot a, NameplateSlot b, float gap) => Math.Abs(a.X - b.X) < (a.Width + b.Width) * .5f + gap;
    }
}
