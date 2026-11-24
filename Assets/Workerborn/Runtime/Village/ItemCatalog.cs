using System;
using System.Collections.Generic;

namespace Workerborn
{
    public enum EquipmentSlot { None, Head, Body, Hands, Feet, MainHand, OffHand, Accessory, Legs, Bag }

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
        public const string HealthPotion = "health_potion";
        public const string Hoe = "iron_hoe";
        public const string Bag = "small_bag";
        public const string Gold = "gold";
        public const string Egg = "egg";
        public const string Timber = "timber";
        public const string Axe = "woodcutting_axe";
        public const string LegendAxe = "legend_axe";
        public const string TimberContract = "timber_contract";
        public const string FarmContract = "farm_contract";
        public const string FishingRod = "fishing_rod";
        public const string Fish = "pond_fish";
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
        public int HealthRestore { get; }
        public ItemSpec(string id, string name, string description, int width, int height, int maxStack, int price,
            EquipmentSlot equipmentSlot = EquipmentSlot.None, int healthRestore = 0)
        {
            Id = id; Name = name; Description = description;
            Width = width; Height = height; MaxStack = maxStack; Price = price;
            EquipmentSlot = equipmentSlot;
            HealthRestore = Math.Max(0, healthRestore);
        }
    }

    public static class ItemCatalog
    {
        private static readonly ItemSpec[] items =
        {
            new ItemSpec(ItemIds.Potato, "감자", "직접 수확한 감자. 농부에게 납품하거나 잡화점에서 개당 3G에 팔 수 있습니다.", 1, 1, 20, 3),
            new ItemSpec(ItemIds.Bread, "갓 구운 빵", "먹으면 에너지를 25 회복합니다. 인벤토리에서 우클릭하거나 사용 버튼을 누르세요.", 1, 1, 10, 10),
            new ItemSpec(ItemIds.HealthPotion, "생명력 포션", "마시면 생명력을 회복합니다.", 1, 1, 10, 15, healthRestore: 30),
            new ItemSpec(ItemIds.Hoe, "튼튼한 괭이", "손에 장착하면 밭에서 작업할 때 스태미나 소모가 1 줄어듭니다.", 1, 3, 1, 60, EquipmentSlot.MainHand),
            new ItemSpec(ItemIds.Bag, "작은 여행 가방", "장착하면 소지품 공간이 2줄 늘어납니다.", 2, 2, 1, 100, EquipmentSlot.Bag),
            new ItemSpec(ItemIds.Gold, "골드", "물건을 사고 일의 대가로 받는 금화.", 1, 1, 1000, 1),
            new ItemSpec(ItemIds.Egg, "달걀", "암탉 주변에서 맨손으로 모을 수 있습니다.", 1, 1, 999, 4),
            new ItemSpec(ItemIds.Timber, "목재", "도끼로 나무를 베어 얻는 자재입니다.", 1, 1, 999, 3),
            new ItemSpec(ItemIds.Axe, "나무꾼 도끼", "주무기에 장착해서 나무를 벱니다.", 1, 2, 1, 12, EquipmentSlot.MainHand),
            new ItemSpec(ItemIds.LegendAxe, "전설의 나무꾼 도끼", "전설의 시련을 통과한 나무꾼에게 주는 도끼입니다.", 1, 2, 1, 12, EquipmentSlot.MainHand),
            new ItemSpec(ItemIds.TimberContract, "목재 납품 계약서", "클릭하면 농장 건설에 필요한 납품 내용을 펼칩니다.", 1, 1, 1, 0),
            new ItemSpec(ItemIds.FarmContract, "농장 아르바이트 계약서", "클릭하면 납품 시간, 요구량과 보수를 펼칩니다.", 1, 1, 1, 0),
            new ItemSpec(ItemIds.FishingRod, "나무 낚싯대", "주무기에 장착하면 낚시터에서 낚시할 수 있습니다.", 1, 3, 1, 12, EquipmentSlot.MainHand),
            new ItemSpec(ItemIds.Fish, "연못 물고기", "연못에서 낚은 작은 물고기입니다.", 1, 1, 20, 5)
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
