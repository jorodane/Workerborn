using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Workerborn.Editor
{
    // Keep newly dragged prefab instances consistent with their shared appearance asset.
    // This only rebuilds character visuals in Edit mode; levels are always authored.
    [InitializeOnLoad]
    internal static class CharacterAppearanceSynchronizer
    {
        private static bool queued;
        static CharacterAppearanceSynchronizer()
        {
            EditorApplication.hierarchyChanged += Queue;
            EditorApplication.projectChanged += Queue;
            EditorApplication.delayCall += Queue;
        }

        private static void Queue()
        {
            if (queued || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            queued = true;
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            foreach (var actor in Resources.FindObjectsOfTypeAll<CharacterActor>())
            {
                var scene = actor.gameObject.scene;
                if (EditorUtility.IsPersistent(actor) || !scene.IsValid() || !scene.isLoaded || actor.Appearance == null)
                    continue;
                if (EditorSceneManager.IsPreviewScene(scene) && PrefabStageUtility.GetPrefabStage(actor.gameObject) == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(actor.Appearance);
                string hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
                if (actor.AppliedAppearanceHash == hash)
                    continue;
                Undo.RegisterFullObjectHierarchyUndo(actor.gameObject, "Apply shared character appearance");
                actor.RebuildAppearance();
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
