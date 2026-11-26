using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SkillBalloonView : MonoBehaviour
    {
        [SerializeField]
        private Image icon;
        [SerializeField]
        private Image progress;
        [SerializeField] private Image nextAction;
        [SerializeField]
        private float worldHeight = 2.25f;
        public RectTransform Rect => (RectTransform)transform;
        public Image Icon => icon;
        public Image Progress => progress;
        public float WorldHeight => worldHeight;

        private Sprite defaultIcon;
        private int workSequence = -1;
        private void Awake() => defaultIcon = icon.sprite;
        public void SetIcon(Sprite sprite) => icon.sprite = sprite != null ? sprite : defaultIcon;
        public void SetProgress(float amount)
        {
            workSequence = -1;
            progress.fillAmount = Mathf.Clamp01(amount);
            if (nextAction != null)
                nextAction.fillAmount = 0f;
        }

        internal void SetWorkSteps(float confirmed, float next, int sequence)
        {
            if (workSequence != sequence)
            {
                workSequence = sequence;
                progress.fillAmount = confirmed;
            }
            else
                progress.fillAmount = Mathf.MoveTowards(progress.fillAmount, confirmed, Time.unscaledDeltaTime * 8f);
            if (nextAction != null)
                nextAction.fillAmount = progress.fillAmount < confirmed - .001f ? confirmed : next;
        }
    }
}
