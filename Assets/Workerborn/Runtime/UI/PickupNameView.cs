using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class PickupNameView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField]
        private Text label;
        [SerializeField]
        private Image background;
        [SerializeField]
        private Vector2 padding = new Vector2(14f, 6f);
        [SerializeField]
        private Color boxColor = new Color(0, 0, 0, .65f);
        private GroundPickup pickup;
        private HarvestWorker worker;
        public bool Hovered { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        public void Bind(GroundPickup item, HarvestWorker owner)
        {
            pickup = item;
            worker = owner;
            label.text = item.DisplayName + (item.Quantity > 1 ? " ×" + item.Quantity : "");
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(48, label.preferredWidth + padding.x));
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(22, label.preferredHeight + padding.y));
        }

        public void SetBox(bool visible)
        {
            background.color = visible ? boxColor : Color.clear;
            background.raycastTarget = visible && pickup != null && pickup.CanCollect;
            label.raycastTarget = false;
            if (!visible)
                Hovered = false;
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && background.raycastTarget && pickup != null)
                pickup.RequestInteraction(worker);
        }

        public void OnPointerEnter(PointerEventData data) => Hovered = true;
        public void OnPointerExit(PointerEventData data) => Hovered = false;
    }
}
