using System;

namespace Workerborn
{
    [Serializable]
    public struct CharacterGaze
    {
        internal const float MaxYaw = 60f;
        internal const float MaxUp = 27f;
        internal const float MaxDown = 36f;
        private const float RadiansToDegrees = 180f / MathF.PI;

        public float yaw, pitch, weight;

        internal CharacterGaze Validated()
        {
            if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || !float.IsFinite(weight) || weight <= 0f)
                return default;
            return new CharacterGaze
            {
                yaw = Math.Clamp(yaw, -MaxYaw, MaxYaw),
                pitch = Math.Clamp(pitch, -MaxUp, MaxDown),
                weight = Math.Min(1f, weight)
            };
        }

        internal static CharacterGaze Toward(float x, float y, float z, float attention = 1f)
        {
            float horizontal = MathF.Sqrt(x * x + z * z);
            if (!float.IsFinite(horizontal) || !float.IsFinite(y) || horizontal * horizontal + y * y < .01f)
                return default;
            float turn = MathF.Atan2(x, z) * RadiansToDegrees;
            float away = Math.Clamp((MathF.Abs(turn) - 75f) / 60f, 0f, 1f);
            return new CharacterGaze
            {
                yaw = turn,
                pitch = MathF.Atan2(-y, horizontal) * RadiansToDegrees,
                weight = attention * (1f - away * away * (3f - 2f * away))
            }.Validated();
        }
    }
}
