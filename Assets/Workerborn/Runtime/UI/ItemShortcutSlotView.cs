using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ItemShortcutSlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image frame;
        [SerializeField] private Sprite alternateFrame;
        [SerializeField] private TMP_Text keyLabel;
        [SerializeField] private CanvasGroup content;
        [SerializeField] private UiItemView item;
        [SerializeField] private Color hoverColor = new Color(.37f, .5f, .62f);
        [SerializeField] private Color equippedColor = new Color(.38f, .63f, .77f);

        private VillageHud hud;
        private Color idleColor;
        private Sprite defaultFrame;
        private bool hovered, equipped, occupied;
        private Vector2 hoverPosition;
        private int shownBinding = -1;
        internal int Index { get; private set; }

        internal void Bind(VillageHud owner, int index)
        {
            hud = owner;
            Index = index;
            idleColor = frame.color;
            defaultFrame = frame.sprite;
            keyLabel.text = ((index + 1) % VillageState.ItemShortcutCount).ToString();
            item.ShowIconOnly(showQuantity: true);
        }

        internal void ShowBinding(int binding)
        {
            if (shownBinding == binding)
                return;
            shownBinding = binding;
            frame.sprite = binding >= VillageState.ItemShortcutCount ? alternateFrame : defaultFrame;
            if (hovered && occupied)
                hud.ShowShortcutTooltip(Index, hoverPosition);
        }

        internal void ShowItem(ItemDefinition definition, string id, int quantity, bool worn)
        {
            occupied = true;
            equipped = worn;
            item.Display(definition, id, quantity);
            item.gameObject.SetActive(true);
            content.alpha = quantity > 0 || worn ? 1f : .38f;
            RefreshFrame();
        }

        internal void ShowSkill(Sprite sprite, string name)
        {
            occupied = true;
            equipped = false;
            item.DisplayIcon(sprite, name);
            item.gameObject.SetActive(true);
            content.alpha = 1f;
            RefreshFrame();
        }

        internal void Clear()
        {
            if (occupied && hovered)
                hud.HideItemTooltip();
            occupied = false;
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
            hoverPosition = data.position;
            RefreshFrame();
            if (occupied)
                hud.ShowShortcutTooltip(Index, data.position);
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
