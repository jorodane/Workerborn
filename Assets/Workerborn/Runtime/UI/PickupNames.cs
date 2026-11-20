using System.Collections.Generic;
using UnityEngine;

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
        private readonly Dictionary<WorldInteractable, WorldNameplateView> names = new Dictionary<WorldInteractable, WorldNameplateView>();
        private readonly List<WorldInteractable> removed = new List<WorldInteractable>();
        private readonly List<NameplateSlot> slots = new List<NameplateSlot>();
        private readonly Dictionary<long, WorldNameplateView> visible = new Dictionary<long, WorldNameplateView>();
        private VillagePrototype game;
        private float scroll;
        public void Initialize(VillagePrototype owner) => game = owner;
        private void LateUpdate()
        {
            if (game == null || namePrefab == null)
                return;
            bool alt = WorkerbornInput.Held("Gameplay/Names");
            var canvas = (RectTransform)transform;
            foreach (var target in WorldInteractable.Active)
            {
                if (target == null || !target.CanInteract || !target.ShowNameByDefault && !alt || names.ContainsKey(target))
                    continue;
                WorldNameplateView view;
                if (target is GroundPickup pickup)
                {
                    var pickupView = Instantiate(namePrefab, transform);
                    pickupView.Bind(pickup, game.Worker);
                    view = pickupView;
                }
                else
                {
                    view = Instantiate(game.Hud.Library.NpcName, transform);
                    view.Bind(target, game.Targeting);
                }
                names.Add(target, view);
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
                bool onScreen = pair.Key.CanInteract && (alt || pair.Key.ShowNameByDefault) &&
                    screen.z > game.WorldCamera.nearClipPlane && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
                pair.Value.gameObject.SetActive(onScreen);
                if (!onScreen)
                    continue;
                pair.Value.SetBox(alt);
                hovering |= pair.Value.Hovered;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var point);
                Vector2 size = pair.Value.Rect.sizeDelta;
                float x = alt ? Mathf.Clamp(point.x, canvas.rect.xMin + edgeMargin + size.x * .5f, canvas.rect.xMax - edgeMargin - size.x * .5f) : point.x;
                slots.Add(new NameplateSlot { Order = pair.Key.NameplateOrder, X = x, Y = point.y, Width = size.x, Height = size.y });
                visible.Add(pair.Key.NameplateOrder, pair.Value);
            }

            foreach (var item in removed)
            {
                if (names[item] != null)
                    Destroy(names[item].gameObject);
                names.Remove(item);
            }

            if (alt && hovering && WorkerbornInput.Current != null)
            {
                float delta = WorkerbornInput.Scroll;
                if (Mathf.Abs(delta) >= 10f)
                    delta /= 120f;
                scroll -= delta * 30f;
            }

            if (!alt)
                scroll = 0f;
            float overflow = NameplateLayout.Present(slots, alt, canvas.rect.yMin + edgeMargin + 32, canvas.rect.yMax - edgeMargin, spacing, scroll);
            scroll = Mathf.Clamp(scroll, 0, overflow);
            foreach (var slot in slots)
            {
                var view = visible[slot.Order];
                view.Rect.anchoredPosition = new Vector2(slot.X, slot.PlacedY);
                view.gameObject.SetActive(!alt || slot.PlacedY - slot.Height * .5f >= canvas.rect.yMin + 32 && slot.PlacedY + slot.Height * .5f <= canvas.rect.yMax);
            }
        }
    }
}
