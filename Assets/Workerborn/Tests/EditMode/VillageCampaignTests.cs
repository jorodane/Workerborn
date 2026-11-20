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
