using System;

namespace Workerborn
{
    public static class ItemIds
    {
        public const string Potato = "potato";
        public const string Bread = "bread";
        public const string Hoe = "iron_hoe";
        public const string Bag = "small_bag";
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
        public ItemSpec(string id, string name, string description, int width, int height, int maxStack, int price)
        {
            Id = id; Name = name; Description = description;
            Width = width; Height = height; MaxStack = maxStack; Price = price;
        }
    }

    public static class ItemCatalog
    {
        private static readonly ItemSpec[] items =
        {
            new ItemSpec(ItemIds.Potato, "감자", "직접 수확한 감자. 농부에게 납품하거나 잡화점에서 개당 3G에 팔 수 있습니다.", 1, 1, 20, 3),
            new ItemSpec(ItemIds.Bread, "갓 구운 빵", "먹으면 에너지를 25 회복합니다. 인벤토리에서 우클릭하거나 사용 버튼을 누르세요.", 1, 1, 10, 10),
            new ItemSpec(ItemIds.Hoe, "튼튼한 괭이", "소지하면 수확 속도 +25%, 에너지 소모 -1. 현재 생애에 사용하는 도구입니다.", 1, 3, 1, 60),
            new ItemSpec(ItemIds.Bag, "작은 여행 가방", "소지품 공간이 2줄 늘어납니다. 가방도 환생하면 사라집니다.", 2, 2, 1, 100)
        };

        public static ItemSpec Get(string id)
        {
            foreach (var item in items) if (item.Id == id) return item;
            throw new ArgumentException("Unknown item: " + id, nameof(id));
        }

        public static bool Contains(string id)
        {
            foreach (var item in items) if (item.Id == id) return true;
            return false;
        }
    }
}
