using System;

namespace Workerborn
{
    internal static class WorldCapacity
    {
        public const int MaxCharacters = 256;
        public const int MaxConnections = 12;
        public const int DefaultNewWorld = 4;

        public static bool IsValid(int limit) => limit >= 1 && limit <= MaxCharacters;

        public static int ScaleRequirement(int amount, int characters) =>
            (int)Math.Min(int.MaxValue, (long)Math.Max(0, amount) * characters);
    }
}
