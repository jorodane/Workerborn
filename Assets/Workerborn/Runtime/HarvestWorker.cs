using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Workerborn
{
    public enum WorkerAction { Idle, Moving, Harvesting, PickingUp }

    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class HarvestWorker : MonoBehaviour
    {
        private const float HarvestReach = 1.05f;
        private const float PickupReach = 0.75f;
        private readonly Dictionary<CropDefinition, int> inventory = new Dictionary<CropDefinition, int>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private Camera worldCamera;
        private NavMeshAgent agent;
        private WorkerAnimation animationDriver;
        private HarvestableCrop cropTarget;
        private GroundPickup pickupTarget;
        private float pickupTime;

        public WorkerAction Action { get; private set; }
        public HarvestableCrop HoveredCrop { get; private set; }
        public GroundPickup HoveredPickup { get; private set; }
        public HarvestableCrop LastCrop { get; private set; }
        public HarvestableCrop ActiveCrop => Action == WorkerAction.Harvesting ? cropTarget : null;
        public HarvestableCrop FocusCrop => HoveredCrop != null ? HoveredCrop : cropTarget != null ? cropTarget : LastCrop;
        public int TotalCollected { get; private set; }
        public event Action<string> Message;

        public void Initialize(Camera camera, WorkerAnimation animations)
        {
            worldCamera = camera;
            animationDriver = animations;
            agent = GetComponent<NavMeshAgent>();
            agent.speed = 3.7f;
            agent.angularSpeed = 540f;
            agent.acceleration = 20f;
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.stoppingDistance = 0.08f;
            agent.autoBraking = true;
        }

        private void Update()
        {
            if (worldCamera == null || agent == null || !agent.isOnNavMesh) return;
            ReadInput();
            if (Action == WorkerAction.Moving) UpdateMovement();
            else if (Action == WorkerAction.Harvesting) UpdateHarvest();
            else if (Action == WorkerAction.PickingUp) UpdatePickup();
            animationDriver?.SetAction(Action == WorkerAction.Moving && agent.velocity.sqrMagnitude > 0.02f ? 1
                : Action == WorkerAction.Harvesting || Action == WorkerAction.PickingUp ? 2 : 0);
        }

        private void ReadInput()
        {
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                Cancel();
                Message?.Invoke("작업을 취소했어요. 남은 수확 횟수는 그대로예요.");
            }

            HoveredCrop = null;
            HoveredPickup = null;
            var pointer = Pointer.current;
            if (pointer == null) return;
            Vector2 screen = pointer.position.ReadValue();
            // Raycast the UI now, rather than relying on the previous frame's pointer state.
            if (EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, uiHits);
                if (uiHits.Count > 0) return;
            }
            if (!Physics.Raycast(worldCamera.ScreenPointToRay(screen), out RaycastHit hit, 200f,
                ~(1 << 2), QueryTriggerInteraction.Collide)) return;
            HoveredCrop = hit.collider.GetComponentInParent<HarvestableCrop>();
            HoveredPickup = hit.collider.GetComponentInParent<GroundPickup>();
            if (!pointer.press.wasPressedThisFrame) return;
            if (HoveredPickup != null) CommandPickup(HoveredPickup);
            else if (HoveredCrop != null) CommandHarvest(HoveredCrop);
            else CommandMove(hit.point);
        }

        public void CommandHarvest(HarvestableCrop crop)
        {
            if (crop == null || !crop.isActiveAndEnabled) return;
            if (crop == cropTarget && (Action == WorkerAction.Moving || Action == WorkerAction.Harvesting)) return;
            if (crop.IsDepleted)
            {
                LastCrop = crop;
                Message?.Invoke("이 작물은 수확을 모두 마쳤어요. 다른 작물을 선택해 주세요.");
                return;
            }
            Cancel();
            cropTarget = crop;
            LastCrop = crop;
            if (!NavigateNear(crop.transform.position, HarvestReach * 0.85f)) return;
            Message?.Invoke($"{crop.Definition.DisplayName} 밭으로 이동해요.");
        }

        public void CommandPickup(GroundPickup item)
        {
            if (item == null || !item.isActiveAndEnabled) return;
            if (item == pickupTarget && (Action == WorkerAction.Moving || Action == WorkerAction.PickingUp)) return;
            Cancel();
            pickupTarget = item;
            if (!NavigateNear(item.LandingPosition, PickupReach * 0.7f)) return;
            Message?.Invoke($"떨어진 {item.Definition.DisplayName}를 주우러 가요.");
        }

        public void CommandMove(Vector3 destination)
        {
            Cancel();
            NavigateNear(destination, 0f);
        }

        private bool NavigateNear(Vector3 target, float offset)
        {
            Vector3 direction = transform.position - target;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.back;
            Vector3 approach = target + direction.normalized * Mathf.Min(offset, direction.magnitude);
            if (!NavMesh.SamplePosition(approach, out NavMeshHit hit, 0.8f, NavMesh.AllAreas))
            {
                Cancel();
                Message?.Invoke("밭 안의 이동할 수 있는 곳을 선택해 주세요.");
                return false;
            }
            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                Cancel();
                Message?.Invoke("그곳으로 가는 길을 찾을 수 없어요.");
                return false;
            }
            agent.isStopped = false;
            agent.SetPath(path);
            Action = WorkerAction.Moving;
            return true;
        }

        private void UpdateMovement()
        {
            if (agent.pathPending) return;
            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                Cancel();
                return;
            }
            if (agent.remainingDistance > agent.stoppingDistance + 0.06f) return;
            agent.isStopped = true;
            agent.ResetPath();
            if (cropTarget != null)
            {
                if (!InReach(cropTarget.transform.position, HarvestReach + 0.15f) || !cropTarget.TryBegin(this))
                {
                    Cancel();
                    Message?.Invoke("이 작물에서는 지금 수확할 수 없어요.");
                    return;
                }
                Action = WorkerAction.Harvesting;
                Face(cropTarget.transform.position);
                Message?.Invoke("수확 중이에요. 완료하면 감자가 바닥에 떨어져요.");
            }
            else if (pickupTarget != null)
            {
                Action = WorkerAction.PickingUp;
                pickupTime = 0f;
                Face(pickupTarget.LandingPosition);
            }
            else Action = WorkerAction.Idle;
        }

        private void UpdateHarvest()
        {
            if (cropTarget == null || !cropTarget.isActiveAndEnabled
                || !InReach(cropTarget.transform.position, HarvestReach + 0.3f))
            {
                Cancel();
                return;
            }
            if (!cropTarget.Advance(this, Time.deltaTime)) return;
            Message?.Invoke($"수확 완료! 바닥의 {cropTarget.Definition.DisplayName}를 클릭해서 주워 주세요.");
            cropTarget = null;
            Action = WorkerAction.Idle;
        }

        private void UpdatePickup()
        {
            if (pickupTarget == null || !pickupTarget.isActiveAndEnabled
                || !InReach(pickupTarget.LandingPosition, PickupReach + 0.25f))
            {
                Cancel();
                return;
            }
            pickupTime += Time.deltaTime;
            if (pickupTime < 0.3f || !pickupTarget.CanCollect) return;
            CropDefinition definition = pickupTarget.Definition;
            if (pickupTarget.TryCollect())
            {
                inventory.TryGetValue(definition, out int count);
                inventory[definition] = count + 1;
                TotalCollected++;
                Message?.Invoke($"{definition.DisplayName} +1  ·  가방에 담았어요.");
            }
            pickupTarget = null;
            Action = WorkerAction.Idle;
        }

        private bool InReach(Vector3 position, float range)
        {
            Vector3 delta = position - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= range * range;
        }

        private void Face(Vector3 position)
        {
            Vector3 direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        public void Cancel()
        {
            if (cropTarget != null) cropTarget.Cancel(this);
            cropTarget = null;
            pickupTarget = null;
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
            TotalCollected = 0;
            LastCrop = null;
            HoveredCrop = null;
            HoveredPickup = null;
            agent.Warp(spawn);
            transform.rotation = Quaternion.identity;
        }

        private void OnDisable() => Cancel();
    }
}
