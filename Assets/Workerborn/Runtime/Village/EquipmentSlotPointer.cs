using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class EquipmentSlotPointer : MonoBehaviour, IPointerClickHandler
    {
        public VillageHud Hud { get; set; }
        public EquipmentSlot Slot { get; set; }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) Hud.ClickEquipment(Slot, data.position);
            else Hud.CancelItemDrag();
        }
    }
}
