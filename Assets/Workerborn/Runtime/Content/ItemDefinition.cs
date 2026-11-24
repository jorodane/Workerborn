using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Item", fileName = "Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField]
        private string id;
        [SerializeField]
        private string displayName;
        [SerializeField, TextArea(2, 6)]
        private string description;
        [SerializeField]
        private Sprite icon;
        [SerializeField, Tooltip("Source model. Edit the complete ground prefab to change its scene appearance and collider.")]
        private GameObject worldPrefab;
        [SerializeField, Range(1, VillageState.InventoryColumns)]
        private int width = 1;
        [SerializeField, Range(1, 8)]
        private int height = 1;
        [SerializeField, Min(1)]
        private int maxStack = 20;
        [SerializeField, Min(0)]
        private int price;
        [SerializeField, Min(0), Tooltip("Health restored when one of this item is used.")]
        private int healthRestore;
        [SerializeField] private bool teachesSkill;
        [SerializeField] private SkillId taughtSkill;
        [SerializeField, Tooltip("Ask for confirmation before selling this stack with Ctrl-click.")]
        private bool confirmQuickSale;
        [SerializeField]
        private EquipmentSlot equipmentSlot;
        [SerializeField]
        private CharacterPartDefinition equipmentPart;
        [SerializeField, Tooltip("Prefab for scene placement and the source of meshes, materials and pose for pooled drops.")]
        private GroundPickup groundPrefab;
        public GroundPickup GroundPrefab => groundPrefab;
        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public bool ConfirmQuickSale => confirmQuickSale;
        public GameObject WorldPrefab => worldPrefab;
        public EquipmentSlot EquipmentSlot => equipmentSlot;
        public CharacterPartDefinition EquipmentPart => equipmentPart;

        public ItemSpec ToSpec() => new ItemSpec(id, displayName, description, Mathf.Min(width, VillageState.InventoryColumns),
            height, maxStack, price, equipmentSlot, healthRestore, teachesSkill ? taughtSkill : (SkillId?)null);
    }
}
