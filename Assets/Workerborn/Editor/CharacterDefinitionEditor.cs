using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Workerborn.Editor
{
    [CustomEditor(typeof(CharacterDefinition))]
    public sealed class CharacterDefinitionEditor : UnityEditor.Editor
    {
        private PreviewRenderUtility preview;
        private GameObject model;
        private Vector2 orbit = new Vector2(160f, 5f);
        private float distance = 3.2f;
        private bool dirty = true;
        private int seenRevision = -1;
        private CharacterPartDefinition[] partLibrary;
        private void OnEnable()
        {
            EditorApplication.projectChanged += InvalidateLibrary;
        }

        private void InvalidateLibrary()
        {
            partLibrary = null;
            dirty = true;
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("basePrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("height"));
            DrawPart("hair", "머리", CharacterPartSlot.Hair);
            DrawPart("outfit", "의상", CharacterPartSlot.Outfit);
            DrawPart("eyes", "눈", CharacterPartSlot.Eyes);
            DrawPart("eyebrows", "눈썹", CharacterPartSlot.Eyebrows);
            DrawPart("mouth", "입", CharacterPartSlot.Mouth);
            DrawPart("headwear", "머리 장식", CharacterPartSlot.Headwear);
            DrawPart("rightHand", "오른손", CharacterPartSlot.RightHand);
            DrawPart("leftHand", "왼손", CharacterPartSlot.LeftHand);
            foreach (string field in new[]
            {
                "toonPalette",
                "hideBaseWeapons",
                "hideBaseHair",
                "skinTint",
                "clothingTint",
                "idle",
                "walk",
                "run",
                "work"
            }

            )
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            serializedObject.ApplyModifiedProperties();
            if (EditorGUI.EndChangeCheck())
            {
                dirty = true;
                CharacterEditorRefresh.RefreshActors();
            }

            EditorGUILayout.HelpBox("NPC와 주인공이 같은 외형 에셋을 사용합니다. 파츠가 비어 있으면 기본 프리팹의 해당 파츠를 유지합니다. 아래 미리보기를 드래그해 회전하고 휠로 확대할 수 있습니다.", MessageType.Info);
            if (GUILayout.Button("Scene의 이 외형 갱신"))
                CharacterEditorRefresh.RefreshActors();
        }

        private void DrawPart(string propertyName, string label, CharacterPartSlot slot)
        {
            if (partLibrary == null)
            {
                var parts = new System.Collections.Generic.List<CharacterPartDefinition>();
                foreach (string id in AssetDatabase.FindAssets("t:CharacterPartDefinition"))
                    parts.Add(AssetDatabase.LoadAssetAtPath<CharacterPartDefinition>(AssetDatabase.GUIDToAssetPath(id)));
                parts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                partLibrary = parts.ToArray();
            }

            var property = serializedObject.FindProperty(propertyName);
            var choices = new System.Collections.Generic.List<CharacterPartDefinition>
            {
                null
            };
            var names = new System.Collections.Generic.List<string>
            {
                "기본 프리팹 유지"
            };
            foreach (var part in partLibrary)
                if (part != null && part.Slot == slot)
                {
                    choices.Add(part);
                    names.Add(part.name);
                }

            var current = (CharacterPartDefinition)property.objectReferenceValue;
            if (current != null && !choices.Contains(current))
            {
                choices.Add(current);
                names.Add(current.name + " (슬롯 확인)");
            }

            EditorGUILayout.BeginHorizontal();
            int selected = EditorGUILayout.Popup(label, choices.IndexOf(current), names.ToArray());
            if (selected >= 0)
                property.objectReferenceValue = choices[selected];
            using (new EditorGUI.DisabledScope(property.objectReferenceValue == null))
                if (GUILayout.Button("편집", GUILayout.Width(42)))
                    Selection.activeObject = property.objectReferenceValue;
            EditorGUILayout.EndHorizontal();
        }

        public override bool HasPreviewGUI() => true;
        public override bool RequiresConstantRepaint() => true;
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) => OnPreviewGUI(rect, background);
        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            var definition = (CharacterDefinition)target;
            if (seenRevision != CharacterEditorRefresh.Revision)
            {
                seenRevision = CharacterEditorRefresh.Revision;
                dirty = true;
            }

            if (definition.BasePrefab == null)
            {
                GUI.Label(rect, "기본 캐릭터 프리팹을 지정하세요.");
                return;
            }

            if (preview == null)
            {
                preview = new PreviewRenderUtility();
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = 30f;
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(0.18f, 0.2f, 0.23f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 30f, 0f);
                preview.lights[1].intensity = 0.8f;
            }

            if (dirty || model == null)
            {
                if (model != null)
                    DestroyImmediate(model);
                model = CharacterAssembler.Build(definition, null);
                model.hideFlags = HideFlags.HideAndDontSave;
                preview.AddSingleGO(model);
                if (definition.Idle != null)
                    definition.Idle.SampleAnimation(model.transform.GetChild(0).gameObject, 0f);
                dirty = false;
            }

            var current = Event.current;
            if (rect.Contains(current.mousePosition))
            {
                if (current.type == EventType.MouseDrag && current.button == 0)
                {
                    orbit += current.delta;
                    current.Use();
                    Repaint();
                }

                if (current.type == EventType.ScrollWheel)
                {
                    distance = Mathf.Clamp(distance + current.delta.y * 0.12f, 1.3f, 8f);
                    current.Use();
                    Repaint();
                }
            }

            if (current.type != EventType.Repaint)
                return;
            preview.BeginPreview(rect, background);
            Vector3 focus = Vector3.up * (definition.Height * 0.5f);
            Quaternion rotation = Quaternion.Euler(Mathf.Clamp(orbit.y, -60f, 70f), orbit.x, 0f);
            preview.camera.transform.SetPositionAndRotation(focus + rotation * Vector3.back * distance, rotation);
            preview.Render(true);
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateLibrary;
            preview?.Cleanup();
            preview = null;
            model = null;
        }
    }

    [CustomEditor(typeof(CharacterPartDefinition))]
    public sealed class CharacterPartDefinitionEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor prefabPreview;
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                CharacterEditorRefresh.RefreshActors();
        }

        public override bool HasPreviewGUI() => ((CharacterPartDefinition)target).Prefab != null;
        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            var prefab = ((CharacterPartDefinition)target).Prefab;
            if (prefab == null)
                return;
            CreateCachedEditor(prefab, null, ref prefabPreview);
            prefabPreview.OnInteractivePreviewGUI(rect, background);
        }

        private void OnDisable()
        {
            if (prefabPreview != null)
                DestroyImmediate(prefabPreview);
        }
    }

    [CustomEditor(typeof(CharacterActor))]
    public sealed class CharacterActorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            if (GUILayout.Button("외형을 Scene에 반영") || changed)
            {
                var actor = (CharacterActor)target;
                Undo.RegisterFullObjectHierarchyUndo(actor.gameObject, "Change character appearance");
                actor.RebuildAppearance();
                EditorSceneManager.MarkSceneDirty(actor.gameObject.scene);
            }

            if (((CharacterActor)target).Appearance != null && GUILayout.Button("외형 에셋 선택"))
                Selection.activeObject = ((CharacterActor)target).Appearance;
        }
    }

    internal static class CharacterEditorRefresh
    {
        private static bool queued;
        public static int Revision { get; private set; }

        public static void RefreshActors()
        {
            if (queued || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            Revision++;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                foreach (var actor in Resources.FindObjectsOfTypeAll<CharacterActor>())
                {
                    if (EditorUtility.IsPersistent(actor) || !actor.gameObject.scene.IsValid() || actor.Appearance == null)
                        continue;
                    Undo.RegisterFullObjectHierarchyUndo(actor.gameObject, "Refresh character appearance");
                    actor.RebuildAppearance();
                    EditorSceneManager.MarkSceneDirty(actor.gameObject.scene);
                }
            };
        }
    }
}
