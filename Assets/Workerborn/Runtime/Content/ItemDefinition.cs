using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Item", fileName = "Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea(2, 6)] private string description;
        [SerializeField] private Sprite icon;
        [SerializeField] private GameObject worldPrefab;
        [SerializeField, Range(1, 8)] private int width = 1;
        [SerializeField, Range(1, 8)] private int height = 1;
        [SerializeField, Min(1)] private int maxStack = 20;
        [SerializeField, Min(0)] private int price;
        [SerializeField] private EquipmentSlot equipmentSlot;
        [SerializeField] private CharacterPartDefinition equipmentPart;
        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public GameObject WorldPrefab => worldPrefab;
        public EquipmentSlot EquipmentSlot => equipmentSlot;
        public CharacterPartDefinition EquipmentPart => equipmentPart;
        public ItemSpec ToSpec() => new ItemSpec(id, displayName, description, width, height, maxStack, price, equipmentSlot);
    }
}
