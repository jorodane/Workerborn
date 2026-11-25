using System;
using System.Collections.Generic;

namespace Workerborn
{
    [Serializable]
    internal sealed class HarvestPickupResult
    {
        public string id;
        public string itemId;
        public int quantity;
        public int remaining;
    }

    internal sealed class GroundDropPiles
    {
        private const float MergeDistance = GroundDropSave.PickupDistance;
        private readonly Dictionary<string, GroundDropSave> drops = new Dictionary<string, GroundDropSave>();
        private readonly SortedSet<(long Until, string Id)> protectedDrops = new SortedSet<(long, string)>();
        private readonly Dictionary<(string Item, string Room, string RoomOwner, int X, int Z), List<GroundDropSave>> cells =
            new Dictionary<(string, string, string, int, int), List<GroundDropSave>>();

        internal IEnumerable<GroundDropSave> Active => drops.Values;
        internal bool TryGet(string id, out GroundDropSave drop) => drops.TryGetValue(id, out drop);

        internal bool Add(GroundDropSave drop)
        {
            if (!drops.TryAdd(drop.id, drop))
                return false;
            if (ItemCatalog.Contains(drop.itemId))
                drop.condition = drop.condition.ForItem(ItemCatalog.Get(drop.itemId));
            protectedDrops.Add((drop.reservedUntilUtc, drop.id));
            return true;
        }

        internal void Remove(string id)
        {
            if (!drops.Remove(id, out var drop) || protectedDrops.Remove((drop.reservedUntilUtc, id)))
                return;
            var cell = Cell(drop);
            if (cells.TryGetValue(cell, out var pile))
            {
                pile.Remove(drop);
                if (pile.Count == 0)
                    cells.Remove(cell);
            }
        }

        internal HarvestPickupResult[] CollectHarvests(VillageState state, string playerId, long now, IReadOnlyList<string> ids,
            Func<GroundDropSave, bool> isInReach = null)
        {
            int remainingBudget = state.HasInstantHarvestAcquisition ? int.MaxValue : state.HarvestItemsPerSecond;
            if (state.AwaitingIncarnation || remainingBudget <= 0 || ids.Count == 0)
                return Array.Empty<HarvestPickupResult>();
            var collected = new List<HarvestPickupResult>();
            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id) || !TryGet(id, out var drop) || !drop.CanAutoCollect(playerId, now) ||
                    isInReach != null && !isInReach(drop))
                    continue;
                int taken = state.Inventory.TryAddUpTo(drop.itemId, Math.Min(drop.quantity, remainingBudget), drop.condition);
                if (taken <= 0)
                    continue;
                drop.quantity -= taken;
                if (drop.quantity == 0)
                    Remove(id);
                remainingBudget -= taken;
                collected.Add(new HarvestPickupResult { id = id, itemId = drop.itemId, quantity = taken, remaining = drop.quantity });
                if (remainingBudget == 0)
                    break;
            }
            return collected.ToArray();
        }

        internal void ReleaseExpired(long now, Action<string> changed = null)
        {
            while (protectedDrops.Count > 0 && protectedDrops.Min.Until <= now)
            {
                var reservation = protectedDrops.Min;
                protectedDrops.Remove(reservation);
                var drop = drops[reservation.Id];
                var cell = Cell(drop);
                var pile = FindPile(drop, cell);
                if (pile != null)
                {
                    pile.quantity += drop.quantity;
                    pile.combined = true;
                    drops.Remove(drop.id);
                    changed?.Invoke(pile.id);
                    changed?.Invoke(drop.id);
                    continue;
                }
                if (!cells.TryGetValue(cell, out var list))
                {
                    list = new List<GroundDropSave>();
                    cells.Add(cell, list);
                }
                list.Add(drop);
            }
        }

        private GroundDropSave FindPile(GroundDropSave drop, (string Item, string Room, string RoomOwner, int X, int Z) cell)
        {
            float bestDistance = MergeDistance * MergeDistance;
            GroundDropSave nearest = null;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue((cell.Item, cell.Room, cell.RoomOwner, cell.X + x, cell.Z + z), out var pile))
                        continue;
                    foreach (var candidate in pile)
                    {
                        float dx = drop.MapX - candidate.MapX;
                        float dz = drop.MapZ - candidate.MapZ;
                        float distance = dx * dx + dz * dz;
                        if (distance <= bestDistance && candidate.harvestYield == drop.harvestYield &&
                            candidate.condition.Equals(drop.condition) && candidate.quantity <= int.MaxValue - drop.quantity)
                        {
                            bestDistance = distance;
                            nearest = candidate;
                        }
                    }
                }
            return nearest;
        }

        private static (string, string, string, int, int) Cell(GroundDropSave drop) =>
            (drop.itemId, drop.RoomId, drop.RoomId?.EndsWith("/guest", StringComparison.Ordinal) == true ? drop.ownerId : null,
             (int)Math.Floor(drop.MapX / MergeDistance), (int)Math.Floor(drop.MapZ / MergeDistance));
    }
}
