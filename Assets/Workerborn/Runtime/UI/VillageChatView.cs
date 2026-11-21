using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class VillageChatView : MonoBehaviour
    {
        [SerializeField] private ScrollRect history;
        [SerializeField] private TMP_Text messages;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Button send;
        private const int HistoryLimit = 80;
        private readonly Queue<string> lines = new Queue<string>(HistoryLimit);
        private Func<string, bool> submit;
        private int submittedFrame = -1;
        private int openedFrame = -1;

        internal bool IsTyping => input.isFocused ||
            EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject;

        internal void Bind(Func<string, bool> post)
        {
            submit = post;
            input.characterLimit = VillageChatText.MaximumLength;
            input.onSubmit.AddListener(Send);
            send.onClick.AddListener(() => Send(input.text));
        }

        internal void OpenInput()
        {
            if (submittedFrame == Time.frameCount)
                return;
            openedFrame = Time.frameCount;
            input.Select();
            input.ActivateInputField();
        }

        internal void LeaveInput()
        {
            input.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void Send(string message)
        {
            if (submittedFrame == Time.frameCount || openedFrame == Time.frameCount)
                return;
            submittedFrame = Time.frameCount;
            if (submit(message))
                input.SetTextWithoutNotify("");
            LeaveInput();
        }

        internal void Append(string speaker, string message)
        {
            if (lines.Count == HistoryLimit)
                lines.Dequeue();
            lines.Enqueue($"{speaker}  {message}");
            messages.text = string.Join("\n", lines);
            float height = messages.GetPreferredValues(messages.text, Mathf.Max(1f, messages.rectTransform.rect.width), Mathf.Infinity).y;
            history.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(history.viewport.rect.height, height + 8f));
            history.StopMovement();
            history.verticalNormalizedPosition = 0f;
        }
    }
}
