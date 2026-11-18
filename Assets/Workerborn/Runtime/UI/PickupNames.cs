using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Workerborn
{
    public sealed class PickupNames : MonoBehaviour
    {
        [SerializeField]
        private PickupNameView namePrefab;
        [SerializeField]
        private float spacing = 3f;
        [SerializeField]
        private float edgeMargin = 8f;
        private readonly Dictionary<GroundPickup, PickupNameView> names = new Dictionary<GroundPickup, PickupNameView>();
        private readonly List<GroundPickup> removed = new List<GroundPickup>();
        private readonly List<NameplateSlot> slots = new List<NameplateSlot>();
        private readonly Dictionary<long, PickupNameView> visible = new Dictionary<long, PickupNameView>();
        private VillagePrototype game;
        private float scroll;
        public void Initialize(VillagePrototype owner) => game = owner;
        private void LateUpdate()
        {
            if (game == null || namePrefab == null)
                return;
            bool alt = Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
            var canvas = (RectTransform)transform;
            foreach (var target in WorldInteractable.Active)
            {
                var pickup = target as GroundPickup;
                if (pickup == null || names.ContainsKey(pickup))
                    continue;
                var view = Instantiate(namePrefab, transform);
                view.Bind(pickup, game.Worker);
                names.Add(pickup, view);
            }

            slots.Clear();
            visible.Clear();
            removed.Clear();
            bool hovering = false;
            foreach (var pair in names)
            {
                if (pair.Key == null || !pair.Key.isActiveAndEnabled)
                {
                    removed.Add(pair.Key);
                    continue;
                }

                var screen = game.WorldCamera.WorldToScreenPoint(pair.Key.NamePosition);
                bool onScreen = screen.z > game.WorldCamera.nearClipPlane && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
                pair.Value.gameObject.SetActive(onScreen);
                if (!onScreen)
                    continue;
                pair.Value.SetBox(alt);
                hovering |= pair.Value.Hovered;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var point);
                Vector2 size = pair.Value.Rect.sizeDelta;
                float x = Mathf.Clamp(point.x, canvas.rect.xMin + edgeMargin + size.x * .5f, canvas.rect.xMax - edgeMargin - size.x * .5f);
                slots.Add(new NameplateSlot { Order = pair.Key.CreationOrder, X = x, Y = point.y, Width = size.x, Height = size.y });
                visible.Add(pair.Key.CreationOrder, pair.Value);
            }

            foreach (var item in removed)
            {
                if (names[item] != null)
                    Destroy(names[item].gameObject);
                names.Remove(item);
            }

            if (alt && hovering && Mouse.current != null)
            {
                float delta = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(delta) >= 10f)
                    delta /= 120f;
                scroll -= delta * 30f;
            }

            foreach (var item in removed)
            {
                if (names.TryGetValue(item, out var view) && view != null)
                    Destroy(view.gameObject);
                names.Remove(item);
            }

            if (!alt)
                scroll = 0f;
            float overflow = NameplateLayout.Arrange(slots, canvas.rect.yMin + edgeMargin + 32, canvas.rect.yMax - edgeMargin, spacing, scroll);
            scroll = Mathf.Clamp(scroll, 0, overflow);
            foreach (var slot in slots)
            {
                var view = visible[slot.Order];
                view.Rect.anchoredPosition = new Vector2(slot.X, slot.PlacedY);
                view.gameObject.SetActive(slot.PlacedY - slot.Height * .5f >= canvas.rect.yMin + 32 && slot.PlacedY + slot.Height * .5f <= canvas.rect.yMax);
            }
        }
    }
}
