using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Workerborn.Editor
{
    // A failed/early import can leave a cached null renderer reference in a pipeline asset.
    // Repair only missing default slots, preserving valid renderer choices and renderer features.
    public static class VillageRenderPipelineReferences
    {
        [InitializeOnLoadMethod]
        private static void AfterDomainReload() => EditorApplication.delayCall += RepairWhenReady;
        private static void RepairWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RepairWhenReady;
                return;
            }

            RepairMissingReferences();
        }

        [MenuItem("Workerborn/Rendering/Repair Missing Renderer References")]
        public static void RepairMissingReferences()
        {
            Repair("PC");
            Repair("Mobile");
        }

        private static void Repair(string platform)
        {
            string root = "Assets/Workerborn/Rendering/";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(root + platform + "_Pipeline.asset");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(root + platform + "_Renderer.asset");
            if (pipeline == null || renderer == null)
                return;
            var serialized = new SerializedObject(pipeline);
            var list = serialized.FindProperty("m_RendererDataList");
            var index = serialized.FindProperty("m_DefaultRendererIndex");
            if (list == null || index == null)
                return;
            bool changed = false;
            if (list.arraySize == 0)
            {
                list.arraySize = 1;
                changed = true;
            }

            if (index.intValue < 0 || index.intValue >= list.arraySize)
            {
                index.intValue = 0;
                changed = true;
            }

            var slot = list.GetArrayElementAtIndex(index.intValue);
            if (slot.objectReferenceValue == null)
            {
                slot.objectReferenceValue = renderer;
                changed = true;
            }

            if (!changed)
                return;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
        }
    }
}
