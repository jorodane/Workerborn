using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class UiItemView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField]
        private Image icon;
        [SerializeField]
        private TMP_Text count;
        [SerializeField]
        private TMP_Text fallback;
        [SerializeField]
        private Image background;
        [SerializeField]
        private Button button;
        [SerializeField]
        private InventoryItemDrag pointer;
        private VillageHud tooltipHud;
        private string tooltipItem;
        private int tooltipQuantity;
        private EquipmentCondition tooltipCondition;
        private QuantityAction? tradeTooltip;
        public RectTransform Rect => (RectTransform)transform;

        public void Display(ItemDefinition definition, string itemId, int quantity, EquipmentCondition condition = default)
        {
            tooltipItem = itemId;
            tooltipQuantity = quantity;
            tooltipCondition = condition;
            icon.sprite = definition != null ? definition.Icon : null;
            icon.enabled = icon.sprite != null;
            fallback.text = icon.enabled ? "" : ItemCatalog.Get(itemId).Name;
            count.text = quantity > 1 ? quantity.ToString() : "";
        }

        public void Inventory(VillageHud hud, int id)
        {
            tooltipHud = hud;
            tradeTooltip = QuantityAction.Sell;
            pointer.enabled = true;
            pointer.Hud = hud;
            pointer.EntryId = id;
            button.onClick.RemoveAllListeners();
            background.raycastTarget = true;
        }

        public void Shop(VillageHud hud, Action action)
        {
            tooltipHud = hud;
            tradeTooltip = QuantityAction.Buy;
            pointer.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            background.raycastTarget = true;
            button.interactable = true;
        }

        public void Passive(VillageHud inspect = null)
        {
            tooltipHud = inspect;
            tradeTooltip = null;
            pointer.enabled = false;
            button.onClick.RemoveAllListeners();
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            button.interactable = false;
            background.raycastTarget = inspect != null;
        }

        internal void SetTradeTooltip(QuantityAction? action) => tradeTooltip = action;

        internal void ShowIconOnly(bool showQuantity = false)
        {
            Passive();
            background.enabled = false;
            count.enabled = showQuantity;
        }

        public void OnPointerEnter(PointerEventData data) =>
            tooltipHud?.ShowItemTooltip(tooltipItem, data.position, tradeTooltip == QuantityAction.Sell ? tooltipQuantity : 0,
                shopListing: tradeTooltip == QuantityAction.Buy, condition: tooltipCondition);
        public void OnPointerExit(PointerEventData data) => tooltipHud?.HideItemTooltip();
        private void OnDisable() => tooltipHud?.HideItemTooltip();
    }
}
