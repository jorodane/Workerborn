using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    // An interactive name can act as a pointer proxy for its 3D target.
    [DefaultExecutionOrder(-40)]
    public sealed class WorldNameplateView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField]
        private TMP_Text label;
        [SerializeField]
        private Image background;
        [SerializeField] private HealthBarView healthBar;
        [SerializeField] private Image trackingLeft;
        [SerializeField] private Image trackingRight;
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
        [SerializeField] private Color interactionName = new Color(.65f, .89f, 1f);
        [SerializeField] private Color npcName = new Color(1f, .9f, .48f);
        [SerializeField] private Color itemName = Color.white;
        [SerializeField] private Color enemyName = new Color(1f, .63f, .35f);
        private bool boxVisible, pointerInside, pointerDown;
        private int shownPickupQuantity;
        private PointerTargeting targeting;
        private WorldTargetReference target;
        public WorldInteractable Target => target.Target;
        internal WorldInteractable Subject => target.Subject;
        public RectTransform Rect => (RectTransform)transform;
        internal float VisibleHealthHeight => healthBar.gameObject.activeSelf ? ((RectTransform)healthBar.transform).rect.height + 2f : 0f;

        // The ReplyBubble sprite's top stroke is inset by 3 pixels. Slicing scales the borders below their combined height.
        internal static float SpeechRimInset(float bubbleHeight) => 3f * Mathf.Clamp01(bubbleHeight / 66f);

        public void Bind(WorldInteractable target, PointerTargeting controller, string text = null)
        {
            this.target = new WorldTargetReference(target);
            targeting = controller;
            shownPickupQuantity = target is GroundPickup item ? item.Quantity : 0;
            SetText(text ?? target.DisplayName);
            if (target is MonsterInteractable { Combat: not null } monster)
                SetHealth(false, monster.Health, monster.Combat.Health);
            else
                SetHealth(false);
            SetTracked(false);
            SetBox(false);
        }

        internal void PlaceAt(Vector2 center)
        {
            var rect = Rect;
            Vector2 centerOffset = Vector2.Scale(rect.rect.center, rect.localScale);
            var position = rect.localPosition;
            position.x = center.x - centerOffset.x;
            position.y = center.y - centerOffset.y;
            if (rect.localPosition != position)
                rect.localPosition = position;
            gameObject.SetActive(true);
        }

        internal void Unbind()
        {
            target = default;
            targeting = null;
            SetHealth(false);
            SetTracked(false);
            SetBox(false);
        }

        public void SetText(string text)
        {
            label.text = text;
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(48, label.preferredWidth + padding.x));
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(22, label.preferredHeight + padding.y));
        }

        internal void SetHealth(bool visible, int health = 0, int maximum = 0)
        {
            visible &= maximum > 0;
            healthBar.SetValue(health, maximum);
            if (healthBar.gameObject.activeSelf != visible)
                healthBar.gameObject.SetActive(visible);
        }

        internal void SetTracked(bool tracked)
        {
            if (trackingLeft.gameObject.activeSelf == tracked)
                return;
            trackingLeft.gameObject.SetActive(tracked);
            trackingRight.gameObject.SetActive(tracked);
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
            if (Subject is GroundPickup item && item.Quantity != shownPickupQuantity)
            {
                shownPickupQuantity = item.Quantity;
                SetText(item.DisplayName);
            }
            bool alt = boxVisible && WorkerbornInput.Held("Gameplay/Names");
            var current = alt ? Target : null;
            background.raycastTarget = current != null;
            label.color = !alt ? nameColor : Subject switch
            {
                null => nameColor,
                MonsterInteractable => enemyName,
                GroundPickup => itemName,
                AnimalNpc => npcName,
                VillageInteractable npc when npc.Role != VillageRole.NoticeBoard && npc.Role != VillageRole.Shrine => npcName,
                _ => interactionName
            };
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
