using UnityEngine;

namespace Workerborn
{
    public sealed partial class HarvestWorker
    {
        private const float HoverGazeDistance = 12f;
        private const float HoverGazeDelay = .12f;
        private VillageInteractable gazeOwner;
        private WorldTargetReference hoveredGaze;
        private float gazeReadyAt;

        private Vector3 EyePosition => animationDriver?.EyePosition ??
            (actor?.VisualRoot != null ? actor.VisualRoot.position : StandingPosition) +
            Vector3.up * ((actor?.Appearance?.Height ?? 1.7f) * .92f);

        private void UpdateGazeTarget()
        {
            if (animationDriver == null)
                return;
            if (village == null || village.IsWorldTransitioning || CharacterState is not { Health: > 0 } || IsEating ||
                restingVisual != null || jumpingVisual != null || IsStandingUp || CarriedLogs > 0)
            {
                hoveredGaze = default;
                animationDriver.SetGazeTarget(null);
                return;
            }
            bool working = Action is WorkerAction.Harvesting or WorkerAction.Attacking;
            Vector3? point = InteractionGazePoint() ?? PointerGazePoint();
            animationDriver.SetGazeTarget(point, working ? .55f : 1f);
        }

        private Vector3? InteractionGazePoint()
        {
            var hud = village.Hud;
            if (IsLocallyControlled && hud.ConversationTarget != null)
                return GazePoint(hud.ConversationTarget);

            var partner = hud.IsSpeakingWith(gazeOwner) ? village.Worker : gazeOwner?.Routine?.ConversationGazePartner;
            if (partner != null && partner.isActiveAndEnabled && SharesMap(partner.StandingPosition))
                return partner.EyePosition;

            if (FishingPoint is Vector3 cast && cropTarget is { CanInteract: true } && SharesMap(cast))
                return cast;
            return GazePoint(strikeTarget) ?? GazePoint(cropTarget) ?? GazePoint(interactionTarget) ??
                GazePoint(MountedWorkTarget) ?? GazePoint(pickupTarget.Target);
        }

        private Vector3? PointerGazePoint()
        {
            var subject = IsLocallyControlled && !village.ControlsBlocked && village.CameraRig?.IsOrbiting != true
                ? village.Hud.ContextGazeTarget ?? Targeting?.CurrentTarget : null;
            if (subject == null)
            {
                hoveredGaze = default;
                return null;
            }
            if (hoveredGaze.Target != subject)
            {
                hoveredGaze = new WorldTargetReference(subject);
                gazeReadyAt = Time.unscaledTime + HoverGazeDelay;
            }
            if (Time.unscaledTime < gazeReadyAt ||
                (subject.transform.position - StandingPosition).sqrMagnitude > HoverGazeDistance * HoverGazeDistance)
                return null;
            return GazePoint(subject);
        }

        private Vector3? GazePoint(WorldInteractable subject)
        {
            if (subject == null || !subject.CanInteract || !SharesMap(subject.transform.position))
                return null;
            if (subject.TryGetComponent<HarvestWorker>(out var person))
                return person == this ? (Vector3?)null : person.EyePosition;
            if (subject is GroundPickup item)
                return item.VisualPosition + Vector3.up * .04f;
            if (subject is HarvestableCrop && subject.TryGetComponent<Collider>(out var volume) && volume.enabled)
                return volume.bounds.center;
            return subject.TargetCenter;
        }

        private void ClearGazeFocus()
        {
            hoveredGaze = default;
            animationDriver?.ClearGaze();
        }
    }
}
