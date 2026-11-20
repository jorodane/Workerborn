using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Workerborn.Editor
{
    public static class VillagePrototypeMenu
    {
        public const string ScenePath = "Assets/Workerborn/Scenes/VillagePrototype.unity";
        private const string P09Path = "Assets/Workerborn/Data/Characters/Player_P09_Trial.asset";

        [MenuItem("Workerborn/Village Prototype/Open Village")]
        public static void OpenVillage() => TryOpenVillage();

        [MenuItem("Workerborn/Village Prototype/Play Village")]
        public static void PlayVillage()
        {
            if (TryOpenVillage())
                EditorApplication.isPlaying = true;
        }

        [MenuItem("Workerborn/Village Prototype/Play With P09")]
        public static void PlayWithP09() => PlayWithAppearance(AssetDatabase.LoadAssetAtPath<CharacterDefinition>(P09Path));

        internal static void PlayWithAppearance(CharacterDefinition definition)
        {
            if (definition == null || definition.BasePrefab == null)
            {
                Debug.LogError("Choose a Workerborn character appearance with a base prefab.");
                return;
            }
            if (!TryOpenVillage())
                return;
            VillagePrototype.QueueEditorAppearanceTrial(definition);
            EditorApplication.isPlaying = true;
        }

        private static bool TryOpenVillage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            return EditorSceneManager.OpenScene(ScenePath).IsValid();
        }
    }
}
