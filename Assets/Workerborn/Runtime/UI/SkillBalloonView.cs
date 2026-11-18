using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SkillBalloonView : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text title;
        [SerializeField]
        private TMP_Text remaining;
        [SerializeField]
        private Image progress;
        [SerializeField]
        private float worldHeight = 2.25f;
        public RectTransform Rect => (RectTransform)transform;
        public TMP_Text Title => title;
        public TMP_Text Remaining => remaining;
        public Image Progress => progress;
        public float WorldHeight => worldHeight;
    }
}
