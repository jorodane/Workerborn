using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class InventoryGridPointer : MonoBehaviour, IPointerClickHandler
    {
        public VillageHud Hud { get; set; }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) Hud.EndItemDrag(data.position);
            else if (data.button == PointerEventData.InputButton.Right) Hud.CancelItemDrag();
        }
    }
}
