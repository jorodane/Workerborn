using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        internal Rect TrackingGroundBounds
        {
            get
            {
                if (CurrentInterior != null)
                    return CurrentInterior.MapBounds;
                var terrain = GroundTerrain;
                if (terrain != null && terrain.terrainData != null)
                {
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    return new Rect(origin.x, origin.z, size.x, size.z);
                }
                var floor = navigation != null ? navigation.GroundBox : null;
                if (floor == null)
                    return default;
                Bounds bounds = floor.bounds;
                return new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);
            }
        }

        internal bool CanTrackQuest(VillageQuestPage page)
        {
            var s = State;
            if (s.AwaitingIncarnation)
                return false;
            return page switch
            {
                VillageQuestPage.FirstWork => s.FarmAvailable && s.IntroAccepted && !s.IntroClaimed,
                VillageQuestPage.Expansion => s.ExpansionQuestActive,
                VillageQuestPage.FarmFoundation => !s.FarmAvailable,
                VillageQuestPage.TimberContract => s.TimberContractActive,
                VillageQuestPage.FarmJob => s.FarmAvailable && s.Job == JobStatus.Active,
                VillageQuestPage.LegendFounding => s.Rules.LegendFoundingTimber > 0 && !s.HasFoundedLegend(LegendPlayerId),
                VillageQuestPage.LegendTrial or VillageQuestPage.LegendGuidance => s.Legend.founders.Length > 0 && !s.HasProvenLegend(LegendPlayerId),
                _ => false
            };
        }

        internal WorldInteractable QuestTrackingTarget(VillageQuestPage page)
        {
            if (!CanTrackQuest(page))
                return null;
            var s = State;
            var farmer = FindInteraction(VillageRole.Farmer);
            switch (page)
            {
                case VillageQuestPage.FarmFoundation when s.FoundationStage == FarmFoundationStage.SelectingSite:
                    return NearestTrackingTarget(farmSites);
                case VillageQuestPage.FarmFoundation when s.FoundationStage == FarmFoundationStage.ReadyToRebirth:
                    return s.CanAdvanceEra ? FindInteraction(VillageRole.Shrine) : null;
                case VillageQuestPage.FarmFoundation:
                case VillageQuestPage.TimberContract:
                    var contract = s.Rules.Foundation;
                    return s.CanConfirmTimberDelivery || s.Inventory.Count(contract.DocumentItemId) == 0 || s.Inventory.Count(contract.TimberItemId) > 0
                        ? farmer : HarvestTrackingTarget(contract.TimberItemId);
                case VillageQuestPage.FirstWork:
                    if (s.CanClaimIntroQuest)
                        return farmer;
                    if (!s.JobWindowOpen)
                        return null;
                    return s.Job == JobStatus.Active ? FarmJobTrackingTarget(farmer) : s.LastJobDay != s.Day ? farmer : null;
                case VillageQuestPage.FarmJob:
                    return s.JobWindowOpen ? FarmJobTrackingTarget(farmer) : null;
                case VillageQuestPage.Expansion:
                    int goldNeeded = Mathf.Min(s.Rules.DonationGoldStep, s.ExpansionGoldRequired - s.DonatedGold);
                    int itemsNeeded = Mathf.Min(s.Rules.DonationItemStep, s.ExpansionItemsRequired - s.DonatedPotatoes);
                    if (s.CanConfirmExpansion || goldNeeded > 0 && s.Gold >= goldNeeded ||
                        itemsNeeded > 0 && s.Inventory.Count(s.Rules.DonationItemId) >= itemsNeeded)
                        return farmer;
                    return itemsNeeded > 0 ? HarvestTrackingTarget(s.Rules.DonationItemId) : null;
                case VillageQuestPage.LegendFounding:
                    return s.CanRecordLegend(LegendPlayerId) ? FindInteraction(VillageRole.Shrine) : null;
                case VillageQuestPage.LegendTrial:
                case VillageQuestPage.LegendGuidance:
                    return s.CanProveLegend(LegendPlayerId) ? FindInteraction(VillageRole.Shrine) : null;
                default:
                    return null;
            }
        }

        private WorldInteractable FarmJobTrackingTarget(VillageInteractable farmer) =>
            State.Inventory.Count(State.Rules.JobItemId) >= State.JobRequirement ? farmer : HarvestTrackingTarget(State.Rules.JobItemId);

        private WorldInteractable HarvestTrackingTarget(string item)
        {
            WorldInteractable closest = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 origin = CurrentInterior != null ? ExteriorPosition(CurrentInterior) : worker.StandingPosition;
            foreach (var crop in allCrops)
            {
                if (crop == null || !crop.isActiveAndEnabled || !crop.CanInteract || crop.Definition == null ||
                    crop.Definition.ItemId != item || !CanHarvest(crop, showNotice: false))
                    continue;
                PreferCloser(crop, origin, ref closest, ref bestDistance);
            }
            return closest;
        }

        private WorldInteractable NearestTrackingTarget(VillageFarmSite[] sites)
        {
            WorldInteractable closest = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 origin = CurrentInterior != null ? ExteriorPosition(CurrentInterior) : worker.StandingPosition;
            foreach (var site in sites)
                PreferCloser(site, origin, ref closest, ref bestDistance);
            return closest;
        }

        private static void PreferCloser(WorldInteractable target, Vector3 origin, ref WorldInteractable closest, ref float best)
        {
            if (target == null || !target.isActiveAndEnabled || !target.CanInteract)
                return;
            Vector3 delta = target.transform.position - origin;
            float distance = delta.x * delta.x + delta.z * delta.z;
            if (distance < best)
            {
                closest = target;
                best = distance;
            }
        }
    }
}
