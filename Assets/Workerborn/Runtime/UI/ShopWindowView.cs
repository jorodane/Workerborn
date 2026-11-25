using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ShopWindowView : VillageWindowView
    {
        [SerializeField]
        private RectTransform stock;
        [SerializeField]
        private GridLayoutGroup cells;
        [SerializeField]
        private TMP_Text description;
        [SerializeField]
        private TMP_Text funds;
        [SerializeField]
        private ShopSellTarget sellTarget;
        private VillageInteractable merchant;
        internal VillageInteractable Merchant => merchant;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            sellTarget.Hud = hud;
        }

        public void Open(VillageInteractable npc)
        {
            merchant = npc;
            description.text = string.Empty;
            Refresh();
        }

        public override void Refresh()
        {
            funds.text = $"소지금  {Game.State.Gold:N0} Gold";
            if (merchant == null || merchant.Definition == null)
                return;
            InventoryWindowView.Clear(stock);
            var items = merchant.Definition.ShopItems;
            var layout = new GridInventory(8, 8);
            if (items == null)
                return;
            foreach (var definition in items)
            {
                if (definition == null || !layout.TryAdd(definition.Id, 1))
                    continue;
                var placed = layout.Entries[layout.Entries.Count - 1];
                var item = Instantiate(Hud.Library.Item, stock);
                item.Display(definition, definition.Id, 1);
                var size = cells.cellSize + cells.spacing;
                item.Rect.sizeDelta = new Vector2(placed.Spec.Width * size.x - 2, placed.Spec.Height * size.y - 2);
                item.Rect.anchoredPosition = new Vector2(placed.X * size.x, -placed.Y * size.y);
                string id = definition.Id;
                item.Shop(Hud, () =>
                {
                    description.text = definition.DisplayName + "  ·  " + definition.ToSpec().Price + " Gold";
                    Hud.ClickShopItem(id, WorkerbornInput.Point);
                });
            }
        }
    }
}
