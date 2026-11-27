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
        private NavMeshPath dialoguePath;
        private bool followingDialogue;
        private float dialogueSide;
        private float nextDialoguePath;

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
                dialogueSide = 0f;
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
            if (dialogueSide == 0f)
                dialogueSide = Vector3.Dot(StandingPosition - partner.StandingPosition, right) < 0f ? -1f : 1f;

            nextDialoguePath = Time.time + DialoguePathInterval;
            Vector3 lead = partner.StandingPosition + heading * .6f;
            if (TryDialoguePath(lead + right * (CompanionSpacing * dialogueSide)))
                return;
            if (TryDialoguePath(lead - right * (CompanionSpacing * dialogueSide)))
                dialogueSide = -dialogueSide;
            else
                TryDialoguePath(partner.StandingPosition - heading * CompanionSpacing);
        }

        private bool TryDialoguePath(Vector3 desired)
        {
            if (!NavMesh.SamplePosition(desired, out var hit, .55f, agent.areaMask) || !SharesMap(hit.position) ||
                dialoguePartner.agent.Raycast(hit.position, out _))
                return false;
            if (InReach(hit.position, .35f) || followingDialogue && agent.hasPath &&
                (agent.destination - hit.position).sqrMagnitude < .35f * .35f)
                return true;

            dialoguePath ??= new NavMeshPath();
            if (!agent.CalculatePath(hit.position, dialoguePath) || dialoguePath.status != NavMeshPathStatus.PathComplete)
                return false;
            if (Action == WorkerAction.Sitting)
            {
                Cancel();
                return true;
            }
            if (!agent.SetPath(dialoguePath))
                return false;
            followingDialogue = true;
            walkPath = dialoguePartner.IsWalking;
            agent.isStopped = false;
            Action = WorkerAction.Moving;
            return true;
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
