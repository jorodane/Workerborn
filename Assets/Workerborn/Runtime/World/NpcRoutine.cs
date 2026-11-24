using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    internal sealed class NpcRoutine : MonoBehaviour
    {
        private const float RetryDelay = .8f;
        private VillagePrototype game;
        private VillageInteractable npc;
        private HarvestWorker character;
        private NpcScheduleEntry scheduled;
        private FacilityTarget goal;
        private Vector3 goalPosition;
        private float retryAt;
        private bool planned;
        private bool paused;
        private NpcActivity waitingActivity;

        internal NpcActivity Activity => character.Action switch
        {
            WorkerAction.Moving => NpcActivity.Walking,
            WorkerAction.Harvesting => NpcActivity.Harvesting,
            WorkerAction.Sitting => NpcActivity.Resting,
            _ => waitingActivity
        };
        internal bool IsSeated => character.OccupiedSeat != null;

        internal void Bind(VillagePrototype owner, VillageInteractable subject, HarvestWorker worker)
        {
            game = owner;
            npc = subject;
            character = worker;
        }

        internal bool ReservesSeat(Vector3 position) => goal.Seat != null &&
            (goal.Seat.Position - position).sqrMagnitude < .04f;

        private void Update()
        {
            if (game == null || !game.CanRunNpcSchedules || character.IsLocallyControlled || !character.EnsureNavigation())
                return;
            if (game.Worker.IsInteractingWith(npc) || game.Hud.IsSpeakingWith(npc))
            {
                if (!paused)
                {
                    paused = true;
                    var interruptedActivity = Activity;
                    if (character.Action is WorkerAction.Moving or WorkerAction.Harvesting)
                        ClearGoal();
                    waitingActivity = interruptedActivity;
                }
                return;
            }
            if (paused)
            {
                paused = false;
                waitingActivity = NpcActivity.Idle;
            }

            var next = NpcScheduleEntry.Resolve(npc.Definition.DailySchedule, game.State, npc.Definition.Job);
            if (scheduled != next)
            {
                if (scheduled != null || character.Action == WorkerAction.Sitting && next?.Activity != NpcActivity.Resting)
                    ClearGoal();
                scheduled = next;
            }
            if (planned && !GoalStillAvailable())
                ClearGoal();
            if (scheduled?.Facility == null || Time.time < retryAt)
                return;

            if (character.Action == WorkerAction.Sitting)
                return;
            if (character.Action != WorkerAction.Idle)
                return;

            retryAt = Time.time + RetryDelay;
            if (goal.Seat != null && TrySit())
                return;
            if (scheduled.Activity == NpcActivity.Harvesting && TryCollectHarvest())
                return;

            if (scheduled.Activity == NpcActivity.Resting || scheduled.Activity == NpcActivity.Harvesting)
            {
                var target = FindTarget(scheduled.Activity);
                if (target.Facility != null)
                {
                    StartTask(target);
                    return;
                }
                if (scheduled.Activity == NpcActivity.Resting)
                    return;
            }

            var activity = scheduled.Activity == NpcActivity.Harvesting ? NpcActivity.Working : scheduled.Activity;
            var standing = FindTarget(activity);
            if (standing.Facility != null)
                StartTask(standing);
        }

        private void StartTask(FacilityTarget target)
        {
            planned = true;
            goal = target;
            goalPosition = target.Position;
            waitingActivity = target.Activity == NpcActivity.Harvesting ? NpcActivity.Working : NpcActivity.Idle;
            if (target.Crop != null)
            {
                target.Crop.RequestInteraction(character);
                return;
            }
            if (target.Seat != null)
            {
                if (!TrySit())
                    character.CommandMove(goalPosition, walk: true);
                return;
            }
            if (character.InReach(goalPosition, .25f))
                waitingActivity = target.Activity;
            else
                character.CommandMove(goalPosition, walk: true);
        }

        private bool TrySit()
        {
            if (!goal.Seat.Covers(character.StandingPosition))
                return false;
            if (character.ToggleSit(goal.Seat))
                return true;
            ClearGoal();
            return false;
        }

        private bool TryCollectHarvest()
        {
            GroundPickup nearest = null;
            float bestDistance = float.PositiveInfinity;
            bool landing = false;
            foreach (var target in WorldInteractable.Active)
            {
                if (target is not GroundPickup item || !item.CanCollect ||
                    item.HarvestedBy != character.CharacterId || !character.SharesMap(item.LandingPosition) ||
                    !character.CharacterState.Inventory.CanAdd(item.ItemId, item.Quantity))
                    continue;
                if (item.IsInFlight)
                {
                    landing = true;
                    continue;
                }
                float distance = (item.LandingPosition - character.StandingPosition).sqrMagnitude;
                if (distance < bestDistance && (character.IsInPickupRange(item) ||
                    character.TryGetReachablePoint(item.LandingPosition, item.InteractionDistance * .8f, out _)))
                {
                    nearest = item;
                    bestDistance = distance;
                }
            }
            if (nearest == null)
                return landing;
            planned = false;
            goal = default;
            nearest.RequestInteraction(character);
            return true;
        }

        private FacilityTarget FindTarget(NpcActivity activity)
        {
            FacilityTarget nearest = default;
            float bestDistance = float.PositiveInfinity;
            foreach (var facility in FindObjectsByType<VillageFacility>(FindObjectsSortMode.None))
            {
                if (!MatchesFacility(facility))
                    continue;
                if (activity == NpcActivity.Resting)
                {
                    foreach (var seat in facility.GetComponentsInChildren<SeatSlot>())
                        Consider(new FacilityTarget(facility, activity, seat: seat));
                }
                else if (activity == NpcActivity.Harvesting)
                {
                    foreach (var crop in facility.GetComponentsInChildren<HarvestableCrop>())
                        Consider(new FacilityTarget(facility, activity, crop: crop));
                }
                else
                    Consider(new FacilityTarget(facility, activity));
            }
            return nearest;

            void Consider(FacilityTarget target)
            {
                if (!CanUse(target) || target.Crop != null && (target.Crop.IsBeingHarvested ||
                    !game.CanHarvest(character, target.Crop, showNotice: false)))
                    return;
                Vector3 point = target.Position;
                float distance = (point - character.StandingPosition).sqrMagnitude;
                float offset = target.Crop != null ? target.Crop.InteractionDistance * .85f : 0f;
                if (distance >= bestDistance || !character.TryGetReachablePoint(point, offset, out var approach) ||
                    target.Seat != null && !target.Seat.Covers(approach))
                    return;
                bestDistance = distance;
                nearest = target;
            }
        }

        private bool GoalStillAvailable() => MatchesFacility(goal.Facility) && CanUse(goal) &&
            (goal.Position - goalPosition).sqrMagnitude < .0004f;

        private bool MatchesFacility(VillageFacility facility) => facility != null && facility.isActiveAndEnabled &&
            facility.gameObject.scene == game.gameObject.scene && facility.FacilityType == scheduled?.Facility;

        private bool CanUse(FacilityTarget target)
        {
            if (target.Activity == NpcActivity.Resting)
                return target.Seat != null && target.Seat.isActiveAndEnabled && target.Seat.transform.IsChildOf(target.Facility.transform) &&
                    (character.OccupiedSeat == target.Seat || !game.SeatInUse(target.Seat.Position, this));
            if (target.Activity == NpcActivity.Harvesting)
                return target.Crop != null && target.Crop.CanInteract && target.Crop is not FishingWater &&
                    target.Crop.transform.IsChildOf(target.Facility.transform);
            return target.Facility.StandingPoint.gameObject.activeInHierarchy;
        }

        private void ClearGoal()
        {
            if (character.Action != WorkerAction.Idle)
                character.Cancel();
            planned = false;
            retryAt = 0f;
            waitingActivity = NpcActivity.Idle;
            goal = default;
        }

        private void OnDisable()
        {
            if (character != null && !character.IsLocallyControlled)
                ClearGoal();
        }

        private readonly struct FacilityTarget
        {
            public readonly VillageFacility Facility;
            public readonly NpcActivity Activity;
            public readonly SeatSlot Seat;
            public readonly HarvestableCrop Crop;

            public FacilityTarget(VillageFacility facility, NpcActivity activity, SeatSlot seat = null, HarvestableCrop crop = null)
            {
                Facility = facility;
                Activity = activity;
                Seat = seat;
                Crop = crop;
            }

            public Vector3 Position => Seat != null ? Seat.ApproachPosition :
                Crop != null ? Crop.transform.position : Facility.StandingPoint.position;
        }
    }
}
