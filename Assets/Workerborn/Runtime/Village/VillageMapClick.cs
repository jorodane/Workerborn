using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class VillageMapClick : MonoBehaviour, IPointerClickHandler
    {
        public VillagePrototype Game { get; set; }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Game == null || Game.ControlsBlocked) return;
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
            float u = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, local.x);
            float v = Mathf.InverseLerp(rect.rect.yMin, rect.rect.yMax, local.y);
            if (Game.MinimapCamera != null &&
                Game.TryMapGround(Game.MinimapCamera.ViewportPointToRay(new Vector3(u, v, 0f)), out var hit))
                Game.Worker.CommandMove(hit.point);
        }
    }
}
