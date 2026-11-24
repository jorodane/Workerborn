using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class HarvestWorker
    {
        private FishingBobber bobber;
        internal Vector3? FishingPoint { get; private set; }

        internal void CommandFish(FishingWater water, Vector3 point, float progress = 0f)
        {
            if (village == null || water == null || !water.CanInteract || !AcceptsInteractionCommands || agent == null || !agent.isOnNavMesh)
                return;
            if (cropTarget == water && FishingPoint is Vector3 pending && (pending - point).sqrMagnitude < .01f)
                return;
            if (!SharesMap(point) || !village.CanHarvest(this, water))
                return;
            if (!TryFindCastPath(water, point, out var path, out var stand))
            {
                Message?.Invoke("이 지점에 찌를 던질 수 있는 물가를 찾지 못했어요.");
                return;
            }

            bool alreadyThere = (agent.nextPosition - stand).sqrMagnitude <= .12f * .12f;
            if (alreadyThere)
                Cancel();
            else if (!FollowPath(path))
                return;

            cropTarget = LastCrop = water;
            FishingPoint = point;
            savedHarvestProgress = progress;
            if (alreadyThere)
                BeginHarvesting();
        }

        private bool TryFindCastPath(FishingWater water, Vector3 point, out NavMeshPath path, out Vector3 stand)
        {
            const float sampleRadius = .6f;
            const float probeStep = .65f;
            const float shorelineTolerance = .12f;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            float range = water.InteractionDistance;
            float closestShore = float.PositiveInfinity;
            float selectedDistance = float.PositiveInfinity;
            float bestTravel = float.PositiveInfinity;
            Vector3 bestPosition = default;
            NavMeshPath bestPath = null;
            var candidatePath = new NavMeshPath();

            void Consider(Vector3 candidate)
            {
                Vector3 delta = candidate - point;
                float distance = new Vector2(delta.x, delta.z).magnitude;
                if (distance > range || distance > closestShore + shorelineTolerance || water.CoversGround(candidate) ||
                    !agent.CalculatePath(candidate, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete)
                    return;

                float travel = 0f;
                Vector3 previous = agent.nextPosition;
                foreach (Vector3 corner in candidatePath.corners)
                {
                    travel += Vector3.Distance(previous, corner);
                    previous = corner;
                }
                closestShore = Mathf.Min(closestShore, distance);
                if (selectedDistance <= closestShore + shorelineTolerance && travel >= bestTravel)
                    return;

                selectedDistance = distance;
                bestTravel = travel;
                bestPosition = candidate;
                var spare = bestPath;
                bestPath = candidatePath;
                candidatePath = spare ?? new NavMeshPath();
            }

            Consider(agent.nextPosition);
            if (NavMesh.SamplePosition(point, out var nearest, range, filter))
                Consider(nearest.position);

            // The nearest NavMesh polygon may be on the other side of an impassable bank.
            for (float radius = probeStep; radius - sampleRadius <= Mathf.Min(range, closestShore + shorelineTolerance); radius += probeStep)
            {
                int samples = Mathf.CeilToInt(2f * Mathf.PI * radius / probeStep);
                for (int index = 0; index < samples; index++)
                {
                    float angle = 2f * Mathf.PI * index / samples;
                    var probe = village.SurfacePosition(point.x + radius * Mathf.Cos(angle), point.z + radius * Mathf.Sin(angle));
                    if (NavMesh.SamplePosition(probe, out var reachable, sampleRadius, filter))
                        Consider(reachable.position);
                }
            }

            path = bestPath;
            stand = bestPosition;
            return bestPath != null;
        }

        private void ShowFishingCast(Vector3 point)
        {
            if (fishingBobberPrefab == null)
                return;
            if (bobber == null)
                bobber = Instantiate(fishingBobberPrefab, transform);
            var animator = Actor?.Animator;
            var hand = animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var from = hand != null ? hand.position : transform.TransformPoint(new Vector3(.28f, 1.25f, .28f));
            bobber.Launch(from, point);
        }

        private void ClearFishingCast()
        {
            FishingPoint = null;
            if (bobber != null && bobber.gameObject.activeSelf)
                bobber.gameObject.SetActive(false);
        }
    }
}
