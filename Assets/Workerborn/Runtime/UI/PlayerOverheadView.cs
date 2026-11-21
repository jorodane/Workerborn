using System.Collections.Generic;
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
        private const int RecentMessages = 3;
        private const float NameGap = 4f;
        private readonly List<string> spoken = new List<string>(RecentMessages);
        private float shownUntil;
        private bool speechChanged;
        private RectTransform Rect => (RectTransform)transform;

        internal void SetName(string name) => playerName.text = name;

        internal void Say(string message)
        {
            if (Time.unscaledTime >= shownUntil)
                spoken.Clear();
            if (spoken.Count == RecentMessages)
                spoken.RemoveAt(0);
            spoken.Add(message);
            shownUntil = Time.unscaledTime + secondsVisible;
            speechChanged = true;
        }

        private void LayoutSpeech()
        {
            speechChanged = false;
            bubble.gameObject.SetActive(true);
            string text = string.Join("\n", spoken);
            float width = Mathf.Clamp(speech.GetPreferredValues(text).x, 40f, Mathf.Max(40f, bubbleWidth - bubblePadding.x));
            float height = speech.GetPreferredValues(text, width, Mathf.Infinity).y;
            bubble.sizeDelta = new Vector2(width + bubblePadding.x, height + bubblePadding.y);
            speech.text = text;
            speech.maxVisibleLines = int.MaxValue;
            speech.ForceMeshUpdate();

            var layout = speech.textInfo;
            if (layout.lineCount > VisibleLines)
            {
                int firstLine = layout.lineCount - VisibleLines;
                int firstCharacter = layout.lineInfo[firstLine].firstCharacterIndex;
                int offset = layout.characterInfo[firstCharacter].index;
                TrimEarlierText(offset);
                speech.text = text.Substring(offset);
                height = speech.GetPreferredValues(speech.text, width, Mathf.Infinity).y;
            }

            speech.maxVisibleLines = VisibleLines;
            bubble.sizeDelta = new Vector2(width + bubblePadding.x, height + bubblePadding.y);
            playerName.rectTransform.anchoredPosition = Vector2.up * (bubble.sizeDelta.y + NameGap);
        }

        private void TrimEarlierText(int offset)
        {
            // The layout offset includes the single newline separating each post.
            while (spoken.Count > 1 && offset > spoken[0].Length)
            {
                offset -= spoken[0].Length + 1;
                spoken.RemoveAt(0);
            }
            if (offset > 0)
                spoken[0] = spoken[0].Substring(offset);
        }

        internal void Position(Camera camera, RectTransform canvas, Vector3 world)
        {
            if (spoken.Count > 0 && Time.unscaledTime >= shownUntil)
            {
                spoken.Clear();
                speechChanged = false;
                bubble.gameObject.SetActive(false);
                playerName.rectTransform.anchoredPosition = Vector2.zero;
            }

            var screen = camera.WorldToScreenPoint(world);
            bool visible = screen.z > 0f && camera.pixelRect.Contains((Vector2)screen);
            Rect.gameObject.SetActive(visible);
            if (!visible)
                return;
            if (speechChanged)
                LayoutSpeech();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var local))
                Rect.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }
}
