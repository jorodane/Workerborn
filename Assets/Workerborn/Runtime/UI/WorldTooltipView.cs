using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class WorldTooltipView : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text label;
        [SerializeField]
        private Vector2 cursorOffset = new Vector2(18, -22);
        public Vector2 CursorOffset => cursorOffset;
        public TMP_Text Label => label;
        public RectTransform Rect => (RectTransform)transform;
    }
}
