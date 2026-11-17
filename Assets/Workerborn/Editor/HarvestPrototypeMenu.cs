using UnityEditor;
using UnityEditor.SceneManagement;

namespace Workerborn.Editor
{
    public static class HarvestPrototypeMenu
    {
        public const string ScenePath = "Assets/Workerborn/Scenes/HarvestPrototype.unity";

        [MenuItem("Workerborn/Harvest Prototype/Open Scene")]
        public static void OpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Workerborn/Harvest Prototype/Select Potato Settings")]
        public static void SelectPotatoSettings()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<CropDefinition>("Assets/Workerborn/Data/Potato.asset");
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
