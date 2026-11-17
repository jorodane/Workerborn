using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class InventoryItemDrag : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public VillageHud Hud { get; set; }
        public int EntryId { get; set; }
        private bool dragged;
        public void OnPointerDown(PointerEventData eventData) => dragged = false;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (dragged) return;
            Hud.SelectInventoryItem(EntryId);
            if (eventData.button == PointerEventData.InputButton.Right) Hud.UseSelectedInventoryItem();
        }
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            dragged = true;
            Hud.BeginItemDrag(EntryId, eventData.position);
        }
        public void OnDrag(PointerEventData eventData) { if (dragged) Hud.MoveItemDrag(eventData.position); }
        public void OnEndDrag(PointerEventData eventData) { if (dragged) Hud.EndItemDrag(eventData.position); }
        private void OnDisable() { if (dragged && Hud != null) Hud.CancelItemDrag(); }
    }
}
