using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    [DefaultExecutionOrder(110)]
    public sealed class PickupNames : MonoBehaviour
    {
        private readonly Dictionary<WorldInteractable, WorldNameplateView> names = new Dictionary<WorldInteractable, WorldNameplateView>();
        private readonly List<WorldInteractable> removed = new List<WorldInteractable>();
        private readonly List<(Transform View, float Depth)> visible = new List<(Transform, float)>();
        private PrefabPool<WorldNameplateView> nameplates;
        private VillagePrototype game;

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

            foreach (var pair in names)
            {
                var target = pair.Key;
                var view = pair.Value;
                if ((!alt && !target.ShowNameByDefault) ||
                    !TryProject(game, canvas, target.transform.position, target.NamePosition, out var point, out float depth))
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
                view.SetBox(alt);
                view.PlaceAt(point);
                IncludeDepth(view.transform, depth);
            }

            visible.Sort(CompareDepth);
            for (int i = 0; i < visible.Count; i++)
                if (visible[i].View != null && visible[i].View.GetSiblingIndex() != i)
                    visible[i].View.SetSiblingIndex(i);
            visible.Clear();
        }

        internal void IncludeDepth(Transform view, float depth) => visible.Add((view, depth));

        private void OnDisable() => visible.Clear();

        private static int CompareDepth((Transform View, float Depth) a, (Transform View, float Depth) b)
        {
            int depth = b.Depth.CompareTo(a.Depth);
            return depth != 0 ? depth : a.View.GetInstanceID().CompareTo(b.View.GetInstanceID());
        }

        internal static bool TryProject(VillagePrototype game, RectTransform canvas, Vector3 position, Vector3 namePosition,
            out Vector2 point, out float depth)
        {
            point = default;
            depth = 0f;
            if (!game.SharesMap(position))
                return false;

            var camera = game.WorldCamera;
            Vector3 screen = camera.WorldToScreenPoint(namePosition);
            depth = screen.z;
            return screen.z >= camera.nearClipPlane && screen.z <= camera.farClipPlane &&
                WorkerbornInput.IsInView(camera, screen) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out point);
        }

        private static bool CanDisplay(WorldInteractable target) => target != null && target.isActiveAndEnabled &&
            (target.CanInteract || target is MonsterInteractable { WasRecentlyHit: true });
    }
}
