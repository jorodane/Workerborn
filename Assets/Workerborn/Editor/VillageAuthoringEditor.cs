using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Workerborn.Editor
{
    [CustomEditor(typeof(VillageNavigation))]
    public sealed class VillageNavigationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("마을과 장애물은 Scene에 저장됩니다. 길찾기 데이터만 선택적으로 베이크할 수 있습니다. 비어 있으면 Play 시 배치된 Collider로 길찾기 데이터만 계산합니다.", MessageType.Info);
            if (!GUILayout.Button("현재 Scene의 길찾기 데이터 저장"))
                return;
            string path = EditorUtility.SaveFilePanelInProject("Save navigation", "VillageNavigation", "asset", "Scene의 장애물 위치를 변경했다면 다시 베이크하세요.");
            if (string.IsNullOrEmpty(path))
                return;
            var navigation = (VillageNavigation)target;
            var data = navigation.BuildData();
            if (data == null)
                return;
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                DestroyImmediate(data);
                data = existing;
            }
            else
                AssetDatabase.CreateAsset(data, path);
            Undo.RecordObject(navigation, "Assign navigation data");
            navigation.SetBakedData(data);
            EditorUtility.SetDirty(navigation);
            EditorUtility.SetDirty(data);
            EditorSceneManager.MarkSceneDirty(navigation.gameObject.scene);
            AssetDatabase.SaveAssets();
        }
    }

    // Duplication must not make two editable plants share the same saved depletion state.
    [InitializeOnLoad]
    internal static class CropIdentityEditor
    {
        private static bool pending;
        private static readonly Dictionary<int, string> known = new Dictionary<int, string>();
        static CropIdentityEditor()
        {
            EditorApplication.hierarchyChanged += Queue;
            EditorApplication.delayCall += Queue;
        }

        private static void Queue()
        {
            if (pending || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            pending = true;
            EditorApplication.delayCall += Assign;
        }

        private static void Assign()
        {
            pending = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var ids = new Dictionary<int, HashSet<string>>();
            var crops = Resources.FindObjectsOfTypeAll<WorldInteractable>();
            System.Array.Sort(crops, (a, b) => known.ContainsKey(b.GetInstanceID()).CompareTo(known.ContainsKey(a.GetInstanceID())));
            foreach (var crop in crops)
            {
                if (EditorUtility.IsPersistent(crop) || !crop.gameObject.scene.IsValid() || !crop.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(crop.gameObject.scene))
                    continue;
                int scene = crop.gameObject.scene.handle;
                if (!ids.TryGetValue(scene, out var seen))
                    ids[scene] = seen = new HashSet<string>();
                if (!string.IsNullOrEmpty(crop.PersistentId) && seen.Add(crop.PersistentId))
                {
                    known[crop.GetInstanceID()] = crop.PersistentId;
                    continue;
                }

                var serialized = new SerializedObject(crop);
                Undo.RecordObject(crop, "Assign interaction save ID");
                string id = System.Guid.NewGuid().ToString("N");
                serialized.FindProperty("persistentId").stringValue = id;
                serialized.ApplyModifiedProperties();
                seen.Add(id);
                known[crop.GetInstanceID()] = id;
                known[crop.GetInstanceID()] = id;
                EditorSceneManager.MarkSceneDirty(crop.gameObject.scene);
            }
        }
    }
}
