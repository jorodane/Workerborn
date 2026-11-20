using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class UiListRow : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text title;
        [SerializeField]
        private TMP_Text details;
        [SerializeField]
        private Image icon;
        [SerializeField]
        private Image progress;
        [SerializeField]
        private Button select;
        private Color titleColor;

        private void Awake() => titleColor = title.color;
        public void HighlightTitle(bool selected) => title.color = selected ? new Color(1f, .88f, .55f) : titleColor;

        public void Bind(string name, string description, Sprite sprite, float ratio, Action click)
        {
            title.text = name;
            details.text = description;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            progress.fillAmount = Mathf.Clamp01(ratio);
            select.onClick.RemoveAllListeners();
            select.interactable = click != null;
            if (click != null)
                select.onClick.AddListener(() => click());
        }
    }
}
