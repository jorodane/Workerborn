using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    // Used by Inspector previews and explicit editor rebuilds, never to generate a level on Play.
    public static class CharacterAssembler
    {
        public static GameObject Build(CharacterDefinition definition, Transform parent)
        {
            if (definition == null || definition.BasePrefab == null)
                throw new ArgumentException("Choose a character base prefab.");
            var root = new GameObject("Appearance");
            root.transform.SetParent(parent, false);
            var rig = InstantiateSource(definition.BasePrefab, root.transform);
            rig.name = "Rig";
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name))
                    bones.Add(bone.name, bone);
            var baseRenderers = rig.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in baseRenderers)
            {
                string name = renderer.name.ToLowerInvariant();
                if (definition.HideBaseWeapons && IsWeapon(name))
                    renderer.enabled = false;
                if (definition.HideBaseHair && Matches(name, CharacterPartSlot.Hair))
                    renderer.enabled = false;
            }

            foreach (var part in definition.Parts)
            {
                if (part.Prefab == null)
                {
                    ApplyMaterialPart(part, baseRenderers);
                    continue;
                }
                if (AttachPart(part, rig.transform, bones) == null)
                    continue;
                foreach (var renderer in baseRenderers)
                    if (Matches(renderer.name, part.Slot) && MatchesHand(renderer.transform, part.Slot))
                        renderer.enabled = false;
            }

            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2;
            definition.ToonPalette?.Apply(rig.transform);
            ApplyTints(rig.transform, definition);
            var renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = default;
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                    continue;
                var part = renderer.GetComponentInParent<CharacterPartInstance>();
                if (part != null && (part.Definition.Slot == CharacterPartSlot.RightHand || part.Definition.Slot == CharacterPartSlot.LeftHand))
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            if (found && bounds.size.y > 0.001f)
            {
                Vector3 bottom = root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                float scale = definition.Height / (bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(root.transform.lossyScale.y)));
                rig.transform.localPosition -= bottom;
                root.transform.localScale = Vector3.one * scale;
            }

            return root;
        }

        private static void ApplyMaterialPart(CharacterPartDefinition part, Renderer[] renderers)
        {
            if (string.IsNullOrEmpty(part.MaterialRenderer) || part.Materials == null || part.Materials.Length != 1 || part.Materials[0] == null)
                return;
            var renderer = Array.Find(renderers, mesh => mesh.name == part.MaterialRenderer);
            if (renderer == null)
                return;
            var materials = renderer.sharedMaterials;
            if (part.MaterialIndex >= 0 && part.MaterialIndex < materials.Length)
            {
                materials[part.MaterialIndex] = part.Materials[0];
                renderer.sharedMaterials = materials;
            }
        }

        public static GameObject AttachPart(CharacterPartDefinition part, Transform rig, Dictionary<string, Transform> bones = null)
        {
            if (part == null || part.Prefab == null || rig == null)
                return null;
            if (bones == null)
            {
                bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
                    if (!bones.ContainsKey(bone.name))
                        bones.Add(bone.name, bone);
            }

            Transform attachment = rig;
            if (!string.IsNullOrEmpty(part.AttachmentBone))
            {
                if (!bones.TryGetValue(part.AttachmentBone, out attachment))
                {
                    Debug.LogError($"{part.name}: bone '{part.AttachmentBone}' is missing in {rig.name}.");
                    return null;
                }
            }

            var module = InstantiateSource(part.Prefab, attachment);
            var instance = module.GetComponent<CharacterPartInstance>();
            if (instance == null)
                instance = module.AddComponent<CharacterPartInstance>();
            instance.Initialize(part);
            module.name = part.Slot + " - " + part.name;
            module.transform.localPosition = part.LocalPosition;
            module.transform.localRotation = Quaternion.Euler(part.LocalEulerAngles);
            module.transform.localScale = part.LocalScale;
            foreach (var animator in module.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (var renderer in module.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = renderer.bones;
                var mapped = new Transform[source.Length];
                bool valid = true;
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == null || !bones.TryGetValue(source[i].name, out mapped[i]))
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                {
                    Debug.LogError($"{part.name}: the skinned module has bones missing from the selected rig.");
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(module);
                    else
                        UnityEngine.Object.DestroyImmediate(module);
                    return null;
                }

                renderer.bones = mapped;
                if (renderer.rootBone != null && bones.TryGetValue(renderer.rootBone.name, out var bone))
                    renderer.rootBone = bone;
                renderer.updateWhenOffscreen = true;
            }

            foreach (var renderer in module.GetComponentsInChildren<Renderer>(true))
            {
                if (part.Materials != null && part.Materials.Length > 0)
                    renderer.sharedMaterials = part.Materials;
                Tint(renderer, part.Tint);
            }

            foreach (var collider in module.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            return module;
        }

        private static GameObject InstantiateSource(GameObject source, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(source))
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(source, parent);
#endif
            return UnityEngine.Object.Instantiate(source, parent);
        }

        public static void ApplyTints(Transform model, CharacterDefinition definition)
        {
            if (model == null || definition == null)
                return;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<CharacterPartInstance>() != null)
                    continue;
                string name = renderer.name.ToLowerInvariant();
                if (definition.HideBaseWeapons && IsWeapon(name))
                    renderer.enabled = false;
                if (definition.HideBaseHair && Matches(name, CharacterPartSlot.Hair))
                    renderer.enabled = false;
                if (name == "char_head" || name == "char_body" || name.StartsWith("m02_body_") || name.StartsWith("f02_body_"))
                    Tint(renderer, definition.SkinTint);
                else if (Matches(name, CharacterPartSlot.Outfit))
                    Tint(renderer, definition.ClothingTint);
            }
        }

        private static void Tint(Renderer renderer, Color color)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        private static bool IsWeapon(string name) => name.Contains("sword") || name.Contains("shield") || name.Contains("bow_") || name.Contains("arrow_") || name.Contains("knife_") || name.Contains("shuriken");
        private static bool MatchesHand(Transform part, CharacterPartSlot slot)
        {
            if (slot != CharacterPartSlot.RightHand && slot != CharacterPartSlot.LeftHand)
                return true;
            for (var parent = part; parent != null; parent = parent.parent)
            {
                if (parent.name == "weapon_R")
                    return slot == CharacterPartSlot.RightHand;
                if (parent.name == "weapon_L")
                    return slot == CharacterPartSlot.LeftHand;
            }

            return true;
        }

        private static bool Matches(string name, CharacterPartSlot slot)
        {
            name = name.ToLowerInvariant();
            switch (slot)
            {
                case CharacterPartSlot.Hair:
                    return name.StartsWith("hair_") || name.StartsWith("c_hair_");
                case CharacterPartSlot.Outfit:
                    return name == "char_body" || name.StartsWith("outfit_") || name.StartsWith("warrior_") || name.StartsWith("thief_") || name.StartsWith("archer_");
                case CharacterPartSlot.Eyes:
                    return name.StartsWith("eye_");
                case CharacterPartSlot.Eyebrows:
                    return name.StartsWith("eyebrow_");
                case CharacterPartSlot.Mouth:
                    return name.StartsWith("mouth_");
                case CharacterPartSlot.Headwear:
                    return name.StartsWith("hat") || name.StartsWith("helmet");
                case CharacterPartSlot.RightHand:
                case CharacterPartSlot.LeftHand:
                    return IsWeapon(name);
                default:
                    return false;
            }
        }
    }
}
