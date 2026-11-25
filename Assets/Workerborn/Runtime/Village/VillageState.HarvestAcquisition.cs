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
    }

    public sealed partial class VillageState
    {
        private const int HarvestAcquisitionSaveSchema = 10;
        private readonly List<HarvestAcquisitionSave> pendingHarvests = new List<HarvestAcquisitionSave>();
        private long nextHarvestReadyAt = long.MaxValue;
        private int lastAcquisitionInventoryVersion = -1;

        public int HarvestAcquisitionDelayMilliseconds => RankOf(SkillId.HarvestAcquisition) == SkillProgression.MaxRank
            ? 0
            : Math.Max(1, SkillBonus(SkillId.HarvestAcquisition, SkillEffect.HarvestAcquisitionDelayMilliseconds));

        internal bool TryAcquireHarvest(string itemId, int quantity, long now)
        {
            if (AwaitingIncarnation || !IsSkillLearned(SkillId.HarvestAcquisition))
                return false;
            int delay = HarvestAcquisitionDelayMilliseconds;
            if (delay == 0)
                return Inventory.TryAdd(itemId, quantity);

            // Check the new harvest and all undelivered harvests against the same bag space.
            var preview = pendingHarvests.Count == 0 ? Inventory : Inventory.Copy();
            foreach (var pending in pendingHarvests)
                if (!preview.TryAdd(pending.itemId, pending.quantity))
                    return false;
            if (!preview.CanAdd(itemId, quantity))
                return false;
            QueueHarvestAcquisition(new HarvestAcquisitionSave { itemId = itemId, quantity = quantity, readyAtUtc = now + delay });
            return true;
        }

        internal void CollectReadyHarvests(long now, Action<string, int> onCollect = null)
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
                onCollect?.Invoke(pending.itemId, pending.quantity);
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
