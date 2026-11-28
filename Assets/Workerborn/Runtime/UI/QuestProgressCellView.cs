using System.Text;
using TMPro;
using UnityEngine;

namespace Workerborn
{
    public sealed class QuestProgressCellView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text objectives;
        private readonly StringBuilder lines = new StringBuilder();

        internal void Show(QuestProgress quest)
        {
            gameObject.SetActive(true);
            title.text = quest.DisplayTitle;
            lines.Clear();
            if (quest.Finish != QuestFinish.Report)
                foreach (var objective in quest.Objectives)
                    Append(objective.Text, objective.Complete);
            if (quest.FinishText != null)
                Append(quest.FinishText, false);

            objectives.text = lines.ToString();
            objectives.gameObject.SetActive(lines.Length > 0);
            var rect = (RectTransform)transform;
            float inset = -title.rectTransform.sizeDelta.x;
            float width = Mathf.Max(1f, rect.rect.width - inset);
            float titleHeight = Mathf.Ceil(title.GetPreferredValues(title.text, width, Mathf.Infinity).y);
            title.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, titleHeight);
            float bodyTop = 10f + titleHeight + 4f;
            objectives.rectTransform.anchoredPosition = Vector2.down * bodyTop;
            float bodyHeight = lines.Length == 0 ? 0f :
                Mathf.Ceil(objectives.GetPreferredValues(objectives.text, width, Mathf.Infinity).y);
            objectives.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bodyHeight);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bodyTop + bodyHeight + 8f);
        }

        private void Append(string text, bool complete)
        {
            if (lines.Length > 0)
                lines.AppendLine();
            if (complete)
                lines.Append("<color=#8ACBFF><s>").Append(text).Append("</s></color>");
            else
                lines.Append(text);
        }
    }
}
