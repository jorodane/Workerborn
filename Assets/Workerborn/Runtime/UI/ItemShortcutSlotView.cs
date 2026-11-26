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
        private float keySize;
        internal int Index { get; private set; }
        internal int Binding { get; private set; } = -1;

        internal void Bind(VillageHud owner, int index)
        {
            hud = owner;
            Index = index;
            idleColor = frame.color;
            defaultFrame = frame.sprite;
            keySize = keyLabel.fontSize;
            keyLabel.text = ((index + 1) % VillageState.ItemShortcutCount).ToString();
            item.ShowIconOnly(showQuantity: true);
        }

        internal void ShowBinding(int binding)
        {
            if (Binding == binding)
                return;
            Binding = binding;
            bool shifted = binding >= VillageState.ItemShortcutCount;
            frame.sprite = shifted ? alternateFrame : defaultFrame;
            keyLabel.fontSize = shifted ? keySize * .75f : keySize;
            string key = ((Index + 1) % VillageState.ItemShortcutCount).ToString();
            keyLabel.text = shifted ? $"Shift+{key}" : key;
            if (hovered && occupied)
                hud.ShowShortcutTooltip(Binding, hoverPosition);
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

        public void OnPointerClick(PointerEventData data) => hud?.ClickItemShortcut(Binding, data.button);

        public void OnPointerEnter(PointerEventData data)
        {
            if (hud == null)
                return;
            hovered = true;
            hoverPosition = data.position;
            RefreshFrame();
            if (occupied)
                hud.ShowShortcutTooltip(Binding, data.position);
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
