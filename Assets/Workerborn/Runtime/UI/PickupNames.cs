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
        private PrefabPool<PickupNameView> pickupViews;
        private PrefabPool<WorldNameplateView> worldViews;
        private VillagePrototype game;
        private float scroll;
        private long? scrollOrder;

        public void Initialize(VillagePrototype owner, int prewarm)
        {
            game = owner;
            if (namePrefab != null)
            {
                pickupViews = new PrefabPool<PickupNameView>(namePrefab, transform);
                pickupViews.Prewarm(prewarm);
            }
            worldViews = new PrefabPool<WorldNameplateView>(game.Hud.Library.NpcName, transform);
        }

        private void LateUpdate()
        {
            if (game == null || pickupViews == null)
                return;
            bool alt = WorkerbornInput.Held("Gameplay/Names");
            var canvas = (RectTransform)transform;

            removed.Clear();
            foreach (var pair in names)
                if (pair.Value.Target == null)
                    removed.Add(pair.Key);
            foreach (var item in removed)
            {
                var view = names[item];
                view.Unbind();
                if (view is PickupNameView pickupView)
                    pickupViews.Return(pickupView);
                else
                    worldViews.Return(view);
                names.Remove(item);
            }

            foreach (var target in WorldInteractable.Active)
            {
                if (target == null || !target.CanInteract || !target.ShowNameByDefault && !alt || names.ContainsKey(target))
                    continue;
                WorldNameplateView view;
                if (target is GroundPickup pickup)
                {
                    var pickupView = pickupViews.Rent();
                    pickupView.Bind(pickup, game.Worker);
                    view = pickupView;
                }
                else
                {
                    view = worldViews.Rent();
                    view.Bind(target, game.Targeting);
                }
                names.Add(target, view);
            }

            slots.Clear();
            visible.Clear();
            long? hoveredOrder = null;
            foreach (var pair in names)
            {
                var screen = game.WorldCamera.WorldToScreenPoint(pair.Key.NamePosition);
                bool onScreen = pair.Key.CanInteract && (alt || pair.Key.ShowNameByDefault) &&
                    screen.z > game.WorldCamera.nearClipPlane && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
                if (!onScreen)
                {
                    pair.Value.gameObject.SetActive(false);
                    continue;
                }
                pair.Value.SetBox(alt);
                if (pair.Value.Hovered)
                    hoveredOrder = pair.Key.NameplateOrder;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var point);
                Vector2 size = pair.Value.Rect.sizeDelta;
                float x = alt ? Mathf.Clamp(point.x, canvas.rect.xMin + edgeMargin + size.x * .5f, canvas.rect.xMax - edgeMargin - size.x * .5f) : point.x;
                var objectScreen = game.WorldCamera.WorldToScreenPoint(pair.Key.transform.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, objectScreen, null, out var objectPoint);
                slots.Add(new NameplateSlot { Order = pair.Key.NameplateOrder, X = x, Y = point.y, ObjectY = objectPoint.y, Width = size.x, Height = size.y });
                visible.Add(pair.Key.NameplateOrder, pair.Value);
            }

            if (alt && hoveredOrder.HasValue && WorkerbornInput.Current != null)
            {
                float delta = WorkerbornInput.Scroll;
                if (Mathf.Abs(delta) >= 10f)
                    delta /= 120f;
                if (delta != 0f)
                {
                    scrollOrder = hoveredOrder;
                    scroll -= delta * 30f;
                }
            }

            if (!alt)
            {
                scroll = 0f;
                scrollOrder = null;
            }
            float overflow = NameplateLayout.Present(slots, alt, canvas.rect.yMin + edgeMargin + 32, canvas.rect.yMax - edgeMargin,
                spacing, scroll, scrollOrder);
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
