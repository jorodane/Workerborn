using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Workerborn.Editor
{
    internal static class InspectorTable
    {
        public static Rect Cell(Rect row, int column, params float[] widths)
        {
            float x = row.x;
            for (int i = 0; i < column; i++)
                x += widths[i] * row.width;
            return new Rect(x + 2, row.y, Mathf.Max(1, widths[column] * row.width - 5), EditorGUIUtility.singleLineHeight);
        }
    }

    [CustomEditor(typeof(VillageEffectLibrary))]
    public sealed class VillageEffectLibraryEditor : UnityEditor.Editor
    {
        private static readonly float[] widths = { .25f, .35f, .19f, .21f };
        private ReorderableList table;

        private void OnEnable()
        {
            var rows = serializedObject.FindProperty("effects");
            table = new ReorderableList(serializedObject, rows, true, true, true, true);
            table.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(InspectorTable.Cell(rect, 0, widths), "이펙트");
                EditorGUI.LabelField(InspectorTable.Cell(rect, 1, widths), "프리팹");
                EditorGUI.LabelField(InspectorTable.Cell(rect, 2, widths), "사전 생성");
                EditorGUI.LabelField(InspectorTable.Cell(rect, 3, widths), "지속(초)");
            };
            table.drawElementCallback = (rect, index, active, focused) =>
            {
                var entry = rows.GetArrayElementAtIndex(index);
                var effect = entry.FindPropertyRelative("effect");
                rect.y += 1;
                EditorGUI.PropertyField(InspectorTable.Cell(rect, 0, widths), effect, GUIContent.none);
                EditorGUI.PropertyField(InspectorTable.Cell(rect, 1, widths), entry.FindPropertyRelative("prefab"), GUIContent.none);
                EditorGUI.PropertyField(InspectorTable.Cell(rect, 2, widths), entry.FindPropertyRelative("prewarm"), GUIContent.none);
                using (new EditorGUI.DisabledScope(VillageEffectLibrary.UsesTrail((VillageEffectKind)effect.enumValueIndex)))
                    EditorGUI.PropertyField(InspectorTable.Cell(rect, 3, widths), entry.FindPropertyRelative("lifetime"), GUIContent.none);
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            table.DoLayoutList();
            serializedObject.ApplyModifiedProperties();

            var seen = new HashSet<VillageEffectKind>();
            foreach (var entry in ((VillageEffectLibrary)target).Effects)
            {
                if (!seen.Add(entry.effect))
                {
                    EditorGUILayout.HelpBox("이펙트 종류마다 한 행을 사용하세요. 중복이면 처음 지정된 프리팹을 사용합니다.", MessageType.Warning);
                    break;
                }
            }
            EditorGUILayout.HelpBox("드롭·획득 트레일은 루트에 LootFlightTrail이 있는 프리팹을 지정하세요.", MessageType.Info);
        }
    }

    [CustomEditor(typeof(SkillDefinition))]
    public sealed class SkillDefinitionEditor : UnityEditor.Editor
    {
        private static readonly float[] widths = { .14f, .14f, .37f, .15f, .20f };
        private static readonly string[] columns = { "시작", "마지막", "효과", "수치", "랭크당 +" };
        private static readonly string[] fields = { "fromRank", "throughRank", "effect", "amount", "increasePerRank" };
        private static readonly string[] effectNames =
        {
            "획득 배율 보너스", "스태미나 절약", "최대 스태미나", "맨손 피해", "럭키 확률", "대박 확률"
        };
        private ReorderableList table;

        private void OnEnable()
        {
            var rows = serializedObject.FindProperty("rankEffects");
            table = new ReorderableList(serializedObject, rows, true, true, true, true);
            table.drawHeaderCallback = rect =>
            {
                for (int i = 0; i < columns.Length; i++)
                    EditorGUI.LabelField(InspectorTable.Cell(rect, i, widths), columns[i]);
            };
            table.drawElementCallback = (rect, index, active, focused) =>
            {
                var entry = rows.GetArrayElementAtIndex(index);
                rect.y += 1;
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = entry.FindPropertyRelative(fields[i]);
                    var cell = InspectorTable.Cell(rect, i, widths);
                    if (i < 2)
                        field.intValue = Mathf.Clamp(EditorGUI.IntField(cell, field.intValue), 0, SkillProgression.MaxRank);
                    else if (i == 2)
                        field.enumValueIndex = EditorGUI.Popup(cell, field.enumValueIndex, effectNames);
                    else
                        EditorGUI.PropertyField(cell, field, GUIContent.none);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "rankEffects");
            table.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("0은 연습입니다. 범위 안에서 도달한 각 랭크의 수치를 합산합니다. 럭키 확률 수치는 %p 단위입니다.", MessageType.Info);
        }
    }
}
