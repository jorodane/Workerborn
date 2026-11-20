using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    // A pointer proxy for the same WorldInteractable as its 3D collider.
    [DefaultExecutionOrder(-40)]
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
        [SerializeField]
        private Color nameColor = new Color(1f, .98f, .91f);
        [SerializeField]
        private Color boxedNameColor = new Color(.65f, .89f, 1f);
        private bool boxVisible, pointerInside, pointerDown;
        private bool capturedScale;
        private Vector3 authoredScale;
        private PointerTargeting targeting;
        private WorldTargetReference target;
        public WorldInteractable Target => target.Target;
        public bool Hovered => boxVisible && pointerInside;
        public RectTransform Rect => (RectTransform)transform;
        internal NameplateSlot LayoutSlot { get; } = new NameplateSlot();
        internal Vector2 LayoutSize => Vector2.Scale(Rect.rect.size,
            new Vector2(Mathf.Abs(authoredScale.x), Mathf.Abs(authoredScale.y)));

        public void Bind(WorldInteractable target, PointerTargeting controller, string text = null)
        {
            if (!capturedScale)
            {
                authoredScale = Rect.localScale;
                capturedScale = true;
            }
            Rect.localScale = authoredScale;
            this.target = new WorldTargetReference(target);
            targeting = controller;
            SetText(text ?? target.DisplayName);
            SetBox(false);
        }

        internal void ApplyLayout()
        {
            var rect = Rect;
            var slot = LayoutSlot;
            var scale = new Vector3(authoredScale.x * slot.Scale, authoredScale.y * slot.Scale, authoredScale.z);
            if (rect.localScale != scale)
                rect.localScale = scale;
            Vector2 centerOffset = Vector2.Scale(rect.rect.center, scale);
            var position = rect.localPosition;
            position.x = slot.PlacedX - centerOffset.x;
            position.y = slot.PlacedY - centerOffset.y;
            if (rect.localPosition != position)
                rect.localPosition = position;
            gameObject.SetActive(slot.Visible);
        }

        internal void Unbind()
        {
            target = default;
            targeting = null;
            SetBox(false);
        }

        public void SetText(string text)
        {
            label.text = text;
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(48, label.preferredWidth + padding.x));
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(22, label.preferredHeight + padding.y));
        }

        public void SetBox(bool visible)
        {
            boxVisible = visible;
            label.raycastTarget = false;
            if (!visible)
                pointerInside = pointerDown = false;
            Refresh();
        }

        private void Awake() => SetBox(boxVisible);
        private void Update() => Refresh();
        private void Refresh()
        {
            bool alt = boxVisible && WorkerbornInput.Held("Gameplay/Names");
            var current = alt ? Target : null;
            background.raycastTarget = current != null;
            label.color = alt ? boxedNameColor : nameColor;
            if (!alt)
                pointerInside = pointerDown = false;
            bool selected = current != null && targeting != null && targeting.CurrentTarget == current;
            bool pressed = selected && targeting.IsPressed;
            background.color = !alt ? Color.clear : pressed || pointerDown ? pressedColor : selected || pointerInside ? hoverColor : normalColor;
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
