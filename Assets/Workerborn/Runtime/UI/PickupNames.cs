using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class PickupNames : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        private float spacing = 3f;
        [SerializeField, Min(0f)]
        private float edgeMargin = 8f;
        private readonly Dictionary<WorldInteractable, WorldNameplateView> names = new Dictionary<WorldInteractable, WorldNameplateView>();
        private readonly List<WorldInteractable> removed = new List<WorldInteractable>();
        private readonly List<NameplateSlot> slots = new List<NameplateSlot>();
        private readonly List<WorldNameplateView> visible = new List<WorldNameplateView>();
        private PrefabPool<WorldNameplateView> nameplates;
        private VillagePrototype game;
        private float scroll;
        private long? scrollOrder;

        public void Initialize(VillagePrototype owner, int prewarm)
        {
            game = owner;
            nameplates = new PrefabPool<WorldNameplateView>(game.Hud.Library.Nameplate, transform);
            nameplates.Prewarm(prewarm);
        }

        private void LateUpdate()
        {
            if (game == null)
                return;
            bool alt = WorkerbornInput.Held("Gameplay/Names");
            var canvas = (RectTransform)transform;

            removed.Clear();
            foreach (var pair in names)
                if (!CanDisplay(pair.Value.Subject))
                    removed.Add(pair.Key);
            foreach (var item in removed)
            {
                var view = names[item];
                view.Unbind();
                nameplates.Return(view);
                names.Remove(item);
            }

            foreach (var target in WorldInteractable.Active)
            {
                if (!CanDisplay(target) || !game.SharesMap(target.transform.position) ||
                    !target.ShowNameByDefault && !alt || names.ContainsKey(target))
                    continue;
                var view = nameplates.Rent();
                view.Bind(target, game.Targeting);
                names.Add(target, view);
            }

            slots.Clear();
            visible.Clear();
            long? hoveredOrder = null;
            foreach (var pair in names)
            {
                var screen = game.WorldCamera.WorldToScreenPoint(pair.Key.NamePosition);
                bool onScreen = game.SharesMap(pair.Key.transform.position) && (alt || pair.Key.ShowNameByDefault) &&
                    screen.z > game.WorldCamera.nearClipPlane && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
                if (!onScreen)
                {
                    pair.Value.gameObject.SetActive(false);
                    continue;
                }
                pair.Value.SetBox(alt);
                if (pair.Key is MonsterInteractable { Combat: not null } monster)
                {
                    var targeting = game.Targeting;
                    bool showHealth = monster.WasRecentlyHit || game.Worker.IsInteractingWith(monster) ||
                        targeting != null && (targeting.CurrentTarget == monster || targeting.IsOverNameplate(pair.Value));
                    pair.Value.SetHealth(showHealth, monster.Health, monster.Combat.Health);
                }
                if (pair.Value.Hovered)
                    hoveredOrder = pair.Key.NameplateOrder;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var point);
                Vector2 size = pair.Value.LayoutSize;
                var slot = pair.Value.LayoutSlot;
                slot.Order = pair.Key.NameplateOrder;
                slot.X = point.x;
                slot.Y = point.y;
                if (alt)
                {
                    Vector3 position = pair.Key.transform.position;
                    Vector3 ground = game.SurfacePosition(position.x, position.z);
                    var groundScreen = game.WorldCamera.WorldToScreenPoint(ground);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, groundScreen, null, out var groundPoint);
                    slot.ObjectY = groundPoint.y;
                }
                slot.Width = size.x;
                slot.Height = size.y;
                slots.Add(slot);
                visible.Add(pair.Value);
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
                spacing, scroll, scrollOrder, canvas.rect.xMin + edgeMargin, canvas.rect.xMax - edgeMargin);
            scroll = Mathf.Clamp(scroll, 0, overflow);
            foreach (var view in visible)
                view.ApplyLayout();
        }

        private static bool CanDisplay(WorldInteractable target) => target != null && target.isActiveAndEnabled &&
            (target.CanInteract || target is MonsterInteractable { WasRecentlyHit: true });
    }
}
