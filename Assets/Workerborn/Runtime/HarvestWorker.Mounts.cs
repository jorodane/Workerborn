using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class HarvestWorker
    {
        internal VillageMount CurrentMount { get; private set; }
        private Transform ridingVisual;
        private Vector3 ridingVisualBase;
        private Quaternion ridingVisualRotation;
        private float footRadius;
        private MountedWork? mountedApproach, workAfterDismount;

        private readonly struct MountedWork
        {
            internal readonly WorldTargetReference Target;
            internal readonly System.Action Resume;
            internal readonly bool FollowTarget;

            internal MountedWork(WorldInteractable target, System.Action resume, bool followTarget = false)
            {
                Target = new WorldTargetReference(target);
                Resume = resume;
                FollowTarget = followTarget;
            }
        }

        internal bool WaitingForWorkDismount => workAfterDismount.HasValue;
        internal bool CanDismountForWork => workAfterDismount is { } work && CanRideToWork(work);
        private WorldInteractable MountedWorkTarget => mountedApproach?.Target.Target ?? workAfterDismount?.Target.Target;

        internal bool CanMount => CurrentMount == null && CarriedLogs == 0 &&
            CharacterState is { AwaitingIncarnation: false, Health: > 0 };

        private float TravelSpeed(bool walking) => CurrentMount != null ? CurrentMount.Speed(walking) :
            (walking ? walkSpeed : runSpeed) * CarrySpeed * (CharacterState?.ActionSpeed ?? 1f);

        internal void InteractWithMount(VillageMount mount)
        {
            if (!IsLocallyControlled || !CanMount || !AcceptsMovementCommands ||
                village?.IsRegisteredMount(mount) != true || !SharesMap(mount.transform.position))
                return;
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

        private void ToggleSitOrRide()
        {
            if (CurrentMount != null)
            {
                RequestDismount();
                return;
            }
            if (CanMount && !IsStandingUp && AcceptsMovementCommands && Action is (WorkerAction.Idle or WorkerAction.Moving) &&
                village?.FindNearbyMount(Targeting?.CurrentTarget as VillageMount) is VillageMount mount && mount != null)
            {
                village.RequestMount(mount);
                return;
            }
            ToggleSit();
        }

        internal void RequestDismount()
        {
            if (CurrentMount == null || !AcceptsMovementCommands)
                return;
            Cancel();
            village.RequestMount(null);
        }

        private bool RideToWork(WorldInteractable target, System.Action resume, Vector3? point = null)
        {
            var work = new MountedWork(target, resume, followTarget: !point.HasValue);
            if (CurrentMount == null || !CanRideToWork(work))
                return false;

            Vector3 destination = point ?? target.transform.position;
            float offset = target == null ? Mathf.Max(0f, pickupDistance - .85f) :
                point.HasValue ? CurrentMount.DismountDistance : target.InteractionDistance * .8f;
            if (!TryApproachPath(destination, offset, out var path, out var approach) &&
                !TryApproachPath(destination, 0f, out path, out approach))
            {
                Message?.Invoke("작업할 곳 가까이 갈 수 없어요.");
                return false;
            }
            approachTargetPosition = destination;
            return FollowMountedWork(work, path, approach);
        }

        private bool FollowMountedWork(MountedWork work, NavMeshPath path, Vector3 approach)
        {
            if (!CanRideToWork(work))
                return false;
            if (InReach(approach, .16f))
                return DismountForWork(work);
            if (!FollowPath(path))
                return false;
            mountedApproach = work;
            return true;
        }

        private bool CanRideToWork(MountedWork work) => IsLocallyControlled && village != null &&
            CharacterState is { AwaitingIncarnation: false, Health: > 0 } &&
            (work.Target.IsAssigned ? work.Target.Target is { } target && AcceptsMovementCommands && SharesMap(target.transform.position) :
                AcceptsGroundItemCommands && !village.Hud.HasQuantityPrompt);

        private bool DismountForWork(MountedWork work)
        {
            Cancel();
            workAfterDismount = work;
            if (village.RequestMount(null, automaticDismount: true))
                return true;
            workAfterDismount = null;
            return false;
        }

        private bool FinishMountedApproach()
        {
            if (mountedApproach is not { } work)
                return false;

            if (work.FollowTarget && work.Target.Target is { } target &&
                !InReach(target.transform.position, target.InteractionDistance + .35f) &&
                (target.transform.position - approachTargetPosition).sqrMagnitude >= .2f * .2f)
            {
                if (!RideToWork(target, work.Resume))
                    Cancel();
            }
            else
                DismountForWork(work);
            return true;
        }

        private void ResumeDismountedWork()
        {
            if (workAfterDismount is not { } work || village.AwaitingWorldCommand)
                return;
            workAfterDismount = null;
            if (CurrentMount == null && CanRideToWork(work))
                work.Resume();
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
            ClearStandUp();
            StandUpFromBed();
            if (!agent.Warp(point.position))
                return false;

            transform.rotation = mount.transform.rotation;
            CurrentMount = mount;
            footRadius = agent.radius;
            agent.radius = Mathf.Max(footRadius, mount.NavigationRadius);
            agent.speed = TravelSpeed(false);
            ridingVisual = actor?.VisualRoot;
            if (ridingVisual != null)
            {
                ridingVisualBase = ridingVisual.localPosition;
                ridingVisualRotation = ridingVisual.localRotation;
                AlignToMount();
            }
            animationDriver?.SetRiding(mount, ridingVisual);
            return true;
        }

        internal void AlignToMount()
        {
            if (CurrentMount != null && ridingVisual != null)
                CurrentMount.PlaceRider(ridingVisual, ridingVisualBase, ridingVisualRotation);
        }

        internal void EndRiding(bool stepAside, bool parkForTravel = false)
        {
            var mount = CurrentMount;
            if (ReferenceEquals(mount, null))
                return;

            var queuedWork = stepAside && !parkForTravel ? workAfterDismount : null;
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
            {
                ridingVisual.localPosition = ridingVisualBase;
                ridingVisual.localRotation = ridingVisualRotation;
            }
            ridingVisual = null;
            if (agent == null)
                return;
            agent.radius = footRadius;
            agent.speed = TravelSpeed(false);
            if (stepAside && mount != null && agent.isOnNavMesh)
                StepOffMount(mount.DismountDistance);
            workAfterDismount = queuedWork;
        }

        private void StepOffMount(float distance)
        {
            Vector3 center = transform.position;
            Vector3 right = transform.right;
            if (!TryStep(right) && !TryStep(-right))
                TryStep(-transform.forward);

            bool TryStep(Vector3 direction)
            {
                Vector3 side = center + direction * distance;
                Vector3 surface = village.SurfacePosition(side.x, side.z);
                return NavMesh.SamplePosition(surface, out var landing, .3f, NavMesh.AllAreas) &&
                    SharesMap(landing.position) && !agent.Raycast(landing.position, out _) && agent.Warp(landing.position);
            }
        }
    }
}
