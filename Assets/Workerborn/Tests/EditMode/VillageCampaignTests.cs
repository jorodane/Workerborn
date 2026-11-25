using System;
using System.Collections.Generic;
using System.Linq;
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
        public void GroundOwnershipSurvivesWorldReloadAndOnlyOnePlayerCanClaimTheDrop()
        {
            const long now = 100_000;
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 0f, -4.4f);
            Assert.That(campaign.Connect("a", "하나", out var owner), Is.True);
            Assert.That(campaign.Connect("b", "둘", out var other), Is.True);
            Incarnate(campaign, "a");
            Incarnate(campaign, "b");
            StockResources(campaign, rules, ItemIds.Potato, 5);
            var entry = VillageState.Restore(owner.character, rules).Inventory.Entries.Single();
            var draft = new GroundDropSave { itemId = entry.ItemId, quantity = entry.Quantity, x = 0f, z = -4.4f, ownerId = other.id };

            Assert.That(campaign.TryPlaceGroundDrop("a", entry.Id, draft, now, out var placed, out _), Is.True);
            Assert.That(placed.ownerId, Is.EqualTo(owner.id));
            Assert.That(campaign.TryCollectGroundDrop("b", placed.id, now + 59_999, out _, out _), Is.False);
            Assert.That(campaign.TryCollectGroundDrop("a", placed.id, now, out int collected, out _), Is.True);
            Assert.That(collected, Is.EqualTo(5));
            entry = VillageState.Restore(owner.character, rules).Inventory.Entries.Single();
            Assert.That(campaign.TryPlaceGroundDrop("a", entry.Id, draft, now, out placed, out _), Is.True);

            campaign = new VillageCampaign(rules, Sites, 0f, -4.4f, campaign.Export());
            Assert.That(campaign.Connect("a", "하나", out owner), Is.True);
            Assert.That(campaign.Connect("b", "둘", out other), Is.True);
            Assert.That(campaign.TryCollectGroundDrop("b", placed.id, now + 59_999, out _, out _), Is.False);
            Assert.That(campaign.TryCollectGroundDrop("b", placed.id, now + 60_000, out collected, out _), Is.True);
            Assert.That(collected, Is.EqualTo(5));
            Assert.That(campaign.TryCollectGroundDrop("a", placed.id, now + 60_001, out _, out _), Is.False);
            Assert.That(VillageState.Restore(owner.character, rules).Inventory.Count(ItemIds.Potato), Is.Zero);
            Assert.That(VillageState.Restore(other.character, rules).Inventory.Count(ItemIds.Potato), Is.EqualTo(5));
        }

        [Test]
        public void AutomaticHarvestPickupUsesNearbyLandedQuantitiesAtTheLearnedRate()
        {
            const long now = 100_000;
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 0f, -4.4f);
            Assert.That(campaign.Connect("a", "하나", out var owner), Is.True);
            Assert.That(campaign.Connect("b", "둘", out var other), Is.True);
            Incarnate(campaign, "a");
            Incarnate(campaign, "b");
            VillageState Learn(CampaignProfile profile, int rank)
            {
                var state = VillageState.Restore(profile.character, rules);
                state.LearnSkill(SkillId.HarvestAcquisition, rank);
                Assert.That(campaign.Checkpoint(profile.identity, state.Export()), Is.True);
                return state;
            }
            var state = Learn(owner, 1);
            Learn(other, 0);
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, 4), Is.True);
            Assert.That(campaign.Checkpoint("a", state.Export()), Is.True);
            var manual = new GroundDropSave { itemId = ItemIds.Potato, z = -4.4f, quantity = 4, harvestYield = true };
            Assert.That(campaign.TryPlaceGroundDrop("a", state.Inventory.Entries.Single().Id, manual, now, out var discarded, out _), Is.True);
            var drafts = new[]
            {
                new GroundDropSave { itemId = ItemIds.Potato, z = -4.4f, quantity = 5, flightMilliseconds = 480 },
                new GroundDropSave { itemId = ItemIds.Potato, x = 3f, z = -4.4f, quantity = 5 },
                new GroundDropSave { itemId = ItemIds.Potato, x = -1f, z = -4.4f, quantity = 3 }
            };
            Assert.That(campaign.TryPublishGatheredDrops("a", owner.character, drafts, now, out var drops), Is.True);
            var ids = new[] { discarded.id, drops[1].id, drops[0].id };
            Assert.That(campaign.CollectAutomaticHarvests("a", owner.character, ids, now + 479), Is.Empty);
            Assert.That(campaign.CollectAutomaticHarvests("b", other.character, ids, now + 480), Is.Empty);

            var result = campaign.CollectAutomaticHarvests("a", owner.character, ids, now + 480).Single();
            Assert.That(result.id, Is.EqualTo(drops[0].id));
            Assert.That(result.quantity, Is.EqualTo(2));
            Assert.That(result.remaining, Is.EqualTo(3));
            Assert.That(campaign.CollectAutomaticHarvests("a", owner.character, ids, now + 1_479), Is.Empty);
            Assert.That(campaign.CollectAutomaticHarvests("a", owner.character, ids, now + 1_480).Single().quantity, Is.EqualTo(2));
            Assert.That(campaign.TryCollectGroundDrop("a", drops[0].id, now + 1_481, out int manualQuantity, out _), Is.True);
            Assert.That(manualQuantity, Is.EqualTo(1));
            Assert.That(VillageState.Restore(owner.character, rules).Inventory.Count(ItemIds.Potato), Is.EqualTo(5));
            Assert.That(discarded.quantity, Is.EqualTo(4));

            campaign.ReleaseExpiredDrops(now + GroundDropSave.ReservationMilliseconds, null);
            var publicPiles = new[] { discarded.id, drops[2].id };
            result = campaign.CollectAutomaticHarvests("b", other.character, publicPiles, now + GroundDropSave.ReservationMilliseconds).Single();
            Assert.That(result.id, Is.EqualTo(drops[2].id));
            Assert.That(result.quantity, Is.EqualTo(1));
            Assert.That(result.remaining, Is.EqualTo(2));
            Assert.That(discarded.quantity, Is.EqualTo(4));
        }

        [Test]
        public void DroppedEquipmentKeepsItsConditionAndDoesNotMergeWithNewEquipment()
        {
            const long now = 100_000;
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 0f, -4.4f);
            Assert.That(campaign.Connect("a", "하나", out var owner), Is.True);
            Assert.That(campaign.Connect("b", "둘", out var buyer), Is.True);
            Incarnate(campaign, "a");
            Incarnate(campaign, "b");
            var state = VillageState.Restore(owner.character, rules);
            var condition = new EquipmentCondition(7, 62);
            Assert.That(state.Inventory.TryAdd(ItemIds.Axe, 1, condition), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            Assert.That(campaign.Checkpoint("a", state.Export()), Is.True);

            var draft = new GroundDropSave { itemId = ItemIds.Axe, z = -4.4f };
            var axe = state.Inventory.Entries.First();
            Assert.That(campaign.TryPlaceGroundDrop("a", axe.Id, draft, now, out var wornDrop, out _), Is.True);
            Assert.That(wornDrop.condition, Is.EqualTo(condition));
            Assert.That(campaign.TryPlaceGroundDrop("a", state.Inventory.Entries.Last().Id, draft, now, out _, out _), Is.True);
            campaign = new VillageCampaign(rules, Sites, 0f, -4.4f, campaign.Export());
            Assert.That(campaign.Connect("b", "둘", out buyer), Is.True);
            campaign.ReleaseExpiredDrops(now + 60_000, null);
            Assert.That(campaign.SharedGroundDrops.Count(), Is.EqualTo(2));
            Assert.That(campaign.TryCollectGroundDrop("b", wornDrop.id, now + 60_000, out _, out _), Is.True);
            Assert.That(VillageState.Restore(buyer.character, rules).Inventory.Entries.Single().Condition, Is.EqualTo(condition));
        }

        [Test]
        public void OnlyReleasedNearbyItemsOfTheSameKindMergeAndTheRemainderStaysOnTheGround()
        {
            const long now = 100_000;
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 0f, -4.4f);
            Assert.That(campaign.Connect("a", "하나", out var owner), Is.True);
            Assert.That(campaign.Connect("b", "둘", out _), Is.True);
            Incarnate(campaign, "a");
            Incarnate(campaign, "b");

            var samePosition = new InteriorPositionSave { id = "inn", localX = 0f, localZ = -4.4f };
            var drafts = new[]
            {
                new GroundDropSave { itemId = ItemIds.Potato, quantity = 50, x = 0f, z = -4.4f },
                new GroundDropSave { itemId = ItemIds.Potato, quantity = 10, x = 2.05f, z = -4.4f },
                new GroundDropSave { itemId = ItemIds.Bread, quantity = 5, x = 0f, z = -4.4f },
                new GroundDropSave { itemId = ItemIds.Potato, quantity = 32, x = 0f, z = -4.4f, interior = samePosition }
            };
            Assert.That(campaign.TryPublishGatheredDrops("a", owner.character, drafts, now, out var drops), Is.True);
            var late = new GroundDropSave { itemId = ItemIds.Potato, quantity = 75, x = -1.3f, z = -4.4f };
            Assert.That(campaign.TryPublishGatheredDrops("b", campaign.Profile("b").character, new[] { late }, now + 1_000, out var lateDrops), Is.True);

            campaign.ReleaseExpiredDrops(now + 60_000, null);
            Assert.That(campaign.SharedGroundDrops.Count(), Is.EqualTo(5));
            Assert.That(drops[0].quantity, Is.EqualTo(50));
            var changes = new List<string>();
            campaign.ReleaseExpiredDrops(now + 61_000, changes.Add);
            Assert.That(changes, Is.EquivalentTo(new[] { drops[0].id, lateDrops[0].id }));
            Assert.That(campaign.SharedGroundDrops.Select(drop => drop.quantity), Is.EquivalentTo(new[] { 125, 10, 5, 32 }));
            Assert.That(drops[0].combined, Is.True);

            var state = VillageState.Restore(owner.character, rules);
            int space = state.Inventory.Columns * state.Inventory.Rows * ItemCatalog.Get(ItemIds.Potato).MaxStack;
            StockResources(campaign, rules, ItemIds.Potato, space - 5);
            Assert.That(campaign.TryCollectGroundDrop("a", drops[0].id, now + 61_001, out int quantity, out _), Is.True);
            Assert.That(quantity, Is.EqualTo(5));
            Assert.That(drops[0].quantity, Is.EqualTo(120));
            Assert.That(campaign.TryCollectGroundDrop("a", drops[0].id, now + 61_001, out _, out _), Is.False);
            Assert.That(campaign.TryGetGroundDrop(drops[0].id, out _), Is.True);
        }

        [Test]
        public void HostingASoloWorldKeepsItsDroppedItemsAndReservationDeadline()
        {
            const long now = 100_000;
            var rules = Rules();
            var campaign = new VillageCampaign(rules, Sites, 0f, -4.4f);
            Assert.That(campaign.Connect("a", "하나", out var owner), Is.True);
            Incarnate(campaign, "a");
            var drop = new GroundDropSave { id = Guid.NewGuid().ToString("N"), itemId = ItemIds.Potato, quantity = 3, z = -4.4f };
            drop.ReserveFor(owner.id, now);
            owner.character.drops = new[] { drop };
            campaign.StoreSolo("a", campaign.World, owner.character);
            Assert.That(owner.character.drops, Is.Empty);

            campaign = new VillageCampaign(rules, Sites, 0f, -4.4f, campaign.Export());
            Assert.That(campaign.Connect("b", "둘", out _), Is.True);
            Incarnate(campaign, "b");
            Assert.That(campaign.SharedGroundDrops.Single().reservedUntilUtc, Is.EqualTo(now + 60_000));
            Assert.That(campaign.TryCollectGroundDrop("b", drop.id, now + 59_999, out _, out _), Is.False);
            Assert.That(campaign.TryCollectGroundDrop("b", drop.id, now + 60_000, out int quantity, out _), Is.True);
            Assert.That(quantity, Is.EqualTo(3));
        }

        [Test]
        public void APortableCharacterKeepsSeparateProgressInEachWorld()
        {
            const string identity = "local:character";
            var look = new CharacterLook { body = 1, hair = 7, outfit = 4, face = 2 };
            var rules = Rules();
            var first = new VillageCampaign(rules, Sites, 10, 22);
            Assert.That(first.Connect(identity, "첫 이름", out var profile, look), Is.True);
            Assert.That(first.Avatars()[0].look.hair, Is.EqualTo(7));
            Assert.That(first.Perform(identity, CampaignAction.Incarnate, null, look.ToSelection(), false, out _), Is.True);
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
            Assert.That(newcomer.character.awaitingIncarnation, Is.True);
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
            Incarnate(campaign, "a");
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
            Incarnate(campaign, "a");
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
            Incarnate(campaign, "a");
            Assert.That(campaign.Perform("a", CampaignAction.AcceptFoundation, null, 1, true, out _), Is.True);
            StockResources(campaign, rules, ItemIds.Timber, 18);
            Assert.That(campaign.Perform("a", CampaignAction.DeliverTimber, null, 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.SelectFarmSite, "east", 1, true, out _), Is.True);
            Assert.That(campaign.Perform("a", CampaignAction.VoteForEra, null, 1, true, out _), Is.True);
            Assert.That(campaign.Tick(0), Is.True);
            Incarnate(campaign, "a");
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
            Assert.That(player.AwaitingIncarnation, Is.True);
            Assert.That(player.TryIncarnate(0), Is.True);
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
            Incarnate(campaign, "b");
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

        [Test]
        public void WaitingForIncarnationSurvivesDaysAndRejectsVillageProgress()
        {
            var rules = Rules();
            bool OnClouds(float x, float z) => x is >= 90 and <= 110 && z is >= 90 and <= 110;
            var cloudSpawn = new SessionAvatar { x = 100, y = 30, z = 100 };
            var campaign = new VillageCampaign(rules, Sites, 10, 22);
            campaign.ConfigureCloudArea(OnClouds, cloudSpawn);
            Assert.That(campaign.Connect("a", "일꾼", out var profile), Is.True);
            Assert.That(campaign.Avatars()[0].x, Is.EqualTo(100));
            campaign.Move("a", new SessionAvatar { x = 10, z = 22 });
            Assert.That(campaign.Avatars()[0].x, Is.EqualTo(100));
            var waiting = VillageState.Restore(profile.character, rules);
            Assert.That(waiting.AdvanceTime(1440f * 2f / rules.MinutesPerSecond), Is.False);
            Assert.That(waiting.Day, Is.EqualTo(3));
            Assert.That(waiting.LifeMinutes, Is.Zero);
            Assert.That(waiting.LifeDay, Is.EqualTo(1));
            Assert.That(waiting.TrySleep(false), Is.False);
            Assert.That(waiting.TryRebirth(), Is.False);
            Assert.That(campaign.Perform("a", CampaignAction.AcceptFoundation, null, 1, false, out _), Is.False);
            var injectedLevel = waiting.Export();
            injectedLevel.level = 2;
            injectedLevel.totalLevel = 2;
            Assert.That(campaign.Checkpoint("a", injectedLevel), Is.False);
            var bypass = waiting.Export();
            bypass.awaitingIncarnation = false;
            Assert.That(campaign.Checkpoint("a", bypass), Is.False);
            Assert.That(waiting.Inventory.TryAdd(ItemIds.Gold, 1), Is.True);
            Assert.That(campaign.Checkpoint("a", waiting.Export()), Is.False);

            campaign.Tick(1440f * 2f / rules.MinutesPerSecond);
            campaign = new VillageCampaign(rules, Sites, 10, 22, campaign.Export());
            campaign.ConfigureCloudArea(OnClouds, cloudSpawn);
            Assert.That(campaign.Connect("a", "일꾼", out profile), Is.True);
            Assert.That(profile.character.awaitingIncarnation, Is.True);
            Assert.That(profile.character.lifeDay, Is.EqualTo(1));
            Assert.That(campaign.Avatars()[0].x, Is.EqualTo(100));
            var look = new CharacterLook { body = 1, hair = 13, outfit = 12, face = 3 };
            Assert.That(campaign.Perform("a", CampaignAction.Incarnate, null, -1, false, out _), Is.False);
            Assert.That(campaign.Perform("a", CampaignAction.Incarnate, null, look.ToSelection(), false, out _), Is.True);
            var born = VillageState.Restore(profile.character, rules);
            Assert.That(born.AwaitingIncarnation, Is.False);
            Assert.That(born.Level, Is.EqualTo(1));
            Assert.That(born.LifeMinutes, Is.Zero);
            Assert.That(born.Inventory.Entries, Is.Empty);
            Assert.That(born.LifeLook, Is.EqualTo(look));
            Assert.That(campaign.Avatars()[0].x, Is.EqualTo(10));
            campaign.Move("a", new SessionAvatar { x = 100, y = 30, z = 100 });
            Assert.That(campaign.Avatars()[0].x, Is.EqualTo(10));
        }

        private static void Incarnate(VillageCampaign campaign, string identity) =>
            Assert.That(campaign.Perform(identity, CampaignAction.Incarnate, null, 0, false, out _), Is.True);

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
            Incarnate(campaign, "a");
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
            if (campaign.Profile(identity).character.awaitingIncarnation)
                Incarnate(campaign, identity);
            var state = VillageState.Restore(campaign.Profile(identity).character, rules);
            Assert.That(state.Inventory.TryAdd(ItemIds.Bread, 1), Is.True);
            var checkpoint = state.Export();
            checkpoint.playerX = 230;
            checkpoint.playerZ = 150;
            Assert.That(campaign.Checkpoint(identity, checkpoint), Is.True);
        }
    }
}
