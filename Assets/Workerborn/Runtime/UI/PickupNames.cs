using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    // Player overhead positions are submitted by the HUD and remote avatars during LateUpdate.
    [DefaultExecutionOrder(110)]
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
        private readonly List<PlayerOverheadView> players = new List<PlayerOverheadView>();
        private PrefabPool<WorldNameplateView> nameplates;
        private VillagePrototype game;

        public void Initialize(VillagePrototype owner, int prewarm)
        {
            game = owner;
            nameplates = new PrefabPool<WorldNameplateView>(game.Hud.Library.Nameplate, transform);
            nameplates.Prewarm(prewarm);
        }

        internal void IncludePlayer(PlayerOverheadView player, float? objectY)
        {
            players.Add(player);
            slots.Add(player.Nameplate.PrepareLayout(player.NameCenter, objectY, true));
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

            visible.Clear();
            foreach (var pair in names)
            {
                var target = pair.Key;
                var view = pair.Value;
                if ((!alt && !target.ShowNameByDefault) ||
                    !TryProject(game, canvas, target.transform.position, target.NamePosition, alt, out var point, out var objectY))
                {
                    view.gameObject.SetActive(false);
                    continue;
                }
                if (target is MonsterInteractable { Combat: not null } monster)
                {
                    var targeting = game.Targeting;
                    bool showHealth = alt || monster.WasRecentlyHit || game.Worker.IsInteractingWith(monster) ||
                        targeting != null && (targeting.CurrentTarget == monster || targeting.IsOverNameplate(view));
                    view.SetHealth(showHealth, monster.Health, monster.Combat.Health);
                }
                slots.Add(view.PrepareLayout(point, objectY, alt));
                visible.Add(view);
            }

            var bounds = canvas.rect;
            NameplateLayout.Present(slots, alt, bounds.yMin + edgeMargin + 32, bounds.yMax - edgeMargin,
                spacing, bounds.xMin + edgeMargin, bounds.xMax - edgeMargin);
            foreach (var view in visible)
                view.ApplyLayout();
            foreach (var player in players)
                if (player != null)
                    player.ApplyNameLayout();
            slots.Clear();
            players.Clear();
        }

        private void OnDisable()
        {
            slots.Clear();
            players.Clear();
        }

        internal static bool TryProject(VillagePrototype game, RectTransform canvas, Vector3 position, Vector3 namePosition,
            bool alt, out Vector2 point, out float? objectY)
        {
            point = default;
            objectY = null;
            if (!game.SharesMap(position))
                return false;

            var camera = game.WorldCamera;
            Vector3 screen = camera.WorldToScreenPoint(namePosition);
            bool nameProjectable = CanProject(camera, screen);
            Vector3 groundScreen = alt ? camera.WorldToScreenPoint(game.SurfacePosition(position.x, position.z)) : default;
            bool groundProjectable = alt && CanProject(camera, groundScreen);
            if (!(nameProjectable && camera.pixelRect.Contains((Vector2)screen)) &&
                !(groundProjectable && camera.pixelRect.Contains((Vector2)groundScreen)))
                return false;

            if (groundProjectable && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, groundScreen, null, out var groundPoint))
                objectY = groundPoint.y;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, nameProjectable ? screen : groundScreen, null, out point);
        }

        private static bool CanProject(Camera camera, Vector3 screen) => screen.z >= camera.nearClipPlane &&
            screen.z <= camera.farClipPlane && WorkerbornInput.IsFinite((Vector2)screen);

        private static bool CanDisplay(WorldInteractable target) => target != null && target.isActiveAndEnabled &&
            (target.CanInteract || target is MonsterInteractable { WasRecentlyHit: true });
    }
}
