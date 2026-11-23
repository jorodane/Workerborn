using UnityEngine;

namespace Workerborn
{
    internal enum VillageMapSurface { None, Building, Paving, Field, Water }

    internal readonly struct VillageMapFootprint
    {
        private readonly Transform transform;
        private readonly Vector3 center;
        private readonly Vector2 size;
        private readonly VillageMapSurface surface;
        internal Vector3 WorldCenter => transform.TransformPoint(center);

        internal VillageMapFootprint(Transform transform, Vector3 center, Vector2 size, VillageMapSurface surface)
        {
            this.transform = transform;
            this.center = center;
            this.size = size;
            this.surface = surface;
        }

        internal void Paint(Color32[] pixels, int columns, int rows, Rect area)
        {
            Vector2 corner = Xz(transform.TransformPoint(center - new Vector3(size.x * .5f, 0f, size.y * .5f)));
            Vector2 east = Xz(transform.TransformVector(new Vector3(size.x, 0f, 0f)));
            Vector2 north = Xz(transform.TransformVector(new Vector3(0f, 0f, size.y)));
            float determinant = Cross(east, north);
            if (Mathf.Abs(determinant) < .001f)
                return;
            Vector2 far = corner + east + north;
            Vector2 min = Vector2.Min(Vector2.Min(corner, far), Vector2.Min(corner + east, corner + north));
            Vector2 max = Vector2.Max(Vector2.Max(corner, far), Vector2.Max(corner + east, corner + north));
            var step = new Vector2(area.width / columns, area.height / rows);
            int firstX = Mathf.Clamp(Mathf.FloorToInt((min.x - area.xMin) / step.x), 0, columns);
            int firstZ = Mathf.Clamp(Mathf.FloorToInt((min.y - area.yMin) / step.y), 0, rows);
            int lastX = Mathf.Clamp(Mathf.CeilToInt((max.x - area.xMin) / step.x), 0, columns);
            int lastZ = Mathf.Clamp(Mathf.CeilToInt((max.y - area.yMin) / step.y), 0, rows);
            float width = east.magnitude;
            float depth = north.magnitude;
            float line = Mathf.Max(.18f, Mathf.Max(step.x, step.y));
            Color fill = surface switch
            {
                VillageMapSurface.Building => new Color(.66f, .48f, .30f),
                VillageMapSurface.Field => new Color(.74f, .59f, .39f),
                VillageMapSurface.Water => new Color(.48f, .65f, .63f),
                _ => new Color(.84f, .76f, .60f)
            };
            Color ink = new Color(.36f, .28f, .19f);
            Color32 edge = Color.Lerp(fill, ink, surface == VillageMapSurface.Building ? .68f : .40f);
            Color32 fieldRow = Color.Lerp(fill, ink, .20f);
            Color32 baseColor = fill;
            for (int z = firstZ; z < lastZ; z++)
                for (int x = firstX; x < lastX; x++)
                {
                    Vector2 offset = new Vector2(area.xMin + (x + .5f) * step.x, area.yMin + (z + .5f) * step.y) - corner;
                    float u = Cross(offset, north) / determinant;
                    float v = Cross(east, offset) / determinant;
                    if (u < 0f || u > 1f || v < 0f || v > 1f)
                        continue;
                    bool border = Mathf.Min(u, 1f - u) * width < line || Mathf.Min(v, 1f - v) * depth < line;
                    bool cultivated = surface == VillageMapSurface.Field && Mathf.Repeat(v * depth, 1f) < line;
                    pixels[z * columns + x] = border ? edge : cultivated ? fieldRow : baseColor;
                }
        }

        private static Vector2 Xz(Vector3 value) => new Vector2(value.x, value.z);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
