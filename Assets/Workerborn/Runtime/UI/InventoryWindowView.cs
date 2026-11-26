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
        private TMP_Text gold;
        [SerializeField]
        private EquipmentSlotPointer[] equipment;
        [SerializeField]
        private RectTransform[] equipmentIcons;
        [SerializeField] private Button[] equipmentSetTabs;
        [SerializeField] private Color activeEquipmentSetColor = new Color(.34f, .59f, .73f);
        private Color idleEquipmentSetColor;
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
            equippedItems = new UiItemView[equipment.Length];
            foreach (var slot in equipment)
                slot.Bind(hud);
            idleEquipmentSetColor = equipmentSetTabs[0].targetGraphic.color;
            for (int i = 0; i < equipmentSetTabs.Length; i++)
            {
                int set = i;
                equipmentSetTabs[i].onClick.AddListener(() => hud.SelectEquipmentSet(set));
            }
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
            for (int i = 0; i < equipmentSetTabs.Length; i++)
                equipmentSetTabs[i].targetGraphic.color = i == Game.State.EquipmentSet ? activeEquipmentSetColor : idleEquipmentSetColor;
            for (int i = 0; i < equipment.Length; i++)
            {
                var visual = equippedItems[i];
                var slot = equipment[i].Slot;
                bool occupied = Game.State.Equipment.TryGetValue(slot, out var id);
                bool blocked = Game.State.IsEquipmentBlocked(slot);
                equipment[i].ShowOccupied(occupied);
                if (!occupied && !blocked)
                {
                    if (visual != null)
                        visual.gameObject.SetActive(false);
                    continue;
                }

                if (visual == null)
                {
                    visual = Instantiate(Hud.Library.Item, equipmentIcons[i]);
                    visual.ShowIconOnly();
                    visual.Rect.anchorMin = Vector2.zero;
                    visual.Rect.anchorMax = Vector2.one;
                    visual.Rect.offsetMin = Vector2.zero;
                    visual.Rect.offsetMax = Vector2.zero;
                    equippedItems[i] = visual;
                }
                if (blocked)
                    visual.DisplayIcon(null, "<b><size=16>X</size></b>");
                else
                    visual.Display(Game.Settings.FindItem(id), id, 1);
                visual.SetIconOpacity(Game.State.IsEquipmentFallback(slot) ? .55f : 1f);
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
