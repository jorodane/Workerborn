using System;
using System.Collections.Generic;

namespace Workerborn
{
    public sealed class NameplateSlot
    {
        public long Order;
        public float X, Y, Width, Height, PlacedY;
    }

    // Canvas coordinates, positive Y upwards. Horizontal positions never change.
    public static class NameplateLayout
    {
        public static float Arrange(List<NameplateSlot> labels, float bottom, float top, float gap, float overflowScroll = 0f)
        {
            labels.Sort((a, b) =>
            {
                int height = Math.Round(b.Y * 2).CompareTo(Math.Round(a.Y * 2));
                return height != 0 ? height : a.Order.CompareTo(b.Order);
            });
            var groups = new List<List<NameplateSlot>>();
            foreach (var label in labels)
            {
                var joined = new List<NameplateSlot>
                {
                    label
                };
                for (int i = groups.Count - 1; i >= 0; i--)
                    if (groups[i].Exists(other => HorizontalOverlap(label, other, gap)))
                    {
                        joined.AddRange(groups[i]);
                        groups.RemoveAt(i);
                    }

                groups.Add(joined);
            }

            float maxOverflow = 0f;
            foreach (var group in groups)
            {
                group.Sort((a, b) => labels.IndexOf(a).CompareTo(labels.IndexOf(b)));
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
                maxOverflow = Math.Max(maxOverflow, overflow);
                float shift = overflow > 0f ? top - highest + Math.Min(Math.Max(overflowScroll, 0f), overflow) : Math.Max(bottom - lowest, Math.Min(top - highest, displacement / group.Count));
                foreach (var label in group)
                    label.PlacedY += shift;
            }

            return maxOverflow;
        }

        private static bool HorizontalOverlap(NameplateSlot a, NameplateSlot b, float gap) => Math.Abs(a.X - b.X) < (a.Width + b.Width) * .5f + gap;
    }
}
