using System.Collections.Generic;
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
        private CharacterDefinition trialAppearance = null;
        private CharacterLook? customLook;
        public CharacterDefinition Appearance => trialAppearance != null ? trialAppearance :
            npc != null ? npc.Appearance : playerSettings != null ? playerSettings.Player : appearance;
        public Transform VisualRoot => visualRoot;
        public Vector3 NamePosition => transform.position + Vector3.up * ((Appearance?.Height ?? 1.7f) + .21f);
        private CharacterLook CurrentLook => customLook ?? Appearance.DefaultLook;

        internal void ApplyProfileLook(CharacterLook look)
        {
            customLook = look.Validated();
            if (Appearance?.UsesP09 == true && visualRoot != null)
                P09Wardrobe.Apply(visualRoot, CurrentLook);
        }
        public Animator Animator => visualRoot != null ? visualRoot.GetComponentInChildren<Animator>() : null;

        [SerializeField, HideInInspector]
        private string appliedAppearanceHash;
        public string AppliedAppearanceHash => appliedAppearanceHash;

        private sealed class WornPart
        {
            public string Id;
            public GameObject Visual;
            public readonly List<CharacterGarment> HiddenGarments = new List<CharacterGarment>();
            public readonly List<Renderer> HiddenHair = new List<Renderer>();
        }
        private readonly Dictionary<EquipmentSlot, WornPart> worn = new Dictionary<EquipmentSlot, WornPart>();
        private readonly List<EquipmentSlot> changed = new List<EquipmentSlot>();

        public void ShowEquipment(IReadOnlyDictionary<EquipmentSlot, string> equipment, VillageSettings settings)
        {
            if (visualRoot == null || visualRoot.childCount == 0)
                return;
            var rig = visualRoot.GetChild(0);
            bool changedAppearance = false;
            bool p09 = Appearance?.UsesP09 == true;
            changed.Clear();
            foreach (var entry in worn)
            {
                if (equipment.TryGetValue(entry.Key, out var id) && entry.Value.Id == id)
                    continue;
                var from = entry.Value;
                if (from.Visual != null)
                {
                    from.Visual.SetActive(false);
                    Destroy(from.Visual);
                }
                foreach (var garment in from.HiddenGarments)
                    if (garment != null)
                        garment.gameObject.SetActive(true);
                foreach (var hair in from.HiddenHair)
                    if (hair != null)
                        hair.enabled = true;
                changed.Add(entry.Key);
                changedAppearance = true;
            }
            foreach (var slot in changed)
                worn.Remove(slot);

            foreach (var entry in equipment)
            {
                if (worn.ContainsKey(entry.Key))
                    continue;
                var part = settings.FindItem(entry.Value)?.EquipmentPart;
                if (part == null)
                    continue;
                if (p09 && part.P09ArmorSet > 0)
                {
                    worn.Add(entry.Key, new WornPart { Id = entry.Value });
                    changedAppearance = true;
                    continue;
                }
                var defaults = rig.GetComponentsInChildren<CharacterGarment>();
                var partVisual = CharacterAssembler.AttachPart(part, rig, appearance: Appearance, optional: p09 || trialAppearance != null);
                if (partVisual == null)
                {
                    if (p09 || trialAppearance != null)
                        worn.Add(entry.Key, new WornPart { Id = entry.Value });
                    continue;
                }
                var applied = new WornPart { Id = entry.Value, Visual = partVisual };
                foreach (var garment in defaults)
                    if (garment.Slot == entry.Key)
                    {
                        applied.HiddenGarments.Add(garment);
                        garment.gameObject.SetActive(false);
                    }
                if (part.HideHair)
                    foreach (var renderer in rig.GetComponentsInChildren<Renderer>())
                        if (renderer.enabled && CharacterAssembler.IsHair(renderer.name))
                        {
                            applied.HiddenHair.Add(renderer);
                            renderer.enabled = false;
                        }
                foreach (var child in partVisual.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = renderLayer;
                worn.Add(entry.Key, applied);
                changedAppearance = true;
            }
            if (changedAppearance)
            {
                if (p09)
                    P09Wardrobe.Apply(rig, CurrentLook, equipment, settings);
                CharacterGarment.RefreshSkin(rig);
            }
        }

        public void SetPortraitLayer(bool portrait)
        {
            if (visualRoot != null)
                foreach (var child in visualRoot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = portrait ? 10 : renderLayer;
        }

        private void OnEnable()
        {
            if (Appearance?.UsesP09 == true && visualRoot != null)
                P09Wardrobe.Apply(visualRoot, CurrentLook);
            Appearance?.ToonPalette?.Apply(visualRoot);
            CharacterAssembler.ApplyTints(visualRoot, Appearance);
            if (visualRoot != null)
                foreach (var child in visualRoot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = renderLayer;
            CharacterGarment.RefreshSkin(visualRoot);
        }

#if UNITY_EDITOR
        internal bool TryUseTrialAppearance(CharacterDefinition definition)
        {
            var avatar = definition != null && definition.BasePrefab != null
                ? definition.BasePrefab.GetComponentInChildren<Animator>(true)?.avatar
                : null;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                return false;
            trialAppearance = definition;
            RebuildAppearance();
            return true;
        }
#endif

        public void RebuildAppearance()
        {
            if (Appearance == null || Appearance.BasePrefab == null)
                return;
            var previous = visualRoot;
            var next = CharacterAssembler.Build(Appearance, transform, customLook);
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
