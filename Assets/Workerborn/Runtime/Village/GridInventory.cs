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

        public GridInventory Copy()
        {
            return new GridInventory(Columns, Rows) { entries = CopyEntries(), nextId = nextId, Version = Version };
        }

        public void ReplaceWith(GridInventory other)
        {
            if (other == null || other.Columns != Columns) throw new ArgumentException(nameof(other));
            entries = other.CopyEntries(); Rows = other.Rows; nextId = other.nextId; Version++;
        }

        public bool TryExchange(string removedItem, int removedCount, string addedItem, int addedCount)
        {
            var copy = Copy();
            if (removedCount < 0 || addedCount < 0 || removedCount > 0 && !copy.TryRemove(removedItem, removedCount)
                || addedCount > 0 && !copy.TryAdd(addedItem, addedCount)) return false;
            ReplaceWith(copy);
            return true;
        }

        public bool TryExchangeAt(int entryId, int removedCount, string addedItem, int addedCount)
        {
            var copy = Copy();
            if (!copy.TryRemoveAt(entryId, removedCount) || addedCount < 0 ||
                addedCount > 0 && !copy.TryAdd(addedItem, addedCount))
                return false;
            ReplaceWith(copy);
            return true;
        }

        public bool TryRemoveAt(int id, int quantity)
        {
            var entry = Find(id);
            if (entry == null || quantity < 1 || entry.Quantity < quantity) return false;
            entry.Quantity -= quantity;
            if (entry.Quantity == 0) entries.Remove(entry);
            Version++;
            return true;
        }

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
            if (!ItemCatalog.Contains(itemId) || quantity <= 0) return false;
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

        public int MaximumSplitQuantity(int id)
        {
            var source = Find(id);
            return source != null && source.Quantity > 1 && FirstFit(entries, source.Spec, out _, out _)
                ? source.Quantity - 1 : 0;
        }

        public bool TrySplit(int id) => TrySplit(id, (Find(id)?.Quantity ?? 0) / 2);

        public bool TrySplit(int id, int quantity)
        {
            var source = Find(id);
            if (source == null || quantity < 1 || quantity >= source.Quantity || !FirstFit(entries, source.Spec, out int x, out int y))
                return false;
            source.Quantity -= quantity;
            entries.Add(new InventoryEntry { Id = nextId++, ItemId = source.ItemId, Quantity = quantity, X = x, Y = y });
            Version++;
            return true;
        }

        public bool TryPlaceHeld(int id, int x, int y, out int nextHeld)
        {
            nextHeld = id;
            var entry = Find(id);
            if (entry == null) { nextHeld = 0; return false; }
            if (!CanPlace(entry, x, y, out var target)) return false;
            if (target != null && target.ItemId != entry.ItemId)
            {
                int oldX = entry.X, oldY = entry.Y;
                entry.X = x; entry.Y = y; target.X = oldX; target.Y = oldY;
                nextHeld = target.Id; Version++;
                return true;
            }
            if (!TryMove(id, x, y)) return false;
            nextHeld = target != null && Find(id) != null ? id : 0;
            return true;
        }

        public bool CanPlaceHeld(int id, int x, int y)
        {
            var entry = Find(id);
            return entry != null && CanPlace(entry, x, y, out _);
        }

        private bool CanPlace(InventoryEntry entry, int x, int y, out InventoryEntry target)
        {
            target = null;
            foreach (var other in entries)
                if (other.Id != entry.Id && x >= other.X && x < other.X + other.Spec.Width
                    && y >= other.Y && y < other.Y + other.Spec.Height)
                {
                    target = other;
                    break;
                }
            if (target == null)
                return Fits(entries, entry.Spec, x, y, entry.Id);
            if (target.ItemId == entry.ItemId)
                return target.X == x && target.Y == y && target.Quantity < target.Spec.MaxStack;

            return Fits(entries, entry.Spec, x, y, entry.Id, target.Id)
                && Fits(entries, target.Spec, entry.X, entry.Y, entry.Id, target.Id)
                && (x >= entry.X + target.Spec.Width || x + entry.Spec.Width <= entry.X
                    || y >= entry.Y + target.Spec.Height || y + entry.Spec.Height <= entry.Y);
        }

        public void ExpandRows(int rows)
        {
            if (rows < Rows || rows > 16) throw new ArgumentOutOfRangeException(nameof(rows));
            Rows = rows;
            Version++;
        }

        public bool TryShrinkRows(int rows)
        {
            if (rows < 1 || rows > Rows)
                throw new ArgumentOutOfRangeException(nameof(rows));
            var smaller = new GridInventory(Columns, rows) { nextId = nextId };
            var overflow = new List<InventoryEntry>();
            foreach (var entry in entries)
            {
                if (entry.Y + entry.Spec.Height <= rows)
                    smaller.entries.Add(entry.Copy());
                else
                    overflow.Add(entry.Copy());
            }
            foreach (var entry in overflow)
            {
                if (!smaller.FirstFit(smaller.entries, entry.Spec, out int x, out int y))
                    return false;
                entry.X = x;
                entry.Y = y;
                smaller.entries.Add(entry);
            }
            ReplaceWith(smaller);
            return true;
        }

        private bool FirstFit(List<InventoryEntry> items, ItemSpec spec, out int x, out int y)
        {
            for (y = 0; y <= Rows - spec.Height; y++)
                for (x = 0; x <= Columns - spec.Width; x++)
                    if (Fits(items, spec, x, y)) return true;
            x = y = -1;
            return false;
        }

        private bool Fits(List<InventoryEntry> items, ItemSpec spec, int x, int y, int ignore = -1, int secondIgnore = -1)
        {
            if (x < 0 || y < 0 || x + spec.Width > Columns || y + spec.Height > Rows) return false;
            foreach (var item in items)
            {
                if (item.Id == ignore || item.Id == secondIgnore) continue;
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
