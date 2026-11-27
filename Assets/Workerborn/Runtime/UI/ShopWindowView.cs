using System.Collections.Generic;
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
        private RectTransform viewport;
        [SerializeField]
        private ScrollRect scroll;
        [SerializeField]
        private TMP_Text description;
        [SerializeField]
        private TMP_Text funds;
        [SerializeField]
        private ShopSellTarget sellTarget;
        private VillageInteractable merchant;
        private readonly List<(UiItemView Item, InventoryEntry Placement)> tiles = new List<(UiItemView, InventoryEntry)>();
        private float laidOutWidth;
        private const int Columns = 8;
        internal VillageInteractable Merchant => merchant;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            sellTarget.Hud = hud;
        }

        public void Open(VillageInteractable npc)
        {
            description.text = string.Empty;
            if (merchant != npc)
            {
                merchant = npc;
                BuildStock();
            }
            Refresh();
            LayoutStock();
            scroll.verticalNormalizedPosition = 1f;
        }

        internal void Embed(RectTransform socket)
        {
            Rect.SetParent(socket, false);
            Rect.anchorMin = Vector2.zero;
            Rect.anchorMax = Vector2.one;
            Rect.offsetMin = Rect.offsetMax = Vector2.zero;
        }

        public override void Refresh() => funds.text = $"소지금  {Game.State.Gold:N0} Gold";

        private void BuildStock()
        {
            laidOutWidth = -1f;
            tiles.Clear();
            InventoryWindowView.Clear(stock);
            var items = merchant?.Definition?.ShopItems;
            if (items == null)
                return;
            var layout = new GridInventory(Columns, 8);
            foreach (var definition in items)
            {
                if (definition == null || layout.Count(definition.Id) > 0 || !layout.TryAdd(definition.Id, 1))
                    continue;
                var placed = layout.Entries[layout.Entries.Count - 1];
                var item = Instantiate(Hud.Library.Item, stock);
                item.Display(definition, definition.Id, 1);
                tiles.Add((item, placed));
                string id = definition.Id;
                item.Shop(Hud, () =>
                {
                    description.text = definition.DisplayName + "  ·  " + definition.ToSpec().Price + " Gold";
                    Hud.ClickShopItem(id, WorkerbornInput.Point);
                });
            }
        }

        private void LateUpdate() => LayoutStock();

        private void LayoutStock()
        {
            float width = viewport.rect.width;
            if (Mathf.Approximately(laidOutWidth, width))
                return;
            laidOutWidth = width;
            float size = Mathf.Clamp(Mathf.Floor((width - 4f) / Columns), 3f, 43f);
            int rows = 0;
            foreach (var (view, entry) in tiles)
            {
                view.Rect.sizeDelta = new Vector2(entry.Spec.Width * size - 2f, entry.Spec.Height * size - 2f);
                view.Rect.anchoredPosition = new Vector2(entry.X * size, -entry.Y * size);
                rows = Mathf.Max(rows, entry.Y + entry.Spec.Height);
            }
            stock.sizeDelta = new Vector2(Columns * size, rows * size);
        }
    }
}
