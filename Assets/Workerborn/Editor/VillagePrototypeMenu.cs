using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Workerborn.Editor
{
    public static class VillagePrototypeMenu
    {
        public const string ScenePath = "Assets/Workerborn/Scenes/VillagePrototype.unity";

        [MenuItem("Workerborn/Village Prototype/Open Village")]
        public static void OpenVillage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Workerborn/Village Prototype/Play Village")]
        public static void PlayVillage()
        {
            OpenVillage();
            if (SceneManager.GetActiveScene().path == ScenePath) EditorApplication.isPlaying = true;
        }
    }
}
