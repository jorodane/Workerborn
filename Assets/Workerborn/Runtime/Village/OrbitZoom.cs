using System;

namespace Workerborn
{
    public static class OrbitZoom
    {
        public static float Apply(float distance, float scroll, float minimum, float maximum, float sensitivity = 0.18f)
        {
            // Input System backends report either 1 or 120 units per wheel notch.
            float notches = Math.Abs(scroll) >= 10f ? scroll / 120f : scroll;
            return Math.Max(minimum, Math.Min(maximum, distance * (float)Math.Exp(-notches * sensitivity)));
        }
    }
}
