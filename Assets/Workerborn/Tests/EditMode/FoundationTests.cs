using System.Linq;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class FoundationTests
    {
        private static VillageRules Rules() => new VillageRules
        {
            Foundation = new FarmFoundationRules
            {
                Title = "첫 농장",
                TimberItemId = ItemIds.Timber,
                TimberCount = 6,
                DocumentItemId = ItemIds.TimberContract,
                RewardAP = 2,
                RewardExperience = 12
            },
            JobDocumentItemId = ItemIds.FarmContract
        };

        [Test]
        public void TwoLivesConnectBareHandEconomyTimberAndFarmContracts()
        {
            var rules = Rules();
            var state = new VillageState(rules);
            Assert.That(state.NeedsGuide, Is.True);
            Assert.That(state.Gold, Is.Zero);
            Assert.That(state.Equipment, Is.Empty);
            Assert.That(state.TryAcceptJob(), Is.False);
            Assert.That(state.TryChooseFarmSite("south", "남쪽 밭터"), Is.False);
            state.FinishGuideVisit();

            Assert.That(state.TryAcceptFoundation(), Is.True);
            Assert.That(state.TryAcceptFoundation(), Is.False);
            Assert.That(state.Inventory.Count(ItemIds.TimberContract), Is.EqualTo(1));
            Assert.That(state.TryGetDocumentPage(ItemIds.TimberContract, out var page), Is.True);
            Assert.That(page, Is.EqualTo(VillageQuestPage.TimberContract));
            Assert.That(state.TrySellItems(ItemIds.TimberContract, 1), Is.False);
            Assert.That(state.TryBuy(ItemIds.Axe), Is.False);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(state.TryGather(ItemIds.Egg, 1, 2, WorkOutcome.Success, out int eggs), Is.True);
                Assert.That(state.Inventory.TryAdd(ItemIds.Egg, eggs), Is.True);
            }
            Assert.That(state.TrySellItems(ItemIds.Egg, 3), Is.True);
            Assert.That(state.Gold, Is.EqualTo(3 * ItemCatalog.Get(ItemIds.Egg).Price));
            Assert.That(state.TryBuy(ItemIds.Axe), Is.True);
            Assert.That(state.HasTool(ItemIds.Axe), Is.False);
            Assert.That(state.TryEquip(state.Inventory.Entries.Single(e => e.ItemId == ItemIds.Axe).Id), Is.True);
            Assert.That(state.HasTool(ItemIds.Axe), Is.True);
            Assert.That(state.LearnSkill(SkillId.LuckyBonus), Is.True);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(state.TryGather(ItemIds.Timber, 1, 4, WorkOutcome.Lucky, out int timber), Is.True);
                Assert.That(state.Inventory.TryAdd(ItemIds.Timber, timber), Is.True);
            }
            Assert.That(state.TryDeliverTimber(), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.TimberContract), Is.Zero);
            Assert.That(state.TimberContractActive, Is.False);
            Assert.That(state.TryDeliverTimber(), Is.False);
            Assert.That(state.TryRebirth(), Is.False);
            Assert.That(state.TryChooseFarmSite("south", "남쪽 밭터"), Is.True);
            Assert.That(state.TryUpgradeSkill(SkillId.Woodcutting), Is.True);
            state = VillageState.Restore(state.Export(), rules);
            Assert.That(state.CanRebirth, Is.False);
            Assert.That(state.CanAdvanceEra, Is.True);
            Assert.That(state.FarmAvailable, Is.False);
            Assert.That(state.TryAdvanceEra(), Is.True);
            Assert.That(state.Life, Is.EqualTo(2));
            Assert.That(state.NeedsGuide, Is.True);
            Assert.That(state.FarmAvailable, Is.True);
            Assert.That(state.FarmSiteId, Is.EqualTo("south"));
            Assert.That(state.Level, Is.EqualTo(1));
            Assert.That(state.RankOf(SkillId.Woodcutting), Is.EqualTo(1));
            Assert.That(state.Equipment, Is.Empty);
            Assert.That(state.Inventory.Entries, Is.Empty);

            state.FinishGuideVisit();
            Assert.That(state.TryAcceptJob(), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.EqualTo(1));
            int retail = ItemCatalog.Get(rules.JobItemId).Price * state.JobRequirement;
            Assert.That(state.JobReward, Is.GreaterThan(retail));
            state.Inventory.TryAdd(rules.JobItemId, state.JobRequirement);
            Assert.That(state.TryDeliverJob(), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.Zero);
            Assert.That(state.Career, Is.EqualTo(1));
        }

        [Test]
        public void JobDocumentRefreshesAtTheNextAcceptanceAndItemPaymentIsAtomic()
        {
            var rules = Rules();
            rules.JobTiers[0].RewardItemId = ItemIds.Bread;
            rules.JobTiers[0].RewardItemCount = 1;
            var state = VillageState.Restore(new VillageSave { schema = 2 }, rules);
            Assert.That(state.FarmAvailable, Is.True);
            Assert.That(state.NeedsGuide, Is.False);
            Assert.That(state.TryAcceptJob(), Is.True);
            state.AdvanceTime((rules.JobEnd - rules.JobStart) / rules.MinutesPerSecond);
            Assert.That(state.Job, Is.EqualTo(JobStatus.Failed));
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.Zero);
            Assert.That(state.TryDeliverJob(), Is.False);
            state.TrySleep(false);
            state.TryWaitForWork();
            Assert.That(state.TryAcceptJob(), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.EqualTo(1));
            Assert.That(state.Inventory.TryAdd(rules.JobItemId, state.JobRequirement + 1), Is.True);
            int freeCells = state.Inventory.Columns * state.Inventory.Rows - state.Inventory.Entries.Count;
            Assert.That(state.Inventory.TryAdd(ItemIds.Egg, ItemCatalog.Get(ItemIds.Egg).MaxStack * freeCells), Is.True);
            state = VillageState.Restore(state.Export(), rules);
            Assert.That(state.TryDeliverJob(), Is.False);
            Assert.That(state.Inventory.Count(rules.JobItemId), Is.EqualTo(state.JobRequirement + 1));
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.EqualTo(1));
            Assert.That(state.Gold, Is.Zero);
            Assert.That(state.Career, Is.Zero);
            state.Inventory.TryRemove(ItemIds.Egg, ItemCatalog.Get(ItemIds.Egg).MaxStack);
            Assert.That(state.TryDeliverJob(), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.Zero);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(1));
        }

        [Test]
        public void CompletedContractsAreRemovedFromExistingSavesAndOtherPlayers()
        {
            var rules = Rules();
            var worker = new VillageState(rules);
            Assert.That(worker.TryAcceptFoundation(), Is.True);
            var other = VillageState.Restore(worker.Export(), rules);
            Assert.That(other.Inventory.Count(ItemIds.TimberContract), Is.EqualTo(1));
            worker.Inventory.TryAdd(ItemIds.Timber, rules.Foundation.TimberCount - 1);
            Assert.That(worker.TryDeliverTimber(), Is.True);
            Assert.That(worker.Inventory.Count(ItemIds.TimberContract), Is.EqualTo(1));
            worker.Inventory.TryAdd(ItemIds.Timber, 1);
            Assert.That(worker.TryDeliverTimber(), Is.True);
            other.ApplyWorld(worker.ExportWorld());
            Assert.That(other.Inventory.Count(ItemIds.TimberContract), Is.Zero);

            var built = VillageState.Restore(new VillageSave { schema = 2 }, rules);
            Assert.That(built.TryAcceptJob(), Is.True);
            built.Inventory.TryAdd(rules.JobItemId, built.JobRequirement);
            Assert.That(built.TryDeliverJob(), Is.True);
            built.Inventory.TryAdd(ItemIds.TimberContract, 1);
            built.Inventory.TryAdd(ItemIds.FarmContract, 1);
            var loaded = VillageState.Restore(built.Export(), rules);
            Assert.That(loaded.Job, Is.EqualTo(JobStatus.Completed));
            Assert.That(loaded.Inventory.Count(ItemIds.TimberContract), Is.Zero);
            Assert.That(loaded.Inventory.Count(ItemIds.FarmContract), Is.Zero);
        }

        [Test]
        public void FailureSpendsStaminaAndGreatLuckScalesTheStack()
        {
            var state = new VillageState();
            Assert.That(state.TryGather(ItemIds.Egg, 1, 2, WorkOutcome.Failure, out int failed), Is.False);
            Assert.That(failed, Is.Zero);
            Assert.That(state.Energy, Is.EqualTo(98));
            Assert.That(state.LifetimeHarvests, Is.Zero);

            var expert = VillageState.Restore(new VillageSave { skillRank = 8 });
            Assert.That(expert.LearnSkill(SkillId.LuckyBonus, 11), Is.True);
            Assert.That(expert.TryGather(ItemIds.Timber, 1, 4, WorkOutcome.GreatLucky, out int timber), Is.True);
            Assert.That(timber, Is.EqualTo(1850));
            Assert.That(expert.Inventory.TryAdd(ItemIds.Timber, timber), Is.True);
            Assert.That(expert.Inventory.Entries.Count, Is.EqualTo(2));
        }
    }
}
