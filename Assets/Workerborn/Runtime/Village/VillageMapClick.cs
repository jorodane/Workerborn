using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class VillageMapClick : MonoBehaviour, IPointerClickHandler
    {
        public VillagePrototype Game { get; set; }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Game.ControlsBlocked) return;
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
            float u = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, local.x);
            float v = Mathf.InverseLerp(rect.rect.yMin, rect.rect.yMax, local.y);
            Game.Worker.CommandMove(new Vector3(Mathf.Lerp(-28f, 23f, u), 0f, Mathf.Lerp(-14.5f, 24.5f, v)));
        }
    }
}
