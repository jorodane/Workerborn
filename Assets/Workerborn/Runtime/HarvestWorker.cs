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
        Attacking,
        Sitting
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public sealed partial class HarvestWorker : MonoBehaviour
    {
        internal const float DefaultPickupDistance = GroundDropSave.PickupDistance;
        private const float JumpHeight = .6f;
        [SerializeField, Min(.1f)] private float runSpeed = 3.7f;
        [SerializeField, Min(.1f)] private float walkSpeed = 1.65f;
        [Header("Nearby Pickup")]
        [SerializeField, Min(.1f)] private float pickupDistance = DefaultPickupDistance;
        [SerializeField, Min(.04f)] private float collectRepeatSeconds = .1f;
        [SerializeField, Min(.01f)] private float collectFastestRepeatSeconds = .025f;
        [SerializeField, Min(2), Tooltip("Successful pickups in a row needed to reach the fastest interval.")]
        private int collectRampPickups = 5;
        [Header("Basic Attack")]
        [SerializeField] private AnimationClip attackClip;
        [SerializeField, Min(1)] private int attackDamage = 10;
        [SerializeField, Min(.2f)] private float attackDelay = .85f;
        [SerializeField, Min(1)] private int attackStaminaCost = 3;
        [Header("Fishing")]
        [SerializeField] private FishingBobber fishingBobberPrefab;
        private readonly Dictionary<CropDefinition, int> inventory = new Dictionary<CropDefinition, int>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private Camera worldCamera;
        private NavMeshAgent agent;
        private WorkerAnimation animationDriver;
        private CharacterActor actor;
        private VillageState characterState;
        private int equipmentVersion = -1;
        private HarvestableCrop cropTarget;
        private WorldTargetReference pickupTarget;
        private WorldInteractable interactionTarget;
        private WorldInteractable strikeTarget;
        private float strikeStartedAt, nextStrikeAt;
        private int strikeStaminaCost;
        private double strikePaidProgress;
        private bool struck;
        private System.Action onInteractionArrival;
        private InventoryEntry pendingDrop;
        private Vector3 dropPoint;
        private Vector3 approachTargetPosition;
        private Vector3 harvestOrigin;
        private float savedHarvestProgress;
        private SkillId? completedWorkSkill;
        private float workFeedbackUntil;
        internal int WorkSequence { get; private set; }
        private VillagePrototype village;
        private int collectedCount;
        private bool walkPath;
        private bool manualMoving;
        private bool manualWalking;
        private bool onSeat;
        private SeatSlot occupiedSeat;
        private Quaternion occupiedSeatFacing;
        private Vector3 occupiedSeatRoot;
        private Transform seatVisual;
        private Vector3 seatVisualBase;
        private System.Action afterStanding;
        private float nextCollect;
        private int consecutiveCollections;
        private Transform jumpingVisual;
        private Vector3 jumpBase;
        private float jumpStartedAt;
        private bool jumpUsesClip;
        private Transform restingVisual;
        private Vector3 restBase;
        private Quaternion restRotation;
        internal bool IsLocallyControlled => worldCamera != null;
        internal bool AcceptsMovementCommands => village == null || !IsLocallyControlled || !village.ControlsBlocked;
        public bool AcceptsInteractionCommands => CurrentMount == null && AcceptsMovementCommands;
        public bool AcceptsGroundItemCommands => village == null || !CharacterState.AwaitingIncarnation &&
            (!IsLocallyControlled || !village.AwaitingWorldCommand && village.Hud.AllowsGroundItems);
        internal bool CanInteractWith(WorldInteractable target) => target is GroundPickup ? AcceptsGroundItemCommands :
            AcceptsMovementCommands && (CurrentMount == null ||
                target is VillageInteractable { CanTarget: true } ||
                CurrentMount.CanAttack && target is MonsterInteractable { Combat: not null });
        public PointerTargeting Targeting => village != null && IsLocallyControlled ? village.Targeting : null;
        internal VillagePrototype Village => village;
        internal VillageState CharacterState => characterState;
        internal CharacterActor Actor => actor;
        internal string CharacterId { get; private set; }
        internal string PlayerId { get; private set; }
        internal bool HasPlayerOwner => !string.IsNullOrEmpty(PlayerId);
        internal SeatSlot OccupiedSeat => occupiedSeat;
        internal float SeatLift { get; private set; }
        internal bool IsStandingUp => animationDriver?.IsStandingUp == true;
        public bool IsWalking => Action == WorkerAction.Moving &&
            (manualMoving ? manualWalking : walkPath);
        public bool HasPendingDrop => pendingDrop != null;
        internal bool IsApproachingInteraction => Action == WorkerAction.Moving &&
            (pendingDrop != null || pickupTarget.IsAssigned || interactionTarget != null);
        internal float VisualJumpOffset { get; private set; }
        internal SkillId? DisplayedWorkSkill => ActiveCrop?.Definition.Skill ??
            (CurrentTimberSwing != TimberSwing.None ? SkillId.Woodcutting :
                Time.unscaledTime < workFeedbackUntil ? completedWorkSkill : null);
        internal float DisplayedWorkProgress => ActiveCrop?.Progress ?? 1f;
        internal (float Confirmed, float Next)? DisplayedWorkSteps
        {
            get
            {
                if (axeCut != null)
                    return (confirmedCut / (float)TimberWork.FullCut, plannedCut / (float)TimberWork.FullCut);
                if (CurrentTimberSwing == TimberSwing.Strike)
                    return (struck ? 1f : 0f, 1f);
                var crop = ActiveCrop;
                return crop != null && crop.Definition.WorkAnimation?.LoopImpact > 0f
                    ? animationDriver?.WorkSteps(crop.Progress) : null;
            }
        }
        internal Vector3 StandingPosition => transform.position;
        public WorkerAction Action { get; private set; }
        public HarvestableCrop HoveredCrop { get; private set; }
        public GroundPickup HoveredPickup { get; private set; }
        public VillageInteractable HoveredInteraction { get; private set; }
        public HarvestableCrop LastCrop { get; private set; }
        public HarvestableCrop ActiveCrop => Action == WorkerAction.Harvesting ? cropTarget : null;
        internal HarvestableCrop PlannedCrop => cropTarget;
        internal float HarvestProgress => ActiveCrop != null ? ActiveCrop.Progress : savedHarvestProgress;
        public HarvestableCrop FocusCrop => HoveredCrop != null ? HoveredCrop : cropTarget != null ? cropTarget : LastCrop;
        public int TotalCollected => characterState?.Inventory.Count(ItemIds.Potato) ?? collectedCount;
        internal bool IsFighting => strikeTarget is MonsterInteractable { Combat: not null } ||
            interactionTarget is MonsterInteractable { Combat: not null };
        internal bool IsInteractingWith(WorldInteractable target) =>
            target == interactionTarget || target == strikeTarget || target == cropTarget;
        internal bool OccupiesSeat(Vector3 position) => onSeat && occupiedSeat != null && (occupiedSeat.Position - position).sqrMagnitude < .04f;

        public event Action<string> Message;
        internal event Action HarvestAttemptFinished;
        internal void BindVillage(VillagePrototype game, VillageState state, string id, string playerId)
        {
            village = game;
            CharacterId = id;
            PlayerId = playerId;
            SetCharacterState(state);
        }

        internal void SetCharacterState(VillageState state)
        {
            characterState = state;
            equipmentVersion = -1;
            if (state.LifeLook.HasValue)
                actor?.ApplyProfileLook(state.LifeLook.Value);
            RefreshEquipment();
        }

        internal void SetInputCamera(Camera camera)
        {
            if (worldCamera == camera)
                return;
            if (manualMoving)
                Cancel();
            ResetCollectHold();
            worldCamera = camera;
        }

        internal void UseActionPresentation(HarvestWorker source)
        {
            if (attackClip == null)
                attackClip = source.attackClip;
            if (fishingBobberPrefab == null)
                fishingBobberPrefab = source.fishingBobberPrefab;
        }

        public void Initialize(Camera camera, WorkerAnimation animations)
        {
            SetInputCamera(camera);
            animationDriver = animations;
            actor = GetComponent<CharacterActor>();
            agent = GetComponent<NavMeshAgent>();
            agent.speed = runSpeed;
            agent.angularSpeed = 540f;
            agent.acceleration = 20f;
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.stoppingDistance = 0.08f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        }

        private void Update()
        {
            if (CurrentMount == null && !ReferenceEquals(CurrentMount, null))
                EndRiding(false);
            if (agent == null || !agent.isOnNavMesh && (village == null || !EnsureNavigation()))
                return;
            RefreshEquipment();
            if (Action != WorkerAction.Sitting && !IsStandingUp)
            {
                ReleaseSeat();
                var resume = afterStanding;
                afterStanding = null;
                resume?.Invoke();
            }
            if (IsLocallyControlled)
                ReadLocalControls();
            if (Action == WorkerAction.Sitting && onSeat && (occupiedSeat == null || !occupiedSeat.isActiveAndEnabled ||
                (occupiedSeat.AnimationRoot(occupiedSeatFacing) - occupiedSeatRoot).sqrMagnitude > .0004f ||
                Quaternion.Angle(occupiedSeatFacing, transform.rotation) > .5f))
                Cancel();
            UpdateVisualJump();
            UpdateEating();
            bool working = Action is WorkerAction.Harvesting or WorkerAction.Attacking;
            bool carried = UpdateTimberCarry();
            bool awaitingAutomaticHarvest = IsLocallyControlled && village?.AwaitingAutomaticHarvest == true;
            if (!manualMoving && Action == WorkerAction.Moving)
            {
                float speed = TravelSpeed(IsWalking);
                if (!Mathf.Approximately(agent.speed, speed))
                    agent.speed = speed;
                UpdateMovement();
            }
            else if (axeCut != null && !awaitingAutomaticHarvest)
                UpdateAxeCut();
            else if (Action == WorkerAction.Harvesting && !awaitingAutomaticHarvest)
                UpdateHarvest();
            else if (Action == WorkerAction.Attacking && !awaitingAutomaticHarvest)
                UpdateAttack();
            if (!working && !carried)
                characterState?.RecoverStamina(Time.deltaTime, Action == WorkerAction.Sitting);
            var motion = Action switch
            {
                WorkerAction.Moving => IsWalking ? WorkerMotion.Walk : WorkerMotion.Run,
                WorkerAction.Harvesting or WorkerAction.Attacking => WorkerMotion.Work,
                WorkerAction.Sitting => onSeat ? WorkerMotion.SeatSit : WorkerMotion.GroundSit,
                _ => WorkerMotion.Idle
            };
            animationDriver?.SetAction((int)motion);
            animationDriver?.SetOvereating(characterState?.Overeating ?? 0f);
        }

        private void LateUpdate()
        {
            if (seatVisual != null)
                seatVisual.localPosition = seatVisualBase + Vector3.up * (SeatLift * (animationDriver?.SeatLiftWeight ?? 1f));
        }

        private void RefreshEquipment()
        {
            if (actor == null || characterState == null || equipmentVersion == characterState.Version)
                return;
            equipmentVersion = characterState.Version;
            actor.ShowEquipment(characterState.Equipment, village.Settings);
        }

        private void ReadLocalControls()
        {
            ReadInput();
            if (village != null && village.Hud.WorldActionsLocked && (Action != WorkerAction.Idle || IsEating))
                Cancel();
            if (WorkerbornInput.Pressed("Gameplay/Sit"))
                ToggleSitOrRide();
            if (WorkerbornInput.Pressed("Gameplay/Jump"))
                CommandJump();
            UpdateManualMovement();
            UpdateNearbyCollection();
        }

        private void UpdateVisualJump()
        {
            if (jumpingVisual != null)
            {
                float progress = (Time.time - jumpStartedAt) / WorkerAnimation.JumpSeconds;
                if (progress >= 1f || !AcceptsInteractionCommands || Action == WorkerAction.Sitting)
                    FinishVisualJump();
                else
                {
                    // Peers without the jump clip can still show the visual hop.
                    VisualJumpOffset = JumpHeight * 4f * progress * (1f - progress);
                    if (!jumpUsesClip)
                        jumpingVisual.localPosition = jumpBase + Vector3.up * VisualJumpOffset;
                }
            }
        }

        internal void CommandJump()
        {
            if (jumpingVisual != null || IsStandingUp || CarriedLogs > 0 || !AcceptsInteractionCommands ||
                Action is not (WorkerAction.Idle or WorkerAction.Moving))
                return;
            StopEating();
            jumpingVisual = actor?.VisualRoot;
            if (jumpingVisual == null)
                return;
            jumpBase = jumpingVisual.localPosition;
            jumpStartedAt = Time.time;
            animationDriver?.SetGesture(WorkerGesture.Jump);
            jumpUsesClip = animationDriver?.Gesture == WorkerGesture.Jump;
        }

        private void FinishVisualJump()
        {
            if (jumpingVisual != null && !jumpUsesClip)
                jumpingVisual.localPosition = jumpBase;
            jumpingVisual = null;
            VisualJumpOffset = 0f;
            if (animationDriver?.Gesture == WorkerGesture.Jump)
                animationDriver.SetGesture(WorkerGesture.None);
        }

        internal bool ToggleSit(SeatSlot preferred = null)
        {
            if (CarriedLogs > 0 || IsStandingUp || !AcceptsInteractionCommands || agent == null || !agent.isOnNavMesh)
                return false;
            if (Action == WorkerAction.Sitting)
            {
                Cancel();
                return true;
            }
            if (Action == WorkerAction.Harvesting || Action == WorkerAction.Attacking)
                return false;

            Cancel();
            FinishVisualJump();
            SeatSlot nearest = null;
            Vector3 foothold = default;
            Quaternion facing = default;
            float bestDistance = float.PositiveInfinity;
            foreach (var seat in FindObjectsByType<SeatSlot>(FindObjectsSortMode.None))
            {
                if (preferred != null && preferred != seat || !seat.isActiveAndEnabled || !seat.Covers(transform.position) ||
                    village?.SeatInUse(seat.Position) == true)
                    continue;
                float distance = (seat.Position - transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                if (!TrySeatFooting(seat, transform.position, out var point, out var direction))
                    continue;
                nearest = seat;
                bestDistance = distance;
                foothold = point;
                facing = direction;
            }
            if (preferred != null && nearest == null)
                return false;
            if (nearest != null)
            {
                if (!agent.Warp(foothold))
                    return false;
                agent.isStopped = true;
                agent.updateRotation = false;
                transform.rotation = facing;
                onSeat = true;
                occupiedSeat = nearest;
                occupiedSeatFacing = facing;
                occupiedSeatRoot = nearest.AnimationRoot(facing);
                SeatLift = Mathf.Clamp(occupiedSeatRoot.y - foothold.y, -.12f, .65f);
                seatVisual = actor?.VisualRoot;
                if (seatVisual != null)
                    seatVisualBase = seatVisual.localPosition;
            }
            Action = WorkerAction.Sitting;
            animationDriver?.SetAction((int)(onSeat ? WorkerMotion.SeatSit : WorkerMotion.GroundSit));
            return true;
        }

        internal bool TrySeatFooting(SeatSlot seat, Vector3 approach, out Vector3 point, out Quaternion facing)
        {
            point = default;
            facing = seat.FacingFor(approach, transform.rotation);
            if (agent == null || !agent.isOnNavMesh)
                return false;

            Vector3 root = seat.AnimationRoot(facing);
            var ground = village != null ? village.SurfacePosition(root.x, root.z) : new Vector3(root.x, transform.position.y, root.z);
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(ground, out var hit, .45f, filter) || !seat.Covers(hit.position) || !SharesMap(hit.position))
                return false;
            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            point = hit.position;
            return true;
        }

        private bool UpdateManualMovement()
        {
            Vector2 input = village?.MovementBlocked == true ? Vector2.zero : WorkerbornInput.Move;
            if (input.sqrMagnitude < .001f)
            {
                if (manualMoving)
                    Cancel();
                return false;
            }

            if (Action == WorkerAction.Sitting)
                Cancel();
            if (IsStandingUp)
            {
                afterStanding = null;
                return false;
            }

            if (!manualMoving)
            {
                Cancel();
                agent.isStopped = false;
                agent.updateRotation = false;
            }
            manualMoving = true;
            manualWalking = WorkerbornInput.Held("Gameplay/Walk");
            Action = WorkerAction.Moving;
            Vector3 right = worldCamera.transform.right;
            right.y = 0f;
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            Vector3 direction = Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
            agent.speed = TravelSpeed(IsWalking);
            MoveWithWorldCollision(direction * (agent.speed * Time.deltaTime));
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                    agent.angularSpeed * Time.deltaTime);
            return true;
        }

        private void MoveWithWorldCollision(Vector3 displacement)
        {
            Vector3 origin = agent.nextPosition;
            Vector3 step = SweepMovement(origin, displacement, out var hit);
            if (hit.collider != null)
            {
                Vector3 normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude > .001f)
                {
                    Vector3 slide = Vector3.ProjectOnPlane(displacement - step, normal);
                    step += SweepMovement(origin + step, slide, out _);
                }
            }
            agent.Move(step);
        }

        private Vector3 SweepMovement(Vector3 origin, Vector3 displacement, out RaycastHit hit)
        {
            const float skin = .035f;
            const int blockers = (1 << VillageNavigationBlocker.Layer) | (1 << VillageMapProp.PointerGeometryLayer);
            float distance = displacement.magnitude;
            hit = default;
            if (distance < .0001f)
                return Vector3.zero;

            float radius = agent.radius;
            Vector3 bottom = origin + Vector3.up * (radius + .08f);
            Vector3 top = origin + Vector3.up * Mathf.Max(radius + .08f, agent.height - radius);
            Vector3 direction = displacement / distance;
            return Physics.CapsuleCast(bottom, top, radius, direction, out hit, distance + skin, blockers, QueryTriggerInteraction.Ignore)
                ? direction * Mathf.Clamp(hit.distance - skin, 0f, distance) : displacement;
        }

        private void UpdateNearbyCollection()
        {
            const string action = "Gameplay/CollectNearby";
            if (!AcceptsGroundItemCommands || !WorkerbornInput.Held(action))
            {
                ResetCollectHold();
                return;
            }

            float now = Time.unscaledTime;
            if (WorkerbornInput.Pressed(action))
                consecutiveCollections = 0;
            else if (now < nextCollect)
                return;

            GroundPickup nearest = null;
            float bestDistance = float.MaxValue;
            foreach (var candidate in WorldInteractable.Active)
            {
                if (!(candidate is GroundPickup item) || !item.CanCollectFor(this) || item.IsInFlight || !IsInPickupRange(item))
                    continue;
                Vector3 offset = item.LandingPosition - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude < bestDistance)
                {
                    nearest = item;
                    bestDistance = offset.sqrMagnitude;
                }
            }

            if (nearest != null && nearest == pickupTarget.Target)
                Cancel();
            bool collected = nearest != null && CollectPickup(nearest);
            int ramp = Mathf.Max(2, collectRampPickups);
            consecutiveCollections = collected ? Mathf.Min(consecutiveCollections + 1, ramp) : 0;
            float progress = Mathf.Clamp01((consecutiveCollections - 1f) / (ramp - 1));
            float initialInterval = Mathf.Max(.04f, collectRepeatSeconds);
            float fastestInterval = Mathf.Clamp(collectFastestRepeatSeconds, .01f, initialInterval);
            nextCollect = now + Mathf.Lerp(initialInterval, fastestInterval, progress * progress) / (CharacterState?.ActionSpeed ?? 1f);
        }

        private void ResetCollectHold()
        {
            consecutiveCollections = 0;
            nextCollect = 0f;
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
            if (village != null || !AcceptsInteractionCommands || !WorkerbornInput.TryGetPointerPosition(out var screen) ||
                !WorkerbornInput.IsInView(worldCamera, screen))
                return;
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
                CommandMove(hit.point, WorkerbornInput.Held("Gameplay/Walk"));
        }

        public void CommandHarvest(HarvestableCrop crop)
        {
            if (crop == null || crop is FishingWater || !crop.isActiveAndEnabled)
                return;
            if (crop == cropTarget && (Action == WorkerAction.Moving || Action == WorkerAction.Harvesting))
                return;
            if (crop.IsDepleted)
            {
                LastCrop = crop;
                Message?.Invoke("이 작물은 수확을 모두 마쳤어요. 다른 작물을 선택해 주세요.");
                return;
            }
            if (village != null && (!AcceptsInteractionCommands || !village.CanHarvest(this, crop)))
                return;
            if (DeferUntilStanding(() => CommandHarvest(crop)))
                return;

            if (!NavigateNear(crop.transform.position, crop.InteractionDistance * 0.85f))
                return;
            cropTarget = crop;
            LastCrop = crop;
            if (village == null)
                Message?.Invoke($"{crop.Definition.DisplayName} 밭으로 이동해요.");
        }

        internal void RestoreHarvest(HarvestableCrop crop, float progress, Vector3? castPoint)
        {
            if (crop is FishingWater water)
            {
                if (castPoint is Vector3 point && water.CanInteract && water.CoversGround(point))
                    CommandFish(water, point, progress);
            }
            else
            {
                crop.RequestInteraction(this);
                if (cropTarget == crop)
                    savedHarvestProgress = progress;
            }
        }

        public bool IsInPickupRange(GroundPickup item) => item != null && InReach(item.LandingPosition, pickupDistance);
        public void CommandPickup(GroundPickup item)
        {
            if (!AcceptsGroundItemCommands || item == null || !item.CanCollect)
                return;
            if (!item.CanCollectFor(this))
            {
                Message?.Invoke("아직 소유자만 주울 수 있는 아이템입니다.");
                return;
            }
            if (IsInPickupRange(item))
            {
                CollectPickup(item);
                return;
            }

            if (!AcceptsMovementCommands)
                return;
            if (pickupTarget.Target == item && Action == WorkerAction.Moving)
                return;
            if (Action == WorkerAction.Sitting || IsStandingUp)
            {
                var pickup = new WorldTargetReference(item);
                if (DeferUntilStanding(() => CommandPickup(pickup.Target as GroundPickup)))
                    return;
            }
            if (NavigateNear(item.LandingPosition, pickupDistance * .8f))
                pickupTarget = new WorldTargetReference(item);
        }

        private bool CollectPickup(GroundPickup item)
        {
            if (!IsInPickupRange(item) || !item.CanCollectFor(this))
                return false;
            if (village != null)
                return village.TryCollectPickup(this, item);
            CropDefinition definition = item.Definition;
            int quantity = item.Quantity;
            string displayName = item.ItemDisplayName;
            if (!item.TryCollect(this))
                return false;
            if (definition != null)
            {
                inventory.TryGetValue(definition, out int count);
                inventory[definition] = count + quantity;
            }

            collectedCount += quantity;
            Message?.Invoke($"{displayName} +{quantity}");
            return true;
        }

        public void CommandMove(Vector3 destination, bool walk = false)
        {
            if (!AcceptsMovementCommands || DeferUntilStanding(() => CommandMove(destination, walk)))
                return;
            if (NavigateNear(destination, 0f, walk) && IsLocallyControlled)
                village?.ShowClick(destination);
        }

        public void CommandInteract(VillageInteractable target)
        {
            if (village != null && !IsLocallyControlled && Action is (WorkerAction.Idle or WorkerAction.Sitting) &&
                !IsStandingUp && target != null && target.CanInteract && CanInteractWith(target) &&
                SharesMap(target.transform.position) && InReach(target.transform.position, target.InteractionDistance) &&
                target.TryGetComponent<HarvestWorker>(out var partner) && HasDirectPathTo(partner))
            {
                target.Arrive(village, this);
                return;
            }
            CommandApproach(target);
        }

        public void CommandAttack(WorldInteractable target)
        {
            if (village == null || CarriedLogs > 0 || target == null || !CanInteractWith(target) || !target.CanReceiveStrike ||
                CharacterState.Health <= 0 || Action == WorkerAction.Attacking || Time.time < nextStrikeAt)
                return;
            if (DeferUntilStanding(() => CommandAttack(target)))
                return;
            if (InReach(target.transform.position, InteractionReach(target)))
                StartAttack(target);
            else if (target != interactionTarget || Action != WorkerAction.Moving)
                CommandApproach(target, () => StartAttack(target));
        }

        private void StartAttack(WorldInteractable target)
        {
            if (target == null || !target.CanReceiveStrike || CarriedLogs > 0 || CharacterState.Health <= 0 ||
                !CanInteractWith(target) || !SharesMap(target.transform.position) ||
                !InReach(target.transform.position, InteractionReach(target)))
                return;
            var mount = CurrentMount;
            if (mount == null && CharacterState.Stamina <= 0d)
            {
                Message?.Invoke("스태미나가 부족해요.");
                return;
            }
            Cancel();
            FinishVisualJump();
            Face(target.transform.position);
            strikeTarget = target;
            Action = WorkerAction.Attacking;
            WorkSequence++;
            workFeedbackUntil = 0f;
            var clip = mount == null ? target.StrikeClip(this) ?? attackClip : null;
            strikeStaminaCost = mount == null ? target.StrikeStaminaCost(CharacterState, attackStaminaCost) : 0;
            if (target is TimberPiece piece)
            {
                BeginAxeCut(new TimberOrder { action = TimberAction.Split, target = piece.TargetId, part = piece.Part },
                    clip, target.StrikeImpact, strikeStaminaCost);
                return;
            }

            StartStrikeClock(mount != null ? mount.AttackSeconds : attackDelay / CharacterState.ActionSpeed);
            if (mount == null)
                animationDriver?.BeginStrike(clip);
        }

        private void StartStrikeClock(float duration)
        {
            strikeStartedAt = Time.time;
            nextStrikeAt = strikeStartedAt + Mathf.Max(.2f, duration);
            strikePaidProgress = 0d;
            struck = false;
        }

        private float StrikePhase => Mathf.InverseLerp(strikeStartedAt, nextStrikeAt, Time.time);

        private void UpdateAttack()
        {
            if (CharacterState.Health <= 0 || strikeTarget == null || !CanInteractWith(strikeTarget) || !SharesMap(strikeTarget.transform.position))
            {
                Cancel();
                return;
            }
            float progress = StrikePhase;
            var mount = CurrentMount;
            if (mount == null)
                animationDriver?.SampleStrike(progress);
            if (!struck)
            {
                Face(strikeTarget.transform.position);
                float impact = mount != null ? mount.AttackImpact : strikeTarget.StrikeImpact;
                double effort = Mathf.Min(1f, progress / impact);
                if (mount == null && !CharacterState.SpendWorkProgress(strikeStaminaCost, strikePaidProgress, effort))
                {
                    Cancel();
                    ReportWorkExhaustion();
                    return;
                }
                strikePaidProgress = effort;
                if (progress >= impact)
                {
                    struck = true;
                    if (mount != null && strikeTarget is MonsterInteractable monster)
                        monster.ReceiveMountedStrike(this, mount.AttackDamage);
                    else if (strikeTarget.CanReceiveStrike)
                    {
                        int bonus = CharacterState.Equipment.ContainsKey(EquipmentSlot.MainHand) ? 0 : CharacterState.UnarmedDamageBonus;
                        strikeTarget.ReceiveStrike(this, Mathf.Max(1, attackDamage + bonus + CharacterState.Strength));
                    }
                }
            }
            if (progress >= 1f)
                Cancel();
        }

        public void CommandApproach(WorldInteractable target, System.Action arrival = null) => Approach(target, null, arrival);

        private float InteractionReach(WorldInteractable target) =>
            CurrentMount != null && target is MonsterInteractable { Combat: not null }
                ? CurrentMount.AttackReach : target.InteractionDistance;

        internal void CommandApproachAt(WorldInteractable target, Vector3 point, System.Action arrival = null) =>
            Approach(target, point, arrival);

        private void Approach(WorldInteractable target, Vector3? point, System.Action arrival)
        {
            if (village == null || target == null || !CanInteractWith(target) || !SharesMap(target.transform.position) ||
                DeferUntilStanding(() => Approach(target, point, arrival)))
                return;
            float offset = point.HasValue ? 0f : InteractionReach(target) * .7f;
            if (!NavigateNear(point ?? target.transform.position, offset, walk: !IsLocallyControlled && target is VillageInteractable))
                return;
            interactionTarget = target;
            onInteractionArrival = arrival;
        }

        public bool CommandDrop(int entryId, Vector3 destination)
        {
            if (village == null || !HasPlayerOwner || CurrentMount != null || !AcceptsGroundItemCommands || agent == null || !agent.isOnNavMesh)
                return false;
            var entry = CharacterState.CarriedItem(entryId);
            if (village.DropPrefabFor(entry, this) == null)
                return false;
            bool inReach = InReach(destination, pickupDistance);
            if (!inReach && IsLocallyControlled && village.Hud.WorldActionsLocked)
            {
                Message?.Invoke("아이템을 손이 닿는 바닥에 놓아 주세요.");
                return false;
            }
            if (DeferUntilStanding(() => CommandDrop(entryId, destination)))
                return true;
            if (!inReach && !NavigateNear(destination, Mathf.Max(0f, pickupDistance - .85f)) &&
                !NavigateNear(destination, 0f))
            {
                Message?.Invoke("아이템을 놓을 곳까지 갈 수 없어요.");
                return false;
            }
            pendingDrop = entry.Copy();
            dropPoint = destination;
            if (inReach)
                return FinishDrop();
            if (IsLocallyControlled)
                village.ShowClick(destination);
            return true;
        }

        private bool NavigateNear(Vector3 target, float offset, bool walk = false)
        {
            if (!TryApproachPath(target, offset, out var path, out _) || !FollowPath(path, walk))
                return false;
            approachTargetPosition = target;
            return true;
        }

        internal bool TryGetReachablePoint(Vector3 target, float offset, out Vector3 point) =>
            TryApproachPath(target, offset, out _, out point);

        private bool TryApproachPath(Vector3 target, float offset, out NavMeshPath path, out Vector3 point)
        {
            path = null;
            point = default;
            if (agent == null || !agent.isOnNavMesh || !SharesMap(target))
                return false;
            Vector3 direction = StandingPosition - target;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                direction = Vector3.back;
            Vector3 approach = target + direction.normalized * Mathf.Min(offset, direction.magnitude);
            if (offset > 0 && village != null && village.GroundTerrain != null)
                approach = village.SurfacePosition(approach.x, approach.z);
            if (!NavMesh.SamplePosition(approach, out NavMeshHit hit, 0.8f, NavMesh.AllAreas))
                return false;

            point = hit.position;
            path = new NavMeshPath();
            return agent.CalculatePath(point, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        internal bool SharesMap(Vector3 point) => village == null || village.SharesMap(StandingPosition, point);

        private bool FollowPath(NavMeshPath path, bool walk = false)
        {
            if (!agent.SetPath(path))
                return false;

            ClearInteractionTargets();
            walkPath = walk;
            agent.speed = TravelSpeed(walk);
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
            if (pickupTarget.IsAssigned)
            {
                var item = pickupTarget.Target as GroundPickup;
                if (item == null)
                {
                    Cancel();
                    return;
                }

                if (IsInPickupRange(item))
                {
                    pickupTarget = default;
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
            if (RetryAnimalApproach())
                return;
            agent.isStopped = true;
            agent.ResetPath();
            if (pendingDrop != null)
            {
                Cancel();
                Message?.Invoke("그곳에 아이템을 놓을 수 없어요.");
            }
            else if (cropTarget != null)
                BeginHarvesting();
            else if (pickupTarget.IsAssigned)
            {
                // A sampled approach point may land outside reach near obstacles.
                pickupTarget = default;
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
                if (CanInteractWith(target) && InReach(target.transform.position, InteractionReach(target)))
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

        private bool RetryAnimalApproach()
        {
            WorldInteractable target = cropTarget != null ? cropTarget : interactionTarget;
            if (target == null || !target.CanInteract || !target.TryGetComponent<AnimalWander>(out _) ||
                InReach(target.transform.position, InteractionReach(target)) ||
                (target.transform.position - approachTargetPosition).sqrMagnitude < .2f * .2f)
                return false;
            var crop = cropTarget;
            var arrival = onInteractionArrival;
            if (!NavigateNear(target.transform.position, InteractionReach(target) * .7f))
                return false;
            cropTarget = crop;
            if (crop == null)
            {
                interactionTarget = target;
                onInteractionArrival = arrival;
            }
            return true;
        }

        private bool FinishDrop()
        {
            var requested = pendingDrop;
            var position = dropPoint;
            Cancel();
            Face(position);
            return village.PlaceInventoryDrop(this, requested, position);
        }

        private Vector3 HarvestPoint => FishingPoint ?? cropTarget.transform.position;

        private void BeginHarvesting()
        {
            if (village != null && !village.CanHarvest(this, cropTarget))
            {
                Cancel();
                return;
            }
            if (!InReach(HarvestPoint, cropTarget.InteractionDistance + .15f) || !cropTarget.TryBegin(this, savedHarvestProgress))
            {
                bool fishing = FishingPoint.HasValue;
                Cancel();
                Message?.Invoke(fishing ? "지금 이곳에서는 낚시할 수 없어요." : "이 대상은 지금 채집할 수 없어요.");
                return;
            }

            Action = WorkerAction.Harvesting;
            WorkSequence++;
            workFeedbackUntil = 0f;
            savedHarvestProgress = 0f;
            FinishVisualJump();
            harvestOrigin = HarvestPoint;
            Face(harvestOrigin);
            if (cropTarget is LoggingTree tree && village != null)
            {
                var definition = tree.Definition;
                var work = definition.WorkAnimation;
                int stamina = CharacterState.WorkEnergyCost(definition.Skill, definition.ItemId,
                    definition.StaminaCost > 0 ? definition.StaminaCost : CharacterState.Rules.HarvestEnergy);
                BeginAxeCut(new TimberOrder { action = TimberAction.Fell, target = tree.PersistentId },
                    work?.Loop ?? attackClip, work?.LoopImpact ?? .5f, stamina);
                return;
            }
            animationDriver?.BeginWork(cropTarget.Definition.WorkAnimation, cropTarget.Definition.HarvestSeconds);
            animationDriver?.SampleWork(cropTarget.Progress);
            if (FishingPoint is Vector3 spot)
                ShowFishingCast(spot);
            if (village == null)
                Message?.Invoke("수확 중이에요. 완료하면 감자가 바닥에 떨어져요.");
        }

        private void UpdateHarvest()
        {
            if (cropTarget == null || !cropTarget.isActiveAndEnabled || !InReach(harvestOrigin, cropTarget.InteractionDistance + .3f))
            {
                Cancel();
                return;
            }
            if (cropTarget is AnimalNpc animal && animal.HasMovedOutOfHarvest(harvestOrigin))
            {
                Cancel();
                ShowGatherResult(false);
                Message?.Invoke("동물이 너무 멀리 움직여서 채집에 실패했어요.");
                return;
            }
            if (village != null && !village.CanHarvest(this, cropTarget, requireStamina: false))
            {
                Cancel();
                return;
            }

            bool completed = cropTarget.Advance(this, Time.deltaTime * (CharacterState?.ActionSpeed ?? 1f));
            animationDriver?.SampleWork(cropTarget.Progress);
            if (!completed)
                return;

            if (cropTarget.Progress >= 1f)
            {
                completedWorkSkill = cropTarget.Definition.Skill;
                workFeedbackUntil = Time.unscaledTime + .22f;
            }
            if (village == null)
            {
                ShowGatherResult(true);
                Message?.Invoke($"수확 완료! 바닥의 {cropTarget.Definition.DisplayName}를 클릭해서 주워 주세요.");
            }
            cropTarget = null;
            savedHarvestProgress = 0f;
            ClearFishingCast();
            Action = WorkerAction.Idle;
            HarvestAttemptFinished?.Invoke();
        }

        internal void ShowGatherResult(bool success) =>
            animationDriver?.SetGesture(success ? WorkerGesture.GatherSuccess : WorkerGesture.GatherFailure);

        internal void ReportWorkExhaustion(string message = "스태미나가 바닥나 작업에 실패했어요.")
        {
            ShowGatherResult(false);
            Message?.Invoke(message);
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

        internal bool HasDirectPathTo(HarvestWorker person) => agent != null && agent.isOnNavMesh &&
            !agent.Raycast(person.StandingPosition, out _);

        internal void FaceConversationPartner(Vector3 position)
        {
            if (Action == WorkerAction.Idle && !IsStandingUp)
                Face(position);
        }

        internal void PlayConversationGesture(ConversationGesture gesture, float seconds) =>
            animationDriver?.PlayConversationGesture(village.Settings.ConversationClip(gesture),
                headOnly: gesture == ConversationGesture.Nod, seconds);

        internal void StopConversationGesture() => animationDriver?.StopConversationGesture();

        private void ClearInteractionTargets()
        {
            StopEating();
            if (manualMoving && agent != null)
                agent.updateRotation = true;
            manualMoving = false;
            walkPath = false;
            if (cropTarget != null)
                cropTarget.Cancel(this);
            cropTarget = null;
            savedHarvestProgress = 0f;
            ClearFishingCast();
            pickupTarget = default;
            interactionTarget = null;
            strikeTarget = null;
            axeCut = null;
            onInteractionArrival = null;
            pendingDrop = null;
        }

        internal bool DeferUntilStanding(System.Action command)
        {
            if (Action == WorkerAction.Sitting)
                Cancel();
            if (!IsStandingUp)
                return false;
            afterStanding = command;
            return true;
        }

        private void ReleaseSeat()
        {
            if (!onSeat)
                return;
            onSeat = false;
            occupiedSeat = null;
            if (seatVisual != null)
                seatVisual.localPosition = seatVisualBase;
            seatVisual = null;
            SeatLift = 0f;
            if (agent != null)
                agent.updateRotation = true;
        }

        private void ClearStandUp()
        {
            afterStanding = null;
            animationDriver?.SkipStandUp();
            ReleaseSeat();
        }

        public void Cancel()
        {
            afterStanding = null;
            ClearInteractionTargets();
            Action = WorkerAction.Idle;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            animationDriver?.SetAction((int)WorkerMotion.Idle);
            if (!IsStandingUp)
                ReleaseSeat();
            if (animationDriver?.Gesture is WorkerGesture.GatherSuccess or WorkerGesture.GatherFailure)
                animationDriver.SetGesture(WorkerGesture.None);
        }

        public void ResetWorker(Vector3 spawn)
        {
            ResetLifeActivity();
            agent.Warp(spawn);
            transform.rotation = Quaternion.identity;
        }

        internal void ResetLifeActivity()
        {
            EndRiding(false, parkForTravel: true);
            FinishTimberTravel();
            Cancel();
            FinishVisualJump();
            ClearStandUp();
            StandUpFromBed();
            ResetCollectHold();
            inventory.Clear();
            collectedCount = 0;
            nextStrikeAt = 0;
            if (IsLocallyControlled)
                village?.Hud.ShowPlayerHealthUntil(0f);
            LastCrop = null;
            HoveredCrop = null;
            HoveredPickup = null;
            HoveredInteraction = null;
        }

        public bool WarpTo(Vector3 position)
        {
            EndRiding(false, parkForTravel: true);
            FinishTimberTravel();
            Cancel();
            FinishVisualJump();
            ClearStandUp();
            StandUpFromBed();
            return agent.Warp(position);
        }

        internal bool EnsureNavigation()
        {
            if (agent == null)
                return false;
            if (agent.isOnNavMesh)
                return true;
            Vector3 position = village.SurfacePosition(transform.position.x, transform.position.z);
            if ((!NavMesh.SamplePosition(position, out var hit, 1.5f, NavMesh.AllAreas) &&
                !NavMesh.SamplePosition(position, out hit, VillagePlot.CellSide * 2f, NavMesh.AllAreas)) || !SharesMap(hit.position))
                return false;
            agent.enabled = false;
            transform.position = hit.position;
            agent.enabled = true;
            return agent.isOnNavMesh;
        }

        internal void LieOnBed(Transform pose)
        {
            FinishVisualJump();
            ClearStandUp();
            restingVisual = actor?.VisualRoot;
            if (restingVisual == null)
                return;
            restBase = restingVisual.localPosition;
            restRotation = restingVisual.localRotation;
            restingVisual.SetPositionAndRotation(pose.position, pose.rotation);
        }

        internal void StandUpFromBed()
        {
            if (restingVisual != null)
                restingVisual.SetLocalPositionAndRotation(restBase, restRotation);
            restingVisual = null;
        }

        private void OnDisable()
        {
            Cancel();
            FinishVisualJump();
            ClearStandUp();
            StandUpFromBed();
            ResetCollectHold();
        }
    }
}
