using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Characters/Appearance", fileName = "Character")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [SerializeField]
        private GameObject basePrefab;
        [SerializeField, Min(0.1f)]
        private float height = 1.7f;
        [Header("A missing part preserves the corresponding part of the base prefab.")]
        [SerializeField]
        private CharacterPartDefinition hair;
        [SerializeField]
        private CharacterPartDefinition outfit;
        [SerializeField]
        private CharacterPartDefinition eyes;
        [SerializeField]
        private CharacterPartDefinition eyebrows;
        [SerializeField]
        private CharacterPartDefinition mouth;
        [SerializeField]
        private CharacterPartDefinition headwear;
        [SerializeField]
        private CharacterPartDefinition rightHand;
        [SerializeField]
        private CharacterPartDefinition leftHand;
        [SerializeField]
        private bool hideBaseWeapons = true;
        [SerializeField]
        private bool hideBaseHair;
        [SerializeField]
        private Color skinTint = Color.white;
        [SerializeField]
        private Color clothingTint = Color.white;
        [Header("P09 appearance")]
        [SerializeField] private bool useP09;
        [SerializeField] private CharacterLook p09Look;
        public bool UsesP09 => useP09;
        public CharacterLook DefaultLook => p09Look.Validated();

        [Header("Tool sockets")]
        [SerializeField, Tooltip("Transform to use in place of the default weapon_R socket.")]
        private string rightHandSocket;
        [SerializeField, Tooltip("Transform to use in place of the default weapon_L socket.")]
        private string leftHandSocket;
        [Header("Existing animations")]
        [SerializeField]
        private AnimationClip idle;
        [SerializeField]
        private AnimationClip walk;
        [SerializeField]
        private AnimationClip run;
        [SerializeField]
        private AnimationClip work;
        [SerializeField]
        private ToonMaterialPalette toonPalette;
        public ToonMaterialPalette ToonPalette => toonPalette;
        public GameObject BasePrefab => basePrefab;
        public float Height => Mathf.Max(0.1f, height);
        public bool HideBaseWeapons => hideBaseWeapons;
        public bool HideBaseHair => hideBaseHair;
        public Color SkinTint => skinTint;
        public Color ClothingTint => clothingTint;
        public AnimationClip Idle => idle;
        public AnimationClip Walk => walk;
        public AnimationClip Run => run != null ? run : walk;
        public AnimationClip Work => work;

        internal string ResolveSocket(string attachment)
        {
            if (attachment == "weapon_R" && !string.IsNullOrWhiteSpace(rightHandSocket))
                return rightHandSocket;
            if (attachment == "weapon_L" && !string.IsNullOrWhiteSpace(leftHandSocket))
                return leftHandSocket;
            return attachment;
        }

        public IEnumerable<CharacterPartDefinition> Parts
        {
            get
            {
                foreach (var part in new[]
                {
                    hair,
                    outfit,
                    eyes,
                    eyebrows,
                    mouth,
                    headwear,
                    rightHand,
                    leftHand
                }

                )
                    if (part != null)
                        yield return part;
            }
        }
    }
}
