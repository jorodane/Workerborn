using System;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class EditableVillageTests
    {
        [TearDown] public void ResetCatalog() => ItemCatalog.ResetDefaults();

        [TestCase(1f)] [TestCase(120f)]
        public void OneWheelNotchProducesTheSameVisibleZoom(float backendDelta)
        {
            Assert.That(OrbitZoom.Apply(10f, backendDelta, 2f, 22f, .18f), Is.EqualTo(8.352702f).Within(.001f));
        }

        [Test]
        public void WheelZoomClampsAtBothEnds()
        {
            Assert.That(OrbitZoom.Apply(2f, 120f, 2f, 22f, .18f), Is.EqualTo(2f));
            Assert.That(OrbitZoom.Apply(22f, -120f, 2f, 22f, .18f), Is.EqualTo(22f));
        }

        [Test]
        public void HeldItemCanSwapThenPlaceTheDisplacedItem()
        {
            var bag = new GridInventory(3, 1);
            bag.TryAdd(ItemIds.Potato, 3); bag.TryAdd(ItemIds.Bread, 2);
            int potato = bag.Entries[0].Id, bread = bag.Entries[1].Id;
            Assert.That(bag.TryPlaceHeld(potato, 1, 0, out int held), Is.True);
            Assert.That(held, Is.EqualTo(bread));
            Assert.That(bag.Find(potato).X, Is.EqualTo(1));
            Assert.That(bag.TryPlaceHeld(held, 2, 0, out held), Is.True);
            Assert.That(held, Is.Zero);
            Assert.That(bag.Count(ItemIds.Potato), Is.EqualTo(3));
            Assert.That(bag.Count(ItemIds.Bread), Is.EqualTo(2));
        }

        [Test]
        public void HeldStackKeepsItsRemainderAndInvalidPlacementChangesNothing()
        {
            var bag = new GridInventory(2, 1);
            bag.TryAdd(ItemIds.Potato, 21);
            int first = bag.Entries[0].Id;
            Assert.That(bag.TryPlaceHeld(first, 1, 0, out int held), Is.True);
            Assert.That(held, Is.EqualTo(first));
            Assert.That(bag.Find(first).Quantity, Is.EqualTo(1));
            int version = bag.Version;
            Assert.That(bag.TryPlaceHeld(held, -1, 0, out _), Is.False);
            Assert.That(bag.Version, Is.EqualTo(version));
            Assert.That(bag.Count(ItemIds.Potato), Is.EqualTo(21));
        }

        [Test]
        public void TradingCanUseTheSpaceVacatedByTheSpentCoins()
        {
            var bag = new GridInventory(1, 1);
            bag.TryAdd(ItemIds.Gold, 10);
            Assert.That(bag.TryExchange(ItemIds.Gold, 10, ItemIds.Bread, 1), Is.True);
            Assert.That(bag.Count(ItemIds.Gold), Is.Zero);
            Assert.That(bag.Count(ItemIds.Bread), Is.EqualTo(1));
        }

        [Test]
        public void FailedExchangePreservesCoinsAndTheirPosition()
        {
            var bag = new GridInventory(1, 1); bag.TryAdd(ItemIds.Gold, 20);
            int version = bag.Version, id = bag.Entries[0].Id;
            Assert.That(bag.TryExchange(ItemIds.Gold, 10, ItemIds.Bread, 1), Is.False);
            Assert.That(bag.Count(ItemIds.Gold), Is.EqualTo(20));
            Assert.That(bag.Find(id).X, Is.Zero);
            Assert.That(bag.Version, Is.EqualTo(version));
        }

        [Test]
        public void ToolOnlyImprovesHarvestingWhileEquippedAndRoundTrips()
        {
            var state = new VillageState();
            state.Inventory.TryAdd(ItemIds.Hoe, 1);
            float rate = state.WorkRate;
            Assert.That(state.HasHoe, Is.False);
            Assert.That(state.TryEquip(state.Inventory.Entries[0].Id), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Hoe), Is.Zero);
            Assert.That(state.WorkRate, Is.GreaterThan(rate));
            var restored = VillageState.Restore(state.Export());
            Assert.That(restored.HasHoe, Is.True);
            Assert.That(restored.TryUnequip(EquipmentSlot.MainHand), Is.True);
            Assert.That(restored.WorkRate, Is.EqualTo(rate));
            Assert.That(restored.Inventory.Count(ItemIds.Hoe), Is.EqualTo(1));
        }

        [Test]
        public void FullBagCannotDiscardAnEquippedToolOnUnequip()
        {
            var state = new VillageState(); state.Inventory.TryAdd(ItemIds.Hoe, 1);
            state.TryEquip(state.Inventory.Entries[0].Id);
            state.Inventory.TryAdd(ItemIds.Potato, 640);
            Assert.That(state.TryUnequip(EquipmentSlot.MainHand), Is.False);
            Assert.That(state.HasHoe, Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(640));
        }

        [Test]
        public void LegacyWalletMigratesWithoutDroppingGoldFromAFullBag()
        {
            var full = new GridInventory(); full.TryAdd(ItemIds.Potato, 640);
            var state = VillageState.Restore(new VillageSave { schema = 1, gold = 2500, inventory = full.Export() });
            Assert.That(state.Gold, Is.EqualTo(2500));
            Assert.That(state.Inventory.Rows, Is.GreaterThan(4));
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(640));
            var saved = state.Export();
            Assert.That(saved.schema, Is.EqualTo(2)); Assert.That(saved.gold, Is.Zero);
            Assert.That(VillageState.Restore(saved).Gold, Is.EqualTo(2500));
        }

        [Test]
        public void DesignerChangesAffectNewJobsButPreserveAnAcceptedContract()
        {
            var state = new VillageState(); state.TryAcceptJob();
            var rules = new VillageRules { MinutesPerSecond = 10f, JobStart = 600, JobEnd = 1200,
                JobTiers = new[] { new JobTier(0, 12, 90, 70) } };
            var restored = VillageState.Restore(state.Export(), rules);
            Assert.That(restored.JobRequirement, Is.EqualTo(6));
            Assert.That(restored.JobReward, Is.EqualTo(45));
            var next = new VillageState(rules);
            next.AdvanceTime(1f);
            Assert.That(next.Minute, Is.EqualTo(610));
            Assert.That(next.TryAcceptJob(), Is.True);
            Assert.That(next.JobRequirement, Is.EqualTo(12));
        }

        [Test]
        public void ConfiguredItemFootprintsAndPricesAreUsedByTheInventory()
        {
            ItemCatalog.Configure(new[] { new ItemSpec("custom", "수확물", "", 2, 2, 3, 17) });
            var bag = new GridInventory(3, 2);
            Assert.That(bag.TryAdd("custom", 3), Is.True);
            Assert.That(bag.TryAdd("custom", 1), Is.False);
            Assert.That(bag.Entries[0].Spec.Price, Is.EqualTo(17));
        }
    }
}
