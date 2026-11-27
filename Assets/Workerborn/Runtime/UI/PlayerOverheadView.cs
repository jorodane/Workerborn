using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Workerborn
{
    public sealed class PlayerOverheadView : MonoBehaviour
    {
        [SerializeField] private WorldNameplateView playerName;
        [SerializeField] private TMP_Text speech;
        [SerializeField] private RectTransform bubble;
        [SerializeField, Min(80f)] private float bubbleWidth = 300f;
        [SerializeField, Min(1f)] private float secondsVisible = 7f;
        [SerializeField] private Vector2 bubblePadding = new Vector2(18f, 12f);
        [SerializeField, Min(.01f)] private float resizeSeconds = .24f;
        [SerializeField, Range(0f, 2.5f)] private float resizeOvershoot = 1.35f;

        private const int VisibleLines = 4;
        private const int RecentMessages = 3;
        private readonly List<string> spoken = new List<string>(RecentMessages);
        private float shownUntil;
        private bool speechChanged;
        private Vector2 startSize, targetSize;
        private float resizeStartedAt;
        private float layoutTopPadding;
        private bool resizing;
        private RectTransform Rect => (RectTransform)transform;
        private float SpeechTopPadding => playerName.Rect.rect.height * .5f + playerName.VisibleHealthHeight + bubblePadding.y * .5f;

        internal WorldNameplateView Nameplate => playerName;
        internal void SetName(string name) => playerName.SetText(name);

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

        private void LayoutSpeech(float now)
        {
            speechChanged = false;
            if (!bubble.gameObject.activeSelf)
                SetSize(Vector2.zero);
            bubble.gameObject.SetActive(true);
            string text = string.Join("\n", spoken);
            float width = Mathf.Clamp(speech.GetPreferredValues(text).x, 40f, Mathf.Max(40f, bubbleWidth - bubblePadding.x));
            float height = speech.GetPreferredValues(text, width, Mathf.Infinity).y;
            var speechRect = speech.rectTransform;
            layoutTopPadding = SpeechTopPadding;
            speechRect.anchoredPosition = new Vector2(bubblePadding.x * .5f, -layoutTopPadding);
            speechRect.sizeDelta = new Vector2(width, height);
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
            speechRect.sizeDelta = new Vector2(width, height);
            ResizeTo(new Vector2(width + bubblePadding.x, height + layoutTopPadding + bubblePadding.y * .5f), now);
        }

        private void ResizeTo(Vector2 size, float now)
        {
            if (targetSize == size)
                return;
            startSize = bubble.sizeDelta;
            targetSize = size;
            resizeStartedAt = now;
            resizing = true;
        }

        private void AnimateSize(float now)
        {
            if (!resizing)
                return;
            float progress = Mathf.Clamp01((now - resizeStartedAt) / Mathf.Max(.01f, resizeSeconds));
            if (progress >= 1f)
            {
                SetSize(targetSize);
                resizing = false;
                if (targetSize == Vector2.zero)
                    bubble.gameObject.SetActive(false);
                return;
            }

            float remaining = progress - 1f;
            float eased = 1f + remaining * remaining * ((resizeOvershoot + 1f) * remaining + resizeOvershoot);
            SetSize(Vector2.Max(Vector2.zero, Vector2.LerpUnclamped(startSize, targetSize, eased)));
        }

        private void SetSize(Vector2 size)
        {
            bubble.sizeDelta = size;
            float rim = size.y - WorldNameplateView.SpeechRimInset(size.y);
            playerName.Rect.anchoredPosition = size.y > 0f
                ? Vector2.up * Mathf.Max(0f, rim - playerName.Rect.rect.center.y)
                : Vector2.zero;
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

        internal void Position(Vector2 local, bool visible)
        {
            float now = Time.unscaledTime;
            if (spoken.Count > 0 && now >= shownUntil)
            {
                spoken.Clear();
                speechChanged = false;
                ResizeTo(Vector2.zero, now);
            }

            AnimateSize(now);
            Rect.gameObject.SetActive(visible);
            if (!visible)
                return;
            if (speechChanged || spoken.Count > 0 && !Mathf.Approximately(layoutTopPadding, SpeechTopPadding))
                LayoutSpeech(now);
            Rect.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }
}
