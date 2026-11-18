using System;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class UiItemView : MonoBehaviour
    {
        [SerializeField]
        private Image icon;
        [SerializeField]
        private Text count;
        [SerializeField]
        private Text fallback;
        [SerializeField]
        private Image background;
        [SerializeField]
        private Button button;
        [SerializeField]
        private InventoryItemDrag pointer;
        public RectTransform Rect => (RectTransform)transform;

        public void Display(ItemDefinition definition, string itemId, int quantity)
        {
            icon.sprite = definition != null ? definition.Icon : null;
            icon.enabled = icon.sprite != null;
            fallback.text = icon.enabled ? "" : ItemCatalog.Get(itemId).Name;
            count.text = quantity > 1 ? quantity.ToString() : "";
        }

        public void Inventory(VillageHud hud, int id)
        {
            pointer.enabled = true;
            pointer.Hud = hud;
            pointer.EntryId = id;
            button.onClick.RemoveAllListeners();
            background.raycastTarget = true;
        }

        public void Shop(Action action)
        {
            pointer.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            background.raycastTarget = true;
            button.interactable = true;
        }

        public void Passive()
        {
            pointer.enabled = false;
            button.onClick.RemoveAllListeners();
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            button.interactable = false;
        }
    }
}
