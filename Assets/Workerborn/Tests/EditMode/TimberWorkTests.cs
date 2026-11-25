using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class TimberWorkTests
    {
        private static TimberSite Pine() => new TimberSite("pine", new TimberPoint(0f, 0f), ItemIds.Axe,
            4, 2.2f, .6f, .92f, 0f, .44f, 6.44f, .19f, 4.56f, new[]
            {
                new TreeProduct("branch", 70, 1),
                new TreeProduct(ItemIds.PineCone, 24, 1),
                new TreeProduct(ItemIds.PineSapling, 6, 1)
            });

        private static VillageState Worker()
        {
            var state = new VillageState();
            state.Inventory.TryAdd(ItemIds.Axe, 1);
            state.TryEquip(state.Inventory.Entries.Single().Id);
            return state;
        }

        private static TimberPose RestPose(TimberSite site, float yaw, float pitch)
        {
            float center = site.Section(TimberPart.Trunk).Center;
            return TimberPose.AtTilt(site.RestPoint(center, yaw, pitch), site.RestHeight(center, pitch), yaw, pitch);
        }

        [Test]
        public void FailedStrikeAndFailedDeliveryLeaveTheProductionSlotAvailable()
        {
            float roll = .8f;
            var work = new TimberWork(new[] { Pine() }, () => roll);
            var worker = Worker();
            var order = new TimberOrder { action = TimberAction.Strike, target = "pine" };
            bool delivered = false;
            Assert.That(worker.SpendWorkProgress(2, 0d, 1d), Is.True);
            Assert.That(Perform(order, Receive), Is.EqualTo(TimberResult.Failed));
            Assert.That(work.Capture().trees, Is.Empty);
            Assert.That(delivered, Is.False);
            Assert.That(worker.Energy, Is.EqualTo(98));

            roll = .4f;
            Assert.That(Perform(order, _ => false), Is.EqualTo(TimberResult.Rejected));
            Assert.That(work.Capture().trees, Is.Empty);
            Assert.That(Perform(order, Receive), Is.EqualTo(TimberResult.Succeeded));
            Assert.That(delivered, Is.True);
            Assert.That(worker.Energy, Is.EqualTo(98));
            Assert.That(work.Capture().trees.Single().usedSlots, Is.EqualTo(1));

            TimberResult Perform(TimberOrder order, System.Func<List<TimberYield>, bool> deliver) =>
                work.Perform(order, worker, "worker", new TimberPoint(0, -1), 0, 1, _ => false, _ => true, deliver, out _);
            bool Receive(List<TimberYield> list)
            {
                delivered = true;
                Assert.That(list.Single().itemId, Is.EqualTo(ItemIds.Branch));
                return true;
            }
        }

        [Test]
        public void FellingRollsRemainingProductsAtTheCrownAndSittingProtectsTheStump()
        {
            var site = Pine();
            var work = new TimberWork(new[] { site }, () => 0f);
            var worker = Worker();
            worker.LearnSkill(SkillId.Woodcutting, 11);
            List<TimberYield> rewards = null;
            bool occupied = true;
            Assert.That(Perform(TimberAction.Strike), Is.EqualTo(TimberResult.Succeeded));
            Assert.That(Perform(TimberAction.Fell), Is.EqualTo(TimberResult.Succeeded));
            var tree = work.Capture().trees.Single();
            Assert.That(tree.Has(TimberPart.Stump), Is.True);
            Assert.That(tree.Has(TimberPart.Trunk), Is.True);
            Assert.That(tree.Has(TimberPart.Upper), Is.False);
            Assert.That(tree.falling, Is.True);
            Assert.That(tree.fallYaw, Is.EqualTo(0f));
            Assert.That(rewards.Count, Is.EqualTo(5));
            Assert.That(rewards.All(reward => reward.point.z == site.CrownDropDistance), Is.True);
            Assert.That(Perform(TimberAction.Split), Is.EqualTo(TimberResult.Rejected));
            TimberPose restingPose = RestPose(site, 0f, 85f);
            Assert.That(work.SettleFall(site.Id, restingPose), Is.True);
            Assert.That(Perform(TimberAction.Split), Is.EqualTo(TimberResult.Rejected));
            Assert.That(work.Capture().trees.Single().Has(TimberPart.Stump), Is.True);

            occupied = false;
            Assert.That(Perform(TimberAction.Split), Is.EqualTo(TimberResult.Succeeded));
            Assert.That(work.Capture().logs.Length, Is.EqualTo(1));
            var loaded = new TimberWork(new[] { site }, () => 0f, work.Capture());
            Assert.That(loaded.Capture().trees.Single().Has(TimberPart.Stump), Is.False);
            Assert.That(loaded.Capture().trees.Single().Has(TimberPart.Trunk), Is.True);
            Assert.That(loaded.Capture().trees.Single().restingPose, Is.EqualTo(restingPose));

            TimberResult Perform(TimberAction action) => work.Perform(new TimberOrder { action = action, target = "pine" },
                worker, "worker", new TimberPoint(0, -1), 0, 1, _ => occupied, _ => true,
                yields => { rewards = yields; return true; }, out _);
        }

        [Test]
        public void SplittingKeepsBothHalvesAndTheirLogsOnTheSettledTreeAxis()
        {
            float roll = 0f;
            var site = Pine();
            var worker = Worker();
            var work = new TimberWork(new[] { site }, () => roll);
            Assert.That(Perform(TimberAction.Fell, TimberPart.Trunk, new TimberPoint(-1f, 0f)), Is.EqualTo(TimberResult.Succeeded));
            var restingPose = new TimberPose(new TimberPoint(6f, 4f), .23f, Quaternion.CreateFromYawPitchRoll(.97f, 1.54f, .73f));
            TimberPoint middle = restingPose.point;
            Assert.That(Perform(TimberAction.Split, TimberPart.Trunk, middle), Is.EqualTo(TimberResult.Rejected));
            work = new TimberWork(new[] { site }, () => roll, work.Capture());
            Assert.That(work.Capture().trees.Single().falling, Is.True);
            Assert.That(work.SettleFall(site.Id, default), Is.False);
            Assert.That(work.SettleFall(site.Id, restingPose), Is.True);
            Assert.That(work.SettleFall(site.Id, RestPose(site, 90f, 90f)), Is.False);
            var pinnedCenter = site.RestPoint(site.Section(TimberPart.Trunk).Center, 90f, 90f);
            Assert.That(Perform(TimberAction.Split, TimberPart.Trunk, pinnedCenter), Is.EqualTo(TimberResult.Rejected));

            roll = .98f;
            Assert.That(Perform(TimberAction.Split, TimberPart.Trunk, middle), Is.EqualTo(TimberResult.Failed));
            Assert.That(work.Capture().trees.Single().Has(TimberPart.Trunk), Is.True);
            roll = 0f;
            Assert.That(Perform(TimberAction.Split, TimberPart.Trunk, middle), Is.EqualTo(TimberResult.Succeeded));
            Assert.That(work.Capture().logs, Is.Empty);

            foreach (var part in new[] { TimberPart.Lower, TimberPart.Upper })
            {
                var section = site.Section(part);
                Assert.That(Perform(TimberAction.Split, part, SectionPoint(section.Center).point), Is.EqualTo(TimberResult.Succeeded));
                var logs = work.Capture().logs.Where(log => log.sourcePart == part).OrderBy(log => log.half).ToArray();
                Assert.That(logs.Length, Is.EqualTo(2));
                Assert.That(logs.All(log => log.sourceTree == site.Id && !log.moved), Is.True);
                for (int i = 0; i < logs.Length; i++)
                {
                    var expected = SectionPoint(section.Half(i).Center);
                    Assert.That(logs[i].point, Is.EqualTo(expected.point));
                    Assert.That(logs[i].cutPose, Is.EqualTo(expected));
                }
            }

            var restored = new TimberWork(new[] { site }, () => roll, work.Capture());
            Assert.That(restored.Capture().trees.Single().restingPose, Is.EqualTo(restingPose));
            Assert.That(restored.Capture().logs.Select(log => log.cutPose), Is.EqualTo(work.Capture().logs.Select(log => log.cutPose)));

            TimberPose SectionPoint(float height)
            {
                var offset = Vector3.Transform(Vector3.UnitY, restingPose.Rotation) * (height - site.Section(TimberPart.Trunk).Center);
                return new TimberPose(new TimberPoint(middle.x + offset.X, middle.z + offset.Z), restingPose.height + offset.Y, restingPose.Rotation);
            }

            TimberResult Perform(TimberAction action, TimberPart part, TimberPoint point) => work.Perform(
                new TimberOrder { action = action, target = site.Id, part = part }, worker, "worker", point,
                0f, 1, _ => false, _ => true, _ => true, out _);
        }

        [Test]
        public void LogLiftOwnershipRangeAndFailureOutputsAreEnforced()
        {
            float roll = 0f;
            var site = Pine();
            var work = new TimberWork(new[] { site }, () => roll);
            var worker = Worker();
            worker.LearnSkill(SkillId.Lifting);
            worker.LearnSkill(SkillId.Throwing);
            List<TimberYield> yields = null;
            var origin = new TimberPoint(0, -1);
            Assert.That(Perform(TimberAction.Fell, "pine", TimberPart.Stump, origin), Is.EqualTo(TimberResult.Succeeded));
            Assert.That(work.SettleFall(site.Id, RestPose(site, 0f, 90f)), Is.True);
            roll = .97f;
            Assert.That(Perform(TimberAction.Split, "pine", TimberPart.Stump, origin), Is.EqualTo(TimberResult.Failed));
            Assert.That(yields.Single().quantity, Is.EqualTo(10));
            Assert.That(yields.Single().itemId, Is.EqualTo(ItemIds.ScrapWood));

            roll = 0f;
            var middle = site.RestPoint(site.Section(TimberPart.Trunk).Center, 0f, 90f);
            Assert.That(Perform(TimberAction.Split, "pine", TimberPart.Trunk, middle), Is.EqualTo(TimberResult.Succeeded));
            var lower = site.RestPoint(site.Section(TimberPart.Lower).Center, 0f, 90f);
            Assert.That(Perform(TimberAction.Split, "pine", TimberPart.Lower, lower), Is.EqualTo(TimberResult.Succeeded));
            var logs = work.Capture().logs;
            Assert.That(logs.Length, Is.EqualTo(2));
            Assert.That(Perform(TimberAction.Lift, logs[0].id, TimberPart.Log, lower), Is.EqualTo(TimberResult.Succeeded));
            Assert.That(Perform(TimberAction.Lift, logs[1].id, TimberPart.Log, lower), Is.EqualTo(TimberResult.Rejected));
            Assert.That(Perform(TimberAction.Split, logs[0].id, TimberPart.Log, lower, "other"), Is.EqualTo(TimberResult.Rejected));
            Assert.That(Perform(TimberAction.Throw, null, TimberPart.Log, lower, aim: lower.Offset(0, 4f)), Is.EqualTo(TimberResult.Rejected));
            Assert.That(work.CountCarried("worker"), Is.EqualTo(1));

            worker.LearnSkill(SkillId.Throwing, 11);
            Assert.That(Perform(TimberAction.Throw, null, TimberPart.Log, lower, aim: lower.Offset(0, 4f)), Is.EqualTo(TimberResult.Succeeded));
            var landed = work.Capture().logs.Single(row => row.id == logs[0].id);
            Assert.That(landed.thrown, Is.True);
            Assert.That(landed.carrier, Is.Null);
            Assert.That(landed.point.z, Is.EqualTo(lower.z + 4f).Within(.001f));

            roll = .97f;
            Assert.That(Perform(TimberAction.Split, landed.id, TimberPart.Log, landed.point), Is.EqualTo(TimberResult.Failed));
            Assert.That(yields.Single().quantity, Is.EqualTo(5));
            Assert.That(yields.Single().itemId, Is.EqualTo(ItemIds.ScrapWood));
            Assert.That(work.Capture().logs.Length, Is.EqualTo(1));

            TimberResult Perform(TimberAction action, string target, TimberPart part, TimberPoint at, string who = "worker", TimberPoint aim = default) =>
                work.Perform(new TimberOrder { action = action, target = target, part = part, destination = aim },
                    worker, who, at, 0f, 1, _ => false, _ => true, rewards => { yields = rewards; return true; }, out _);
        }

        [Test]
        public void TheServerSharesTheFelledTreeAndSavesHeldLogsAtTheCarrier()
        {
            var rules = new VillageRules();
            var campaign = new VillageCampaign(rules, new Dictionary<string, string>(), 0f, -1f);
            var site = Pine();
            campaign.ConfigureTimber(new[] { site }, _ => true, _ => 0f, () => 0f);
            foreach (string id in new[] { "first", "second" })
            {
                Assert.That(campaign.Connect(id, id, out var profile), Is.True);
                Assert.That(campaign.Perform(id, CampaignAction.Incarnate, null, 0, false, out _), Is.True);
                var worker = Worker();
                worker.LearnSkill(SkillId.Lifting);
                var save = profile.character;
                save.inventory = worker.Inventory.Export();
                save.equipment = worker.Export().equipment;
                save.skillRanks = worker.Export().skillRanks;
            }

            var fell = new TimberOrder { action = TimberAction.Fell, target = site.Id };
            Assert.That(Perform("first", fell), Is.True);
            Assert.That(Perform("second", fell), Is.False);
            Assert.That(campaign.SettleTimberFall(site.Id, RestPose(site, 0f, 84f)), Is.True);
            var split = new TimberOrder { action = TimberAction.Split, target = site.Id, part = TimberPart.Stump };
            campaign.Move("second", new SessionAvatar { x = 0, z = 0, motion = (int)WorkerMotion.SeatSit });
            Assert.That(Perform("first", split), Is.False);
            campaign.Move("second", new SessionAvatar { x = 0, z = 0, motion = (int)WorkerMotion.Run });
            Assert.That(Perform("first", split), Is.True);
            string logId = campaign.Woodland.logs.Single().id;
            var lift = new TimberOrder { action = TimberAction.Lift, target = logId, part = TimberPart.Log };
            Assert.That(Perform("first", lift), Is.True);
            Assert.That(Perform("second", lift), Is.False);
            campaign.Move("first", new SessionAvatar { x = 12f, z = 4f, yaw = 90f });

            var saved = campaign.Export();
            Assert.That(campaign.Woodland.logs.Single().carrier, Is.Not.Null);
            Assert.That(saved.woodland.logs.Single().carrier, Is.Null);
            Assert.That(saved.woodland.logs.Single().point.x, Is.EqualTo(13.25f).Within(.01f));
            campaign = new VillageCampaign(rules, new Dictionary<string, string>(), 0f, -1f, saved);
            campaign.ConfigureTimber(new[] { site }, _ => true, _ => 0f, () => 0f);
            Assert.That(campaign.Woodland.logs.Single().point.x, Is.EqualTo(13.25f).Within(.01f));
            Assert.That(campaign.Woodland.trees.Single().Has(TimberPart.Stump), Is.False);

            bool Perform(string id, TimberOrder order) =>
                campaign.PerformTimber(id, campaign.Profile(id).character, order, 1000, null, out _, out _, out _);
        }
    }
}
