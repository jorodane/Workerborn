using System;
using System.Collections.Generic;

namespace Workerborn
{
    public enum PlotGround { Land, Fishing }

    internal readonly struct PlotFootprint
    {
        internal readonly int Width, Depth;
        internal readonly PlotGround Ground;

        internal PlotFootprint(int width, int depth, PlotGround ground)
        {
            Width = width;
            Depth = depth;
            Ground = ground;
        }

        internal bool IsValid => Width >= 1 && Depth >= 1 && (Ground == PlotGround.Land || Ground == PlotGround.Fishing);

        internal bool Contains(PlotFootprint required) => Ground == required.Ground &&
            Width >= required.Width && Depth >= required.Depth;
    }

    internal sealed class PlotFacilityRule
    {
        internal readonly string Id, Name;
        internal readonly PlotFootprint Footprint;
        internal readonly bool Replaceable;

        internal PlotFacilityRule(string id, string name, PlotFootprint footprint, bool replaceable)
        {
            Id = id;
            Name = name;
            Footprint = footprint;
            Replaceable = replaceable;
        }
    }

    [Serializable]
    public sealed class PlotPlacement
    {
        public string plotId;
        public string facilityId;
    }

    public sealed partial class VillageState
    {
        private PlotPlacement[] plotPlacements = Array.Empty<PlotPlacement>();

        internal string FacilityAt(string plotId)
        {
            foreach (var placement in plotPlacements)
                if (placement.plotId == plotId)
                    return placement.facilityId;
            return null;
        }

        internal bool CanInstallFacility(string plotId, int choice, out string reason)
        {
            if (plotId == null || !Rules.BuildPlots.TryGetValue(plotId, out var available) ||
                (uint)choice >= Rules.PlotFacilities.Length || Rules.PlotFacilities[choice] is not PlotFacilityRule facility ||
                !available.Contains(facility.Footprint))
            {
                reason = "이 부지에는 해당 시설을 설치할 수 없어요.";
                return false;
            }

            string installed = FacilityAt(plotId);
            if (installed == facility.Id)
            {
                reason = "이미 같은 시설이 설치되어 있어요.";
                return false;
            }
            if (installed != null && Rules.FindFacility(installed)?.Replaceable != true)
            {
                reason = "설치된 시설을 변경할 수 없어요.";
                return false;
            }
            reason = null;
            return true;
        }

        internal bool TryInstallFacility(string plotId, int choice)
        {
            if (!CanInstallFacility(plotId, choice, out var reason))
                return Result(false, reason);

            var facility = Rules.PlotFacilities[choice];
            int slot = Array.FindIndex(plotPlacements, entry => entry.plotId == plotId);
            var placements = new PlotPlacement[plotPlacements.Length + (slot < 0 ? 1 : 0)];
            plotPlacements.CopyTo(placements, 0);
            placements[slot < 0 ? placements.Length - 1 : slot] = new PlotPlacement { plotId = plotId, facilityId = facility.Id };
            plotPlacements = placements;
            return Result(true, $"{facility.Name} 설치 완료");
        }

        private bool ApplyPlotPlacements(PlotPlacement[] placements)
        {
            placements ??= Array.Empty<PlotPlacement>();
            if (plotPlacements == placements)
                return false;
            bool changed = plotPlacements.Length != placements.Length;
            for (int i = 0; !changed && i < placements.Length; i++)
                changed = placements[i].plotId != plotPlacements[i].plotId || placements[i].facilityId != plotPlacements[i].facilityId;
            if (changed)
                plotPlacements = placements;
            return changed;
        }

        private static PlotPlacement[] RestorePlotPlacements(PlotPlacement[] placements)
        {
            if (placements == null || placements.Length == 0)
                return Array.Empty<PlotPlacement>();
            var ids = new HashSet<string>();
            foreach (var entry in placements)
                if (entry == null || string.IsNullOrWhiteSpace(entry.plotId) || string.IsNullOrWhiteSpace(entry.facilityId) || !ids.Add(entry.plotId))
                    throw new ArgumentException("Invalid plot placement.");
            return placements;
        }
    }
}
