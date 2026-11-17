using System;
using System.Collections.Generic;

namespace Workerborn
{
    public enum EquipmentSlot { None, Head, Body, Hands, Feet, MainHand, OffHand, Accessory }

    [Serializable]
    public sealed class EquipmentEntrySave
    {
        public EquipmentSlot slot;
        public string itemId;
    }

    public static class ItemIds
    {
        public const string Potato = "potato";
        public const string Bread = "bread";
        public const string Hoe = "iron_hoe";
        public const string Bag = "small_bag";
        public const string Gold = "gold";
    }

    public sealed class ItemSpec
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaxStack { get; }
        public int Price { get; }
        public EquipmentSlot EquipmentSlot { get; }
        public ItemSpec(string id, string name, string description, int width, int height, int maxStack, int price, EquipmentSlot equipmentSlot = EquipmentSlot.None)
        {
            Id = id; Name = name; Description = description;
            Width = width; Height = height; MaxStack = maxStack; Price = price;
            EquipmentSlot = equipmentSlot;
        }
    }

    public static class ItemCatalog
    {
        private static readonly ItemSpec[] items =
        {
            new ItemSpec(ItemIds.Potato, "감자", "직접 수확한 감자. 농부에게 납품하거나 잡화점에서 개당 3G에 팔 수 있습니다.", 1, 1, 20, 3),
            new ItemSpec(ItemIds.Bread, "갓 구운 빵", "먹으면 에너지를 25 회복합니다. 인벤토리에서 우클릭하거나 사용 버튼을 누르세요.", 1, 1, 10, 10),
            new ItemSpec(ItemIds.Hoe, "튼튼한 괭이", "손에 장착하면 수확 속도 +25%, 에너지 소모 -1.", 1, 3, 1, 60, EquipmentSlot.MainHand),
            new ItemSpec(ItemIds.Bag, "작은 여행 가방", "소지품 공간이 2줄 늘어납니다. 가방도 환생하면 사라집니다.", 2, 2, 1, 100),
            new ItemSpec(ItemIds.Gold, "골드", "물건을 사고 일의 대가로 받는 금화.", 1, 1, 1000, 1)
        };
        private static readonly Dictionary<string, ItemSpec> configured = new Dictionary<string, ItemSpec>();

        public static void Configure(IEnumerable<ItemSpec> definitions)
        {
            configured.Clear();
            foreach (var item in definitions)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || item.Width < 1 || item.Height < 1 || item.MaxStack < 1 || !configured.TryAdd(item.Id, item))
                    throw new ArgumentException("Invalid or duplicate item definition: " + item.Id);
            }
        }

        public static void ResetDefaults() => configured.Clear();

        public static ItemSpec Get(string id)
        {
            if (id != null && configured.TryGetValue(id, out var configuredItem)) return configuredItem;
            foreach (var item in items) if (item.Id == id) return item;
            throw new ArgumentException("Unknown item: " + id, nameof(id));
        }

        public static bool Contains(string id)
        {
            if (id != null && configured.ContainsKey(id)) return true;
            foreach (var item in items) if (item.Id == id) return true;
            return false;
        }
    }
}
