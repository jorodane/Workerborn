using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    // A click picks the item up; no held mouse button or drag gesture is required.
    public sealed class InventoryItemDrag : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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
    }
}
