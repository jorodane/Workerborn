using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    [DisallowMultipleComponent]
    internal sealed class NpcRoutine : MonoBehaviour
    {
        private const float SeatSearchRadius = 5f;
        private NavMeshPath path;
        private VillagePrototype game;
        private VillageInteractable npc;
        private WorkerAnimation animationDriver;
        private NavMeshAgent agent;
        private NpcScheduleEntry scheduled;
        private Vector3 home, farmPosition, destination;
        private Quaternion facing;
        private SeatSlot seat;
        private HarvestableCrop crop;
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

        internal bool ReservesSeat(Vector3 position) => seat != null && (seat.Position - position).sqrMagnitude < .04f;

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
            var farm = game.FarmMapAnchor;
            Vector3 currentFarm = farm != null ? farm.position : home;
            if (scheduled != next || (next?.Place == NpcSchedulePlace.Farm && farmPosition != currentFarm) ||
                (seat != null && (!seat.isActiveAndEnabled || game.SeatInUse(seat.Position, this))) || (seated && seat == null))
            {
                ClearRoute();
                scheduled = next;
                farmPosition = currentFarm;
            }

            if (!seated && hasRoute && !agent.isOnNavMesh)
                ClearRoute();
            if (!hasRoute)
            {
                if (Time.time < retryAt)
                    return;
                retryAt = Time.time + .8f;
                if (!PlanRoute())
                    return;
                placed = true;
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
            if (arrived && crop != null)
                AnimateHarvest();
        }

        private bool PlanRoute()
        {
            if (!BindNavigation())
                return false;

            Vector3 center = home;
            var place = scheduled?.Place ?? NpcSchedulePlace.Origin;
            if (place == NpcSchedulePlace.Farm)
                center = farmPosition;
            else if (place == NpcSchedulePlace.ScenePoint || place == NpcSchedulePlace.NearbySeat)
            {
                var point = game.FindNpcSchedulePoint(scheduled.PointId);
                if (point != null)
                    center = point.position;
                else if (place == NpcSchedulePlace.ScenePoint)
                    return false;
            }
            center += scheduled?.Offset ?? Vector3.zero;

            if (place == NpcSchedulePlace.Farm && scheduled.Activity == NpcActivity.Harvesting && TryChooseCrop(center))
                return true;
            if (place == NpcSchedulePlace.NearbySeat && TryChooseSeat(center))
                return true;
            return TryDestination(center, place == NpcSchedulePlace.Farm ? 3f : 1.5f) ||
                (place == NpcSchedulePlace.NearbySeat && TryDestination(home, 1.5f));
        }

        private bool TryChooseCrop(Vector3 center)
        {
            var farm = game.FarmMapAnchor;
            HarvestableCrop nearest = null;
            float closest = float.PositiveInfinity;
            Vector3 approach = default;
            foreach (var candidate in game.Crops)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsDepleted ||
                    farm == null || !candidate.transform.IsChildOf(farm))
                    continue;
                Vector3 plant = candidate.transform.position;
                Vector3 outward = plant - center;
                outward.y = 0f;
                float distance = outward.sqrMagnitude;
                if (distance >= closest)
                    continue;
                if (distance < .01f)
                    outward = Vector3.back;
                Vector3 point = plant + outward.normalized * .9f;
                if (!FindPath(point, .65f, out _))
                    continue;
                closest = distance;
                nearest = candidate;
                approach = point;
            }
            return nearest != null && TryDestination(approach, .65f, nearest);
        }

        private bool TryChooseSeat(Vector3 center)
        {
            SeatSlot nearest = null;
            float closest = SeatSearchRadius * SeatSearchRadius;
            foreach (var candidate in FindObjectsByType<SeatSlot>(FindObjectsSortMode.None))
            {
                float distance = (candidate.Position - center).sqrMagnitude;
                if (distance >= closest || !candidate.isActiveAndEnabled || candidate.gameObject.scene != game.gameObject.scene ||
                    game.SeatInUse(candidate.Position, this) || !FindPath(candidate.ApproachPosition, .7f, out _))
                    continue;
                closest = distance;
                nearest = candidate;
            }
            return nearest != null && TryDestination(nearest.ApproachPosition, .7f, reservedSeat: nearest);
        }

        private bool BindNavigation()
        {
            if (agent != null && agent.isOnNavMesh)
                return true;
            Vector3 point = game.SurfacePosition(transform.position.x, transform.position.z);
            if (!NavMesh.SamplePosition(point, out var ground, 1.5f, NavMesh.AllAreas))
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

        private bool FindPath(Vector3 point, float tolerance, out Vector3 ground)
        {
            ground = default;
            Vector3 surface = game.SurfacePosition(point.x, point.z);
            if (!NavMesh.SamplePosition(surface, out var hit, tolerance, NavMesh.AllAreas) ||
                !agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            ground = hit.position;
            return true;
        }

        private bool TryDestination(Vector3 point, float tolerance, HarvestableCrop workTarget = null, SeatSlot reservedSeat = null)
        {
            if (!FindPath(point, tolerance, out var ground))
                return false;
            bool atGoal = !placed ? agent.Warp(ground) : (transform.position - ground).sqrMagnitude <= .15f * .15f;
            if (!atGoal && !agent.SetPath(path))
                return false;

            destination = ground;
            seat = reservedSeat;
            crop = workTarget;
            facing = workTarget != null ? FacingTowards(workTarget.transform.position - destination) : transform.rotation;
            hasRoute = true;
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
            Activity = scheduled?.Activity ?? NpcActivity.Idle;
            if (Activity == NpcActivity.Harvesting && crop == null)
                Activity = NpcActivity.Working;
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
                if (crop != null)
                    transform.rotation = facing;
                animationDriver?.SetAction((int)WorkerMotion.Idle);
            }
        }

        private void AnimateHarvest()
        {
            if (!crop.isActiveAndEnabled || crop.IsDepleted)
            {
                ClearRoute();
                return;
            }
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
            seat = null;
            crop = null;
            hasRoute = arrived = seated = false;
            retryAt = workElapsed = workBreak = 0f;
            Activity = NpcActivity.Idle;
            animationDriver?.SetAction((int)WorkerMotion.Idle);
        }

        private void OnDisable() => ClearRoute();
    }
}
