namespace Workerborn
{
    internal static class VillageChatText
    {
        public const int MaximumLength = 240;

        public static bool TryNormalize(string input, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumLength)
                return false;
            foreach (char character in input)
                if (char.IsControl(character))
                    return false;
            message = input.Trim();
            return true;
        }
    }
}
