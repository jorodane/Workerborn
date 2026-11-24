using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ItemShortcutSlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image frame;
        [SerializeField] private TMP_Text keyLabel;
        [SerializeField] private CanvasGroup content;
        [SerializeField] private UiItemView item;
        [SerializeField] private Color hoverColor = new Color(.37f, .5f, .62f);
        [SerializeField] private Color equippedColor = new Color(.38f, .63f, .77f);

        private VillageHud hud;
        private string itemId;
        private Color idleColor;
        private bool hovered, equipped;
        internal int Index { get; private set; }

        internal void Bind(VillageHud owner, int index)
        {
            hud = owner;
            Index = index;
            keyLabel.text = ((index + 1) % 10).ToString();
            idleColor = frame.color;
            item.ShowIconOnly(showQuantity: true);
        }

        internal void Show(ItemDefinition definition, string id, int quantity, bool worn)
        {
            itemId = id;
            equipped = worn;
            item.Display(definition, id, quantity);
            item.gameObject.SetActive(true);
            content.alpha = quantity > 0 || worn ? 1f : .38f;
            RefreshFrame();
        }

        internal void Clear()
        {
            if (itemId != null && hovered)
                hud.HideItemTooltip();
            itemId = null;
            equipped = false;
            item.gameObject.SetActive(false);
            RefreshFrame();
        }

        private void RefreshFrame() => frame.color = equipped ? equippedColor : hovered ? hoverColor : idleColor;

        public void OnPointerClick(PointerEventData data) => hud?.ClickItemShortcut(Index, data.button);

        public void OnPointerEnter(PointerEventData data)
        {
            if (hud == null)
                return;
            hovered = true;
            RefreshFrame();
            if (itemId != null)
                hud.ShowItemTooltip(itemId, data.position);
        }

        public void OnPointerExit(PointerEventData data) => ResetHover();
        private void OnDisable() => ResetHover();

        private void ResetHover()
        {
            if (!hovered)
                return;
            hovered = false;
            RefreshFrame();
            hud?.HideItemTooltip();
        }
    }
}
