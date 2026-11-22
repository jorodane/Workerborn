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
        private static bool changingPlayMode;
        internal static bool CanEditScene => !changingPlayMode && !EditorApplication.isPlaying &&
            !EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying;

        static CharacterAppearanceSynchronizer()
        {
            EditorApplication.hierarchyChanged += Queue;
            EditorApplication.projectChanged += Queue;
            EditorApplication.delayCall += Queue;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            changingPlayMode = state != PlayModeStateChange.EnteredEditMode;
            if (!changingPlayMode)
            {
                Queue();
                return;
            }
            EditorApplication.delayCall -= Apply;
            queued = false;
        }

        private static void Queue()
        {
            if (queued || !CanEditScene)
                return;
            queued = true;
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            queued = false;
            if (!CanEditScene)
                return;
            foreach (var actor in Resources.FindObjectsOfTypeAll<CharacterActor>())
            {
                if (!CanRebuild(actor))
                    continue;
                string path = AssetDatabase.GetAssetPath(actor.Appearance);
                string hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
                if (actor.AppliedAppearanceHash == hash)
                    continue;
                Rebuild(actor, "Apply shared character appearance");
            }
        }

        internal static void Rebuild(CharacterActor actor, string undoLabel)
        {
            if (!CanRebuild(actor))
                return;
            // Registering the hierarchy undo also marks its scene as modified.
            Undo.RegisterFullObjectHierarchyUndo(actor.gameObject, undoLabel);
            actor.RebuildAppearance();
        }

        private static bool CanRebuild(CharacterActor actor)
        {
            if (!CanEditScene || actor == null || EditorUtility.IsPersistent(actor) || actor.Appearance == null)
                return false;
            var scene = actor.gameObject.scene;
            return scene.IsValid() && scene.isLoaded &&
                (!EditorSceneManager.IsPreviewScene(scene) || PrefabStageUtility.GetPrefabStage(actor.gameObject) != null);
        }
    }
}
