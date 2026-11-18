using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Workerborn.Editor
{
    public static class VillagePointerGeometryAuthoring
    {
        [MenuItem("CONTEXT/VillageMapProp/Rebuild Pointer Geometry")]
        private static void Rebuild(MenuCommand command)
        {
            var prop = (VillageMapProp)command.context;
            var settings = new SerializedObject(prop);
            var visual = settings.FindProperty("visual").objectReferenceValue as Transform;
            if (visual == null || EditorUtility.IsPersistent(prop))
            {
                Debug.LogWarning("Open this prefab in Prefab Mode before rebuilding its pointer geometry.", prop);
                return;
            }

            var old = prop.transform.Find("Pointer geometry");
            if (old != null)
                Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject("Pointer geometry");
            Undo.RegisterCreatedObjectUndo(root, "Rebuild pointer geometry");
            root.transform.SetParent(prop.transform, false);
            root.layer = VillageMapProp.PointerGeometryLayer;
            var transforms = new Dictionary<Transform, Transform>
            {
                [prop.transform] = root.transform
            };
            var excluded = new HashSet<Renderer>();
            foreach (var group in visual.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                var first = new HashSet<Renderer>(lods.Length > 0 ? lods[0].renderers : new Renderer[0]);
                for (int i = 1; i < lods.Length; i++)
                    foreach (var renderer in lods[i].renderers)
                        if (!first.Contains(renderer))
                            excluded.Add(renderer);
            }

            Transform Copy(Transform original)
            {
                if (transforms.TryGetValue(original, out var result))
                    return result;
                var parent = Copy(original.parent);
                var go = new GameObject(original.name);
                Undo.RegisterCreatedObjectUndo(go, "Rebuild pointer geometry");
                go.layer = VillageMapProp.PointerGeometryLayer;
                go.SetActive(original.gameObject.activeSelf);
                result = go.transform;
                result.SetParent(parent, false);
                result.localPosition = original.localPosition;
                result.localRotation = original.localRotation;
                result.localScale = original.localScale;
                transforms.Add(original, result);
                return result;
            }

            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || excluded.Contains(renderer))
                    continue;
                var collider = Undo.AddComponent<MeshCollider>(Copy(filter.transform).gameObject);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }

            // Explicitly baked geometry also covers a directly imported model.
            settings.FindProperty("useImportedMeshColliders").boolValue = false;
            settings.ApplyModifiedProperties();
            EditorUtility.SetDirty(prop);
        }
    }
}
