using UnityEngine;

namespace Workerborn
{
    public enum CharacterPartSlot { Hair, Outfit, Eyes, Eyebrows, Mouth, Headwear, RightHand, LeftHand }

    [CreateAssetMenu(menuName = "Workerborn/Characters/Part", fileName = "CharacterPart")]
    public sealed class CharacterPartDefinition : ScriptableObject
    {
        [SerializeField] private CharacterPartSlot slot;
        [SerializeField] private GameObject prefab;
        [SerializeField, Tooltip("Leave empty for skinned modules or modules authored in rig coordinates.")]
        private string attachmentBone;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private Material[] materials;
        [SerializeField] private Color tint = Color.white;
        [SerializeField, Tooltip("For a material-only part, leave Prefab empty and provide one material above.")]
        private string materialRenderer;
        [SerializeField, Min(0)] private int materialIndex;

        public CharacterPartSlot Slot => slot;
        public GameObject Prefab => prefab;
        public string AttachmentBone => attachmentBone;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public Material[] Materials => materials;
        public Color Tint => tint;
        public string MaterialRenderer => materialRenderer;
        public int MaterialIndex => materialIndex;
    }
}
