using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class EquipmentSlotPointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private VillageHud hud;

        [SerializeField]
        private EquipmentSlot slot;
        public EquipmentSlot Slot { get => slot; set => slot = value; }
        private Image background;
        private Color emptyColor;

        internal void Bind(VillageHud owner)
        {
            hud = owner;
            background = GetComponent<Image>();
            emptyColor = background.color;
        }

        internal void ShowOccupied(bool occupied)
        {
            const float shade = .68f;
            background.color = occupied
                ? new Color(emptyColor.r * shade, emptyColor.g * shade, emptyColor.b * shade, emptyColor.a)
                : emptyColor;
        }

        public void OnPointerEnter(PointerEventData data) => hud?.ShowEquipmentTooltip(Slot, data.position);
        public void OnPointerExit(PointerEventData data) => hud?.HideItemTooltip();

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                hud?.ClickEquipment(Slot, data.position);
            else
                hud?.CancelItemDrag();
        }
    }
}
