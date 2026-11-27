using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    [DefaultExecutionOrder(-10)]
    public sealed class VillageChatView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
    {
        [SerializeField] private ScrollRect history;
        [SerializeField] private TMP_Text messages;
        [SerializeField] private TMP_Text recentMessages;
        [SerializeField, Min(1f)] private float secondsVisible = 7f;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Button send;
        [SerializeField] private CanvasGroup inputChrome;
        [SerializeField] private VerticalGradient historyFrame;
        [SerializeField] private Image inputBackground;
        private const int HistoryLimit = 80;
        private const int RecentLimit = 3;
        private const float VerticalPadding = 8f;
        private readonly List<(string Text, float Expires)> lines = new List<(string, float)>(HistoryLimit);
        private readonly StringBuilder text = new StringBuilder();
        private Graphic scrollHitArea;
        private Vector2 historySize;
        private float nextExpiry = float.PositiveInfinity;
        private bool showingHistory;
        private Func<string, bool> submit;
        private int submittedFrame = -1;
        private int openedFrame = -1;
        private RectTransform layer;
        private int restingOrder;
        private bool pointerOverChat;
        private bool raised;
        private bool resumeEditing;

        internal bool IsTyping => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject;

        internal void Bind(Func<string, bool> post, RectTransform chatLayer)
        {
            submit = post;
            layer = chatLayer;
            restingOrder = layer.GetSiblingIndex();
            scrollHitArea = history.GetComponent<Graphic>();
            input.characterLimit = VillageChatText.MaximumLength;
            input.onSubmit.AddListener(message => Send(message, false));
            send.onClick.AddListener(() => Send(input.text, true));
            SetPresentation(false);
        }

        internal void OpenInput()
        {
            if (submittedFrame == Time.frameCount)
                return;
            openedFrame = Time.frameCount;
            FocusInput();
        }

        private void FocusInput()
        {
            SetPresentation(true);
            RaiseLayer();
            input.Select();
            input.ActivateInputField();
        }

        internal void LeaveInput()
        {
            resumeEditing = false;
            input.DeactivateInputField();
            if (IsTyping)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void Send(string message, bool fromButton)
        {
            if (submittedFrame == Time.frameCount || openedFrame == Time.frameCount)
                return;
            submittedFrame = Time.frameCount;
            if (string.IsNullOrWhiteSpace(message))
            {
                input.SetTextWithoutNotify("");
                LeaveInput();
                return;
            }
            if (submit(message))
                input.SetTextWithoutNotify("");
            if (fromButton)
                FocusInput();
            else
                resumeEditing = true;
        }

        private void LateUpdate()
        {
            if (layer == null)
                return;
            bool focused = IsTyping;
            if (resumeEditing)
            {
                // TMP ends the line after invoking onSubmit. Let pointer selection finish before reactivating.
                if (focused && !input.isFocused)
                    input.ActivateInputField();
                resumeEditing = false;
            }
            if (focused)
                RaiseLayer();
            else if (raised)
            {
                layer.SetSiblingIndex(restingOrder);
                raised = false;
            }
            SetPresentation(focused);
        }

        private void RaiseLayer()
        {
            if (layer.GetSiblingIndex() != layer.parent.childCount - 1)
                layer.SetAsLastSibling();
            raised = true;
        }

        public void OnPointerEnter(PointerEventData eventData) => pointerOverChat = true;
        public void OnPointerExit(PointerEventData eventData) => pointerOverChat = false;
        public void OnScroll(PointerEventData eventData)
        {
            if (showingHistory)
                history.OnScroll(eventData);
        }

        private void SetPresentation(bool focused)
        {
            history.content.gameObject.SetActive(focused);
            recentMessages.gameObject.SetActive(!focused);
            history.enabled = focused;
            if (scrollHitArea != null)
                scrollHitArea.raycastTarget = focused;
            if (focused && (!showingHistory || historySize != history.viewport.rect.size))
                UpdateHistory(follow: !showingHistory || history.verticalNormalizedPosition <= .01f);
            if (!focused && (showingHistory || Time.unscaledTime >= nextExpiry))
                UpdateRecentMessages();
            showingHistory = focused;

            bool overInput = pointerOverChat && RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)inputChrome.transform, WorkerbornInput.Point);
            inputChrome.alpha = focused || overInput ? 1f : 0f;
            historyFrame.enabled = focused;
            // The focused gradient already fills the input strip with its darkest color.
            inputBackground.color = focused ? Color.clear : historyFrame.color;
        }

        internal void Append(string speaker, string message)
        {
            if (lines.Count == HistoryLimit)
                lines.RemoveAt(0);
            lines.Add(($"{speaker} : {message}", Time.unscaledTime + secondsVisible));
            if (showingHistory)
                UpdateHistory(follow: history.verticalNormalizedPosition <= .01f);
            else
                UpdateRecentMessages();
        }

        private void UpdateHistory(bool follow)
        {
            text.Clear();
            foreach (var line in lines)
                AppendLine(line.Text);
            messages.text = text.ToString();
            historySize = history.viewport.rect.size;
            float height = messages.GetPreferredValues(messages.text, Mathf.Max(1f, messages.rectTransform.rect.width), Mathf.Infinity).y;
            history.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(historySize.y, height + VerticalPadding));
            if (follow)
            {
                history.StopMovement();
                history.verticalNormalizedPosition = 0f;
            }
        }

        private void UpdateRecentMessages()
        {
            text.Clear();
            float now = Time.unscaledTime;
            nextExpiry = float.PositiveInfinity;
            for (int index = Mathf.Max(0, lines.Count - RecentLimit); index < lines.Count; index++)
            {
                var line = lines[index];
                if (now >= line.Expires)
                    continue;
                AppendLine(line.Text);
                nextExpiry = Mathf.Min(nextExpiry, line.Expires);
            }
            recentMessages.text = text.ToString();
        }

        private void AppendLine(string line)
        {
            if (text.Length > 0)
                text.Append('\n');
            text.Append(line);
        }
    }
}
