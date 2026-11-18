using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SkillBalloonView : MonoBehaviour
    {
        [SerializeField]
        private Text title;
        [SerializeField]
        private Text remaining;
        [SerializeField]
        private Image progress;
        [SerializeField]
        private float worldHeight = 2.25f;
        public RectTransform Rect => (RectTransform)transform;
        public Text Title => title;
        public Text Remaining => remaining;
        public Image Progress => progress;
        public float WorldHeight => worldHeight;
    }
}
