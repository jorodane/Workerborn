namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageQuestPage? trackedQuest;
        internal WorldInteractable TrackedTarget { get; private set; }

        internal bool IsTrackingQuest(VillageQuestPage page) => trackedQuest == page;

        internal void ToggleQuestTracking(VillageQuestPage page)
        {
            if (trackedQuest == page)
                ClearQuestTracking();
            else if (game.CanTrackQuest(page))
            {
                Jobs.ClearPin();
                trackedQuest = page;
                RefreshQuestTracking();
            }
        }

        internal void ClearQuestTracking()
        {
            if (!trackedQuest.HasValue)
                return;
            trackedQuest = null;
            RefreshQuestTracking();
        }

        private void RefreshQuestTracking()
        {
            if (views.TryGetValue("questdetail", out var view) && view is QuestDetailWindowView detail)
                detail.RefreshTracking();
        }

        private void UpdateTracking()
        {
            TrackedTarget = null;
            if (!game.IsWorldTransitioning && !game.InRebirthZone)
            {
                WorldInteractable target = trackedQuest.HasValue ? game.QuestTrackingTarget(trackedQuest.Value) : Jobs.PinnedProvider;
                if (target != null && target.isActiveAndEnabled && target.CanInteract &&
                    WorkerbornInput.IsFinite(target.transform.position))
                    TrackedTarget = game.SharesMap(target.transform.position) ? target : game.CurrentInterior?.Exit;
            }
            bool aiming = game.Worker.AimingTimberThrow && !game.ControlsBlocked && skillShortcutGhost == null &&
                game.Targeting != null && game.IsTimberGround(TimberLocation.Planar(game.Worker.StandingPosition));
            trackingGuide?.Display(game, aiming ? null : TrackedTarget);
            trackingGuide?.DisplayThrow(game, aiming, game.Targeting != null ? game.Targeting.ThrowAim : TimberThrowAim.None);
        }
    }
}
