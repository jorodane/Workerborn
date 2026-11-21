using TMPro;
using UnityEngine;

namespace Workerborn
{
    public sealed class PlayerOverheadView : MonoBehaviour
    {
        [SerializeField] private TMP_Text playerName;
        [SerializeField] private TMP_Text speech;
        [SerializeField] private RectTransform bubble;
        [SerializeField, Min(80f)] private float bubbleWidth = 300f;
        [SerializeField, Min(1f)] private float secondsVisible = 7f;
        [SerializeField] private Vector2 bubblePadding = new Vector2(18f, 12f);

        private const int VisibleLines = 4;
        private const float NameGap = 4f;
        private float shownUntil;
        private RectTransform Rect => (RectTransform)transform;

        internal void SetName(string name) => playerName.text = name;

        internal void Say(string message)
        {
            bubble.gameObject.SetActive(true);
            speech.maxVisibleLines = VisibleLines;
            speech.text = message;
            float width = Mathf.Clamp(speech.GetPreferredValues(message).x, 40f, bubbleWidth - bubblePadding.x);
            float height = speech.GetPreferredValues(message, width, Mathf.Infinity).y;
            float maximum = speech.GetPreferredValues("가\n가\n가\n가", width, Mathf.Infinity).y;
            height = Mathf.Min(height, maximum) + bubblePadding.y;
            bubble.sizeDelta = new Vector2(width + bubblePadding.x, height);
            playerName.rectTransform.anchoredPosition = Vector2.up * (height + NameGap);
            shownUntil = Time.unscaledTime + secondsVisible;
        }

        internal void Position(Camera camera, RectTransform canvas, Vector3 world)
        {
            if (bubble.gameObject.activeSelf && Time.unscaledTime >= shownUntil)
            {
                bubble.gameObject.SetActive(false);
                playerName.rectTransform.anchoredPosition = Vector2.zero;
            }

            var screen = camera.WorldToScreenPoint(world);
            bool visible = screen.z > 0f && camera.pixelRect.Contains((Vector2)screen);
            Rect.gameObject.SetActive(visible);
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var local))
                Rect.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }
}
