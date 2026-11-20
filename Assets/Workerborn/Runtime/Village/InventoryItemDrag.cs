using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class InventoryItemDrag : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler
    {
        public VillageHud Hud { get; set; }
        public int EntryId { get; set; }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Right) Hud.OpenItemContext(EntryId, data.position);
            else if (data.button == PointerEventData.InputButton.Left) Hud.ClickInventoryItem(EntryId, data.position);
        }
        public void OnPointerEnter(PointerEventData data) => Hud.ShowItemTooltip(EntryId, data.position);
        public void OnPointerExit(PointerEventData data) => Hud.HideItemTooltip();
        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && !Hud.IsHoldingItem)
                Hud.BeginItemDrag(EntryId, data.position, releaseOnMouseUp: true);
        }
        public void OnDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && Hud.IsHoldingItem)
                Hud.MoveItemDrag(data.position);
        }
    }
}
