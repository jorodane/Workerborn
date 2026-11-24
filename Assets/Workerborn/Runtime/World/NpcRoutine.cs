using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    [DisallowMultipleComponent]
    internal sealed class NpcRoutine : MonoBehaviour
    {
        private const float RetryDelay = .8f;
        private NavMeshPath path;
        private VillagePrototype game;
        private VillageInteractable npc;
        private WorkerAnimation animationDriver;
        private NavMeshAgent agent;
        private NpcScheduleEntry scheduled;
        private Vector3 home, approach, destination;
        private Quaternion facing, seatFacing;
        private FacilityUse goal;
        private bool hasRoute, arrived, seated, paused, placed;
        private float retryAt, workElapsed, workBreak;
        internal NpcActivity Activity { get; private set; }
        internal bool IsSeated => seated;

        internal void Bind(VillagePrototype owner, VillageInteractable subject, WorkerAnimation animations)
        {
            game = owner;
            npc = subject;
            animationDriver = animations;
            home = transform.position;
            agent = GetComponent<NavMeshAgent>();
            path = new NavMeshPath();
        }

        internal bool ReservesSeat(Vector3 position) => goal.Seat != null &&
            ((seated ? transform.position : goal.Seat.Position) - position).sqrMagnitude < .04f;

        private void Update()
        {
            if (game == null || !game.CanRunNpcSchedules)
                return;
            if (game.Worker.IsInteractingWith(npc) || game.Hud.IsSpeakingWith(npc))
            {
                if (!paused)
                {
                    paused = true;
                    if (agent != null && agent.isOnNavMesh)
                        agent.isStopped = true;
                    if (!seated)
                        animationDriver?.SetAction((int)WorkerMotion.Idle);
                    workElapsed = 0f;
                }
                return;
            }

            var next = NpcScheduleEntry.Resolve(npc.Definition.DailySchedule, game.State, npc.Definition.Job);
            if (scheduled != next || (hasRoute && !RouteIsValid()))
            {
                ClearRoute();
                scheduled = next;
            }

            if (!hasRoute)
            {
                if (Time.time < retryAt)
                    return;
                retryAt = Time.time + RetryDelay;
                bool planned = PlanRoute();
                placed = true;
                if (!planned)
                    return;
            }

            if (paused)
            {
                paused = false;
                if (!arrived)
                    agent.isStopped = false;
            }
            if (!arrived)
            {
                if (agent.pathPending)
                    return;
                if ((transform.position - destination).sqrMagnitude <= .20f * .20f)
                    Arrive();
                else if (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete)
                    ClearRoute();
                else
                    animationDriver?.SetAction((int)WorkerMotion.Walk);
            }
            if (!arrived)
                return;
            if (goal.Crop != null)
                AnimateHarvest(goal.Crop);
            else if (scheduled?.Activity == NpcActivity.Harvesting && goal.Activity == NpcActivity.Working && Time.time >= retryAt)
            {
                retryAt = Time.time + RetryDelay;
                TryPlanFacility(useCrops: true);
            }
        }

        private bool PlanRoute()
        {
            if (!BindNavigation())
                return false;

            if (scheduled?.Facility == null)
                return TryDestination(home, 1.5f, default);
            return (scheduled.Activity == NpcActivity.Harvesting && TryPlanFacility(useCrops: true)) || TryPlanFacility(useCrops: false);
        }

        private bool TryPlanFacility(bool useCrops)
        {
            FacilityUse nearest = default;
            float bestDistance = float.PositiveInfinity;
            foreach (var facility in FindObjectsByType<VillageFacility>(FindObjectsSortMode.None))
            {
                if (!MatchesFacility(facility))
                    continue;
                if (scheduled.Activity == NpcActivity.Resting)
                {
                    foreach (var seat in facility.GetComponentsInChildren<SeatSlot>())
                        Consider(new FacilityUse(facility, NpcActivity.Resting, seat: seat), ref nearest, ref bestDistance);
                }
                else if (useCrops)
                {
                    foreach (var crop in facility.GetComponentsInChildren<HarvestableCrop>())
                        Consider(new FacilityUse(facility, NpcActivity.Harvesting, crop: crop), ref nearest, ref bestDistance);
                }
                else
                {
                    var activity = scheduled.Activity == NpcActivity.Harvesting ? NpcActivity.Working : scheduled.Activity;
                    Consider(new FacilityUse(facility, activity), ref nearest, ref bestDistance);
                }
            }
            return nearest.Facility != null && TryDestination(nearest.Approach, nearest.Tolerance, nearest);
        }

        private void Consider(FacilityUse candidate, ref FacilityUse nearest, ref float bestDistance)
        {
            if (!CanUse(candidate))
                return;
            Vector3 point = candidate.Approach;
            float distance = (point - transform.position).sqrMagnitude;
            if (distance >= bestDistance || !FindPath(point, candidate.Tolerance, candidate.Seat, out _))
                return;
            bestDistance = distance;
            nearest = candidate;
        }

        private bool MatchesFacility(VillageFacility facility) => facility != null && facility.isActiveAndEnabled &&
            facility.gameObject.scene == game.gameObject.scene && facility.FacilityType == scheduled.Facility;

        private bool CanUse(FacilityUse use)
        {
            if (use.Activity == NpcActivity.Resting)
                return use.Seat != null && use.Seat.isActiveAndEnabled && use.Seat.transform.IsChildOf(use.Facility.transform) &&
                    !game.SeatInUse(use.Seat.Position, this);
            if (use.Activity == NpcActivity.Harvesting)
                return use.Crop != null && use.Crop.isActiveAndEnabled && !use.Crop.IsDepleted &&
                    use.Crop.transform.IsChildOf(use.Facility.transform);
            return use.Facility.StandingPoint.gameObject.activeInHierarchy;
        }

        private bool RouteIsValid()
        {
            if (!seated && !agent.isOnNavMesh)
                return false;
            if (scheduled?.Facility == null)
                return true;
            return MatchesFacility(goal.Facility) && CanUse(goal) && (goal.Approach - approach).sqrMagnitude < .0004f &&
                (goal.Seat == null || Quaternion.Angle(goal.Seat.Facing, seatFacing) < .5f);
        }

        private bool BindNavigation()
        {
            if (agent != null && agent.isOnNavMesh)
                return true;
            Vector3 point = game.SurfacePosition(transform.position.x, transform.position.z);
            if (!NavMesh.SamplePosition(point, out var ground, 1.5f, NavMesh.AllAreas) &&
                !NavMesh.SamplePosition(point, out ground, VillagePlot.CellSide * 2f, NavMesh.AllAreas))
                return false;
            if (agent != null)
                agent.enabled = false;
            transform.position = ground.position;
            if (agent == null)
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
                agent.radius = .28f;
                agent.speed = 1.65f;
                agent.angularSpeed = 540f;
                agent.acceleration = 10f;
                agent.stoppingDistance = .08f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            }
            agent.enabled = true;
            return agent.isOnNavMesh;
        }

        private bool FindPath(Vector3 point, float tolerance, SeatSlot seat, out Vector3 ground)
        {
            ground = default;
            Vector3 surface = game.SurfacePosition(point.x, point.z);
            if (!NavMesh.SamplePosition(surface, out var hit, tolerance, NavMesh.AllAreas) ||
                (seat != null && !seat.Covers(hit.position)) ||
                !agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            ground = hit.position;
            return true;
        }

        private bool TryDestination(Vector3 point, float tolerance, FacilityUse use)
        {
            if (!FindPath(point, tolerance, use.Seat, out var ground))
                return false;
            bool atGoal = !placed ? agent.Warp(ground) : (transform.position - ground).sqrMagnitude <= .15f * .15f;
            if (!atGoal && !agent.SetPath(path))
                return false;

            destination = ground;
            approach = point;
            goal = use;
            seatFacing = goal.Seat != null ? goal.Seat.Facing : Quaternion.identity;
            facing = goal.Crop != null ? FacingTowards(goal.Crop.transform.position - destination) : transform.rotation;
            hasRoute = true;
            arrived = false;
            if (atGoal)
                Arrive();
            else
            {
                Activity = NpcActivity.Walking;
                agent.isStopped = false;
                animationDriver?.SetAction((int)WorkerMotion.Walk);
            }
            return true;
        }

        private static Quaternion FacingTowards(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        }

        private void Arrive()
        {
            arrived = true;
            agent.isStopped = true;
            agent.ResetPath();
            Activity = goal.Activity;
            var seat = goal.Seat;
            if (seat != null)
            {
                seated = true;
                agent.updatePosition = false;
                agent.updateRotation = false;
                transform.SetPositionAndRotation(seat.Position, seat.Facing);
                animationDriver?.SetAction((int)WorkerMotion.SeatSit);
            }
            else
            {
                if (goal.Crop != null)
                    transform.rotation = facing;
                animationDriver?.SetAction((int)WorkerMotion.Idle);
            }
        }

        private void AnimateHarvest(HarvestableCrop crop)
        {
            if (workBreak > 0f)
            {
                workBreak -= Time.deltaTime;
                return;
            }
            float duration = crop.Definition.HarvestSeconds;
            if (workElapsed == 0f)
            {
                transform.rotation = facing;
                animationDriver?.BeginWork(crop.Definition.WorkAnimation, duration);
            }
            workElapsed += Time.deltaTime;
            animationDriver?.SampleWork(workElapsed / duration);
            if (workElapsed >= duration)
            {
                workElapsed = 0f;
                workBreak = 1.5f;
                animationDriver?.SetAction((int)WorkerMotion.Idle);
            }
        }

        private void ClearRoute()
        {
            if (seated)
            {
                transform.position = destination;
                if (agent.isOnNavMesh)
                    agent.Warp(destination);
                agent.updatePosition = true;
                agent.updateRotation = true;
            }
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            goal = default;
            hasRoute = arrived = seated = false;
            retryAt = workElapsed = workBreak = 0f;
            Activity = NpcActivity.Idle;
            animationDriver?.SetAction((int)WorkerMotion.Idle);
        }

        private void OnDisable() => ClearRoute();

        private readonly struct FacilityUse
        {
            internal readonly VillageFacility Facility;
            internal readonly NpcActivity Activity;
            internal readonly SeatSlot Seat;
            internal readonly HarvestableCrop Crop;

            internal FacilityUse(VillageFacility facility, NpcActivity activity, SeatSlot seat = null, HarvestableCrop crop = null)
            {
                Facility = facility;
                Activity = activity;
                Seat = seat;
                Crop = crop;
            }

            internal float Tolerance => Seat != null ? .7f : Crop != null ? .65f : 1.5f;
            internal Vector3 Approach
            {
                get
                {
                    if (Seat != null)
                        return Seat.ApproachPosition;
                    if (Crop == null)
                        return Facility.StandingPoint.position;

                    Vector3 plant = Crop.transform.position;
                    Vector3 approachSide = Vector3.ProjectOnPlane(Facility.StandingPoint.position - plant, Vector3.up);
                    if (approachSide.sqrMagnitude < .01f)
                        approachSide = -Facility.transform.forward;
                    return plant + approachSide.normalized * .9f;
                }
            }
        }
    }
}
