using System;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class UiActionView : MonoBehaviour
    {
        [SerializeField]
        private Text label;
        [SerializeField]
        private Button button;
        public void Bind(string title, Action action, bool enabled = true)
        {
            label.text = title;
            button.interactable = enabled;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
        }
    }
}
