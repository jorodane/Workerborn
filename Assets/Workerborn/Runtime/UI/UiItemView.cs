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
        public RectTransform Rect => (RectTransform)transform;

        public void Display(ItemDefinition definition, string itemId, int quantity)
        {
            tooltipItem = itemId;
            icon.sprite = definition != null ? definition.Icon : null;
            icon.enabled = icon.sprite != null;
            fallback.text = icon.enabled ? "" : ItemCatalog.Get(itemId).Name;
            count.text = quantity > 1 ? quantity.ToString() : "";
        }

        public void Inventory(VillageHud hud, int id)
        {
            tooltipHud = hud;
            pointer.enabled = true;
            pointer.Hud = hud;
            pointer.EntryId = id;
            button.onClick.RemoveAllListeners();
            background.raycastTarget = true;
        }

        public void Shop(VillageHud hud, Action action)
        {
            tooltipHud = hud;
            pointer.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            background.raycastTarget = true;
            button.interactable = true;
        }

        public void Passive(VillageHud inspect = null)
        {
            tooltipHud = inspect;
            pointer.enabled = false;
            button.onClick.RemoveAllListeners();
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            button.interactable = false;
            background.raycastTarget = inspect != null;
        }

        public void OnPointerEnter(PointerEventData data) => tooltipHud?.ShowItemTooltip(tooltipItem, data.position);
        public void OnPointerExit(PointerEventData data) => tooltipHud?.HideItemTooltip();
        private void OnDisable() => tooltipHud?.HideItemTooltip();
    }
}
