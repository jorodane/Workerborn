using System;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class GridInventoryTests
    {
        [Test]
        public void AddingMergesStacksAndCreatesOnlyTheRequiredSlots()
        {
            var inventory = new GridInventory();
            Assert.That(inventory.TryAdd(ItemIds.Potato, 19), Is.True);
            Assert.That(inventory.TryAdd(ItemIds.Potato, 3), Is.True);
            Assert.That(inventory.Count(ItemIds.Potato), Is.EqualTo(22));
            Assert.That(inventory.Entries.Count, Is.EqualTo(2));
            Assert.That(inventory.Entries[0].Quantity, Is.EqualTo(20));
        }

        [Test]
        public void FailedAddDoesNotPartiallyFillAnExistingStack()
        {
            var inventory = new GridInventory(1, 1);
            inventory.TryAdd(ItemIds.Potato, 19);
            int version = inventory.Version;
            Assert.That(inventory.CanAdd(ItemIds.Potato, 2), Is.False);
            Assert.That(inventory.TryAdd(ItemIds.Potato, 2), Is.False);
            Assert.That(inventory.Count(ItemIds.Potato), Is.EqualTo(19));
            Assert.That(inventory.Version, Is.EqualTo(version));
        }

        [Test]
        public void MultiCellToolCannotOverlapItemsOrLeaveTheBag()
        {
            var inventory = new GridInventory(3, 3);
            inventory.TryAdd(ItemIds.Hoe, 1);
            inventory.TryAdd(ItemIds.Potato, 1);
            int hoe = inventory.Entries[0].Id;
            Assert.That(inventory.TryMove(hoe, 1, 0), Is.False);
            Assert.That(inventory.TryMove(hoe, 0, 1), Is.False);
            Assert.That(inventory.TryMove(hoe, -1, 0), Is.False);
            Assert.That(inventory.TryMove(hoe, 2, 0), Is.True);
            Assert.That(inventory.Find(hoe).X, Is.EqualTo(2));
        }

        [Test]
        public void DraggingOntoSameItemMergesWithoutLosingTheRemainder()
        {
            var inventory = new GridInventory(2, 1);
            inventory.TryAdd(ItemIds.Potato, 21);
            int first = inventory.Entries[0].Id;
            Assert.That(inventory.TryMove(first, 1, 0), Is.True);
            Assert.That(inventory.Count(ItemIds.Potato), Is.EqualTo(21));
            Assert.That(inventory.Find(first).Quantity, Is.EqualTo(1));
            Assert.That(inventory.Entries[1].Quantity, Is.EqualTo(20));
        }

        [Test]
        public void FailedRemovalIsTransactional()
        {
            var inventory = new GridInventory();
            inventory.TryAdd(ItemIds.Potato, 4);
            Assert.That(inventory.TryRemove(ItemIds.Potato, 6), Is.False);
            Assert.That(inventory.Count(ItemIds.Potato), Is.EqualTo(4));
            Assert.That(inventory.TryRemove(ItemIds.Potato, 4), Is.True);
            Assert.That(inventory.Entries.Count, Is.Zero);
        }

        [Test]
        public void SplitPreservesTheTotalAndUsesASeparateCell()
        {
            var inventory = new GridInventory(2, 1);
            inventory.TryAdd(ItemIds.Potato, 9);
            int id = inventory.Entries[0].Id;
            Assert.That(inventory.MaximumSplitQuantity(id), Is.EqualTo(8));
            Assert.That(inventory.TrySplit(id, 9), Is.False);
            Assert.That(inventory.TrySplit(id, 3), Is.True);
            Assert.That(inventory.Find(id).Quantity, Is.EqualTo(6));
            Assert.That(inventory.Entries[1].Quantity, Is.EqualTo(3));
            Assert.That(inventory.Count(ItemIds.Potato), Is.EqualTo(9));
            Assert.That(inventory.Entries.Count, Is.EqualTo(2));
            Assert.That(inventory.Entries[0].X, Is.Not.EqualTo(inventory.Entries[1].X));
        }

        [Test]
        public void FullBagCannotSplitAndPreservesTheOriginalStack()
        {
            var inventory = new GridInventory(1, 1);
            inventory.TryAdd(ItemIds.Potato, 9);
            Assert.That(inventory.TrySplit(inventory.Entries[0].Id), Is.False);
            Assert.That(inventory.Count(ItemIds.Potato), Is.EqualTo(9));
        }

        [Test]
        public void InventoryRoundTripPreservesFootprintsStacksAndStableIds()
        {
            var inventory = new GridInventory();
            inventory.TryAdd(ItemIds.Hoe, 1);
            inventory.TryAdd(ItemIds.Potato, 25);
            int id = inventory.Entries[0].Id;
            inventory.TryMove(id, 7, 1);
            var restored = GridInventory.Restore(8, 4, inventory.Export());
            Assert.That(restored.Count(ItemIds.Potato), Is.EqualTo(25));
            Assert.That(restored.Find(id).X, Is.EqualTo(7));
            Assert.That(restored.Find(id).Y, Is.EqualTo(1));
            Assert.That(restored.TryAdd(ItemIds.Bread, 1), Is.True);
            Assert.That(restored.Entries[restored.Entries.Count - 1].Id, Is.GreaterThan(inventory.Entries.Count));
        }

        [Test]
        public void CorruptInventoryWithOverlappingItemsIsRejected()
        {
            var saved = new[]
            {
                new InventoryEntrySave { id = 1, itemId = ItemIds.Hoe, quantity = 1, x = 0, y = 0 },
                new InventoryEntrySave { id = 2, itemId = ItemIds.Potato, quantity = 1, x = 0, y = 1 }
            };
            Assert.Throws<ArgumentException>(() => GridInventory.Restore(8, 4, saved));
        }

        [Test]
        public void InvalidItemAndQuantitiesCannotEnterTheBag()
        {
            var inventory = new GridInventory();
            Assert.That(inventory.TryAdd("unknown", 1), Is.False);
            Assert.That(inventory.TryAdd(ItemIds.Potato, 0), Is.False);
            Assert.That(inventory.TryAdd(ItemIds.Potato, -1), Is.False);
            Assert.That(inventory.TryRemove(ItemIds.Potato, -1), Is.False);
            Assert.That(inventory.Entries.Count, Is.Zero);
        }
    }
}
