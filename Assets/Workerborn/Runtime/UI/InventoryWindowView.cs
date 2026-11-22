using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class InventoryWindowView : VillageWindowView
    {
        [SerializeField]
        private RectTransform scrollContent;
        [SerializeField]
        private RectTransform itemLayer;
        [SerializeField]
        private RectTransform cellLayer;
        [SerializeField]
        private RectTransform itemViewport;
        [SerializeField]
        private Image placementPreview;
        [SerializeField]
        private GridLayoutGroup gridLayout;
        [SerializeField]
        private RawImage portrait;
        [SerializeField]
        private TMP_Text gold;
        [SerializeField]
        private EquipmentSlotPointer[] equipment;
        [SerializeField]
        private RectTransform[] equipmentIcons;
        private UiItemView[] equippedItems;
        public RectTransform ItemLayer => itemLayer;
        public RectTransform ItemViewport => itemViewport;
        public Vector2 CellSize => gridLayout.cellSize + gridLayout.spacing;

        public void ShowPlacement(int x, int y, int width, int height)
        {
            if (placementPreview == null)
                return;
            placementPreview.gameObject.SetActive(width > 0 && height > 0);
            if (width < 1 || height < 1)
                return;
            Vector2 step = CellSize;
            placementPreview.rectTransform.anchoredPosition = new Vector2(x * step.x, -y * step.y);
            placementPreview.rectTransform.sizeDelta = new Vector2(width * step.x, height * step.y);
        }

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            portrait.texture = game.PortraitTexture;
            equippedItems = new UiItemView[equipment.Length];
            foreach (var slot in equipment)
                slot.Hud = hud;
            itemLayer.GetComponent<InventoryGridPointer>().Hud = hud;
        }

        public override void Refresh()
        {
            var inventory = Game.State.Inventory;
            int cells = inventory.Columns * inventory.Rows;
            while (cellLayer.childCount < cells)
                Instantiate(Hud.Library.InventoryCell, cellLayer);
            for (int i = cellLayer.childCount - 1; i >= cells; i--)
            {
                cellLayer.GetChild(i).gameObject.SetActive(false);
                Destroy(cellLayer.GetChild(i).gameObject);
            }

            gridLayout.constraintCount = inventory.Columns;
            var size = new Vector2(inventory.Columns * CellSize.x, inventory.Rows * CellSize.y);
            scrollContent.sizeDelta = size;
            itemLayer.sizeDelta = size;
            cellLayer.sizeDelta = size;
            // Keep the authored panel height; its inventory viewport scrolls when the bag expands.
            gold.text = Game.State.Gold.ToString("N0");
            for (int i = 0; i < equipment.Length; i++)
            {
                var visual = equippedItems[i];
                if (!Game.State.Equipment.TryGetValue(equipment[i].Slot, out var id))
                {
                    if (visual != null)
                        visual.gameObject.SetActive(false);
                    continue;
                }

                if (visual == null)
                {
                    visual = Instantiate(Hud.Library.Item, equipmentIcons[i]);
                    visual.Passive();
                    visual.Rect.anchorMin = Vector2.zero;
                    visual.Rect.anchorMax = Vector2.one;
                    visual.Rect.offsetMin = visual.Rect.offsetMax = Vector2.zero;
                    equippedItems[i] = visual;
                }
                visual.Display(Game.Settings.FindItem(id), id, 1);
                visual.gameObject.SetActive(true);
            }

            Hud.RenderInventoryItems(this);
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                parent.GetChild(i).gameObject.SetActive(false);
                Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
