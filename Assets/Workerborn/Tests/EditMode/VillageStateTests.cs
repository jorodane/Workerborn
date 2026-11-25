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
            int energy = state.Energy;
            int requested = 0;
            Assert.That(state.TryGather(SkillId.Harvesting, ItemIds.Potato, 1, 5, WorkOutcome.Success,
                quantity => { requested = quantity; return false; }, out int harvested), Is.False);
            Assert.That(requested, Is.EqualTo(1));
            Assert.That(harvested, Is.Zero);
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
            Assert.That(state.TryHarvest(2, false, out int drops), Is.True);
            Assert.That(drops, Is.EqualTo(2));
            Assert.That(state.Energy, Is.EqualTo(95));
            Assert.That(state.Experience, Is.EqualTo(8));
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.Zero);
        }

        [Test]
        public void LuckyHarvestMultipliesTheDropCount()
        {
            var state = new VillageState();
            state.TryHarvest(2, true, out int drops);
            Assert.That(drops, Is.EqualTo(4));
            Assert.That(state.LifetimeHarvests, Is.EqualTo(1));
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
            Assert.That(state.PendingPurchase.Quantity, Is.EqualTo(amount));
            Assert.That(state.TryStowPurchase(), Is.False);
            Assert.That(state.TryPlacePurchase(0, 0), Is.False);
            bag = state.Inventory;
            var existing = bag.Entries[1];
            Assert.That(state.TryPlacePurchase(existing.X, existing.Y), Is.True);
            Assert.That(existing.Quantity, Is.EqualTo(bread.MaxStack));
            Assert.That(state.PendingPurchase.Quantity, Is.EqualTo(amount - 1));
            var potato = bag.Entries[2];
            Assert.That(bag.TryRemoveAt(potato.Id, potato.Quantity), Is.True);
            Assert.That(state.TryPlacePurchase(potato.X, potato.Y), Is.True);
            Assert.That(bag.Count(bread.Id), Is.EqualTo(bread.MaxStack * 2));
            Assert.That(state.PendingPurchase.Quantity, Is.EqualTo(1));
            Assert.That(state.TrySellStack(0, 1), Is.True);
            Assert.That(state.PendingPurchase, Is.Null);
        }

        [Test]
        public void CursorPurchaseCanBeEquippedAndCannotFollowAPlayerIntoRebirth()
        {
            var state = Funded();
            Assert.That(state.TryBuyForPlacement(ItemIds.FishingRod, 1), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FishingRod), Is.Zero);
            Assert.That(state.TryEquip(0), Is.True);
            Assert.That(state.PendingPurchase, Is.Null);
            Assert.That(state.Equipment[EquipmentSlot.MainHand], Is.EqualTo(ItemIds.FishingRod));

            Assert.That(state.TryBuyForPlacement(ItemIds.Bread, 2), Is.True);
            state.RebirthPersonalLife();
            Assert.That(state.PendingPurchase, Is.Null);
            Assert.That(state.Export().pendingPurchase, Is.Null);
            var save = state.Export();
            save.pendingPurchase = new PendingPurchaseSave { itemId = ItemIds.Bread, quantity = 2 };
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
        public void FullEnergyDoesNotWasteFoodAndEatingClampsAtMaximum()
        {
            var state = Funded();
            state.TrySleep(true);
            state.TryBuy(ItemIds.Bread);
            Assert.That(state.TryEatBread(), Is.False);
            state.TryHarvest(2, false, out _);
            Assert.That(state.TryEatBread(), Is.True);
            Assert.That(state.Energy, Is.EqualTo(state.StaminaLimit));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
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
        public void ExhaustionForcesRestWithTheCollapsePenalty()
        {
            var state = new VillageState(new VillageRules { HarvestExperience = 0 });
            for (int i = 0; i < 20; i++) state.TryHarvest(2, false, out _);
            Assert.That(state.TryHarvest(2, false, out _), Is.False);
            Assert.That(state.AdvanceTime(0f), Is.True);
            Assert.That(state.Day, Is.EqualTo(2));
            Assert.That(state.Energy, Is.EqualTo(45));
            Assert.That(state.LastRestWasCollapse, Is.True);
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
