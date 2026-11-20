using System;
using UnityEngine;

namespace Workerborn
{
    [Flags]
    public enum SkinRegion
    {
        None = 0, Neck = 1, Chest = 2, Waist = 4, Shoulders = 8,
        UpperArms = 16, Forearms = 32, Hands = 64, Hips = 128,
        Thighs = 256, Knees = 512, Shins = 1024, Feet = 2048
    }

    [DisallowMultipleComponent]
    public sealed class CharacterGarment : MonoBehaviour
    {
        [SerializeField] private EquipmentSlot slot;
        [SerializeField, Tooltip("Hide only skin sections completely covered by this mesh.")]
        private SkinRegion coveredSkin;
        [SerializeField, Tooltip("Optional source renderer that supplies the skin's bone order.")]
        private SkinnedMeshRenderer skinBinding;
        public EquipmentSlot Slot => slot;
        public SkinnedMeshRenderer SkinBinding => skinBinding;

        public static void RefreshSkin(Transform rig)
        {
            if (rig == null)
                return;
            var garments = rig.GetComponentsInChildren<CharacterGarment>(true);
            if (garments.Length == 0)
                return;

            SkinRegion covered = SkinRegion.None;
            foreach (var garment in garments)
                if (garment.isActiveAndEnabled && (!garment.TryGetComponent<Renderer>(out var renderer) || renderer.enabled))
                    covered |= garment.coveredSkin;

            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                SkinRegion region = FindRegion(skin.name);
                if (region != SkinRegion.None)
                    skin.gameObject.SetActive((covered & region) == 0);
            }
        }

        private static SkinRegion FindRegion(string name)
        {
            bool female = name.StartsWith("F02_body_", StringComparison.Ordinal);
            if (!female && !name.StartsWith("M02_body_", StringComparison.Ordinal))
                return SkinRegion.None;
            switch (name.Substring(9))
            {
                case "torso.002": return SkinRegion.Neck;
                case "torso.001": return female ? SkinRegion.Waist : SkinRegion.Chest;
                case "torso": return female ? SkinRegion.Chest : SkinRegion.Waist;
                case "shoulders": return SkinRegion.Shoulders;
                case "arms_upper": return SkinRegion.UpperArms;
                case "arms_lower": return SkinRegion.Forearms;
                case "hands": return SkinRegion.Hands;
                case "hips": return SkinRegion.Hips;
                case "legs_upper": return SkinRegion.Thighs;
                case "legs_knee": return SkinRegion.Knees;
                case "legs_lower": return SkinRegion.Shins;
                case "feet": return SkinRegion.Feet;
                default: return SkinRegion.None;
            }
        }
    }
}
