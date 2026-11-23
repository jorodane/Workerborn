using System;

namespace Workerborn
{
    [Serializable]
    public struct CharacterLook
    {
        public const int HairCount = 13;
        public const int OutfitCount = 12;
        public const int FaceCount = 3;
        public int body;
        public int hair;
        public int outfit;
        public int face;

        public CharacterLook Validated() => new CharacterLook
        {
            body = body == 1 ? 1 : 0,
            hair = hair >= 1 && hair <= HairCount ? hair : 1,
            outfit = outfit >= 1 && outfit <= OutfitCount ? outfit : 3,
            face = face >= 1 && face <= FaceCount ? face : 1
        };

        internal int ToSelection()
        {
            var look = Validated();
            return ((look.body * HairCount + look.hair - 1) * OutfitCount + look.outfit - 1) * FaceCount + look.face - 1;
        }

        internal static bool TryFromSelection(int selection, out CharacterLook look)
        {
            look = default;
            if (selection < 0 || selection >= 2 * HairCount * OutfitCount * FaceCount)
                return false;
            look.face = selection % FaceCount + 1;
            selection /= FaceCount;
            look.outfit = selection % OutfitCount + 1;
            selection /= OutfitCount;
            look.hair = selection % HairCount + 1;
            look.body = selection / HairCount;
            return true;
        }
    }
}
