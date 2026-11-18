using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class WorldTooltipView : MonoBehaviour
    {
        [SerializeField]
        private Text label;
        public Text Label => label;
        public RectTransform Rect => (RectTransform)transform;
    }
}
