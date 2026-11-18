using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    // A pointer proxy for the same WorldInteractable as its 3D collider.
    public class WorldNameplateView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField]
        private TMP_Text label;
        [SerializeField]
        private Image background;
        [SerializeField]
        private Vector2 padding = new Vector2(14f, 6f);
        [SerializeField]
        private Color normalColor = new Color(0, 0, 0, .65f);
        [SerializeField]
        private Color hoverColor = new Color(.23f, .29f, .32f, .85f);
        [SerializeField]
        private Color pressedColor = new Color(.42f, .31f, .12f, .95f);
        private bool boxVisible, pointerInside, pointerDown;
        private PointerTargeting targeting;
        public WorldInteractable Target { get; private set; }
        public bool Hovered => boxVisible && pointerInside;
        public RectTransform Rect => (RectTransform)transform;

        public void Bind(WorldInteractable target, PointerTargeting controller, string text = null)
        {
            Target = target;
            targeting = controller;
            SetText(text ?? target.DisplayName);
        }

        public void SetText(string text)
        {
            if (label.text == text)
                return;
            label.text = text;
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(48, label.preferredWidth + padding.x));
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(22, label.preferredHeight + padding.y));
        }

        public void SetBox(bool visible)
        {
            boxVisible = visible;
            background.raycastTarget = visible && Target != null && Target.CanInteract;
            label.raycastTarget = false;
            if (!visible)
                pointerInside = pointerDown = false;
            Refresh();
        }

        private void LateUpdate() => Refresh();
        private void Refresh()
        {
            bool selected = targeting != null && targeting.CurrentTarget == Target && Target != null;
            bool pressed = selected && targeting.IsPressed;
            background.color = !boxVisible ? Color.clear : pressed || pointerDown ? pressedColor : selected || pointerInside ? hoverColor : normalColor;
        }

        public void OnPointerEnter(PointerEventData data)
        {
            pointerInside = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData data)
        {
            pointerInside = pointerDown = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                pointerDown = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData data)
        {
            pointerDown = false;
            Refresh();
        }

        private void OnDisable()
        {
            pointerInside = pointerDown = false;
        }
    }
}
