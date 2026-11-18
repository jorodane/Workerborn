using System;

namespace Workerborn
{
    public struct TargetMaskHit
    {
        public int Id;
        public float X, Y, DistanceSquared;
    }

    // Finds a point on the actual raster silhouette, including concave boundaries.
    public static class TargetMaskGeometry
    {
        public static bool Closest(byte[] rgba, int width, int height, bool flipY, float cursorX, float cursorY, float screenWidth, float screenHeight, out TargetMaskHit hit)
        {
            hit = new TargetMaskHit
            {
                DistanceSquared = float.MaxValue
            };
            if (rgba == null || rgba.Length != width * height * 4 || width < 1 || height < 1)
                return false;
            float sx = screenWidth / width, sy = screenHeight / height;
            int px = (int)(cursorX / sx), py = (int)(cursorY / sy);
            int inside = IdAt(rgba, width, height, px, py, flipY);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int id = IdAt(rgba, width, height, x, y, flipY);
                    if (id == 0 || inside != 0 && id != inside)
                        continue;
                    float left = x * sx, bottom = y * sy, right = left + sx, top = bottom + sy;
                    if (IdAt(rgba, width, height, x - 1, y, flipY) != id)
                        Consider(id, left, Clamp(cursorY, bottom, top), cursorX, cursorY, ref hit);
                    if (IdAt(rgba, width, height, x + 1, y, flipY) != id)
                        Consider(id, right, Clamp(cursorY, bottom, top), cursorX, cursorY, ref hit);
                    if (IdAt(rgba, width, height, x, y - 1, flipY) != id)
                        Consider(id, Clamp(cursorX, left, right), bottom, cursorX, cursorY, ref hit);
                    if (IdAt(rgba, width, height, x, y + 1, flipY) != id)
                        Consider(id, Clamp(cursorX, left, right), top, cursorX, cursorY, ref hit);
                }

            return hit.Id != 0;
        }

        private static float Clamp(float n, float low, float high) => Math.Max(low, Math.Min(high, n));
        private static int IdAt(byte[] p, int w, int h, int x, int y, bool flip)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return 0;
            int i = ((flip ? h - 1 - y : y) * w + x) * 4;
            return p[i] | p[i + 1] << 8 | p[i + 2] << 16;
        }

        private static void Consider(int id, float x, float y, float cx, float cy, ref TargetMaskHit hit)
        {
            float distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (distance > hit.DistanceSquared || distance == hit.DistanceSquared && id >= hit.Id && hit.Id != 0)
                return;
            hit = new TargetMaskHit
            {
                Id = id,
                X = x,
                Y = y,
                DistanceSquared = distance
            };
        }
    }
}
