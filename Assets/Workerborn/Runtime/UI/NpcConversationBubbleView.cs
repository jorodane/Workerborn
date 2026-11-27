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
            label.text = $"{name}{particle} 대화 중";
            Rect.sizeDelta = new Vector2(Mathf.Clamp(label.GetPreferredValues().x + 24f, 104f, 228f), 28f);
        }

        internal void Place(Vector2 point)
        {
            Rect.localPosition = point + Vector2.up * 24f;
            gameObject.SetActive(true);
        }
    }
}
