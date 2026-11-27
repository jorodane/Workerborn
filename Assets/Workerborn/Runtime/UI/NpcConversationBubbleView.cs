using TMPro;
using UnityEngine;

namespace Workerborn
{
    public sealed class NpcConversationBubbleView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        private RectTransform Rect => (RectTransform)transform;

        internal void SetPartner(string name)
        {
            char last = string.IsNullOrEmpty(name) ? ' ' : name[name.Length - 1];
            string particle = last is >= '가' and <= '힣' && (last - '가') % 28 != 0 ? "과" : "와";
            SetLine($"{name}{particle} 대화 중");
        }

        internal void SetLine(string text)
        {
            label.text = text;
            label.textWrappingMode = TextWrappingModes.Normal;
            float width = Mathf.Clamp(label.GetPreferredValues(text).x + 26f, 104f, 274f);
            float height = Mathf.Max(28f, label.GetPreferredValues(text, width - 20f, Mathf.Infinity).y + 12f);
            Rect.sizeDelta = new Vector2(width, height);
        }

        internal void Place(Vector2 point)
        {
            Rect.localPosition = point + Vector2.up * 24f;
            gameObject.SetActive(true);
        }
    }
}
