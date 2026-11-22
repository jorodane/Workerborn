using System;
using System.Collections.Generic;
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
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Button send;
        [SerializeField] private CanvasGroup inputChrome;
        [SerializeField] private ChatHistoryGradient historyFrame;
        private const int HistoryLimit = 80;
        private readonly Queue<string> lines = new Queue<string>(HistoryLimit);
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
        public void OnScroll(PointerEventData eventData) => history.OnScroll(eventData);

        private void SetPresentation(bool focused)
        {
            bool overInput = pointerOverChat && RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)inputChrome.transform, WorkerbornInput.Point);
            inputChrome.alpha = focused || overInput ? 1f : 0f;
            historyFrame.enabled = focused;
        }

        internal void Append(string speaker, string message)
        {
            bool follow = history.content.rect.height <= history.viewport.rect.height || history.verticalNormalizedPosition <= .01f;
            if (lines.Count == HistoryLimit)
                lines.Dequeue();
            lines.Enqueue($"{speaker} : {message}");
            messages.text = string.Join("\n", lines);
            float height = messages.GetPreferredValues(messages.text, Mathf.Max(1f, messages.rectTransform.rect.width), Mathf.Infinity).y;
            history.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(history.viewport.rect.height, height + 8f));
            if (follow)
            {
                history.StopMovement();
                history.verticalNormalizedPosition = 0f;
            }
        }
    }
}
