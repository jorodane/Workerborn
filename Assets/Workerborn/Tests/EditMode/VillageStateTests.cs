using System;
using System.Linq;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class VillageStateTests
    {
        [Test]
        public void HarvestDeliveryFailureLeavesStaminaAndProgressIntact()
        {
            var state = new VillageState();
            Assert.That(state.LearnSkill(SkillId.SoulGrowth), Is.True);
            int energy = state.Energy;
            int requested = 0;
            Assert.That(state.TryGather(SkillId.Harvesting, ItemIds.Potato, 1, 5, WorkOutcome.Success,
                quantity => { requested = quantity; return false; }, out int harvested, out int experience), Is.False);
            Assert.That(requested, Is.EqualTo(1));
            Assert.That(harvested, Is.Zero);
            Assert.That(experience, Is.Zero);
            Assert.That(state.Energy, Is.EqualTo(energy));
            Assert.That(state.LifetimeHarvests, Is.Zero);
            Assert.That(state.Experience, Is.Zero);
        }

        [Test]
        public void VillageAndResidentRecordsUseTheSameCharacterProgressAndEquipmentRules()
        {
            var resident = new VillageState();
            Assert.That(resident.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            Assert.That(resident.TryEquip(resident.Inventory.Entries.Single().Id), Is.True);
            Assert.That(resident.TryGather(ItemIds.Potato, 1, 5, WorkOutcome.Success, out int quantity), Is.True);
            Assert.That(resident.Inventory.TryAdd(ItemIds.Potato, quantity), Is.True);

            var world = new VillageState().Export();
            var saved = resident.Export<CharacterSave>();
            saved.characterId = "farmer";
            saved.playerX = -5f;
            saved.harvestTargetId = "farm-01";
            saved.harvestProgress = .6f;
            world.characters = new[] { saved };

            var restored = VillageState.Restore(world.characters.Single());
            Assert.That(restored.Equipment[EquipmentSlot.MainHand], Is.EqualTo(ItemIds.Axe));
            Assert.That(restored.Inventory.Count(ItemIds.Potato), Is.EqualTo(quantity));
            Assert.That(restored.Energy, Is.EqualTo(resident.Energy));
            Assert.That(restored.LifetimeHarvests, Is.EqualTo(1));
            Assert.That(VillageState.Restore(world).LifetimeHarvests, Is.Zero);

            var resumed = restored.Export<VillageSave>();
            resumed.CopyPoseAndAction(saved);
            Assert.That(resumed.characterId, Is.EqualTo("farmer"));
            Assert.That(resumed.playerX, Is.EqualTo(-5f));
            Assert.That(resumed.harvestTargetId, Is.EqualTo("farm-01"));
            Assert.That(resumed.harvestProgress, Is.EqualTo(.6f));
        }

        [Test]
        public void ItemShortcutsKeepTheirBindingWhenAStackRunsOutAndIsReplaced()
        {
            var state = new VillageState();
            Assert.That(state.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.HealthPotion, 1), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, 1), Is.True);
            var axe = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Axe);
            var potion = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.HealthPotion);
            var potato = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Potato);
            int inventoryVersion = state.Inventory.Version;
            Assert.That(state.TryAssignItemShortcut(0, axe.Id), Is.True);
            Assert.That(state.TryAssignItemShortcut(9, potion.Id), Is.True);
            Assert.That(state.TryAssignItemShortcut(9, potato.Id), Is.False);
            Assert.That(state.Inventory.Version, Is.EqualTo(inventoryVersion));
            Assert.That(state.Inventory.Find(potion.Id), Is.SameAs(potion));

            var reloaded = VillageState.Restore(state.Export());
            Assert.That(reloaded.ItemShortcutAt(0), Is.EqualTo(ItemIds.Axe));
            Assert.That(reloaded.ItemShortcutAt(9), Is.EqualTo(ItemIds.HealthPotion));
            reloaded.TakeHit(10);
            Assert.That(reloaded.TryDrinkPotion(reloaded.FindShortcutEntry(9).Id), Is.True);
            Assert.That(reloaded.FindShortcutEntry(9), Is.Null);
            reloaded = VillageState.Restore(reloaded.Export());
            Assert.That(reloaded.ItemShortcutAt(9), Is.EqualTo(ItemIds.HealthPotion));
            Assert.That(reloaded.Inventory.TryAdd(ItemIds.HealthPotion, 1), Is.True);
            Assert.That(reloaded.FindShortcutEntry(9)?.ItemId, Is.EqualTo(ItemIds.HealthPotion));

            Assert.That(reloaded.TryClearItemShortcut(9), Is.True);
            Assert.That(reloaded.ItemShortcutAt(9), Is.Null);
            Assert.That(reloaded.Inventory.Count(ItemIds.HealthPotion), Is.EqualTo(1));
        }

        [Test]
        public void ShiftShortcutsPersistActiveSkillsAndReplaceOnlyTheirOwnBanksBinding()
        {
            var state = new VillageState();
            Assert.That(state.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            int axe = state.Inventory.Entries.Single().Id;
            int shifted = VillageState.ItemShortcutCount + 1;
            Assert.That(state.TryAssignItemShortcut(1, axe), Is.True);
            var older = state.Export();
            older.schema = 16;
            older.itemShortcuts = older.itemShortcuts.Take(VillageState.ItemShortcutCount).ToArray();
            state = VillageState.Restore(older);
            Assert.That(state.ItemShortcutAt(1), Is.EqualTo(ItemIds.Axe));
            Assert.That(state.SkillShortcutAt(shifted), Is.Null);
            Assert.That(state.TryAssignSkillShortcut(shifted, SkillId.Throwing), Is.False);
            state.LearnSkill(SkillId.Throwing);
            state.LearnSkill(SkillId.RestMaster);
            Assert.That(state.TryAssignSkillShortcut(shifted, SkillId.RestMaster), Is.False);
            Assert.That(state.TryAssignSkillShortcut(shifted, SkillId.Throwing), Is.True);
            state = VillageState.Restore(state.Export());
            Assert.That(state.ItemShortcutAt(1), Is.EqualTo(ItemIds.Axe));
            Assert.That(state.SkillShortcutAt(shifted), Is.EqualTo(SkillId.Throwing));
            Assert.That(state.TryAssignItemShortcut(shifted, state.Inventory.Entries.Single().Id), Is.True);
            Assert.That(state.SkillShortcutAt(shifted), Is.Null);
            Assert.That(state.TryAssignSkillShortcut(shifted, SkillId.Throwing), Is.True);
            Assert.That(state.ItemShortcutAt(shifted), Is.Null);
            Assert.That(state.TryClearItemShortcut(shifted), Is.True);
            Assert.That(state.SkillShortcutAt(shifted), Is.Null);
            Assert.That(state.ItemShortcutAt(1), Is.EqualTo(ItemIds.Axe));
        }

        [Test]
        public void EquipmentSetsBorrowOnlyUnblockedEmptySlotsAndKeepItemsInTheirOwnSets()
        {
            var state = new VillageState();
            var worn = new EquipmentCondition(5, 24);
            Equip(ItemIds.Axe, worn);
            Equip(ItemIds.Bag);
            Assert.That(state.TrySelectEquipmentSet(1), Is.True);
            Assert.That(state.HasTool(ItemIds.Axe), Is.True);
            Assert.That(state.IsEquipmentFallback(EquipmentSlot.MainHand), Is.True);
            Assert.That(state.BagExpanded, Is.True);
            Assert.That(state.IsEquipmentFallback(EquipmentSlot.Bag), Is.True);
            Assert.That(state.TryToggleEquipmentFallback(EquipmentSlot.MainHand), Is.True);
            Assert.That(state.HasTool(ItemIds.Axe), Is.False);
            Assert.That(state.IsEquipmentBlocked(EquipmentSlot.MainHand), Is.True);
            Assert.That(state.Inventory.Entries, Is.Empty);
            state = VillageState.Restore(state.Export());
            Assert.That(state.IsEquipmentBlocked(EquipmentSlot.MainHand), Is.True);
            Equip(ItemIds.FishingRod);
            Assert.That(state.IsEquipmentBlocked(EquipmentSlot.MainHand), Is.False);
            Assert.That(state.TryToggleEquipmentFallback(EquipmentSlot.Head), Is.True);
            state = VillageState.Restore(state.Export());
            Assert.That(state.EquipmentSet, Is.EqualTo(1));
            Assert.That(state.HasTool(ItemIds.FishingRod), Is.True);
            Assert.That(state.TrySelectEquipmentSet(0), Is.True);
            Assert.That(state.HasTool(ItemIds.Axe), Is.True);
            Assert.That(state.EquippedCondition(EquipmentSlot.MainHand), Is.EqualTo(worn));
            Assert.That(state.Inventory.Entries, Is.Empty);
            Assert.That(state.BagExpanded, Is.True);
            Assert.That(state.IsEquipmentBlocked(EquipmentSlot.Head), Is.False);
            state.RebirthPersonalLife();
            Assert.That(state.Export().otherEquipment, Is.Empty);
            Assert.That(state.Export().otherBlockedEquipment, Is.Zero);
            Assert.That(VillageState.Restore(state.Export()).Equipment, Is.Empty);

            void Equip(string item, EquipmentCondition condition = default)
            {
                Assert.That(state.Inventory.TryAdd(item, 1, condition), Is.True);
                Assert.That(state.TryEquip(state.Inventory.Entries.Single().Id), Is.True);
            }
        }

        [Test]
        public void OldHandSetsKeepTheirItemsAndTheSharedBagBecomesAFallback()
        {
            const int bagRows = VillageState.BaseInventoryRows + 2;
            var save = new VillageState().Export();
            save.schema = 17;
            save.handSet = 1;
            save.bagExpanded = true;
            save.inventoryRows = bagRows;
            save.equipment = new[]
            {
                new EquipmentEntrySave { slot = EquipmentSlot.Bag, itemId = ItemIds.Bag },
                new EquipmentEntrySave { slot = EquipmentSlot.MainHand, itemId = ItemIds.FishingRod }
            };
            var worn = new EquipmentCondition(3, 9);
            save.otherHandEquipment = new[] { new EquipmentEntrySave { slot = EquipmentSlot.MainHand, itemId = ItemIds.Axe, condition = worn } };
            var state = VillageState.Restore(save);
            Assert.That(state.EquipmentSet, Is.EqualTo(1));
            Assert.That(state.HasTool(ItemIds.FishingRod), Is.True);
            Assert.That(state.TrySelectEquipmentSet(0), Is.True);
            Assert.That(state.EquippedCondition(EquipmentSlot.MainHand), Is.EqualTo(worn));
            Assert.That(state.IsEquipmentFallback(EquipmentSlot.Bag), Is.True);
            Assert.That(state.TryToggleEquipmentFallback(EquipmentSlot.Bag), Is.True);
            Assert.That(state.Inventory.Rows, Is.EqualTo(VillageState.BaseInventoryRows));
            Assert.That(state.TryToggleEquipmentFallback(EquipmentSlot.Bag), Is.True);
            Assert.That(state.Inventory.Rows, Is.EqualTo(bagRows));
        }

        [Test]
        public void TakingOffAFallbackBagThroughEitherSetOrItsSlotKeepsAnOverfullInventoryIntact()
        {
            var state = new VillageState();
            Assert.That(state.TrySelectEquipmentSet(1), Is.True);
            Assert.That(state.TryToggleEquipmentFallback(EquipmentSlot.Bag), Is.True);
            Assert.That(state.TrySelectEquipmentSet(0), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.Bag, 1), Is.True);
            Assert.That(state.TryEquip(state.Inventory.Entries.Single().Id), Is.True);
            int potatoes = (state.Inventory.Columns * VillageState.BaseInventoryRows + 1) * ItemCatalog.Get(ItemIds.Potato).MaxStack;
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, potatoes), Is.True);
            Assert.That(state.TrySelectEquipmentSet(1), Is.False);
            Assert.That(state.EquipmentSet, Is.Zero);
            Assert.That(state.BagExpanded, Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(potatoes));

            var save = state.Export();
            save.otherBlockedEquipment = 0;
            state = VillageState.Restore(save);
            Assert.That(state.TrySelectEquipmentSet(1), Is.True);
            Assert.That(state.TryToggleEquipmentFallback(EquipmentSlot.Bag), Is.False);
            Assert.That(state.IsEquipmentFallback(EquipmentSlot.Bag), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(potatoes));
        }

        [Test]
        public void HealthPotionHealsUpToTheLimitAndConsumesOnlyTheSelectedStack()
        {
            var state = new VillageState();
            var potion = ItemCatalog.Get(ItemIds.HealthPotion);
            int quantity = potion.MaxStack + 1;
            Assert.That(state.Inventory.TryAdd(ItemIds.Gold, potion.Price * quantity), Is.True);
            Assert.That(state.TryBuy(ItemIds.HealthPotion, quantity), Is.True);
            var second = state.Inventory.Entries.Single(item => item.ItemId == ItemIds.HealthPotion && item.Quantity == 1);
            Assert.That(state.TryDrinkPotion(second.Id), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.HealthPotion), Is.EqualTo(quantity));

            int stamina = state.Energy;
            state.TakeHit(potion.HealthRestore + 8);
            Assert.That(state.TryDrinkPotion(second.Id), Is.True);
            Assert.That(state.Inventory.Find(second.Id), Is.Null);
            var remaining = state.Inventory.Entries.Single(item => item.ItemId == ItemIds.HealthPotion);
            Assert.That(remaining.Quantity, Is.EqualTo(potion.MaxStack));
            Assert.That(state.Health, Is.EqualTo(state.HealthLimit - 8));

            Assert.That(state.TryDrinkPotion(remaining.Id), Is.True);
            Assert.That(state.Health, Is.EqualTo(state.HealthLimit));
            Assert.That(state.Energy, Is.EqualTo(stamina));
            var restored = VillageState.Restore(state.Export());
            Assert.That(restored.Health, Is.EqualTo(state.HealthLimit));
            Assert.That(restored.Inventory.Count(ItemIds.HealthPotion), Is.EqualTo(potion.MaxStack - 1));
        }

        [Test]
        public void ExpandedWorldCreatesLargerOrdersAsWellAsMorePlants()
        {
            var state = VillageState.Restore(new VillageSave { farmLevel = 2 });
            state.TryAcceptJob();
            Assert.That(state.JobRequirement, Is.EqualTo(10));
            Assert.That(state.JobReward, Is.EqualTo(75));
            Assert.That(state.JobExperience, Is.EqualTo(55));
            Assert.That(VillageState.Restore(state.Export()).JobRequirement, Is.EqualTo(10));
        }

        [Test]
        public void FullInventoryPurchaseDoesNotChargeGold()
        {
            var state = Funded();
            int freeCells = state.Inventory.Columns * state.Inventory.Rows - 1;
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, freeCells * ItemCatalog.Get(ItemIds.Potato).MaxStack), Is.True);
            Assert.That(state.TryBuy(ItemIds.Bread), Is.False);
            Assert.That(state.Gold, Is.EqualTo(500));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
        }

        [Test]
        public void EquippedBagPersistsAndCanOnlyBeRemovedWhenItsContentsFit()
        {
            var state = Funded();
            Assert.That(state.TryBuy(ItemIds.Bag), Is.True);
            Assert.That(state.Inventory.Rows, Is.EqualTo(6));
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, 640), Is.True);

            state = VillageState.Restore(state.Export());
            Assert.That(state.Equipment[EquipmentSlot.Bag], Is.EqualTo(ItemIds.Bag));
            Assert.That(state.TryUnequip(EquipmentSlot.Bag), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(640));
            Assert.That(state.Inventory.Rows, Is.EqualTo(6));

            Assert.That(state.Inventory.TryRemove(ItemIds.Potato, 640), Is.True);
            Assert.That(state.TryUnequip(EquipmentSlot.Bag), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Bag), Is.EqualTo(1));
            Assert.That(state.Inventory.Rows, Is.EqualTo(4));
            Assert.That(state.TryEquip(state.Inventory.Entries[1].Id), Is.True);
            Assert.That(state.Inventory.Rows, Is.EqualTo(6));
        }

        [Test]
        public void LegacyExpandedBagMigratesIntoTheEquipmentSlot()
        {
            var inventory = new GridInventory(8, 6);
            inventory.TryAdd(ItemIds.Bag, 1);
            inventory.TryAdd(ItemIds.Potato, 31);
            var save = new VillageState().Export();
            save.schema = 3;
            save.bagExpanded = true;
            save.inventoryColumns = inventory.Columns;
            save.inventoryRows = inventory.Rows;
            save.inventory = inventory.Export();
            var state = VillageState.Restore(save);
            Assert.That(state.Equipment[EquipmentSlot.Bag], Is.EqualTo(ItemIds.Bag));
            Assert.That(state.Inventory.Count(ItemIds.Bag), Is.Zero);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(31));
            Assert.That(state.Inventory.Rows, Is.EqualTo(6));
        }

        [Test]
        public void FullEightColumnSaveKeepsEveryStackWhenReflowedToSixColumns()
        {
            var legacy = GridInventory.Restore(8, 16, new[]
            {
                new InventoryEntrySave { id = 9, itemId = ItemIds.Bag, quantity = 1, x = 5, y = 14 }
            });
            int potatoes = (8 * 16 - 4) * ItemCatalog.Get(ItemIds.Potato).MaxStack;
            Assert.That(legacy.TryAdd(ItemIds.Potato, potatoes), Is.True);
            var save = new VillageState().Export();
            save.schema = 6;
            save.inventoryColumns = legacy.Columns;
            save.inventoryRows = legacy.Rows;
            save.inventory = legacy.Export();

            var migrated = VillageState.Restore(save);
            Assert.That(migrated.Inventory.Columns, Is.EqualTo(6));
            Assert.That(migrated.Inventory.Rows, Is.GreaterThan(16));
            var roundTrip = VillageState.Restore(migrated.Export()).Inventory;
            Assert.That(roundTrip.Entries.Count, Is.EqualTo(legacy.Entries.Count));
            foreach (var before in legacy.Entries)
            {
                var after = roundTrip.Find(before.Id);
                Assert.That(after, Is.Not.Null);
                Assert.That(after.ItemId, Is.EqualTo(before.ItemId));
                Assert.That(after.Quantity, Is.EqualTo(before.Quantity));
                if (before.X + before.Spec.Width <= roundTrip.Columns)
                {
                    Assert.That(after.X, Is.EqualTo(before.X));
                    Assert.That(after.Y, Is.EqualTo(before.Y));
                }
            }
        }

        [Test]
        public void InvalidActiveJobTermsAreRejectedOnLoad()
        {
            var state = new VillageState();
            state.TryAcceptJob();
            var save = state.Export();
            save.jobReward = -1;
            Assert.Throws<ArgumentException>(() => VillageState.Restore(save));
        }

        private static VillageState Funded()
        {
            return VillageState.Restore(new VillageSave
            {
                gold = 500, level = 3, totalLevel = 10, ap = 20, skillRank = 2,
                career = 4, introAccepted = true, introClaimed = true
            });
        }

        [Test]
        public void FirstJobQuestGrantsGrowthBeforeRebirthBecomesAvailable()
        {
            var state = new VillageState();
            Assert.That(state.LearnSkill(SkillId.SoulGrowth), Is.True);
            state.MeetFarmer();
            Assert.That(state.TryAcceptJob(), Is.True);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(state.TryHarvest(2, false, out int drops), Is.True);
                Assert.That(state.Inventory.TryAdd(ItemIds.Potato, drops), Is.True);
            }
            Assert.That(state.TryDeliverJob(), Is.True);
            Assert.That(state.TryClaimIntroQuest(), Is.True);
            Assert.That(state.Level, Is.EqualTo(3));
            Assert.That(state.CanRebirth, Is.False);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.Zero);
            Assert.That(state.Gold, Is.EqualTo(75));
        }

        [Test]
        public void TimeAdvancesAtTheConfiguredRateAndDialogueCanPauseIt()
        {
            var state = new VillageState();
            state.AdvanceTime(10f);
            Assert.That(state.Minute, Is.EqualTo(510f));
            state.AdvanceTime(10f, true);
            Assert.That(state.Minute, Is.EqualTo(510f));
        }

        [Test]
        public void AnimalDamageUsesHealthAndFollowsTheExistingCollapseFlow()
        {
            var state = new VillageState();
            Assert.That(state.TakeHit(7), Is.EqualTo(7));
            Assert.That(state.Health, Is.EqualTo(state.HealthLimit - 7));
            Assert.That(state.Energy, Is.EqualTo(state.StaminaLimit));
            Assert.That(state.TakeHit(1000), Is.EqualTo(VillageState.MaxEnergy - 7));
            Assert.That(state.TakeHit(7), Is.Zero);
            Assert.That(state.AdvanceTime(0), Is.True);
            Assert.That(state.Day, Is.EqualTo(2));
            Assert.That(state.Energy, Is.EqualTo(state.Rules.CollapseEnergy));
        }

        [Test]
        public void JobFailsAtDeadlineAndCannotPayAfterClosing()
        {
            var state = new VillageState();
            state.TryAcceptJob();
            state.Inventory.TryAdd(ItemIds.Potato, 6);
            state.AdvanceTime(200f);
            Assert.That(state.Job, Is.EqualTo(JobStatus.Failed));
            Assert.That(state.TryDeliverJob(), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(6));
            Assert.That(state.Gold, Is.Zero);
            Assert.That(state.Career, Is.Zero);
        }

        [Test]
        public void JobRewardAndIntroQuestCannotBeClaimedTwice()
        {
            var state = new VillageState();
            Assert.That(state.CanClaimIntroQuest, Is.False);
            state.TryAcceptJob();
            Assert.That(state.CanClaimIntroQuest, Is.False);
            state.Inventory.TryAdd(ItemIds.Potato, 12);
            Assert.That(state.TryDeliverJob(), Is.True);
            Assert.That(state.CanClaimIntroQuest, Is.True);
            Assert.That(state.TryDeliverJob(), Is.False);
            Assert.That(state.TryAcceptJob(), Is.False);
            Assert.That(state.TryClaimIntroQuest(), Is.True);
            Assert.That(state.CanClaimIntroQuest, Is.False);
            Assert.That(state.TryClaimIntroQuest(), Is.False);
            Assert.That(VillageState.Restore(state.Export(), state.Rules).CanClaimIntroQuest, Is.False);
            Assert.That(state.Gold, Is.EqualTo(75));
            Assert.That(state.Career, Is.EqualTo(1));
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(6));
            Assert.That(state.Experience, Is.Zero);
            Assert.That(state.Level, Is.EqualTo(1));
        }

        [Test]
        public void InsufficientDeliveryDoesNotSpendItemsOrAwardAnything()
        {
            var state = new VillageState();
            state.TryAcceptJob();
            state.Inventory.TryAdd(ItemIds.Potato, 5);
            Assert.That(state.TryDeliverJob(), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(5));
            Assert.That(state.Job, Is.EqualTo(JobStatus.Active));
            Assert.That(state.Gold, Is.Zero);
        }

        [Test]
        public void CareerUnlocksLargerJobsAndSurvivesRebirth()
        {
            var state = Funded();
            Assert.That(state.TryAcceptJob(), Is.True);
            Assert.That(state.JobRequirement, Is.EqualTo(10));
            for (int day = 0; day < state.Rules.RebirthDays; day++)
                state.TrySleep(false);
            state.TryWaitForWork();
            Assert.That(state.TryRebirth(), Is.True);
            Assert.That(state.Career, Is.EqualTo(4));
            Assert.That(state.TryAcceptJob(), Is.True);
            Assert.That(state.JobRequirement, Is.EqualTo(10));
        }

        [Test]
        public void HarvestConsumesEnergyAndGrantsExperienceButDoesNotAutoLoot()
        {
            var state = new VillageState();
            Assert.That(state.LearnSkill(SkillId.SoulGrowth), Is.True);
            Assert.That(state.TryHarvest(2, false, out int drops), Is.True);
            Assert.That(drops, Is.EqualTo(2));
            Assert.That(state.Energy, Is.EqualTo(85));
            Assert.That(state.Experience, Is.EqualTo(8));
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.Zero);
        }

        [Test]
        public void LuckyHarvestMultipliesTheDropCount()
        {
            var state = new VillageState();
            Assert.That(state.RollGatheringBonus(0f), Is.EqualTo(WorkOutcome.Success));
            Assert.That(state.TryHarvest(2, true, out int drops), Is.True);
            Assert.That(drops, Is.EqualTo(2));
            Assert.That(state.Experience, Is.Zero);

            Assert.That(state.Inventory.TryAdd(ItemIds.LuckyBonusBook, 1), Is.True);
            Assert.That(state.TryReadSkillBook(state.Inventory.Entries.Single().Id), Is.True);
            Assert.That(state.RollGatheringBonus(.04f), Is.EqualTo(WorkOutcome.Lucky));
            Assert.That(state.RollGatheringBonus(.051f), Is.EqualTo(WorkOutcome.Success));
            Assert.That(state.TryHarvest(2, true, out drops), Is.True);
            Assert.That(drops, Is.EqualTo(4));
            Assert.That(state.LifetimeHarvests, Is.EqualTo(2));
        }

        [TestCase(13)]
        [TestCase(14)]
        public void ExistingPlayersReceiveSoulGrowthWithoutResettingTheirProgress(int schema)
        {
            var previous = new VillageState();
            Assert.That(previous.Inventory.TryAdd(ItemIds.SoulGrowthBook, 1), Is.True);
            var save = previous.Export();
            save.schema = schema;
            save.level = 4;
            save.totalLevel = 4;
            save.experience = 12;
            Assert.That(VillageState.Restore(save).IsSkillLearned(SkillId.SoulGrowth), Is.False);

            var state = VillageState.RestorePlayer(save, null, out bool learned);
            Assert.That(learned, Is.True);
            Assert.That(state.IsSkillLearned(SkillId.SoulGrowth), Is.True);
            Assert.That(state.RankOf(SkillId.SoulGrowth), Is.Zero);
            Assert.That(state.Level, Is.EqualTo(4));
            Assert.That(state.Experience, Is.EqualTo(12));
            Assert.That(state.Inventory.Count(ItemIds.SoulGrowthBook), Is.EqualTo(1));
            Assert.That(state.TryHarvest(1, false, out _), Is.True);
            Assert.That(state.Experience, Is.EqualTo(20));
            Assert.That(state.LearnSkill(SkillId.SoulGrowth, SkillProgression.MaxRank), Is.True);
            Assert.That(state.TryHarvest(1, false, out _), Is.True);
            Assert.That(state.Experience, Is.EqualTo(36));
            Assert.That(state.Level, Is.EqualTo(4));
            var reloaded = VillageState.RestorePlayer(state.Export(), null, out learned);
            Assert.That(learned, Is.False);
            Assert.That(reloaded.ExperienceGainPercent, Is.EqualTo(200));
        }

        [Test]
        public void CriticalHitChanceUsesRankAndDamageUsesGrade()
        {
            var save = new VillageState().Export();
            save.ap = 100;
            var state = VillageState.Restore(save);
            Assert.That(state.ResolveCombatDamage(20, 0f, out bool critical), Is.EqualTo(20));
            Assert.That(critical, Is.False);

            Assert.That(state.Inventory.TryAdd(ItemIds.CriticalHitBook, 1), Is.True);
            Assert.That(state.TryReadSkillBook(state.Inventory.Entries.Single().Id), Is.True);
            Assert.That(state.ResolveCombatDamage(20, .049f, out critical), Is.EqualTo(30));
            Assert.That(critical, Is.True);
            Assert.That(state.ResolveCombatDamage(20, .051f, out _), Is.EqualTo(20));

            Assert.That(state.TryUpgradeSkillToNextGrade(SkillId.CriticalHit), Is.True);
            Assert.That(state.ResolveCombatDamage(20, .051f, out _), Is.EqualTo(32));
            Assert.That(state.TryUpgradeSkill(SkillId.CriticalHit), Is.True);
            Assert.That(state.ResolveCombatDamage(20, .053f, out _), Is.EqualTo(32));
            Assert.That(state.TryUpgradeSkillToNextGrade(SkillId.CriticalHit), Is.True);
            Assert.That(state.ResolveCombatDamage(20, .07f, out _), Is.EqualTo(34));
        }

        [Test]
        public void NewSkillsRequireLearningAndReadingASecondCopyDoesNotConsumeIt()
        {
            var save = new VillageState().Export();
            save.ap = 5;
            var state = VillageState.Restore(save);
            Assert.That(SkillProgression.All.All(skill => !state.IsSkillLearned(skill)), Is.True);
            Assert.That(state.TryUpgradeSkill(SkillId.Fishing), Is.False);
            Assert.That(state.AP, Is.EqualTo(5));

            Assert.That(state.Inventory.TryAdd(ItemIds.FishingBook, 1), Is.True);
            int bookId = state.Inventory.Entries.Single().Id;
            Assert.That(state.TryReadSkillBook(bookId), Is.True);
            Assert.That(state.IsSkillLearned(SkillId.Fishing), Is.True);
            Assert.That(state.RankOf(SkillId.Fishing), Is.Zero);
            Assert.That(state.Inventory.Count(ItemIds.FishingBook), Is.Zero);

            state = VillageState.Restore(state.Export());
            Assert.That(state.IsSkillLearned(SkillId.Fishing), Is.True);
            Assert.That(state.TryUpgradeSkill(SkillId.Fishing), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.FishingBook, 1), Is.True);
            Assert.That(state.TryReadSkillBook(state.Inventory.Entries.Single().Id), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.FishingBook), Is.EqualTo(1));
            Assert.That(state.IsSkillLearned(SkillId.Harvesting), Is.False);
        }

        [Test]
        public void HarvestAcquisitionIsLearnedFromItsBookAndRaisesThePickupRateByGrade()
        {
            var state = VillageState.Restore(new VillageSave { schema = 8, inventoryColumns = VillageState.InventoryColumns, ap = 10_000 });
            var skill = SkillId.HarvestAcquisition;
            Assert.That(state.IsSkillLearned(skill), Is.False);
            Assert.That(state.HarvestItemsPerSecond, Is.Zero);
            Assert.That(state.TryAcquireHarvestImmediately(ItemIds.Potato, 2), Is.False);
            Assert.That(state.Inventory.TryAdd(ItemIds.Gold, ItemCatalog.Get(ItemIds.HarvestAcquisitionBook).Price), Is.True);
            Assert.That(state.TryBuy(ItemIds.HarvestAcquisitionBook), Is.True);
            Assert.That(state.TryReadSkillBook(state.Inventory.Entries.Single().Id), Is.True);
            Assert.That(state.HarvestItemsPerSecond, Is.EqualTo(1));
            Assert.That(state.TryAcquireHarvestImmediately(ItemIds.Potato, 2), Is.False);

            Assert.That(state.TryUpgradeSkill(skill), Is.True);
            Assert.That(state.HarvestItemsPerSecond, Is.EqualTo(2));
            Assert.That(state.TryUpgradeSkillToNextGrade(skill), Is.True);
            Assert.That(state.RankOf(skill), Is.EqualTo(11));
            Assert.That(state.HarvestItemsPerSecond, Is.EqualTo(3));
            Assert.That(state.LearnSkill(skill, SkillProgression.MaxRank), Is.True);
            Assert.That(state.HasInstantHarvestAcquisition, Is.True);
            Assert.That(state.TryAcquireHarvestImmediately(ItemIds.Potato, 3), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(3));
            Assert.That(state.Export().pendingHarvests, Is.Empty);
        }

        [Test]
        public void PendingHarvestsKeepTheirYieldWhenBagSpaceFillsUpAndClearOnRebirth()
        {
            const long now = 100_000;
            var state = new VillageState();
            state.LearnSkill(SkillId.HarvestAcquisition);
            int goldStack = ItemCatalog.Get(ItemIds.Gold).MaxStack;
            int gold = state.Inventory.Columns * state.Inventory.Rows * goldStack;
            int potatoStack = ItemCatalog.Get(ItemIds.Potato).MaxStack;
            Assert.That(state.Inventory.TryAdd(ItemIds.Gold, gold), Is.True);
            var save = state.Export();
            save.schema = 12;
            save.pendingHarvests = new[] { new HarvestAcquisitionSave { itemId = ItemIds.Potato, quantity = potatoStack, readyAtUtc = now + 3_000 } };

            state = VillageState.Restore(save);
            state.CollectReadyHarvests(now + 3_000);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.Zero);
            Assert.That(state.Export().pendingHarvests.Length, Is.EqualTo(1));

            Assert.That(state.Inventory.TryRemove(ItemIds.Gold, goldStack), Is.True);
            state.CollectReadyHarvests(now + 3_000);
            state.CollectReadyHarvests(now + 3_001);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(potatoStack));
            Assert.That(state.Export().pendingHarvests, Is.Empty);

            state = VillageState.Restore(save);
            state.RebirthPersonalLife();
            state.CollectReadyHarvests(now + 7_000);
            Assert.That(state.Inventory.Entries, Is.Empty);
            Assert.That(state.Export().pendingHarvests, Is.Empty);
            Assert.That(state.IsSkillLearned(SkillId.HarvestAcquisition), Is.True);
        }

        [Test]
        public void SkillUpgradeSpendsApAndIncreasesYield()
        {
            var state = Funded();
            int cost = state.NextSkillCost;
            int ap = state.AP;
            int yieldMultiplier = state.YieldMultiplier;
            Assert.That(state.TryUpgradeSkill(), Is.True);
            Assert.That(state.AP, Is.EqualTo(ap - cost));
            Assert.That(state.YieldMultiplier, Is.GreaterThan(yieldMultiplier));
        }

        [Test]
        public void UpgradingToTheNextGradeChargesEveryRankOrLeavesTheSkillIntact()
        {
            var skill = SkillId.HarvestAcquisition;
            var state = new VillageState();
            Assert.That(state.NextSkillGradeRankFor(skill), Is.Zero);
            Assert.That(state.TryUpgradeSkillToNextGrade(skill), Is.False);
            state.LearnSkill(skill);
            Assert.That(state.NextSkillGradeRankFor(skill), Is.EqualTo(1));
            Assert.That(state.NextSkillGradeCostFor(skill), Is.EqualTo(1));

            state.LearnSkill(skill, 8);
            var save = state.Export();
            save.ap = 3;
            state = VillageState.Restore(save);
            Assert.That(state.NextSkillCostFor(skill), Is.EqualTo(1));
            Assert.That(state.NextSkillGradeRankFor(skill), Is.EqualTo(11));
            Assert.That(state.NextSkillGradeCostFor(skill), Is.EqualTo(4));
            Assert.That(state.TryUpgradeSkillToNextGrade(skill), Is.False);
            Assert.That(state.AP, Is.EqualTo(3));
            Assert.That(state.RankOf(skill), Is.EqualTo(8));

            save.ap = 4;
            state = VillageState.Restore(save);
            Assert.That(state.TryUpgradeSkillToNextGrade(skill), Is.True);
            Assert.That(state.AP, Is.Zero);
            Assert.That(state.RankOf(skill), Is.EqualTo(11));
            Assert.That(state.NextSkillCostFor(skill), Is.EqualTo(2));
            Assert.That(state.NextSkillGradeRankFor(skill), Is.EqualTo(21));
            state.LearnSkill(skill, 91);
            Assert.That(state.NextSkillGradeRankFor(skill), Is.Zero);
            Assert.That(state.NextSkillCostFor(skill), Is.EqualTo(10));
        }

        [Test]
        public void IndependentSkillsReachRankOneHundredAndSurviveSaving()
        {
            var state = VillageState.Restore(new VillageSave { skillRank = 4, ap = 20000 });
            Assert.That(state.RankOf(SkillId.Harvesting), Is.EqualTo(4));
            Assert.That(state.RankOf(SkillId.Woodcutting), Is.EqualTo(4));
            Assert.That(state.RankOf(SkillId.EggGathering), Is.EqualTo(4));
            Assert.That(state.RankOf(SkillId.Carving), Is.Zero);
            Assert.That(state.RankOf(SkillId.UnarmedCombat), Is.Zero);
            for (int rank = 5; rank <= SkillProgression.MaxRank; rank++)
                Assert.That(state.TryUpgradeSkill(SkillId.Woodcutting), Is.True);

            Assert.That(state.TryUpgradeSkill(SkillId.Woodcutting), Is.False);
            Assert.That(state.RankOf(SkillId.Woodcutting), Is.EqualTo(100));
            Assert.That(state.RankOf(SkillId.Harvesting), Is.EqualTo(4));
            Assert.That(state.YieldMultiplierFor(SkillId.Woodcutting), Is.GreaterThan(state.YieldMultiplier));
            var restored = VillageState.Restore(state.Export());
            Assert.That(restored.RankOf(SkillId.Woodcutting), Is.EqualTo(100));
            Assert.That(restored.RankOf(SkillId.Harvesting), Is.EqualTo(4));
            Assert.That(restored.UnarmedDamageBonus, Is.Zero);
        }

        [Test]
        public void SkillEffectRowsAccumulateByRankAndChangeActualStats()
        {
            var rules = new VillageRules();
            rules.Nutrition.nourishmentLossPerHour = 0f;
            rules.SetSkill(new SkillProgression(SkillId.UnarmedCombat, "맨손전투", new[]
            {
                new SkillRankEffect(0, 0, SkillEffect.MaxStamina, 4),
                new SkillRankEffect(1, 2, SkillEffect.UnarmedDamage, 3, 2),
                new SkillRankEffect(2, 2, SkillEffect.MaxStamina, 6)
            }));
            var state = VillageState.Restore(new VillageSave { ap = 10, gold = 15 }, rules);
            Assert.That(state.StaminaLimit, Is.EqualTo(104));
            Assert.That(state.TryUpgradeSkill(SkillId.UnarmedCombat), Is.True);
            Assert.That(state.UnarmedDamageBonus, Is.EqualTo(3));
            Assert.That(state.TryUpgradeSkill(SkillId.UnarmedCombat), Is.True);
            Assert.That(state.UnarmedDamageBonus, Is.EqualTo(8));
            Assert.That(state.TrySleep(true), Is.True);
            Assert.That(state.Energy, Is.EqualTo(110));
            Assert.That(VillageState.Restore(state.Export(), rules).Energy, Is.EqualTo(110));
        }

        [Test]
        public void SellingADraggedStackDebitsOnlyThatStack()
        {
            var state = new VillageState();
            int capacity = ItemCatalog.Get(ItemIds.Potato).MaxStack;
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, capacity + 5), Is.True);
            int first = state.Inventory.Entries[0].Id;
            int selected = state.Inventory.Entries[1].Id;
            Assert.That(state.MaximumSellQuantity(selected), Is.EqualTo(5));
            Assert.That(state.TrySellStack(selected, 6), Is.False);
            Assert.That(state.Inventory.Find(selected).Quantity, Is.EqualTo(5));
            Assert.That(state.TrySellStack(selected, 3), Is.True);
            Assert.That(state.Inventory.Find(first).Quantity, Is.EqualTo(capacity));
            Assert.That(state.Inventory.Find(selected).Quantity, Is.EqualTo(2));
            Assert.That(state.Gold, Is.EqualTo(ItemCatalog.Get(ItemIds.Potato).Price * 3));
        }

        [Test]
        public void FailedPurchaseDoesNotChargeGoldOrGrantTheItem()
        {
            var state = new VillageState();
            Assert.That(state.TryBuy(ItemIds.Bread), Is.False);
            Assert.That(state.Gold, Is.Zero);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
        }

        [Test]
        public void CursorPurchaseSurvivesReloadAndPlacesOnlyWhatFitsInTheChosenCell()
        {
            var state = Funded();
            var bag = state.Inventory;
            var bread = ItemCatalog.Get(ItemIds.Bread);
            Assert.That(bag.TryAdd(bread.Id, bread.MaxStack - 1), Is.True);
            Assert.That(bag.TryAdd(ItemIds.Potato, (bag.Columns * bag.Rows - 2) * ItemCatalog.Get(ItemIds.Potato).MaxStack), Is.True);
            int gold = state.Gold;
            Assert.That(state.TryBuy(ItemIds.FishingRod), Is.False);
            Assert.That(state.Gold, Is.EqualTo(gold));

            int amount = bread.MaxStack + 2;
            Assert.That(state.TryBuyForPlacement(bread.Id, amount), Is.True);
            Assert.That(state.TryBuyForPlacement(bread.Id, 1), Is.False);
            Assert.That(state.Gold, Is.EqualTo(gold - amount * bread.Price));
            Assert.That(bag.Count(bread.Id), Is.EqualTo(bread.MaxStack - 1));

            state = VillageState.Restore(state.Export());
            Assert.That(state.CursorItem.Quantity, Is.EqualTo(amount));
            Assert.That(state.TryStowCursorItem(), Is.False);
            Assert.That(state.TryPlaceCursorItem(0, 0), Is.False);
            bag = state.Inventory;
            var existing = bag.Entries[1];
            Assert.That(state.TryPlaceCursorItem(existing.X, existing.Y), Is.True);
            Assert.That(existing.Quantity, Is.EqualTo(bread.MaxStack));
            Assert.That(state.CursorItem.Quantity, Is.EqualTo(amount - 1));
            var potato = bag.Entries[2];
            Assert.That(bag.TryRemoveAt(potato.Id, potato.Quantity), Is.True);
            Assert.That(state.TryPlaceCursorItem(potato.X, potato.Y), Is.True);
            Assert.That(bag.Count(bread.Id), Is.EqualTo(bread.MaxStack * 2));
            Assert.That(state.CursorItem.Quantity, Is.EqualTo(1));
            Assert.That(state.TrySellStack(0, 1), Is.True);
            Assert.That(state.CursorItem, Is.Null);
        }

        [Test]
        public void EquipmentConditionSurvivesSwapsAndLegacyEquipmentStartsCleanAtFullDurability()
        {
            var state = new VillageState();
            var worn = new EquipmentCondition(0, 56);
            Assert.That(state.Inventory.TryAdd(ItemIds.FishingRod, 1, worn), Is.True);
            Assert.That(state.TryEquip(state.Inventory.Entries.Single().Id), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);

            var save = state.Export();
            state = VillageState.Restore(save);
            Assert.That(state.EquippedCondition(EquipmentSlot.MainHand), Is.EqualTo(worn));
            Assert.That(state.TryEquip(state.Inventory.Entries.Single().Id), Is.True);
            Assert.That(state.Inventory.Entries.Single().Condition, Is.EqualTo(worn));
            Assert.That(state.TryUnequip(EquipmentSlot.MainHand), Is.True);
            var axe = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Axe);
            Assert.That(axe.Condition, Is.EqualTo(new EquipmentCondition(axe.Spec.MaxDurability)));

            save.schema = 10;
            save.equipment.Single().condition = default;
            save.inventory.Single().condition = default;
            state = VillageState.Restore(save);
            Assert.That(state.EquippedCondition(EquipmentSlot.MainHand),
                Is.EqualTo(new EquipmentCondition(ItemCatalog.Get(ItemIds.FishingRod).MaxDurability)));
            Assert.That(state.Inventory.Entries.Single().Condition, Is.EqualTo(new EquipmentCondition(axe.Spec.MaxDurability)));
        }

        [Test]
        public void SplittingInAFullBagHoldsTheNewStackAcrossSaveAndRestore()
        {
            var state = new VillageState();
            int slots = state.Inventory.Columns * state.Inventory.Rows;
            int stackSize = ItemCatalog.Get(ItemIds.Gold).MaxStack;
            Assert.That(state.Inventory.TryAdd(ItemIds.Gold, slots * stackSize), Is.True);
            var source = state.Inventory.Entries.First();
            Assert.That(state.TrySplitStack(source.Id, 7), Is.True);
            Assert.That(state.TrySplitStack(source.Id, 1), Is.False);
            Assert.That(state.Inventory.Entries.Count, Is.EqualTo(slots));
            Assert.That(state.Inventory.Find(source.Id).Quantity, Is.EqualTo(stackSize - 7));

            state = VillageState.Restore(state.Export());
            Assert.That(state.CarriedItem(0).ItemId, Is.EqualTo(ItemIds.Gold));
            Assert.That(state.CarriedItem(0).Quantity, Is.EqualTo(7));
            Assert.That(state.TryPlaceCursorItem(source.X, source.Y), Is.True);
            Assert.That(state.CarriedItem(0), Is.Null);
            Assert.That(state.Gold, Is.EqualTo(slots * stackSize));
        }

        [Test]
        public void CursorPurchaseCanBeEquippedAndCannotFollowAPlayerIntoRebirth()
        {
            var state = Funded();
            Assert.That(state.TryBuyForPlacement(ItemIds.FishingRod, 1), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FishingRod), Is.Zero);
            state = VillageState.Restore(state.Export());
            Assert.That(state.CursorItem.Condition, Is.EqualTo(new EquipmentCondition(ItemCatalog.Get(ItemIds.FishingRod).MaxDurability)));
            Assert.That(state.TryEquip(0), Is.True);
            Assert.That(state.CursorItem, Is.Null);
            Assert.That(state.Equipment[EquipmentSlot.MainHand], Is.EqualTo(ItemIds.FishingRod));

            Assert.That(state.TryBuyForPlacement(ItemIds.Bread, 2), Is.True);
            state.RebirthPersonalLife();
            Assert.That(state.CursorItem, Is.Null);
            Assert.That(state.Export().pendingPurchase, Is.Null);
            var save = state.Export();
            save.pendingPurchase = new CursorItemSave { itemId = ItemIds.Bread, quantity = 2 };
            Assert.Throws<ArgumentException>(() => VillageState.Restore(save));
        }

        [Test]
        public void BuyingABagExpandsTheGridAndCannotBeRepeated()
        {
            var state = Funded();
            Assert.That(state.TryBuy(ItemIds.Bag), Is.True);
            Assert.That(state.Inventory.Rows, Is.EqualTo(6));
            Assert.That(state.Equipment[EquipmentSlot.Bag], Is.EqualTo(ItemIds.Bag));
            Assert.That(state.TryBuy(ItemIds.Bag), Is.False);
            Assert.That(state.Gold, Is.EqualTo(400));
        }

        [Test]
        public void BreadRestoresStaminaCapacityAndEatingStopsWhenFull()
        {
            var state = new VillageState();
            Assert.That(state.Inventory.TryAdd(ItemIds.Bread, 6), Is.True);
            Assert.That(state.AdvanceTime(8f * 60f / state.Rules.MinutesPerSecond), Is.False);
            int hungryLimit = state.StaminaLimit;
            Assert.That(hungryLimit, Is.LessThan(state.WellFedStaminaLimit));
            double before = state.Stamina;
            Assert.That(state.TryEatBread(), Is.True);
            Assert.That(state.StaminaLimit, Is.GreaterThan(hungryLimit));
            Assert.That(state.Stamina, Is.EqualTo(before));
            Assert.That(state.Fullness, Is.EqualTo(20));
            Assert.That(state.Overeating, Is.Zero);
            for (int i = 0; i < 4; i++)
                Assert.That(state.TryEatBread(), Is.True);
            Assert.That(state.Fullness, Is.EqualTo(state.FullnessLimit));
            Assert.That(state.LastMessage, Does.Contain("너무 많이 먹은"));
            Assert.That(state.ActionSpeed, Is.LessThan(1f));
            Assert.That(state.TryEatBread(), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(1));

            var restored = VillageState.Restore(state.Export());
            Assert.That(restored.Fullness, Is.EqualTo(state.Fullness));
            restored.AdvanceTime(2f * 60f / restored.Rules.MinutesPerSecond);
            Assert.That(restored.Fullness, Is.LessThan(state.Fullness));
            Assert.That(restored.ActionSpeed, Is.GreaterThan(state.ActionSpeed));
            Assert.That(restored.TryEatBread(), Is.True);
        }

        [Test]
        public void EatingConsumesTheSelectedFoodStack()
        {
            var state = new VillageState();
            Assert.That(state.Inventory.TryAdd(ItemIds.Bread, 11), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, 1), Is.True);
            var fullStack = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Bread && entry.Quantity == 10);
            var serving = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Bread && entry.Quantity == 1);
            var potato = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Potato);

            Assert.That(state.TryEatFood(potato.Id), Is.False);
            Assert.That(state.Inventory.Find(potato.Id).Quantity, Is.EqualTo(1));
            Assert.That(state.Fullness, Is.Zero);
            Assert.That(state.TryEatFood(serving.Id), Is.True);
            Assert.That(state.Inventory.Find(fullStack.Id).Quantity, Is.EqualTo(10));
            Assert.That(state.Inventory.Find(serving.Id), Is.Null);
            Assert.That(state.TryEatFood(serving.Id), Is.False);
            Assert.That(state.Fullness, Is.EqualTo(20));
        }

        [Test]
        public void WorkUsesNourishmentAndMealsAreOncePerWindowAcrossSaves()
        {
            var state = new VillageState();
            double fed = state.Export().nourishment;
            Assert.That(state.Inventory.TryAdd(ItemIds.Bread, 3), Is.True);
            Assert.That(state.SpendWorkStamina(30d), Is.True);
            Assert.That(state.Export().nourishment, Is.LessThan(fed));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(3));
            Assert.That(state.NpcMealDue, Is.True);
            Assert.That(state.TryEatBread(), Is.True);

            state = VillageState.Restore(state.Export());
            Assert.That(state.NpcMealDue, Is.False);
            AdvanceTo(10 * 60 + 1);
            Assert.That(state.NpcMealDue, Is.False);
            AdvanceTo(12 * 60 + 1);
            Assert.That(state.NpcMealDue, Is.True);
            Assert.That(state.TryEatBread(), Is.True);
            Assert.That(state.NpcMealDue, Is.False);
            AdvanceTo(18 * 60 + 1);
            Assert.That(state.NpcMealDue, Is.True);
            Assert.That(state.TryEatBread(), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
            Assert.That(state.NpcMealDue, Is.False);

            void AdvanceTo(int minute) => state.AdvanceTime((minute - state.Minute) / state.Rules.MinutesPerSecond);
        }

        [Test]
        public void GritRaisesTheHungryFloorAndDigestionClearsFullnessFaster()
        {
            var save = new VillageState().Export();
            save.level = 3;
            save.totalLevel = 3;
            save.attributePoints = 2;
            save.nourishment = 0d;
            save.fullness = 60d;
            var character = VillageState.Restore(save);
            int hungryFloor = character.StaminaLimit;
            Assert.That(character.TrySpendAttribute(LifeAttribute.Grit), Is.True);
            Assert.That(character.StaminaLimit, Is.EqualTo(hungryFloor + 1));
            Assert.That(character.TrySpendAttribute(LifeAttribute.Digestion), Is.True);
            character = VillageState.Restore(character.Export());
            Assert.That(character.Grit, Is.EqualTo(1));
            Assert.That(character.Digestion, Is.EqualTo(1));
            var peer = VillageState.Restore(save);
            character.AdvanceTime(3f * 60f / character.Rules.MinutesPerSecond);
            peer.AdvanceTime(3f * 60f / peer.Rules.MinutesPerSecond);
            Assert.That(character.StaminaLimit, Is.EqualTo(character.MinimumStaminaLimit));
            Assert.That(character.FullnessFill, Is.LessThan(peer.FullnessFill));

            save.schema = 18;
            var migrated = VillageState.Restore(save);
            Assert.That(migrated.Fullness, Is.Zero);
            Assert.That(migrated.StaminaLimit, Is.EqualTo(migrated.WellFedStaminaLimit));
        }

        [Test]
        public void WorldContributionsBecomeAnUpgradeOnEraTransition()
        {
            var state = Funded();
            state.Inventory.TryAdd(ItemIds.Potato, 20);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(state.TryDonateGold(), Is.True);
                Assert.That(state.TryDonatePotatoes(), Is.True);
            }
            Assert.That(state.FarmUpgradePending, Is.True);
            Assert.That(state.FarmLevel, Is.EqualTo(1));
            Assert.That(state.TryAdvanceEra(), Is.True);
            Assert.That(state.FarmLevel, Is.EqualTo(2));
            Assert.That(state.FarmUpgradePending, Is.False);
            Assert.That(state.TryDonateGold(), Is.False);
        }

        [Test]
        public void RebirthResetsPropertyButPreservesSkillsApCareerAndTheWorld()
        {
            var state = Funded();
            state.TryBuy(ItemIds.Bag);
            state.TryBuy(ItemIds.Hoe);
            state.Inventory.TryAdd(ItemIds.Potato, 10);
            state.TryDonatePotatoes();
            state.TryDonateGold();
            state.TryUpgradeSkill();
            int rank = state.SkillRank;
            int ap = state.AP;
            long total = state.TotalLevel;
            for (int day = 0; day <= state.Rules.RebirthDays; day++)
                state.TrySleep(false);
            Assert.That(state.TryRebirth(), Is.True);
            Assert.That(state.Level, Is.EqualTo(1));
            Assert.That(state.Experience, Is.Zero);
            Assert.That(state.Gold, Is.Zero);
            Assert.That(state.Inventory.Entries.Count, Is.Zero);
            Assert.That(state.Inventory.Rows, Is.EqualTo(4));
            Assert.That(state.BagExpanded, Is.False);
            Assert.That(state.SkillRank, Is.EqualTo(rank));
            Assert.That(state.AP, Is.EqualTo(ap));
            Assert.That(state.Career, Is.EqualTo(4));
            Assert.That(state.TotalLevel, Is.EqualTo(total));
            Assert.That(state.DonatedGold, Is.EqualTo(25));
            Assert.That(state.DonatedPotatoes, Is.EqualTo(5));
        }

        [Test]
        public void RebirthRequiresTheConfiguredLifeAge()
        {
            var state = new VillageState();
            Assert.That(state.TryRebirth(), Is.False);
            Assert.That(state.Life, Is.EqualTo(1));
            Assert.That(state.Day, Is.EqualTo(1));
        }

        [Test]
        public void SleepRestoresEnergyAndOpensAnotherDaysJob()
        {
            var state = Funded();
            state.TryAcceptJob();
            Assert.That(state.TrySleep(true), Is.True);
            Assert.That(state.Energy, Is.EqualTo(state.StaminaLimit));
            Assert.That(state.Gold, Is.EqualTo(490));
            Assert.That(state.Minute, Is.EqualTo(360));
            Assert.That(state.Job, Is.EqualTo(JobStatus.Failed));
            Assert.That(state.TryAcceptJob(), Is.False);
            Assert.That(state.TryWaitForWork(), Is.True);
            Assert.That(state.TryAcceptJob(), Is.True);
        }

        [Test]
        public void ExhaustionDoesNotCollapseTheCharacterOrAdvanceTheDay()
        {
            var state = new VillageState(new VillageRules { HarvestExperience = 0 });
            for (int i = 0; i < 20; i++) state.TryHarvest(2, false, out _);
            Assert.That(state.TryHarvest(2, false, out _), Is.False);
            Assert.That(state.SpendWorkStamina(state.Stamina), Is.True);
            Assert.That(state.WillAdvanceDay(0f, paused: false), Is.False);
            Assert.That(state.AdvanceTime(0f), Is.False);
            Assert.That(state.RecoverInSharedWorld(), Is.False);
            Assert.That(state.Day, Is.EqualTo(1));
            Assert.That(state.Energy, Is.Zero);
            Assert.That(state.LastRestWasCollapse, Is.False);
        }

        [Test]
        public void RunningOutDuringWorkFailsButPayingExactlyAtCompletionSucceeds()
        {
            var worker = new VillageState();
            Assert.That(worker.SpendWorkStamina(98d), Is.True);
            Assert.That(worker.SpendWorkProgress(4, 0d, .25d), Is.True);
            Assert.That(worker.Stamina, Is.EqualTo(1d));
            Assert.That(worker.SpendWorkProgress(4, .25d, .5d), Is.False);
            Assert.That(worker.Stamina, Is.Zero);
            worker.RecoverStamina(3f, sitting: false);
            Assert.That(worker.Stamina, Is.EqualTo(4d));
            Assert.That(worker.SpendWorkProgress(4, 0d, .75d), Is.True);
            Assert.That(worker.SpendWorkProgress(4, .75d, 1d), Is.True);
            Assert.That(worker.Stamina, Is.Zero);
        }

        [Test]
        public void RecoveryDelayAndFractionSurviveSavingAndRestMasterOnlyAffectsSitting()
        {
            var worker = new VillageState();
            Assert.That(worker.SpendWorkStamina(90.25d), Is.True);
            worker.RecoverStamina(.5f, sitting: true);
            Assert.That(worker.Stamina, Is.EqualTo(9.75d));
            worker = VillageState.Restore(worker.Export());
            Assert.That(worker.Stamina, Is.EqualTo(9.75d));
            worker.RecoverStamina(.5f, sitting: false);
            Assert.That(worker.Stamina, Is.EqualTo(9.75d));
            worker.RecoverStamina(.5f, sitting: false);
            Assert.That(worker.Stamina, Is.EqualTo(10.75d));
            worker.RecoverStamina(.5f, sitting: true);
            Assert.That(worker.Stamina, Is.EqualTo(13.25d));

            Assert.That(worker.Inventory.TryAdd(ItemIds.RestMasterBook, 1), Is.True);
            Assert.That(worker.TryReadSkillBook(worker.Inventory.Entries.Single().Id), Is.True);
            worker.RecoverStamina(.5f, sitting: true);
            Assert.That(worker.Stamina, Is.EqualTo(16.25d));
            Assert.That(worker.LearnSkill(SkillId.RestMaster, 10), Is.True);
            worker.RecoverStamina(.5f, sitting: false);
            Assert.That(worker.Stamina, Is.EqualTo(17.25d));
            worker.RecoverStamina(.5f, sitting: true);
            Assert.That(worker.Stamina, Is.EqualTo(20.5d));

            Assert.That(worker.SpendWorkStamina(.5d), Is.True);
            worker.RecoverStamina(.5f, sitting: true);
            Assert.That(worker.Stamina, Is.EqualTo(20d));
            worker.RecoverStamina(60f, sitting: true);
            Assert.That(worker.Stamina, Is.EqualTo(worker.StaminaLimit));
        }

        [Test]
        public void SaveRoundTripPreservesProgressAndInventory()
        {
            var state = Funded();
            state.TryBuy(ItemIds.Bag);
            state.TryAcceptJob();
            state.Inventory.TryAdd(ItemIds.Potato, 10);
            state.TryHarvest(2, false, out _);
            var restored = VillageState.Restore(state.Export());
            Assert.That(restored.Gold, Is.EqualTo(state.Gold));
            Assert.That(restored.SkillRank, Is.EqualTo(state.SkillRank));
            Assert.That(restored.Job, Is.EqualTo(JobStatus.Active));
            Assert.That(restored.Inventory.Count(ItemIds.Potato), Is.EqualTo(10));
            Assert.That(restored.Inventory.Rows, Is.EqualTo(6));
            Assert.That(restored.Energy, Is.EqualTo(state.Energy));
        }

        [Test]
        public void CorruptSaveIsRejectedRatherThanGrantingInvalidState()
        {
            var save = new VillageSave { gold = -100 };
            Assert.Throws<ArgumentException>(() => VillageState.Restore(save));
            save = new VillageSave { minute = float.NaN };
            Assert.Throws<ArgumentException>(() => VillageState.Restore(save));
            save = new VillageSave { schema = 99 };
            Assert.Throws<ArgumentException>(() => VillageState.Restore(save));
        }
    }
}
