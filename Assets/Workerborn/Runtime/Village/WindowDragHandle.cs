using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform window;
        private string positionKey;
        private Vector2 start;
        private Vector2 pointerStart;
        private bool dragging;
        internal bool HasSavedPosition { get; private set; }

        internal void Bind(RectTransform target, string windowId)
        {
            window = target;
            positionKey = windowId == null ? null : "Workerborn.UI.Position." + windowId;
            if (positionKey == null || !PlayerPrefs.HasKey(positionKey + ".x") || !PlayerPrefs.HasKey(positionKey + ".y"))
                return;
            var saved = new Vector2(PlayerPrefs.GetFloat(positionKey + ".x"), PlayerPrefs.GetFloat(positionKey + ".y"));
            if (WorkerbornInput.IsFinite(saved))
            {
                HasSavedPosition = true;
                window.anchoredPosition = saved;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || window == null ||
                !WorkerbornInput.IsFinite(eventData.pressPosition))
                return;
            start = window.anchoredPosition;
            dragging = RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)window.parent,
                eventData.pressPosition, eventData.pressEventCamera, out pointerStart);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData.button != PointerEventData.InputButton.Left || !WorkerbornInput.IsFinite(eventData.position))
                return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)window.parent, eventData.position,
                eventData.pressEventCamera, out var pointer))
            {
                window.anchoredPosition = start + pointer - pointerStart;
                KeepOnScreen(window);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                FinishDrag();
        }

        private void OnDisable() => FinishDrag();

        internal void FinishDrag()
        {
            if (!dragging)
                return;
            dragging = false;
            if (window == null || positionKey == null || window.anchoredPosition == start)
                return;
            HasSavedPosition = true;
            PlayerPrefs.SetFloat(positionKey + ".x", window.anchoredPosition.x);
            PlayerPrefs.SetFloat(positionKey + ".y", window.anchoredPosition.y);
            PlayerPrefs.Save();
        }

        internal static Rect BoundsInParent(RectTransform rect)
        {
            var origin = (Vector2)rect.localPosition;
            return new Rect(origin + Vector2.Scale(rect.rect.min, rect.localScale), Vector2.Scale(rect.rect.size, rect.localScale));
        }

        internal static void KeepOnScreen(RectTransform rect)
        {
            var windowBounds = BoundsInParent(rect);
            var canvas = ((RectTransform)rect.parent).rect;
            float x = windowBounds.width > canvas.width ? canvas.center.x - windowBounds.center.x
                : Mathf.Clamp(0f, canvas.xMin - windowBounds.xMin, canvas.xMax - windowBounds.xMax);
            float y = windowBounds.height > canvas.height ? canvas.yMax - windowBounds.yMax
                : Mathf.Clamp(0f, canvas.yMin - windowBounds.yMin, canvas.yMax - windowBounds.yMax);
            rect.anchoredPosition += new Vector2(x, y);
        }
    }
}
