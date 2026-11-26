using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class HarvestWorker
    {
        internal VillageMount CurrentMount { get; private set; }
        private Transform ridingVisual;
        private Vector3 ridingVisualBase;
        private float footRadius;

        internal bool CanMount => CurrentMount == null && CarriedLogs == 0 &&
            CharacterState is { AwaitingIncarnation: false, Health: > 0 };

        private float TravelSpeed(bool walking) => CurrentMount != null ? CurrentMount.Speed(walking) :
            (walking ? walkSpeed : runSpeed) * CarrySpeed;

        internal void InteractWithMount(VillageMount mount)
        {
            if (!IsLocallyControlled || !AcceptsMovementCommands || !SharesMap(mount.transform.position))
                return;
            if (CurrentMount == mount)
            {
                RequestDismount();
                return;
            }
            if (!CanMount)
            {
                Message?.Invoke("지금은 말에 탈 수 없어요.");
                return;
            }
            if (!string.IsNullOrEmpty(mount.Rider))
            {
                Message?.Invoke("다른 사람이 타고 있어요.");
                return;
            }
            if (InReach(mount.transform.position, mount.InteractionDistance))
                village.RequestMount(mount);
            else
                CommandApproach(mount, () => village.RequestMount(mount));
        }

        private void RequestDismount()
        {
            if (CurrentMount == null || !AcceptsMovementCommands)
                return;
            Cancel();
            village.RequestMount(null);
        }

        internal bool BeginRiding(VillageMount mount)
        {
            if (CurrentMount == mount)
                return true;
            if (!CanMount || agent == null || !agent.isOnNavMesh || !SharesMap(mount.transform.position) ||
                !NavMesh.SamplePosition(mount.transform.position, out var point, .65f, NavMesh.AllAreas))
                return false;

            Cancel();
            FinishVisualJump();
            StandUpFromBed();
            if (!agent.Warp(point.position))
                return false;

            transform.rotation = mount.transform.rotation;
            CurrentMount = mount;
            footRadius = agent.radius;
            agent.radius = Mathf.Max(footRadius, .48f);
            agent.speed = TravelSpeed(false);
            ridingVisual = actor?.VisualRoot;
            if (ridingVisual != null)
            {
                ridingVisualBase = ridingVisual.localPosition;
                ridingVisual.localPosition = ridingVisualBase + mount.RiderOffset;
            }
            animationDriver?.SetRiding(mount);
            return true;
        }

        internal void EndRiding(bool stepAside, bool parkForTravel = false)
        {
            var mount = CurrentMount;
            if (ReferenceEquals(mount, null))
                return;

            if (mount != null)
            {
                mount.Present(null, null, transform.position, transform.eulerAngles.y, true);
                if (parkForTravel)
                    village.ParkMountForTravel(mount);
            }
            CurrentMount = null;
            Cancel();
            animationDriver?.SetRiding(null);
            if (ridingVisual != null)
                ridingVisual.localPosition = ridingVisualBase;
            ridingVisual = null;
            if (agent == null)
                return;
            agent.radius = footRadius;
            agent.speed = TravelSpeed(false);
            if (stepAside && agent.isOnNavMesh)
                StepOffMount();
        }

        private void StepOffMount()
        {
            Vector3 center = transform.position;
            Vector3 right = transform.right;
            if (!TryStep(right) && !TryStep(-right))
                TryStep(-transform.forward);

            bool TryStep(Vector3 direction)
            {
                Vector3 side = center + direction * .95f;
                Vector3 surface = village.SurfacePosition(side.x, side.z);
                return NavMesh.SamplePosition(surface, out var landing, .3f, NavMesh.AllAreas) &&
                    SharesMap(landing.position) && !agent.Raycast(landing.position, out _) && agent.Warp(landing.position);
            }
        }
    }
}
