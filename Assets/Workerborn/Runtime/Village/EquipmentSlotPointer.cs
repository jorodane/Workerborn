using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class EquipmentSlotPointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public VillageHud Hud { get; set; }

        [SerializeField]
        private EquipmentSlot slot;
        public EquipmentSlot Slot { get => slot; set => slot = value; }

        public void OnPointerEnter(PointerEventData data) => Hud?.ShowEquipmentTooltip(Slot, data.position);
        public void OnPointerExit(PointerEventData data) => Hud?.HideItemTooltip();

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                Hud.ClickEquipment(Slot, data.position);
            else
                Hud.CancelItemDrag();
        }
    }
}
