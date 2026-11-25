using System;
using System.Collections.Generic;

namespace Workerborn
{
    [Serializable]
    public struct HarvestAcquisitionSave
    {
        public string itemId;
        public int quantity;
        public long readyAtUtc;
        public bool hasSource;
        public float sourceX, sourceY, sourceZ;
    }

    public sealed partial class VillageState
    {
        private const int HarvestAcquisitionSaveSchema = 10;
        // Receives saved harvests from before automatic pickup used ground drops.
        private readonly List<HarvestAcquisitionSave> pendingHarvests = new List<HarvestAcquisitionSave>();
        private long nextHarvestReadyAt = long.MaxValue;
        private int lastAcquisitionInventoryVersion = -1;

        public bool HasInstantHarvestAcquisition => IsSkillLearned(SkillId.HarvestAcquisition) &&
            RankOf(SkillId.HarvestAcquisition) == SkillProgression.MaxRank;

        public int HarvestItemsPerSecond => IsSkillLearned(SkillId.HarvestAcquisition)
            ? Math.Max(1, SkillBonus(SkillId.HarvestAcquisition, SkillEffect.HarvestItemsPerSecond))
            : 0;

        internal bool TryAcquireHarvestImmediately(string itemId, int quantity) =>
            !AwaitingIncarnation && HasInstantHarvestAcquisition && Inventory.TryAdd(itemId, quantity);

        internal void CollectReadyHarvests(long now, Action<HarvestAcquisitionSave> onCollect = null)
        {
            if (pendingHarvests.Count == 0 || now < nextHarvestReadyAt && Inventory.Version == lastAcquisitionInventoryVersion)
                return;
            nextHarvestReadyAt = long.MaxValue;
            for (int i = 0; i < pendingHarvests.Count; i++)
            {
                var pending = pendingHarvests[i];
                if (now < pending.readyAtUtc)
                {
                    nextHarvestReadyAt = Math.Min(nextHarvestReadyAt, pending.readyAtUtc);
                    continue;
                }
                if (!Inventory.TryAdd(pending.itemId, pending.quantity))
                    continue;
                pendingHarvests.RemoveAt(i--);
                onCollect?.Invoke(pending);
            }
            // An expired harvest that did not fit is retried whenever bag contents change.
            lastAcquisitionInventoryVersion = Inventory.Version;
        }

        private void QueueHarvestAcquisition(HarvestAcquisitionSave harvest)
        {
            pendingHarvests.Add(harvest);
            nextHarvestReadyAt = Math.Min(nextHarvestReadyAt, harvest.readyAtUtc);
        }

        private void RestoreHarvestAcquisitions(CharacterSave save)
        {
            if (save.schema < HarvestAcquisitionSaveSchema || save.pendingHarvests == null)
                return;
            foreach (var harvest in save.pendingHarvests)
            {
                if (!IsSkillLearned(SkillId.HarvestAcquisition) || !ItemCatalog.Contains(harvest.itemId) ||
                    harvest.quantity < 1 || harvest.readyAtUtc <= 0)
                    throw new ArgumentException("Invalid pending harvest acquisition.");
                QueueHarvestAcquisition(harvest);
            }
        }
    }
}
