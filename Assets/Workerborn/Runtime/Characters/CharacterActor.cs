using UnityEngine;

namespace Workerborn
{
    [ExecuteAlways]
    public sealed class CharacterActor : MonoBehaviour
    {
        [SerializeField]
        private CharacterDefinition appearance;
        [SerializeField]
        private NpcDefinition npc;
        [SerializeField]
        private VillageSettings playerSettings;
        [SerializeField]
        private Transform visualRoot;
        [SerializeField, Range(0, 31)]
        private int renderLayer = 2;
        public CharacterDefinition Appearance => npc != null ? npc.Appearance : playerSettings != null ? playerSettings.Player : appearance;
        public Transform VisualRoot => visualRoot;
        public Animator Animator => visualRoot != null ? visualRoot.GetComponentInChildren<Animator>() : null;

        [SerializeField, HideInInspector]
        private string appliedAppearanceHash;
        public string AppliedAppearanceHash => appliedAppearanceHash;

        private GameObject equippedVisual;
        private string equippedItem;
        public void ShowEquipment(string itemId, CharacterPartDefinition part)
        {
            if (equippedItem == itemId)
                return;
            equippedItem = itemId;
            if (equippedVisual != null)
            {
                equippedVisual.SetActive(false);
                Destroy(equippedVisual);
            }

            equippedVisual = null;
            if (part == null || visualRoot == null || visualRoot.childCount == 0)
                return;
            equippedVisual = CharacterAssembler.AttachPart(part, visualRoot.GetChild(0));
            if (equippedVisual != null)
                foreach (var child in equippedVisual.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = renderLayer;
        }

        public void SetPortraitLayer(bool portrait)
        {
            if (visualRoot != null)
                foreach (var child in visualRoot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = portrait ? 10 : renderLayer;
        }

        private void OnEnable()
        {
            Appearance?.ToonPalette?.Apply(visualRoot);
            CharacterAssembler.ApplyTints(visualRoot, Appearance);
            if (visualRoot != null)
                foreach (var child in visualRoot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = renderLayer;
        }

        public void RebuildAppearance()
        {
            if (Appearance == null || Appearance.BasePrefab == null)
                return;
            var previous = visualRoot;
            var next = CharacterAssembler.Build(Appearance, transform);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RegisterCreatedObjectUndo(next, "Build character appearance");
                UnityEditor.Undo.RecordObject(this, "Assign character appearance");
            }

#endif
            visualRoot = next.transform;
            OnEnable();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                appliedAppearanceHash = UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(Appearance)).ToString();
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            }

#endif
            if (previous == null)
                return;
            if (Application.isPlaying)
            {
                previous.gameObject.SetActive(false);
                Destroy(previous.gameObject);
            }
            else
            {
#if UNITY_EDITOR
                UnityEditor.Undo.DestroyObjectImmediate(previous.gameObject);
#else
                DestroyImmediate(previous.gameObject);
#endif
            }
        }
    }
}
