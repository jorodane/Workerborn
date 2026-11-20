using System;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class VillageStateTests
    {
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
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, 620), Is.True);
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
            save.inventoryRows = 6;
            save.inventory = inventory.Export();
            var state = VillageState.Restore(save);
            Assert.That(state.Equipment[EquipmentSlot.Bag], Is.EqualTo(ItemIds.Bag));
            Assert.That(state.Inventory.Count(ItemIds.Bag), Is.Zero);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.EqualTo(31));
            Assert.That(state.Inventory.Rows, Is.EqualTo(6));
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
        public void FirstJobQuestAndLevelThreeFormAPlayableRebirthPath()
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
            Assert.That(state.CanRebirth, Is.True);
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
        public void AnimalDamageUsesStaminaAndFollowsTheExistingCollapseFlow()
        {
            var state = new VillageState();
            Assert.That(state.TakeHit(7), Is.EqualTo(7));
            Assert.That(state.Energy, Is.EqualTo(VillageState.MaxEnergy - 7));
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
            state.TryAcceptJob();
            state.Inventory.TryAdd(ItemIds.Potato, 12);
            Assert.That(state.TryDeliverJob(), Is.True);
            Assert.That(state.TryDeliverJob(), Is.False);
            Assert.That(state.TryAcceptJob(), Is.False);
            Assert.That(state.TryClaimIntroQuest(), Is.True);
            Assert.That(state.TryClaimIntroQuest(), Is.False);
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
            state.TryRebirth();
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
        public void FailedPurchaseDoesNotChargeGoldOrGrantTheItem()
        {
            var state = new VillageState();
            Assert.That(state.TryBuy(ItemIds.Bread), Is.False);
            Assert.That(state.Gold, Is.Zero);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
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
            state.TryBuy(ItemIds.Bread);
            Assert.That(state.TryEatBread(), Is.False);
            state.TryHarvest(2, false, out _);
            Assert.That(state.TryEatBread(), Is.True);
            Assert.That(state.Energy, Is.EqualTo(100));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
        }

        [Test]
        public void WorldContributionsBecomeAnUpgradeOnlyOnRebirth()
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
            Assert.That(state.TryRebirth(), Is.True);
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
            int total = state.TotalLevel;
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
        public void RebirthCannotBypassTheIntroductoryQuest()
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
            Assert.That(state.Energy, Is.EqualTo(100));
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
            var state = new VillageState();
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
