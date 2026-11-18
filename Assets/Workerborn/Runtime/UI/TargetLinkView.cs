using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class TargetLinkView : MonoBehaviour
    {
        [SerializeField]
        private Image line;
        [SerializeField, Min(.5f)]
        private float thickness = 1.5f;
        public void Show(RectTransform canvas, Vector2 from, Vector2 to, Color tint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, from, null, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, to, null, out var b);
            var rect = line.rectTransform;
            rect.anchoredPosition = a;
            rect.sizeDelta = new Vector2(Vector2.Distance(a, b), thickness);
            rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            line.color = tint;
            line.raycastTarget = false;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
