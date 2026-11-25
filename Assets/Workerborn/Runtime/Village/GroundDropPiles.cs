using System;
using System.Collections.Generic;

namespace Workerborn
{
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
                        if (distance <= bestDistance && candidate.condition.Equals(drop.condition) && candidate.quantity <= int.MaxValue - drop.quantity)
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
