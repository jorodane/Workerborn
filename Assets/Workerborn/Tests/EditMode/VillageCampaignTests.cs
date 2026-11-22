using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class VillageCampaignTests
    {
        private static VillageRules Rules() => new VillageRules
        {
            MinutesPerSecond = 1,
            Foundation = new FarmFoundationRules
            {
                TimberItemId = ItemIds.Timber,
                DocumentItemId = ItemIds.TimberContract,
                TimberCount = 6,
                RewardAP = 2,
                RewardExperience = 36
            }
        };

        [Test]
        public void APortableCharacterKeepsSeparateProgressInEachWorld()
        {
            const string identity = "local:character";
            var look = new CharacterLook { body = 1, hair = 7, outfit = 4, face = 2 };
            var rules = Rules();
            var first = new VillageCampaign(rules, Sites, 10, 22);
            Assert.That(first.Connect(identity, "첫 이름", out var profile, look), Is.True);
            Assert.That(first.Avatars()[0].look.hair, Is.EqualTo(7));
            var player = VillageState.Restore(profile.character, rules);
            player.Inventory.TryAdd(ItemIds.Bread, 3);
            var world = player.ExportWorld();
            world.AddMinutes(1440);
            first.StoreSolo(identity, world, player.Export());

            var loaded = new VillageCampaign(rules, Sites, 10, 22, first.Export());
            Assert.That(loaded.WorldId, Is.EqualTo(first.WorldId));
            Assert.That(loaded.Connect(identity, "새 이름", out var saved, look), Is.True);
            Assert.That(saved.name, Is.EqualTo("새 이름"));
            Assert.That(loaded.World.Clock, Is.EqualTo(world.Clock));
            Assert.That(VillageState.Restore(saved.character, rules).Inventory.Count(ItemIds.Bread), Is.EqualTo(3));

            var fresh = new VillageCampaign(rules, Sites, 10, 22);
            Assert.That(fresh.WorldId, Is.Not.EqualTo(first.WorldId));
            Assert.That(fresh.Connect(identity, "새 이름", out var newcomer, look), Is.True);
            Assert.That(fresh.World.Clock, Is.LessThan(loaded.World.Clock));
            Assert.That(VillageState.Restore(newcomer.character, rules).Inventory.Count(ItemIds.Bread), Is.Zero);
        }

        [Test]
        public void CharacterSlotsArePersistentAndIndependentOfTheSimultaneousConnectionLimit()
        {
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 10, 22, initialCharacterLimit: 13);
            for (int i = 0; i < 12; i++)
                Assert.That(campaign.Connect($"player-{i}", "일꾼", out _), Is.True);
            Assert.That(campaign.Connect("player-12", "일꾼", out _), Is.False);
            Assert.That(campaign.CreatedCharacters, Is.EqualTo(12));

            campaign.Disconnect("player-0");
            Assert.That(campaign.Connect("player-12", "일꾼", out _), Is.True);
            campaign.Disconnect("player-1");
            Assert.That(campaign.Connect("player-13", "일꾼", out _), Is.False);
            Assert.That(campaign.Connect("player-0", "일꾼", out _), Is.True);

            var restored = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            Assert.That(restored.Connect("player-0", "일꾼", out _), Is.True);
            Assert.That(restored.Connect("player-13", "일꾼", out _), Is.False);
            var status = restored.Status("player-0", false);
            Assert.That(status.connected, Is.EqualTo(1));
            Assert.That(status.characterLimit, Is.EqualTo(13));
            Assert.That(status.createdCharacters, Is.EqualTo(13));
            Assert.That(status.requirementCharacters, Is.EqualTo(13));
        }

        [Test]
        public void RaisingCharacterLimitOpensSlotsNowAndChangesTheNextEraRequirements()
        {
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 10, 22, initialCharacterLimit: 1);
            Assert.That(campaign.Connect("a", "하나", out _), Is.True);
            Assert.That(campaign.Connect("b", "둘", out _), Is.False);
            Assert.That(campaign.TrySetCharacterLimit(3, out _), Is.True);
            Assert.That(campaign.Connect("b", "둘", out _), Is.True);

            campaign = new VillageCampaign(rules, Sites, 10, 22, campaign.Export(), initialCharacterLimit: 4);
            Assert.That(campaign.Connect("a", "하나", out var player), Is.True);
            Assert.That(campaign.CharacterLimit, Is.EqualTo(3));
            Assert.That(VillageState.Restore(player.character, rules).TimberRequired, Is.EqualTo(6));
            Assert.That(campaign.Perform("a", CampaignAction.AcceptFoundation, null, 1, true, out _), Is.True);
            StockResources(campaign, rules, ItemIds.Timber, 6);
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.SelectFarmSite, "east", 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.VoteForEra, null, 1, true, out _), Is.True);
            Assert.That(campaign.Tick(0), Is.True);
            Assert.That(campaign.World.era, Is.EqualTo(2));
            Assert.That(campaign.World.requirementCharacters, Is.EqualTo(3));
            var state = VillageState.Restore(campaign.Profile("a").character, rules);
            Assert.That(state.ExpansionGoldRequired, Is.EqualTo(3 * rules.DonationGold));
            Assert.That(state.ExpansionItemsRequired, Is.EqualTo(3 * rules.DonationItems));
        }

        [Test]
        public void ReducingCharacterLimitPreservesOverTargetTimberAndExistingProfiles()
        {
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 10, 22, initialCharacterLimit: 4);
            Assert.That(campaign.Connect("a", "하나", out var player), Is.True);
            Assert.That(VillageState.Restore(player.character, rules).TimberRequired, Is.EqualTo(24));
            Assert.That(campaign.Connect("b", "둘", out _), Is.True);
            campaign.Disconnect("b");
            Assert.That(campaign.Perform("a", CampaignAction.AcceptFoundation, null, 1, true, out _), Is.True);
            StockResources(campaign, rules, ItemIds.Timber, 14);
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.True);

            Assert.That(campaign.TrySetCharacterLimit(0, out _), Is.False);
            Assert.That(campaign.TrySetCharacterLimit(WorldCapacity.MaxCharacters + 1, out _), Is.False);
            Assert.That(campaign.TrySetCharacterLimit(1, out _), Is.False);
            Assert.That(campaign.TrySetCharacterLimit(2, out _), Is.True);
            Assert.That(campaign.World.timberDelivered, Is.EqualTo(14));
            Assert.That(campaign.World.foundationStage, Is.EqualTo((int)FarmFoundationStage.CollectingTimber));

            campaign = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            Assert.That(campaign.Connect("a", "하나", out player), Is.True);
            Assert.That(campaign.HasIdentity("b"), Is.True);
            var state = VillageState.Restore(player.character, rules);
            Assert.That(state.TimberRequired, Is.EqualTo(12));
            Assert.That(state.CanConfirmTimberDelivery, Is.True);
            Assert.That(state.Inventory.Count(ItemIds.Timber), Is.Zero);
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.True);
            state = VillageState.Restore(campaign.Profile("a").character, rules);
            Assert.That(state.TimberDelivered, Is.EqualTo(14));
            Assert.That(state.FoundationStage, Is.EqualTo(FarmFoundationStage.SelectingSite));
            Assert.That(state.Inventory.Count(ItemIds.TimberContract), Is.Zero);
            Assert.That(state.AP, Is.EqualTo(rules.Foundation.RewardAP + (state.Level - 1) * rules.APPerLevel));
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.False);
        }

        [Test]
        public void ReducingCharacterLimitPreservesTheFarmAndBothExpansionContributions()
        {
            var rules = Rules();
            rules.DonationGold = 20;
            rules.DonationGoldStep = 30;
            rules.DonationItems = 3;
            rules.DonationItemStep = 6;
            rules.ExpansionRewardGold = 7;
            var campaign = new VillageCampaign(rules, Sites, 10, 22, initialCharacterLimit: 3);
            Assert.That(campaign.Connect("a", "하나", out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.AcceptFoundation, null, 1, true, out _), Is.True);
            StockResources(campaign, rules, ItemIds.Timber, 18);
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.SelectFarmSite, "east", 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.VoteForEra, null, 1, true, out _), Is.True);
            Assert.That(campaign.Tick(0), Is.True);
            StockResources(campaign, rules, ItemIds.Gold, 30);
            StockResources(campaign, rules, ItemIds.Potato, 6);
            Assert.That(campaign.Perform("a", CampaignAction.DonateGold, null, 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.DonateItems, null, 1, true, out _), Is.True);
            Assert.That(campaign.World.expansionReady, Is.False);

            Assert.That(campaign.TrySetCharacterLimit(1, out _), Is.True);
            campaign = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            Assert.That(campaign.Connect("a", "하나", out var player), Is.True);
            var state = VillageState.Restore(player.character, rules);
            Assert.That(state.Era, Is.EqualTo(2));
            Assert.That(state.FarmSiteId, Is.EqualTo("east"));
            Assert.That(state.FarmLevel, Is.EqualTo(1));
            Assert.That(state.CanConfirmExpansion, Is.True);
            Assert.That(state.FarmUpgradePending, Is.False);
            Assert.That(state.Gold, Is.Zero);
            Assert.That(campaign.Perform("a", CampaignAction.DonateGold, null, 1, true, out _), Is.True);
            state = VillageState.Restore(campaign.Profile("a").character, rules);
            Assert.That(state.DonatedGold, Is.EqualTo(30));
            Assert.That(state.DonatedPotatoes, Is.EqualTo(6));
            Assert.That(state.FarmUpgradePending, Is.True);
            Assert.That(state.FarmLevel, Is.EqualTo(1));
            Assert.That(state.Gold, Is.EqualTo(7));
        }

        [Test]
        public void SavesWithoutACharacterLimitKeepTheirExistingQuestBalance()
        {
            var campaign = ReadyCampaign(out var rules);
            var record = campaign.Export();
            record.world.characterLimit = 0;
            record.world.requirementCharacters = 0;
            var restored = new VillageCampaign(rules, Sites, 10, 22, record);
            Assert.That(restored.Connect("a", "하나", out var player), Is.True);
            Assert.That(restored.CharacterLimit, Is.EqualTo(WorldCapacity.MaxCharacters));
            var state = VillageState.Restore(player.character, rules);
            Assert.That(state.TimberRequired, Is.EqualTo(6));
            Assert.That(state.CanAdvanceEra, Is.True);
        }

        [Test]
        public void ACharacterBanPersistsAndRejectsAReplacementToken()
        {
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 10, 22, initialCharacterLimit: 3);
            const string appearance = "7ccc4dfe484b48d592cff22787bba284";
            Assert.That(campaign.Connect("first-token", "하나", out var player, portableId: appearance), Is.True);
            Assert.That(player.createdUtc, Is.GreaterThan(0));
            Assert.That(player.lastConnectedUtc, Is.GreaterThanOrEqualTo(player.createdUtc));
            Assert.That(campaign.TrySetBan(player.id, true, () => true, out var identity), Is.True);
            Assert.That(identity, Is.EqualTo("first-token"));
            Assert.That(player.lastDisconnectedUtc, Is.GreaterThanOrEqualTo(player.lastConnectedUtc));
            Assert.That(campaign.ConnectedCount, Is.Zero);
            Assert.That(campaign.CreatedCharacters, Is.EqualTo(1));

            campaign = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            Assert.That(campaign.Connect("first-token", "하나", out _), Is.False);
            Assert.That(campaign.Connect("replacement-token", "하나", out _, portableId: appearance), Is.False);
            Assert.That(campaign.MatchesPortableCharacter("first-token", appearance), Is.True);
            Assert.That(campaign.MatchesPortableCharacter("first-token", "different-character"), Is.False);
            Assert.That(campaign.HasPortableCharacter(appearance), Is.True);
            Assert.That(campaign.TrySetBan(player.id, false, () => true, out _), Is.True);
            Assert.That(campaign.Connect("first-token", "하나", out var returning, portableId: appearance), Is.True);
            Assert.That(returning.id, Is.EqualTo(player.id));
            Assert.That(returning.createdUtc, Is.EqualTo(player.createdUtc));
        }

        [Test]
        public void CharacterDeletionReleasesTheSlotAndKeepsSharedProgress()
        {
            var campaign = ReadyCampaign(out var rules);
            Assert.That(campaign.Connect("b", "둘", out var second), Is.True);
            Assert.That(campaign.TrySetCharacterLimit(2, out _), Is.True);
            Assert.That(campaign.TrySetCharacterLimit(1, out _), Is.False);
            CampaignRecord saved = null;
            Assert.That(campaign.TryDeleteCharacter(second.id, () =>
            {
                saved = campaign.Export();
                return true;
            }, out var identity), Is.True);
            Assert.That(identity, Is.EqualTo("b"));
            Assert.That(campaign.ConnectedCount, Is.EqualTo(1));
            Assert.That(campaign.TrySetCharacterLimit(1, out _), Is.True);
            var restored = new VillageCampaign(rules, Sites, 10, 22, saved);
            Assert.That(restored.CreatedCharacters, Is.EqualTo(1));
            Assert.That(restored.HasIdentity("b"), Is.False);
            Assert.That(restored.World.timberDelivered, Is.EqualTo(6));
            Assert.That(restored.World.farmSiteId, Is.EqualTo("east"));
            Assert.That(restored.CanAdvance, Is.True);
        }

        [Test]
        public void FailedRosterWritesLeaveTheOriginalCharacterConnected()
        {
            var campaign = new VillageCampaign(Rules(), Sites, 10, 22, initialCharacterLimit: 1);
            Assert.That(campaign.Connect("a", "하나", out var player), Is.True);
            Assert.That(campaign.TrySetBan(player.id, true, () => false, out _), Is.False);
            Assert.That(player.banned, Is.False);
            Assert.That(player.lastDisconnectedUtc, Is.Zero);
            Assert.That(campaign.TryDeleteCharacter(player.id, () => false, out _), Is.False);
            Assert.That(campaign.IsConnected("a"), Is.True);
            Assert.That(campaign.Profile("a"), Is.SameAs(player));
            Assert.That(campaign.HasFreeCharacterSlot, Is.False);
        }

        [Test]
        public void OperatorRightsAreStoredForOneWorldAndRequireThatCharacterToBeConnected()
        {
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 10, 22);
            Assert.That(campaign.Connect("operator", "하나", out var character), Is.True);
            Assert.That(campaign.Connect("guest", "둘", out _), Is.True);
            Assert.That(campaign.IsOperator("operator"), Is.False);
            Assert.That(campaign.TrySetOperator(character, true, () => false), Is.False);
            Assert.That(campaign.IsOperator("operator"), Is.False);
            Assert.That(campaign.TrySetOperator(character, true, () => true), Is.True);
            Assert.That(campaign.IsOperator("operator"), Is.True);
            Assert.That(campaign.IsOperator("guest"), Is.False);
            campaign.Disconnect("operator");
            Assert.That(campaign.IsOperator("operator"), Is.False);

            var restored = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            Assert.That(restored.Connect("operator", "하나", out var restoredCharacter), Is.True);
            Assert.That(restored.IsOperator("operator"), Is.True);
            Assert.That(restored.TrySetOperator(restoredCharacter, false, () => false), Is.False);
            Assert.That(restored.IsOperator("operator"), Is.True);
            Assert.That(restored.TrySetOperator(restoredCharacter, false, () => true), Is.True);
            Assert.That(restored.IsOperator("operator"), Is.False);

            var otherWorld = new VillageCampaign(rules, Sites, 10, 22);
            Assert.That(otherWorld.Connect("operator", "하나", out _), Is.True);
            Assert.That(otherWorld.IsOperator("operator"), Is.False);
        }

        [Test]
        public void BanningAnOperatorSuspendsAccessAndDeletingTheRecordRemovesItsGrant()
        {
            var campaign = new VillageCampaign(Rules(), Sites, 10, 22);
            Assert.That(campaign.Connect("operator", "하나", out var character), Is.True);
            Assert.That(campaign.TrySetOperator(character, true, () => true), Is.True);
            Assert.That(campaign.TrySetBan(character.id, true, () => true, out _), Is.True);
            Assert.That(campaign.IsOperator("operator"), Is.False);
            Assert.That(campaign.Connect("operator", "하나", out _), Is.False);
            Assert.That(campaign.TrySetBan(character.id, false, () => true, out _), Is.True);
            Assert.That(campaign.Connect("operator", "하나", out _), Is.True);
            Assert.That(campaign.IsOperator("operator"), Is.True);
            Assert.That(campaign.TryDeleteCharacter(character.id, () => true, out _), Is.True);
            Assert.That(campaign.Connect("operator", "하나", out _), Is.True);
            Assert.That(campaign.IsOperator("operator"), Is.False);
        }

        [Test]
        public void PersonalRebirthUsesExactLifeAgeAndPreservesPartialDeliveries()
        {
            var rules = Rules();
            var player = new VillageState(rules);
            player.TryAcceptFoundation();
            player.Inventory.TryAdd(ItemIds.Timber, 4);
            Assert.That(player.TryDeliverTimber(), Is.True);
            Assert.That(player.TimberDelivered, Is.EqualTo(4));
            var world = player.ExportWorld();
            world.AddMinutes(7 * 1440 - 1);
            player.ApplyWorld(world);
            Assert.That(player.CanRebirth, Is.False);
            world.AddMinutes(1);
            player.ApplyWorld(world);
            Assert.That(player.TryRebirth(), Is.True);
            Assert.That(player.Era, Is.EqualTo(1));
            Assert.That(player.TimberDelivered, Is.EqualTo(4));
            Assert.That(player.Inventory.Entries, Is.Empty);
            Assert.That(player.TryAcceptFoundation(), Is.True);
            player.Inventory.TryAdd(ItemIds.Timber, 4);
            Assert.That(player.TryDeliverTimber(), Is.True);
            Assert.That(player.Inventory.Count(ItemIds.Timber), Is.EqualTo(2));
            Assert.That(player.AttributePoints, Is.EqualTo(1));
            Assert.That(player.CanAdvanceEra, Is.False);
            Assert.That(player.TryChooseFarmSite("east", "동쪽 밭터"), Is.True);
            Assert.That(player.TryAdvanceEra(), Is.True);
            Assert.That(player.Life, Is.EqualTo(3));
            Assert.That(player.Era, Is.EqualTo(2));
            Assert.That(player.AP, Is.GreaterThanOrEqualTo(rules.Foundation.RewardAP));
            Assert.That(player.AttributePoints, Is.Zero);
        }

        [Test]
        public void CappedLevelKeepsExperienceAndOnePointPerEarnedLevel()
        {
            var rules = Rules();
            var save = new VillageState(rules).Export();
            save.level = 199;
            save.totalLevel = 10000000000;
            save.experience = 2009;
            var player = VillageState.Restore(save, rules);
            Assert.That(player.TryHarvest(1, false, out _), Is.True);
            Assert.That(player.Level, Is.EqualTo(200));
            Assert.That(player.TotalLevel, Is.EqualTo(10000000001));
            Assert.That(player.AttributePoints, Is.EqualTo(1));
            Assert.That(player.TrySpendAttribute(LifeAttribute.Strength), Is.True);
            Assert.That(player.TrySpendAttribute(LifeAttribute.Knowledge), Is.False);
            Assert.That(player.Strength, Is.EqualTo(1));
            save = player.Export();
            save.experience = int.MaxValue;
            player = VillageState.Restore(save, rules);
            Assert.That(player.TryHarvest(1, false, out _), Is.True);
            Assert.That(player.Experience, Is.GreaterThan(int.MaxValue));
            Assert.That(player.TotalLevel, Is.EqualTo(10000000001));
            Assert.That(player.HealthLimit, Is.EqualTo(498));
            Assert.That(player.StaminaLimit, Is.EqualTo(299));
        }

        [Test]
        public void UnanimousConnectedVotesAdvanceAndOfflineProfilesKeepTheirCharacter()
        {
            var campaign = ReadyCampaign(out var rules);
            Assert.That(campaign.Connect("b", "둘", out _), Is.True);
            Assert.That(campaign.Connect("c", "셋", out _), Is.True);
            GiveItem(campaign, rules, "c");
            campaign.Disconnect("c");
            Assert.That(campaign.Perform("a", CampaignAction.VoteForEra, null, 1, true, out _), Is.True);
            Assert.That(campaign.Tick(1), Is.False);
            Assert.That(campaign.Perform("b", CampaignAction.VoteForEra, null, 1, false, out _), Is.True);
            Assert.That(campaign.Tick(1), Is.True);
            Assert.That(campaign.World.era, Is.EqualTo(2));
            Assert.That(campaign.Profile("a").character.life, Is.EqualTo(2));
            Assert.That(campaign.Profile("b").character.inventory, Is.Empty);
            var restored = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            Assert.That(restored.Connect("c", "셋", out var profile), Is.True);
            Assert.That(profile.character.playerX, Is.EqualTo(10));
            Assert.That(profile.character.playerZ, Is.EqualTo(22));
            Assert.That(profile.character.era, Is.EqualTo(2));
            Assert.That(profile.character.life, Is.EqualTo(1));
            Assert.That(VillageState.Restore(profile.character, rules).Inventory.Count(ItemIds.Bread), Is.EqualTo(1));
        }

        [Test]
        public void AnnouncedTransitionCanBeChangedAndOnlyResetsConnectedOptIns()
        {
            var campaign = ReadyCampaign(out var rules);
            Assert.That(campaign.Connect("b", "둘", out _), Is.True);
            Assert.That(campaign.Connect("c", "셋", out _), Is.True);
            GiveItem(campaign, rules, "a");
            GiveItem(campaign, rules, "b");
            GiveItem(campaign, rules, "c");
            Assert.That(campaign.Perform("b", CampaignAction.AnnounceEra, null, 1, false, out _), Is.False);
            Assert.That(campaign.Announce(CampaignAction.AnnounceEra, 0, out _), Is.False);
            Assert.That(campaign.Announce(CampaignAction.AnnounceEra, 2, out _), Is.True);
            Assert.That(campaign.Perform("b", CampaignAction.ParticipateInEra, null, 1, false, out _), Is.True);
            Assert.That(campaign.Announce(CampaignAction.CancelAnnouncement, 0, out _), Is.True);
            Assert.That(campaign.Status("b", false).optedIn, Is.False);
            Assert.That(campaign.Announce(CampaignAction.AnnounceEra, 2, out _), Is.True);
            Assert.That(campaign.Perform("b", CampaignAction.ParticipateInEra, null, 1, false, out _), Is.True);
            Assert.That(campaign.Perform("c", CampaignAction.ParticipateInEra, null, 1, false, out _), Is.True);
            campaign.Disconnect("c");
            Assert.That(campaign.Announce(CampaignAction.AnnounceEra, 1, out _), Is.True);
            Assert.That(campaign.Tick(1439), Is.False);
            Assert.That(campaign.Tick(1), Is.True);
            Assert.That(campaign.Profile("a").character.life, Is.EqualTo(1));
            Assert.That(VillageState.Restore(campaign.Profile("a").character, rules).Inventory.Count(ItemIds.Bread), Is.EqualTo(1));
            Assert.That(campaign.Profile("b").character.life, Is.EqualTo(2));
            Assert.That(campaign.Profile("b").character.inventory, Is.Empty);
            Assert.That(campaign.Connect("c", "셋", out var reconnect), Is.True);
            Assert.That(reconnect.character.life, Is.EqualTo(1));
            Assert.That(VillageState.Restore(reconnect.character, rules).Inventory.Count(ItemIds.Bread), Is.EqualTo(1));
        }

        [Test]
        public void CheckpointsCannotReplaceTheWorldAndStaleLivesAreRejected()
        {
            var campaign = ReadyCampaign(out _);
            var profile = campaign.Profile("a").character;
            profile.farmLevel = 2;
            profile.timberDelivered = 0;
            profile.legend = new WoodcuttingLegend();
            Assert.That(campaign.Checkpoint("a", profile), Is.True);
            Assert.That(profile.timberDelivered, Is.EqualTo(6));
            Assert.That(campaign.World.farmLevel, Is.EqualTo(1));
            Assert.That(campaign.Perform("a", CampaignAction.VoteForEra, null, 1, true, out _), Is.True);
            Assert.That(campaign.Tick(0), Is.True);
            Assert.That(campaign.Checkpoint("a", profile), Is.False);
        }

        [Test]
        public void ADifferentPlayerCanCompleteAMaturedLegend()
        {
            var rules = Rules();
            rules.LegendFoundingTimber = 1;
            rules.LegendTrialTimber = 1;
            var person = new VillageState(rules);
            Assert.That(person.TryGather(ItemIds.Timber, 1, 0, WorkOutcome.Success, out _), Is.True);
            Assert.That(person.TryRecordLegend("a", "첫 나무꾼"), Is.True);
            var world = person.ExportWorld();
            var challenger = new VillageState(rules);
            challenger.BeginInWorld(world);
            challenger.Inventory.TryAdd(ItemIds.Timber, 1);
            Assert.That(challenger.TryProveLegend("b"), Is.False);
            world.AddMinutes(rules.LegendMaturationDays * 1440d);
            person.ApplyWorld(world);
            challenger.ApplyWorld(world);
            Assert.That(person.HasLegendGuidance("a"), Is.True);
            Assert.That(challenger.TryProveLegend("b"), Is.True);
            Assert.That(challenger.Inventory.Count(ItemIds.LegendAxe), Is.EqualTo(1));
            Assert.That(challenger.Legend.founders[0].player, Is.EqualTo("a"));
            Assert.That(challenger.LegendPlacesAvailable, Is.Zero);
            Assert.That(challenger.HasProvenLegend("b"), Is.True);
        }

        [Test]
        public void ChatValidatesSizeAndPreservesPlainUnicodeText()
        {
            Assert.That(VillageChatText.TryNormalize("   안녕하세요!  ", out var message), Is.True);
            Assert.That(message, Is.EqualTo("안녕하세요!"));
            Assert.That(VillageChatText.TryNormalize(new string('가', VillageChatText.MaximumLength), out _), Is.True);
            Assert.That(VillageChatText.TryNormalize(new string('가', VillageChatText.MaximumLength + 1), out _), Is.False);
            Assert.That(VillageChatText.TryNormalize(" ", out _), Is.False);
            Assert.That(VillageChatText.TryNormalize("가\n나", out _), Is.False);
        }

        [Test]
        public async Task TcpPeersExchangeBoundedFramesOverLoopback()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var accepting = listener.AcceptTcpClientAsync();
                var outgoing = new TcpClient();
                var endpoint = (IPEndPoint)listener.LocalEndpoint;
                await outgoing.ConnectAsync(endpoint.Address, endpoint.Port);
                using var client = new VillageTcpPeer(outgoing);
                using var server = new VillageTcpPeer(await accepting);
                byte[] hello = Encoding.UTF8.GetBytes("안녕하세요");
                byte[] payload = new byte[VillageTcpPeer.MaxPayload - 1];
                payload[payload.Length - 1] = 16;
                Assert.That(client.Send(hello), Is.True);
                Assert.That(client.Send(payload), Is.True);
                Assert.That(server.Send(new byte[] { 1, 2, 3 }), Is.True);
                Assert.That(await Frame(server), Is.EqualTo(hello));
                Assert.That(await Frame(server), Is.EqualTo(payload));
                Assert.That(await Frame(client), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(server.Send(new byte[VillageTcpPeer.MaxPayload + 1]), Is.False);
            }
            finally { listener.Stop(); }
        }

        private static async Task<byte[]> Frame(VillageTcpPeer peer)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                if (peer.TryRead(out var data))
                    return data;
                await Task.Delay(5);
            }
            Assert.Fail("A framed message did not reach its peer.");
            return null;
        }

        private static readonly Dictionary<string, string> Sites = new Dictionary<string, string> { ["east"] = "동쪽 밭터" };

        private static void StockResources(VillageCampaign campaign, VillageRules rules, string item, int amount)
        {
            var player = VillageState.Restore(campaign.Profile("a").character, rules);
            Assert.That(player.Inventory.TryAdd(item, amount), Is.True);
            Assert.That(campaign.Checkpoint("a", player.Export()), Is.True);
        }

        private static VillageCampaign ReadyCampaign(out VillageRules rules)
        {
            rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 10, 22);
            Assert.That(campaign.Connect("a", "하나", out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.AcceptFoundation, null, 1, true, out _), Is.True);
            var inventory = VillageState.Restore(campaign.Profile("a").character, rules);
            inventory.Inventory.TryAdd(ItemIds.Timber, 6);
            Assert.That(campaign.Checkpoint("a", inventory.Export()), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.SelectFarmSite, "east", 1, true, out _), Is.True);
            return campaign;
        }

        private static void GiveItem(VillageCampaign campaign, VillageRules rules, string identity)
        {
            var state = VillageState.Restore(campaign.Profile(identity).character, rules);
            Assert.That(state.Inventory.TryAdd(ItemIds.Bread, 1), Is.True);
            var checkpoint = state.Export();
            checkpoint.playerX = 230;
            checkpoint.playerZ = 150;
            Assert.That(campaign.Checkpoint(identity, checkpoint), Is.True);
        }
    }
}
