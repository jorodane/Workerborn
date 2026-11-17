using System;
using System.Collections.Generic;

namespace Workerborn
{
    [Serializable]
    public sealed class InventoryEntrySave
    {
        public int id;
        public string itemId;
        public int quantity;
        public int x;
        public int y;
    }

    public sealed class InventoryEntry
    {
        public int Id { get; internal set; }
        public string ItemId { get; internal set; }
        public int Quantity { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public ItemSpec Spec => ItemCatalog.Get(ItemId);
        internal InventoryEntry Copy() => new InventoryEntry { Id = Id, ItemId = ItemId, Quantity = Quantity, X = X, Y = Y };
    }

    // Adds are transactional: a full bag never silently destroys part of a pickup.
    public sealed class GridInventory
    {
        private List<InventoryEntry> entries = new List<InventoryEntry>();
        private int nextId = 1;
        public int Columns { get; }
        public int Rows { get; private set; }
        public int Version { get; private set; }
        public IReadOnlyList<InventoryEntry> Entries => entries;

        public GridInventory(int columns = 8, int rows = 4)
        {
            if (columns < 1 || columns > 16 || rows < 1 || rows > 16) throw new ArgumentOutOfRangeException();
            Columns = columns; Rows = rows;
        }

        public int Count(string itemId)
        {
            int count = 0;
            foreach (var entry in entries) if (entry.ItemId == itemId) count += entry.Quantity;
            return count;
        }

        public InventoryEntry Find(int id) => entries.Find(entry => entry.Id == id);

        public bool CanAdd(string itemId, int quantity) => BuildAdded(itemId, quantity, out _, out _);

        public bool TryAdd(string itemId, int quantity)
        {
            if (!BuildAdded(itemId, quantity, out var added, out int next)) return false;
            entries = added;
            nextId = next;
            Version++;
            return true;
        }

        private bool BuildAdded(string itemId, int quantity, out List<InventoryEntry> result, out int next)
        {
            result = null;
            next = nextId;
            if (!ItemCatalog.Contains(itemId) || quantity <= 0 || quantity > 10000) return false;
            var spec = ItemCatalog.Get(itemId);
            var copy = CopyEntries();
            int remaining = quantity;
            foreach (var entry in copy)
            {
                if (entry.ItemId != itemId) continue;
                int amount = Math.Min(remaining, spec.MaxStack - entry.Quantity);
                entry.Quantity += amount;
                remaining -= amount;
            }
            while (remaining > 0)
            {
                if (!FirstFit(copy, spec, out int x, out int y)) return false;
                int amount = Math.Min(remaining, spec.MaxStack);
                copy.Add(new InventoryEntry { Id = next++, ItemId = itemId, Quantity = amount, X = x, Y = y });
                remaining -= amount;
            }
            result = copy;
            return true;
        }

        public bool TryRemove(string itemId, int quantity)
        {
            if (quantity <= 0 || Count(itemId) < quantity) return false;
            int remaining = quantity;
            for (int i = entries.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = entries[i];
                if (entry.ItemId != itemId) continue;
                int amount = Math.Min(remaining, entry.Quantity);
                entry.Quantity -= amount;
                remaining -= amount;
                if (entry.Quantity == 0) entries.RemoveAt(i);
            }
            Version++;
            return true;
        }

        public bool TryMove(int id, int x, int y)
        {
            var entry = Find(id);
            if (entry == null) return false;
            var target = entries.Find(other => other.Id != id && other.X == x && other.Y == y);
            if (target != null && target.ItemId == entry.ItemId && entry.Spec.MaxStack > 1)
            {
                int transfer = Math.Min(entry.Quantity, target.Spec.MaxStack - target.Quantity);
                if (transfer == 0) return false;
                target.Quantity += transfer;
                entry.Quantity -= transfer;
                if (entry.Quantity == 0) entries.Remove(entry);
            }
            else
            {
                if (!Fits(entries, entry.Spec, x, y, id)) return false;
                entry.X = x; entry.Y = y;
            }
            Version++;
            return true;
        }

        public bool TrySplit(int id)
        {
            var source = Find(id);
            if (source == null || source.Quantity < 2 || !FirstFit(entries, source.Spec, out int x, out int y)) return false;
            int quantity = source.Quantity / 2;
            source.Quantity -= quantity;
            entries.Add(new InventoryEntry { Id = nextId++, ItemId = source.ItemId, Quantity = quantity, X = x, Y = y });
            Version++;
            return true;
        }

        public void ExpandRows(int rows)
        {
            if (rows < Rows || rows > 16) throw new ArgumentOutOfRangeException(nameof(rows));
            Rows = rows;
            Version++;
        }

        private bool FirstFit(List<InventoryEntry> items, ItemSpec spec, out int x, out int y)
        {
            for (y = 0; y <= Rows - spec.Height; y++)
                for (x = 0; x <= Columns - spec.Width; x++)
                    if (Fits(items, spec, x, y)) return true;
            x = y = -1;
            return false;
        }

        private bool Fits(List<InventoryEntry> items, ItemSpec spec, int x, int y, int ignore = -1)
        {
            if (x < 0 || y < 0 || x + spec.Width > Columns || y + spec.Height > Rows) return false;
            foreach (var item in items)
            {
                if (item.Id == ignore) continue;
                if (x < item.X + item.Spec.Width && x + spec.Width > item.X
                    && y < item.Y + item.Spec.Height && y + spec.Height > item.Y) return false;
            }
            return true;
        }

        private List<InventoryEntry> CopyEntries()
        {
            var copy = new List<InventoryEntry>();
            foreach (var entry in entries) copy.Add(entry.Copy());
            return copy;
        }

        public InventoryEntrySave[] Export()
        {
            var result = new InventoryEntrySave[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                result[i] = new InventoryEntrySave { id = entry.Id, itemId = entry.ItemId, quantity = entry.Quantity, x = entry.X, y = entry.Y };
            }
            return result;
        }

        public static GridInventory Restore(int columns, int rows, InventoryEntrySave[] saved)
        {
            var result = new GridInventory(columns, rows);
            if (saved == null) return result;
            if (saved.Length > columns * rows) throw new ArgumentException("Too many inventory entries.");
            var ids = new HashSet<int>();
            foreach (var item in saved)
            {
                if (item == null || item.id < 1 || item.id > 1000000 || !ids.Add(item.id) || !ItemCatalog.Contains(item.itemId))
                    throw new ArgumentException("Invalid inventory item.");
                var spec = ItemCatalog.Get(item.itemId);
                if (item.quantity < 1 || item.quantity > spec.MaxStack || !result.Fits(result.entries, spec, item.x, item.y))
                    throw new ArgumentException("Invalid inventory placement or quantity.");
                result.entries.Add(new InventoryEntry { Id = item.id, ItemId = item.itemId, Quantity = item.quantity, X = item.x, Y = item.y });
                result.nextId = Math.Max(result.nextId, item.id + 1);
            }
            return result;
        }
    }
}
