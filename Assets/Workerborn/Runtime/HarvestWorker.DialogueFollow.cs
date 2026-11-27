using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class HarvestWorker
    {
        private const float ConversationRange = 8f;
        private const float CompanionSpacing = 1.3f;
        private const float DialoguePathInterval = .2f;
        private HarvestWorker dialoguePartner;
        private NavMeshPath leftDialoguePath, rightDialoguePath;
        private bool followingDialogue;
        private float nextDialoguePath;

        private bool CanWalkAwayFromTrade => IsLocallyControlled && village?.Hud is
            { IsShopOpen: true, AllowsGroundItems: true, HasQuantityPrompt: false, IsHoldingItem: false };
        internal bool AcceptsDirectMovement => AcceptsMovementCommands || CanWalkAwayFromTrade;

        private void LeaveDialogueForMovement()
        {
            if (IsLocallyControlled && village?.Hud.IsDialogueOpen == true)
                village.Hud.CloseDialogue(animate: true);
        }

        private void UpdateDialogueFollow()
        {
            if (village == null || village.IsWorldTransitioning || village.AwaitingWorldCommand)
            {
                StopDialogueFollow();
                return;
            }

            var hud = village.Hud;
            var target = hud.ConversationTarget;
            if (target == null || !target.isActiveAndEnabled || !SharesMap(target.transform.position) ||
                hud.DialogueStartedInReach && !InReach(target.transform.position, ConversationRange))
            {
                StopDialogueFollow();
                dialoguePartner = null;
                if (!ReferenceEquals(target, null))
                    hud.CloseDialogue(animate: true);
                return;
            }

            if (!hud.DialogueStartedInReach || !target.TryGetComponent<HarvestWorker>(out var partner) || partner == this)
            {
                StopDialogueFollow();
                dialoguePartner = null;
                return;
            }
            if (dialoguePartner != partner)
            {
                StopDialogueFollow();
                dialoguePartner = partner;
                nextDialoguePath = 0f;
            }
            if (partner.Action != WorkerAction.Moving || partner.agent == null || !partner.agent.isOnNavMesh ||
                Time.time < nextDialoguePath || IsEating || IsStandingUp || CarriedLogs > 0 || restingVisual != null ||
                Action != WorkerAction.Idle && Action != WorkerAction.Sitting && !followingDialogue)
                return;

            Vector3 heading = Vector3.ProjectOnPlane(partner.agent.velocity, Vector3.up);
            if (heading.sqrMagnitude < .04f)
                heading = Vector3.ProjectOnPlane(partner.agent.steeringTarget - partner.StandingPosition, Vector3.up);
            if (heading.sqrMagnitude < .04f)
                return;
            heading.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, heading);

            nextDialoguePath = Time.time + DialoguePathInterval;
            Vector3 lead = partner.StandingPosition + heading * .6f;
            leftDialoguePath ??= new NavMeshPath();
            rightDialoguePath ??= new NavMeshPath();
            float leftDistance = MeasureDialoguePath(lead - right * CompanionSpacing, leftDialoguePath, out var left);
            float rightDistance = MeasureDialoguePath(lead + right * CompanionSpacing, rightDialoguePath, out var rightPoint);
            if (rightDistance < leftDistance)
                SetDialoguePath(rightPoint, rightDialoguePath);
            else if (!float.IsPositiveInfinity(leftDistance))
                SetDialoguePath(left, leftDialoguePath);
            else
            {
                float behind = MeasureDialoguePath(partner.StandingPosition - heading * CompanionSpacing, leftDialoguePath, out var point);
                if (!float.IsPositiveInfinity(behind))
                    SetDialoguePath(point, leftDialoguePath);
                else
                    StopDialogueFollow();
            }
        }

        private float MeasureDialoguePath(Vector3 desired, NavMeshPath path, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(desired, out var hit, .55f, agent.areaMask) || !SharesMap(hit.position) ||
                dialoguePartner.agent.Raycast(hit.position, out _) ||
                !agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return float.PositiveInfinity;

            point = hit.position;
            Vector3 previous = agent.nextPosition;
            float distance = 0f;
            foreach (var corner in path.corners)
            {
                distance += Vector3.Distance(previous, corner);
                previous = corner;
            }
            // Keep the current side when both routes are almost equally short.
            if (followingDialogue && agent.hasPath && (agent.destination - point).sqrMagnitude < 1f)
                distance -= .3f;
            return distance;
        }

        private void SetDialoguePath(Vector3 point, NavMeshPath path)
        {
            if (InReach(point, .35f))
            {
                StopDialogueFollow();
                return;
            }
            if (followingDialogue && agent.hasPath && (agent.destination - point).sqrMagnitude < .35f * .35f)
                return;
            if (Action == WorkerAction.Sitting)
            {
                Cancel();
                return;
            }
            if (!agent.SetPath(path))
                return;
            followingDialogue = true;
            walkPath = dialoguePartner.IsWalking;
            agent.isStopped = false;
            Action = WorkerAction.Moving;
        }

        private float DialogueTravelSpeed() => dialoguePartner != null && dialoguePartner.agent != null
            ? Mathf.Min(TravelSpeed(false), Mathf.Max(TravelSpeed(IsWalking), dialoguePartner.agent.speed + .35f))
            : TravelSpeed(IsWalking);

        private void StopDialogueFollow()
        {
            if (followingDialogue)
                Cancel();
        }
    }
}
