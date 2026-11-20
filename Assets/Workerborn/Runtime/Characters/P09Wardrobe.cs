using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    // The P09 prefab already has every garment bound to its Humanoid bones.
    internal static class P09Wardrobe
    {
        public static void Apply(Transform root, CharacterLook source, IReadOnlyDictionary<EquipmentSlot, string> gear = null, VillageSettings settings = null)
        {
            var look = source.Validated();
            bool male = look.body == 1;
            string body = male ? "Male" : "Female";
            var parts = root.GetComponentsInChildren<Transform>(true);
            int cap = Armor(EquipmentSlot.Head, 0, gear, settings);
            int torso = Armor(EquipmentSlot.Body, look.outfit, gear, settings);
            int arms = Armor(EquipmentSlot.Hands, look.outfit, gear, settings);
            // The kit supplies trousers and footwear in a single Leg mesh.
            int legs = Armor(EquipmentSlot.Legs, Armor(EquipmentSlot.Feet, look.outfit, gear, settings), gear, settings);
            int waist = torso;
            string chestMesh = ResolvePart(parts, body, torso, "Chest");
            string armMesh = ResolvePart(parts, body, arms, "Arm");
            string legMesh = ResolvePart(parts, body, legs, "Leg");
            string waistMesh = ResolvePart(parts, body, waist, "Waist");
            string headMesh = cap == 0 ? null : ResolvePart(parts, body, cap, "Head");

            foreach (var part in parts)
            {
                string name = part.name;
                switch (name)
                {
                    case "Male": Set(part, male); continue;
                    case "Female": Set(part, !male); continue;
                    case "SkinHead_Male": Set(part, male && headMesh == null); continue;
                    case "SkinHead_Female": Set(part, !male && headMesh == null); continue;
                    case "Nakid_Body": Set(part, false); continue;
                    case "Weapon": Set(part, false); continue;
                }
                if (Numbered(name, "Hair_", 2, out int hair))
                    Set(part, hair == look.hair && headMesh == null);
                else if (name.StartsWith("Male_Face_", StringComparison.Ordinal) || name.StartsWith("Female_Face_", StringComparison.Ordinal))
                    Set(part, name == $"{body}_Face_{look.face:00}");
                else if (Numbered(name, "Armor_", 3, out int set))
                    Set(part, set == torso || set == arms || set == legs || set == waist || set == cap);
                else if (name.Contains("_Armor_") && part.TryGetComponent<SkinnedMeshRenderer>(out _))
                    Set(part, name == chestMesh || name == armMesh || name == legMesh || name == waistMesh || name == headMesh);
                else if (name == body + "_Body_Chest")
                    Set(part, chestMesh == null);
                else if (name == body + "_Body_Arm")
                    Set(part, armMesh == null);
                else if (name == body + "_Body_Leg")
                    Set(part, legMesh == null);
            }
        }

        private static bool Numbered(string name, string prefix, int digits, out int number)
        {
            number = 0;
            return name.Length == prefix.Length + digits && name.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(name.Substring(prefix.Length), out number);
        }

        private static string ResolvePart(Transform[] parts, string body, int set, string slot)
        {
            string name = $"{body}_Armor_{set:000}_{slot}";
            string alternative = body == "Female" ? $"Fem_Armor_{set:000}_{slot}" : name;
            foreach (var part in parts)
                if (part.name == name || part.name == alternative)
                    return part.name;
            return null;
        }

        private static int Armor(EquipmentSlot slot, int fallback, IReadOnlyDictionary<EquipmentSlot, string> gear, VillageSettings settings)
        {
            if (settings == null || gear == null || !gear.TryGetValue(slot, out var id))
                return fallback;
            int set = settings.FindItem(id)?.EquipmentPart?.P09ArmorSet ?? 0;
            return set > 0 ? set : fallback;
        }

        private static void Set(Transform part, bool visible)
        {
            if (part.gameObject.activeSelf != visible)
                part.gameObject.SetActive(visible);
        }
    }
}
