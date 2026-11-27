using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class VillageMapClick : MonoBehaviour, IPointerClickHandler
    {
        public VillagePrototype Game { get; set; }
        internal VillageMapView Map { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Game == null || Map == null || !Game.Worker.AcceptsDirectMovement)
                return;
            if (Map.TryGetMapPoint(eventData.position, eventData.pressEventCamera, out var point) &&
                Game.TryMapGround(point, out var hit))
                Game.Worker.CommandMove(hit.point, WorkerbornInput.Held("Gameplay/Walk"));
        }
    }
}
