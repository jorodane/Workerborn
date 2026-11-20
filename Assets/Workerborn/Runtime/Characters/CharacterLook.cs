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
    }
}
