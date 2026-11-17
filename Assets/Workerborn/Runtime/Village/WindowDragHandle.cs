using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform Window { get; set; }
        private Vector2 start;
        private Vector2 pointerStart;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Window == null) return;
            Window.SetAsLastSibling();
            start = Window.anchoredPosition;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Window.parent, eventData.position,
                eventData.pressEventCamera, out pointerStart);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Window == null) return;
            var parent = (RectTransform)Window.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 pointer);
            Vector2 next = start + pointer - pointerStart;
            float horizontal = Mathf.Max(0f, parent.rect.width * 0.5f - Window.rect.width * 0.5f);
            float vertical = Mathf.Max(0f, parent.rect.height * 0.5f - Window.rect.height * 0.5f);
            Window.anchoredPosition = new Vector2(Mathf.Clamp(next.x, -horizontal, horizontal), Mathf.Clamp(next.y, -vertical, vertical));
        }
    }
}
