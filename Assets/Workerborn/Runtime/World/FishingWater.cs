using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class FishingWater : HarvestableCrop
    {
        [SerializeField, Tooltip("Matches the visible water. Use a MeshCollider for an irregular shoreline.")]
        private Collider surface;

        protected override float DefaultDistance => 7f;
        public override bool CanInteract => surface != null && surface.enabled && base.CanInteract;

        public void CastFromPointer(HarvestWorker worker, Ray pointerRay)
        {
            if (TryGetCastPoint(pointerRay, out var point) && BeginInteraction(worker))
                worker.CommandFish(this, point);
        }

        internal override void RequestInteraction(HarvestWorker worker, Ray pointerRay) => CastFromPointer(worker, pointerRay);

        protected override void Interact(HarvestWorker worker)
        {
            var camera = worker.Village?.WorldCamera;
            if (camera != null && TryGetCastPoint(camera.ScreenPointToRay(WorkerbornInput.Point), out var point))
                worker.CommandFish(this, point);
        }

        internal override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions, Ray pointerRay)
        {
            if (TryGetCastPoint(pointerRay, out var point))
                actions.Add(new WorldContextAction(InteractionVerb, () =>
                {
                    if (BeginInteraction(village.Worker))
                        village.Worker.CommandFish(this, point);
                }));
        }

        private bool TryGetCastPoint(Ray ray, out Vector3 point)
        {
            point = default;
            if (surface == null || !surface.Raycast(ray, out var hit, Mathf.Infinity))
                return false;
            point = hit.point;
            return true;
        }

        internal bool CoversGround(Vector3 point)
        {
            Bounds bounds = surface.bounds;
            float aboveWater = bounds.max.y + .06f;
            if (point.y > aboveWater || point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z)
                return false;
            var ray = new Ray(new Vector3(point.x, aboveWater, point.z), Vector3.down);
            return surface.Raycast(ray, out var hit, bounds.size.y + .12f) && point.y <= hit.point.y + .06f;
        }
    }
}
