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
        [SerializeField, Min(1), Tooltip("Starting and maximum durability for equippable items.")]
        private int maxDurability = 100;
        [SerializeField]
        private CharacterPartDefinition equipmentPart;
        [SerializeField, Tooltip("Prefab for scene placement and the source of meshes, materials and pose for pooled drops.")]
        private GroundPickup groundPrefab;
        [Header("Food")]
        [SerializeField, Min(0), Tooltip("Fullness restored per serving. A zero value on bread uses the village nutrition settings.")]
        private int foodFullness;
        [SerializeField, Min(0f)] private float foodNourishment;
        [SerializeField, Tooltip("Part shown in the right hand while eating.")]
        private CharacterPartDefinition eatingPart;
        public GroundPickup GroundPrefab => groundPrefab;
        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public bool ConfirmQuickSale => confirmQuickSale;
        public GameObject WorldPrefab => worldPrefab;
        public EquipmentSlot EquipmentSlot => equipmentSlot;
        public CharacterPartDefinition EquipmentPart => equipmentPart;
        internal CharacterPartDefinition EatingPart => eatingPart;

        public ItemSpec ToSpec() => new ItemSpec(id, displayName, description, Mathf.Min(width, VillageState.InventoryColumns),
            height, maxStack, price, equipmentSlot, healthRestore, teachesSkill ? taughtSkill : (SkillId?)null, maxDurability,
            foodFullness, foodNourishment);
    }
}
