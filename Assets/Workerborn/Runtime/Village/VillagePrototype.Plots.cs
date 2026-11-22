using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private readonly Dictionary<string, VillagePlot> buildPlots = new Dictionary<string, VillagePlot>();

        private VillageRules CreateVillageRules()
        {
            var rules = settings.CreateRules(FindInteraction(VillageRole.Farmer)?.Definition);
            foreach (var plot in mapRoot.GetComponentsInChildren<VillagePlot>(true))
            {
                if (!plot.IsConfigured || !buildPlots.TryAdd(plot.Id, plot))
                {
                    Debug.LogError("Assign a valid construction plot and a unique plot ID.", plot);
                    continue;
                }
                rules.BuildPlots.Add(plot.Id, plot.Footprint);
            }
            return rules;
        }

        internal bool BuildFacility(VillagePlot plot, int choice) => plot != null && plot.IsConfigured && buildPlots.TryGetValue(plot.Id, out var registered) &&
            registered == plot && PerformWorld(CampaignAction.BuildFacility, plot.Id, choice);

        private bool InstallLocalFacility(string plotId, int choice)
        {
            if (!State.TryInstallFacility(plotId, choice))
            {
                hud.ShowToast(State.LastMessage);
                return false;
            }
            RefreshBuiltPlots();
            hud.ShowToast(State.LastMessage);
            hud.RefreshWindows();
            SaveGame(false);
            return true;
        }

        private bool RenderPlots()
        {
            bool geometryChanged = false;
            bool navigationChanged = false;
            foreach (var plot in buildPlots.Values)
            {
                var facility = settings.FindPlotFacility(State.FacilityAt(plot.Id));
                if (facility != null && !plot.Footprint.Contains(facility.Footprint))
                    facility = null;
                geometryChanged |= plot.Render(this, facility, out bool obstaclesChanged);
                navigationChanged |= obstaclesChanged;
            }
            if (initialized && geometryChanged)
                targeting?.RefreshStaticGeometry();
            return navigationChanged;
        }

        private void RefreshBuiltPlots()
        {
            if (!RenderPlots())
                return;
            worker.Cancel();
            var agent = worker.GetComponent<NavMeshAgent>();
            agent.enabled = false;
            navigation.Activate(useBakedData: false);
            if (NavMesh.SamplePosition(worker.transform.position, out var point, VillagePlot.CellSide * 2f, NavMesh.AllAreas) ||
                NavMesh.SamplePosition(spawnPoint.position, out point, 2f, NavMesh.AllAreas))
                worker.transform.position = point.position;
            agent.enabled = true;
        }

        internal void RegisterPlotCrop(HarvestableCrop crop, string id)
        {
            crop.AssignPersistentId(id);
            crop.EnsureInitialized();
            pickupPool.CanSpawn(crop.Definition?.PickupPrefab);
            crop.Harvested += DropHarvest;
            allCrops.Add(crop);
            if (crop.gameObject.activeInHierarchy && crop.Definition?.ItemId == ItemIds.Potato)
                crops.Add(crop);
        }

        internal void RemovePlotCrop(HarvestableCrop crop)
        {
            crop.Harvested -= DropHarvest;
            allCrops.Remove(crop);
            crops.Remove(crop);
        }
    }
}
