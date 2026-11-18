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
        [SerializeField]
        private float worldHeight = 2.25f;
        public RectTransform Rect => (RectTransform)transform;
        public Image Icon => icon;
        public Image Progress => progress;
        public float WorldHeight => worldHeight;

        public void SetProgress(float amount) => progress.fillAmount = Mathf.Clamp01(amount);
    }
}
