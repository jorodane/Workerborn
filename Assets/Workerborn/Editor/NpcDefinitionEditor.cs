using UnityEditor;
using UnityEngine;

namespace Workerborn.Editor
{
    [CustomEditor(typeof(NpcDefinition))]
    public sealed class NpcDefinitionEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor appearanceEditor;
        private bool expanded = true;
        private void Prepare()
        {
            var appearance = ((NpcDefinition)target).Appearance;
            if (appearance != null) CreateCachedEditor(appearance, typeof(CharacterDefinitionEditor), ref appearanceEditor);
            else if (appearanceEditor != null) { DestroyImmediate(appearanceEditor); appearanceEditor = null; }
        }
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck(); DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) CharacterEditorRefresh.RefreshActors();
            Prepare();
            if (appearanceEditor == null) return;
            expanded = EditorGUILayout.Foldout(expanded, "캐릭터 파츠와 애니메이션", true);
            if (expanded) { EditorGUI.indentLevel++; appearanceEditor.OnInspectorGUI(); EditorGUI.indentLevel--; }
        }
        public override bool HasPreviewGUI() { Prepare(); return appearanceEditor != null; }
        public override bool RequiresConstantRepaint() => true;
        public override void OnPreviewGUI(Rect rect, GUIStyle background) { Prepare(); appearanceEditor?.OnPreviewGUI(rect, background); }
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) { Prepare(); appearanceEditor?.OnInteractivePreviewGUI(rect, background); }
        private void OnDisable() { if (appearanceEditor != null) DestroyImmediate(appearanceEditor); }
    }

    [CustomEditor(typeof(VillageSettings))]
    public sealed class VillageSettingsEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor playerEditor;
        private bool expanded;
        private void Prepare()
        {
            var player = ((VillageSettings)target).Player;
            if (player != null) CreateCachedEditor(player, typeof(CharacterDefinitionEditor), ref playerEditor);
            else if (playerEditor != null) { DestroyImmediate(playerEditor); playerEditor = null; }
        }
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck(); DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) CharacterEditorRefresh.RefreshActors();
            Prepare();
            if (playerEditor == null) return;
            expanded = EditorGUILayout.Foldout(expanded, "주인공 기본 외형 편집", true);
            if (expanded) playerEditor.OnInspectorGUI();
        }
        public override bool HasPreviewGUI() { Prepare(); return playerEditor != null; }
        public override bool RequiresConstantRepaint() => true;
        public override void OnPreviewGUI(Rect rect, GUIStyle background) { Prepare(); playerEditor?.OnPreviewGUI(rect, background); }
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) { Prepare(); playerEditor?.OnInteractivePreviewGUI(rect, background); }
        private void OnDisable() { if (playerEditor != null) DestroyImmediate(playerEditor); }
    }
}
