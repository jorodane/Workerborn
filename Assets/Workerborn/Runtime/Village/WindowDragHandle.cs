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
            if (eventData.button != PointerEventData.InputButton.Left || Window == null)
                return;
            Window.SetAsLastSibling();
            start = Window.anchoredPosition;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Window.parent, eventData.position, eventData.pressEventCamera, out pointerStart);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || Window == null)
                return;
            var parent = (RectTransform)Window.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 pointer);
            Vector2 next = start + pointer - pointerStart;
            Vector2 size = Window.rect.size;
            Vector2 anchor = Window.anchorMin + Vector2.Scale(Window.anchorMax - Window.anchorMin, Window.pivot);
            Vector2 reference = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
            Vector2 minimum = parent.rect.min + Vector2.Scale(size, Window.pivot);
            Vector2 maximum = parent.rect.max - Vector2.Scale(size, Vector2.one - Window.pivot);
            if (minimum.x > maximum.x)
                minimum.x = maximum.x = parent.rect.center.x + size.x * (Window.pivot.x - .5f);
            if (minimum.y > maximum.y)
                minimum.y = maximum.y = parent.rect.center.y + size.y * (Window.pivot.y - .5f);
            Vector2 pivot = reference + next;
            Window.anchoredPosition = new Vector2(Mathf.Clamp(pivot.x, minimum.x, maximum.x), Mathf.Clamp(pivot.y, minimum.y, maximum.y)) - reference;
        }
    }
}
