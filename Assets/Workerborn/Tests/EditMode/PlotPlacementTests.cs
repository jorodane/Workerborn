using System.Collections.Generic;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class PlotPlacementTests
    {
        private const int Field = 0;
        private const int Shop = 1;
        private const int Square = 2;
        private const int Pond = 3;

        private static VillageRules Rules()
        {
            var rules = new VillageRules();
            rules.BuildPlots.Add("garden", new PlotFootprint(1, 1, PlotGround.Land));
            rules.BuildPlots.Add("wide", new PlotFootprint(2, 1, PlotGround.Land));
            rules.BuildPlots.Add("commons", new PlotFootprint(2, 2, PlotGround.Land));
            rules.BuildPlots.Add("water", new PlotFootprint(1, 2, PlotGround.Fishing));
            rules.PlotFacilities = new[]
            {
                new PlotFacilityRule("potatoes", "감자밭", new PlotFootprint(1, 1, PlotGround.Land), true),
                new PlotFacilityRule("shop", "상점", new PlotFootprint(1, 2, PlotGround.Land), false),
                new PlotFacilityRule("square", "광장", new PlotFootprint(2, 2, PlotGround.Land), false),
                new PlotFacilityRule("pond", "낚시터", new PlotFootprint(1, 2, PlotGround.Fishing), false)
            };
            return rules;
        }

        [Test]
        public void PlotsUseFacingAndGroundTypeAndBuildInTheCurrentLife()
        {
            var state = new VillageState(Rules());
            Assert.That(state.TryInstallFacility("garden", Shop), Is.False);
            Assert.That(state.TryInstallFacility("wide", Shop), Is.False);
            Assert.That(state.TryInstallFacility("garden", Pond), Is.False);
            Assert.That(state.TryInstallFacility("water", Field), Is.False);
            Assert.That(state.TryInstallFacility("missing", Field), Is.False);
            Assert.That(state.TryInstallFacility("garden", 999), Is.False);
            Assert.That(state.TryInstallFacility("garden", Field), Is.True);
            Assert.That(state.TryInstallFacility("water", Pond), Is.True);
            Assert.That(state.TryInstallFacility("commons", Shop), Is.True);
            Assert.That(state.Life, Is.EqualTo(1));
            Assert.That(state.Era, Is.EqualTo(1));
            Assert.That(state.ExportWorld().plotPlacements, Has.Length.EqualTo(3));
        }

        [Test]
        public void FieldCanBeReplacedAndFixedFacilityStaysLockedAfterSave()
        {
            var rules = Rules();
            var state = new VillageState(rules);
            Assert.That(state.TryInstallFacility("commons", Field), Is.True);
            Assert.That(state.TryInstallFacility("commons", Field), Is.False);
            Assert.That(state.TryInstallFacility("commons", Square), Is.True);
            var restored = VillageState.Restore(state.Export(), rules);
            Assert.That(restored.FacilityAt("commons"), Is.EqualTo("square"));
            Assert.That(restored.TryInstallFacility("commons", Shop), Is.False);
            Assert.That(restored.TryInstallFacility("commons", Field), Is.False);
        }

        [Test]
        public void CampaignOwnsThePlacementAgainstAStaleCheckpointAndAfterReload()
        {
            var rules = Rules();
            var sites = new Dictionary<string, string>();
            var campaign = new VillageCampaign(rules, sites, 0, 0);
            Assert.That(campaign.Connect("a", "일꾼", out var a), Is.True);
            Assert.That(campaign.Connect("b", "친구", out var b), Is.True);
            var stale = VillageState.Restore(b.character, rules).Export();

            Assert.That(campaign.Perform("a", CampaignAction.BuildFacility, "commons", Square, false, out _), Is.True);
            Assert.That(campaign.Checkpoint("b", stale), Is.True);
            Assert.That(campaign.Perform("b", CampaignAction.BuildFacility, "commons", Shop, false, out _), Is.False);
            var reloaded = new VillageCampaign(rules, sites, 0, 0, campaign.Export());
            Assert.That(reloaded.World.plotPlacements, Has.Length.EqualTo(1));
            Assert.That(reloaded.World.plotPlacements[0].facilityId, Is.EqualTo("square"));
            var observer = VillageState.Restore(a.character, rules);
            observer.ApplyWorld(reloaded.World);
            Assert.That(observer.FacilityAt("commons"), Is.EqualTo("square"));
        }
    }
}
