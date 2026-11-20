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
        public static bool Closest(byte[] rgba, int width, int height, bool flipY, float cursorX, float cursorY, float screenWidth, float screenHeight, out TargetMaskHit hit, Func<int, bool> eligible = null)
        {
            hit = new TargetMaskHit
            {
                DistanceSquared = float.MaxValue
            };
            if (rgba == null || rgba.Length != width * height * 4 || width < 1 || height < 1)
                return false;
            float sx = screenWidth / width, sy = screenHeight / height;
            int inside = IdAtScreen(rgba, width, height, flipY, cursorX, cursorY, screenWidth, screenHeight);
            if (inside != 0 && eligible != null && !eligible(inside))
                inside = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int id = IdAt(rgba, width, height, x, y, flipY);
                    if (id == 0 || inside != 0 && id != inside || eligible != null && !eligible(id))
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

        public static int IdAtScreen(byte[] pixels, int width, int height, bool flipY, float x, float y, float screenWidth, float screenHeight)
        {
            if (x < 0 || x >= screenWidth || y < 0 || y >= screenHeight)
                return 0;
            return IdAt(pixels, width, height, (int)(x * width / screenWidth), (int)(y * height / screenHeight), flipY);
        }

        private static float Clamp(float n, float low, float high) => Math.Max(low, Math.Min(high, n));
        public static int IdAt(byte[] p, int w, int h, int x, int y, bool flip)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return 0;
            int i = ((flip ? h - 1 - y : y) * w + x) * 4;
            return p[i] | p[i + 1] << 8 | p[i + 2] << 16;
        }

        // Outside pixels are reached from the edge of the selected silhouette's bounds.
        // Enclosed holes receive no outline, even when the mesh has open geometry.
        public static int MarkExterior(byte[] pixels, int width, int height, int id, byte[] output, int[] queue, int padding = 6)
        {
            Array.Clear(output, 0, output.Length);
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int i = 0; i < output.Length; i++)
            {
                int p = i * 4;
                if ((pixels[p] | pixels[p + 1] << 8 | pixels[p + 2] << 16) != id)
                    continue;
                output[i] = 255;
                int x = i % width, y = i / width;
                minX = Math.Min(x, minX);
                minY = Math.Min(y, minY);
                maxX = Math.Max(x, maxX);
                maxY = Math.Max(y, maxY);
            }
            if (maxX < 0)
                return 0;

            minX = Math.Max(0, minX - padding);
            minY = Math.Max(0, minY - padding);
            maxX = Math.Min(width - 1, maxX + padding);
            maxY = Math.Min(height - 1, maxY + padding);
            int tail = 0;
            for (int x = minX; x <= maxX; x++)
            {
                Mark(x + minY * width, output, queue, ref tail);
                Mark(x + maxY * width, output, queue, ref tail);
            }
            for (int y = minY + 1; y < maxY; y++)
            {
                Mark(minX + y * width, output, queue, ref tail);
                Mark(maxX + y * width, output, queue, ref tail);
            }
            for (int head = 0; head < tail; head++)
            {
                int i = queue[head], x = i % width, y = i / width;
                if (x > minX) Mark(i - 1, output, queue, ref tail);
                if (x < maxX) Mark(i + 1, output, queue, ref tail);
                if (y > minY) Mark(i - width, output, queue, ref tail);
                if (y < maxY) Mark(i + width, output, queue, ref tail);
            }
            return tail;
        }

        public static bool DrawExteriorOutline(byte[] pixels, int width, int height, int id, int radius,
            byte[] exterior, int[] queue, byte[] rgba)
        {
            Array.Clear(rgba, 0, rgba.Length);
            int count = MarkExterior(pixels, width, height, id, exterior, queue, radius);
            bool drawn = false;
            for (int i = 0; i < count; i++)
            {
                int pixel = queue[i];
                if (!TouchesSilhouette(exterior, width, height, pixel % width, pixel / width, radius))
                    continue;
                int p = pixel * 4;
                rgba[p] = rgba[p + 1] = rgba[p + 2] = rgba[p + 3] = 255;
                drawn = true;
            }
            return drawn;
        }

        private static bool TouchesSilhouette(byte[] exterior, int width, int height, int x, int y, int radius)
        {
            int limit = radius * radius;
            for (int row = Math.Max(0, y - radius); row < height && row <= y + radius; row++)
                for (int column = Math.Max(0, x - radius); column < width && column <= x + radius; column++)
                {
                    int dx = x - column, dy = y - row;
                    if (dx * dx + dy * dy <= limit && exterior[row * width + column] == 255)
                        return true;
                }
            return false;
        }

        private static void Mark(int index, byte[] output, int[] queue, ref int tail)
        {
            if (output[index] != 0)
                return;
            output[index] = 128;
            queue[tail++] = index;
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
