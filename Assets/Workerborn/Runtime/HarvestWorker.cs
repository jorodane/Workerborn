using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public enum WorkerAction
    {
        Idle,
        Moving,
        Harvesting,
        Attacking
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class HarvestWorker : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float runSpeed = 3.7f;
        [SerializeField, Min(.1f)] private float walkSpeed = 1.65f;
        [SerializeField, Min(.1f)] private float pickupDistance = 1.5f;
        [SerializeField, Min(.04f)] private float collectRepeatSeconds = .1f;
        [Header("Basic Attack")]
        [SerializeField] private AnimationClip attackClip;
        [SerializeField, Min(1)] private int attackDamage = 10;
        [SerializeField, Min(.2f)] private float attackDelay = .85f;
        private readonly Dictionary<CropDefinition, int> inventory = new Dictionary<CropDefinition, int>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private Camera worldCamera;
        private NavMeshAgent agent;
        private WorkerAnimation animationDriver;
        private HarvestableCrop cropTarget;
        private GroundPickup pickupTarget;
        private WorldInteractable interactionTarget;
        private MonsterInteractable strikeTarget;
        private float strikeStartedAt, nextStrikeAt;
        private bool struck;
        private System.Action onInteractionArrival;
        private InventoryEntry pendingDrop;
        private Vector3 dropPoint;
        private VillagePrototype village;
        private int collectedCount;
        private bool manualMoving;
        private float nextCollect;
        public bool AcceptsInteractionCommands => village == null || !village.ControlsBlocked;
        public PointerTargeting Targeting => village != null ? village.Targeting : null;
        internal VillagePrototype Village => village;
        public bool IsWalking => Action == WorkerAction.Moving && WorkerbornInput.Held("Gameplay/Walk");
        public bool HasPendingDrop => pendingDrop != null;
        public WorkerAction Action { get; private set; }
        public HarvestableCrop HoveredCrop { get; private set; }
        public GroundPickup HoveredPickup { get; private set; }
        public VillageInteractable HoveredInteraction { get; private set; }
        public HarvestableCrop LastCrop { get; private set; }
        public HarvestableCrop ActiveCrop => Action == WorkerAction.Harvesting ? cropTarget : null;
        public HarvestableCrop FocusCrop => HoveredCrop != null ? HoveredCrop : cropTarget != null ? cropTarget : LastCrop;
        public int TotalCollected => village != null ? village.State.Inventory.Count(ItemIds.Potato) : collectedCount;

        public event Action<string> Message;
        public void BindVillage(VillagePrototype game) => village = game;
        public void Initialize(Camera camera, WorkerAnimation animations)
        {
            worldCamera = camera;
            animationDriver = animations;
            agent = GetComponent<NavMeshAgent>();
            agent.speed = runSpeed;
            agent.angularSpeed = 540f;
            agent.acceleration = 20f;
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.stoppingDistance = 0.08f;
            agent.autoBraking = true;
        }

        private void Update()
        {
            if (worldCamera == null || agent == null || !agent.isOnNavMesh)
                return;
            ReadInput();
            agent.speed = IsWalking ? walkSpeed : runSpeed;
            bool movingByKeys = UpdateManualMovement();
            UpdateNearbyCollection();
            if (!movingByKeys && Action == WorkerAction.Moving)
                UpdateMovement();
            else if (Action == WorkerAction.Harvesting)
                UpdateHarvest();
            else if (Action == WorkerAction.Attacking)
                UpdateAttack();
            animationDriver?.SetAction(Action == WorkerAction.Moving ? (IsWalking ? 3 : 1) :
                Action == WorkerAction.Harvesting || Action == WorkerAction.Attacking ? 2 : 0);
        }

        private bool UpdateManualMovement()
        {
            Vector2 input = AcceptsInteractionCommands ? WorkerbornInput.Move : Vector2.zero;
            if (input.sqrMagnitude < .001f)
            {
                if (manualMoving)
                    Cancel();
                return false;
            }

            if (!manualMoving)
            {
                Cancel();
                agent.isStopped = false;
            }
            manualMoving = true;
            Action = WorkerAction.Moving;
            Vector3 right = worldCamera.transform.right;
            right.y = 0f;
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            Vector3 direction = Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
            float speed = IsWalking ? walkSpeed : runSpeed;
            agent.Move(direction * (speed * Time.deltaTime));
            Face(transform.position + direction);
            return true;
        }

        private void UpdateNearbyCollection()
        {
            const string action = "Gameplay/CollectNearby";
            if (!AcceptsInteractionCommands || !WorkerbornInput.Held(action) ||
                !WorkerbornInput.Pressed(action) && Time.unscaledTime < nextCollect)
                return;
            nextCollect = Time.unscaledTime + collectRepeatSeconds;

            GroundPickup nearest = null;
            float bestDistance = float.MaxValue;
            foreach (var candidate in WorldInteractable.Active)
            {
                if (!(candidate is GroundPickup item) || !item.CanCollect || item.IsInFlight || !IsInPickupRange(item))
                    continue;
                Vector3 offset = item.LandingPosition - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude < bestDistance)
                {
                    nearest = item;
                    bestDistance = offset.sqrMagnitude;
                }
            }

            if (nearest != null)
            {
                if (nearest == pickupTarget)
                    Cancel();
                CollectPickup(nearest);
            }
        }

        private void ReadInput()
        {
            if (WorkerbornInput.Pressed("Gameplay/Cancel") || village == null && WorkerbornInput.Pressed("Camera/OrbitRight"))
                Cancel();
            var target = village != null ? village.Targeting?.CurrentTarget : null;
            HoveredCrop = target as HarvestableCrop;
            HoveredPickup = target as GroundPickup;
            HoveredInteraction = target as VillageInteractable;
            // The village pointer controller handles both nameplates and world hits.
            if (village != null || !AcceptsInteractionCommands)
                return;
            Vector2 screen = WorkerbornInput.Point;
            if (EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, uiHits);
                if (uiHits.Count > 0)
                    return;
            }

            if (!Physics.Raycast(worldCamera.ScreenPointToRay(screen), out RaycastHit hit, 200f, VillageMapProp.PointerLayers, QueryTriggerInteraction.Collide))
                return;
            target = hit.collider.GetComponentInParent<WorldInteractable>();
            if (target != null && !target.CanInteract)
                return;
            HoveredCrop = target as HarvestableCrop;
            HoveredPickup = target as GroundPickup;
            HoveredInteraction = target as VillageInteractable;
            if (!WorkerbornInput.Pressed("Gameplay/Interact"))
                return;
            if (target != null)
                target.RequestInteraction(this);
            else
                CommandMove(hit.point);
        }

        public void CommandHarvest(HarvestableCrop crop)
        {
            if (crop == null || !crop.isActiveAndEnabled)
                return;
            if (crop == cropTarget && (Action == WorkerAction.Moving || Action == WorkerAction.Harvesting))
                return;
            if (crop.IsDepleted)
            {
                LastCrop = crop;
                Message?.Invoke("이 작물은 수확을 모두 마쳤어요. 다른 작물을 선택해 주세요.");
                return;
            }
            if (village != null && (village.ControlsBlocked || !village.CanHarvest(crop)))
                return;

            ClearInteractionTargets();
            cropTarget = crop;
            LastCrop = crop;
            if (!NavigateNear(crop.transform.position, crop.InteractionDistance * 0.85f))
                return;
            if (village == null)
                Message?.Invoke($"{crop.Definition.DisplayName} 밭으로 이동해요.");
        }

        public bool IsInPickupRange(GroundPickup item) => item != null && InReach(item.LandingPosition, pickupDistance);
        public void CommandPickup(GroundPickup item)
        {
            if (!AcceptsInteractionCommands || item == null || !item.CanCollect)
                return;
            if (IsInPickupRange(item))
            {
                CollectPickup(item);
                return;
            }

            if (pickupTarget == item && Action == WorkerAction.Moving)
                return;
            ClearInteractionTargets();
            pickupTarget = item;
            NavigateNear(item.LandingPosition, pickupDistance * .8f);
        }

        private bool CollectPickup(GroundPickup item)
        {
            if (!IsInPickupRange(item) || !item.CanCollect)
                return false;
            if (village != null)
                return village.TryCollectPickup(item);
            CropDefinition definition = item.Definition;
            int quantity = item.Quantity;
            if (!item.TryCollect())
                return false;
            if (definition != null)
            {
                inventory.TryGetValue(definition, out int count);
                inventory[definition] = count + quantity;
            }

            collectedCount += quantity;
            Message?.Invoke($"{item.DisplayName} +{quantity}");
            return true;
        }

        public void CommandMove(Vector3 destination)
        {
            if (village != null && village.ControlsBlocked)
                return;
            ClearInteractionTargets();
            if (NavigateNear(destination, 0f))
                village?.ShowClick(destination);
        }

        public void CommandInteract(VillageInteractable target) => CommandApproach(target);

        public void CommandAttack(MonsterInteractable target)
        {
            if (village == null || !AcceptsInteractionCommands || target == null || !target.CanAttack ||
                village.State.Energy <= 0 || Action == WorkerAction.Attacking || Time.time < nextStrikeAt)
                return;
            if (InReach(target.transform.position, target.InteractionDistance))
                StartAttack(target);
            else if (target != interactionTarget || Action != WorkerAction.Moving)
                CommandApproach(target, () => StartAttack(target));
        }

        private void StartAttack(MonsterInteractable target)
        {
            if (target == null || !target.CanAttack || village.State.Energy <= 0 || !AcceptsInteractionCommands)
                return;
            Cancel();
            Face(target.transform.position);
            strikeTarget = target;
            Action = WorkerAction.Attacking;
            strikeStartedAt = Time.time;
            nextStrikeAt = strikeStartedAt + Mathf.Max(.2f, attackDelay);
            struck = false;
            animationDriver?.BeginStrike(attackClip);
        }

        private void UpdateAttack()
        {
            if (strikeTarget == null || !AcceptsInteractionCommands || village.State.Energy <= 0)
            {
                Cancel();
                return;
            }
            float progress = Mathf.InverseLerp(strikeStartedAt, nextStrikeAt, Time.time);
            animationDriver?.SampleStrike(progress);
            if (!struck && progress >= .36f)
            {
                struck = true;
                if (strikeTarget.CanAttack && InReach(strikeTarget.transform.position, strikeTarget.InteractionDistance))
                    strikeTarget.ReceiveStrike(this, Mathf.Max(1, attackDamage));
            }
            if (progress >= 1f)
                Cancel();
        }

        public void CommandApproach(WorldInteractable target, System.Action arrival = null)
        {
            if (village == null || target == null || village.ControlsBlocked)
                return;
            ClearInteractionTargets();
            interactionTarget = target;
            onInteractionArrival = arrival;
            NavigateNear(target.transform.position, target.InteractionDistance * 0.7f);
        }

        public bool CommandDrop(int entryId, Vector3 destination)
        {
            if (village == null || village.Hud.HasModal || agent == null || !agent.isOnNavMesh)
                return false;
            var entry = village.State.Inventory.Find(entryId);
            if (village.DropPrefabFor(entry) == null)
                return false;
            ClearInteractionTargets();
            pendingDrop = entry.Copy();
            dropPoint = destination;
            if (InReach(dropPoint, pickupDistance))
                return FinishDrop();
            if (!NavigateNear(destination, pickupDistance * .8f))
                return false;
            village.ShowClick(destination);
            return true;
        }

        private bool NavigateNear(Vector3 target, float offset)
        {
            Vector3 direction = transform.position - target;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                direction = Vector3.back;
            Vector3 approach = target + direction.normalized * Mathf.Min(offset, direction.magnitude);
            if (offset > 0 && village != null && village.GroundTerrain != null)
                approach = village.SurfacePosition(approach.x, approach.z);
            if (!NavMesh.SamplePosition(approach, out NavMeshHit hit, 0.8f, NavMesh.AllAreas))
            {
                Cancel();
                Message?.Invoke("이동할 수 있는 곳을 선택해 주세요.");
                return false;
            }

            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                Cancel();
                Message?.Invoke("그곳으로 가는 길을 찾을 수 없어요.");
                return false;
            }

            if (!agent.SetPath(path))
            {
                Cancel();
                Message?.Invoke("그곳으로 가는 길을 찾을 수 없어요.");
                return false;
            }
            agent.isStopped = false;
            Action = WorkerAction.Moving;
            return true;
        }

        private void UpdateMovement()
        {
            if (pendingDrop != null && InReach(dropPoint, pickupDistance))
            {
                FinishDrop();
                return;
            }
            if (pickupTarget != null)
            {
                if (!pickupTarget.CanCollect)
                {
                    Cancel();
                    return;
                }

                if (IsInPickupRange(pickupTarget))
                {
                    var item = pickupTarget;
                    pickupTarget = null;
                    CollectPickup(item);
                    agent.isStopped = true;
                    agent.ResetPath();
                    Action = WorkerAction.Idle;
                    return;
                }
            }

            if (agent.pathPending)
                return;
            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Cancel();
                return;
            }

            if (agent.remainingDistance > agent.stoppingDistance + 0.06f)
                return;
            agent.isStopped = true;
            agent.ResetPath();
            if (pendingDrop != null)
            {
                Cancel();
                Message?.Invoke("그곳에 아이템을 놓을 수 없어요.");
            }
            else if (cropTarget != null)
            {
                if (village != null && !village.CanHarvest(cropTarget))
                {
                    Cancel();
                    return;
                }
                if (!InReach(cropTarget.transform.position, cropTarget.InteractionDistance + 0.15f) || !cropTarget.TryBegin(this))
                {
                    Cancel();
                    Message?.Invoke("이 작물에서는 지금 수확할 수 없어요.");
                    return;
                }

                Action = WorkerAction.Harvesting;
                Face(cropTarget.transform.position);
                animationDriver?.BeginWork(cropTarget.Definition.WorkAnimation, cropTarget.Definition.HarvestSeconds);
                if (village == null)
                    Message?.Invoke("수확 중이에요. 완료하면 감자가 바닥에 떨어져요.");
            }
            else if (pickupTarget != null)
            {
                // A sampled approach point may land outside reach near obstacles.
                pickupTarget = null;
                Action = WorkerAction.Idle;
                Message?.Invoke("아이템에 충분히 가까이 갈 수 없어요.");
            }
            else if (interactionTarget != null)
            {
                var target = interactionTarget;
                var arrival = onInteractionArrival;
                interactionTarget = null;
                onInteractionArrival = null;
                Action = WorkerAction.Idle;
                if (InReach(target.transform.position, target.InteractionDistance))
                {
                    Face(target.transform.position);
                    target.Arrive(village, this, arrival);
                }
                else
                    Message?.Invoke("조금 더 가까이 가야 해요.");
            }
            else
                Action = WorkerAction.Idle;
        }

        private bool FinishDrop()
        {
            var requested = pendingDrop;
            var position = dropPoint;
            Cancel();
            Face(position);
            return village.PlaceInventoryDrop(requested, position);
        }

        private void UpdateHarvest()
        {
            if (cropTarget == null || !cropTarget.isActiveAndEnabled || !InReach(cropTarget.transform.position, cropTarget.InteractionDistance + 0.3f))
            {
                Cancel();
                return;
            }
            if (village != null && !village.CanHarvest(cropTarget))
            {
                Cancel();
                return;
            }

            if (!cropTarget.Advance(this, Time.deltaTime))
            {
                animationDriver?.SampleWork(cropTarget.Progress);
                return;
            }

            animationDriver?.SampleWork(1f);
            if (village == null)
                Message?.Invoke($"수확 완료! 바닥의 {cropTarget.Definition.DisplayName}를 클릭해서 주워 주세요.");
            cropTarget = null;
            Action = WorkerAction.Idle;
        }

        internal bool InReach(Vector3 position, float range)
        {
            Vector3 delta = position - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= range * range;
        }

        private void Face(Vector3 position)
        {
            Vector3 direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(direction);
        }

        private void ClearInteractionTargets()
        {
            manualMoving = false;
            if (cropTarget != null)
                cropTarget.Cancel(this);
            cropTarget = null;
            pickupTarget = null;
            interactionTarget = null;
            strikeTarget = null;
            onInteractionArrival = null;
            pendingDrop = null;
        }

        public void Cancel()
        {
            ClearInteractionTargets();
            Action = WorkerAction.Idle;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            animationDriver?.SetAction(0);
        }

        public void ResetWorker(Vector3 spawn)
        {
            Cancel();
            inventory.Clear();
            collectedCount = 0;
            nextStrikeAt = 0;
            LastCrop = null;
            HoveredCrop = null;
            HoveredPickup = null;
            HoveredInteraction = null;
            agent.Warp(spawn);
            transform.rotation = Quaternion.identity;
        }

        public void WarpTo(Vector3 position)
        {
            Cancel();
            agent.Warp(position);
        }

        private void OnDisable() => Cancel();
    }
}
