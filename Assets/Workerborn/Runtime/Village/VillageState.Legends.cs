using System;
using System.Linq;

namespace Workerborn
{
    [Serializable]
    public sealed class LegendFounder
    {
        public string player;
        public string name;
        public int era, life;
        public double recordedAt;
    }

    [Serializable]
    public sealed class WoodcuttingLegend
    {
        public LegendFounder[] founders = Array.Empty<LegendFounder>();
        public string[] recognized = Array.Empty<string>();

        public int PlacesAvailable(double clock, int era, int days) =>
            Math.Max(0, founders.Count(founder => era > founder.era || clock - founder.recordedAt >= days * 1440d) - recognized.Length);
    }

    public sealed partial class VillageState
    {
        public int LifetimeTimber { get; private set; }
        public WoodcuttingLegend Legend { get; private set; } = new WoodcuttingLegend();

        public int LegendPlacesAvailable => Legend.PlacesAvailable(WorldMinute, Era, Rules.LegendMaturationDays);
        public bool HasFoundedLegend(string player) => Legend.founders.Any(founder => founder.player == player);

        public bool HasLegendGuidance(string player) => !HasProvenLegend(player) && Legend.founders.Any(founder =>
            founder.player == player && founder.life == Life && WorldMinute - founder.recordedAt >= Rules.LegendMaturationDays * 1440d);

        public bool CanRecordLegend(string player) => Rules.LegendFoundingTimber > 0 &&
            LifetimeTimber >= Rules.LegendFoundingTimber && !HasFoundedLegend(player);

        public bool HasProvenLegend(string player) => Array.IndexOf(Legend.recognized, player) >= 0;

        public bool CanProveLegend(string player) => Rules.LegendTrialTimber > 0 && !HasProvenLegend(player) &&
            LegendPlacesAvailable > 0;

        public bool TryRecordLegend(string player, string name)
        {
            if (string.IsNullOrWhiteSpace(player) || !CanRecordLegend(player))
                return Result(false, "아직 전설을 기록할 수 없어요.");
            Legend = new WoodcuttingLegend
            {
                founders = Legend.founders.Append(new LegendFounder
                {
                    player = player,
                    name = name,
                    era = Era,
                    life = Life,
                    recordedAt = WorldMinute
                }).ToArray(),
                recognized = Legend.recognized
            };
            return Result(true, "전설의 나무꾼 이야기가 세계의 기록에 남았어요.");
        }

        public bool TryProveLegend(string player)
        {
            if (string.IsNullOrWhiteSpace(player) || !CanProveLegend(player))
                return Result(false, "지금 참가할 수 있는 전설의 시련이 없어요.");
            if (!Inventory.TryExchange(ItemIds.Timber, Rules.LegendTrialTimber, Rules.LegendRewardItem, 1))
                return Result(false, "시련에 필요한 목재 또는 보상을 받을 공간을 확인해 주세요.");
            Legend = new WoodcuttingLegend { founders = Legend.founders, recognized = Legend.recognized.Append(player).ToArray() };
            return Result(true, "전설의 나무꾼으로 인정받았어요.");
        }

        private static WoodcuttingLegend RestoreLegend(WoodcuttingLegend value)
        {
            if (value == null)
                return new WoodcuttingLegend();
            if (value.founders == null || value.recognized == null || value.founders.Length > 256 ||
                value.recognized.Length > value.founders.Length ||
                value.founders.Any(f => f == null || string.IsNullOrEmpty(f.player) || f.era < 1 ||
                    double.IsNaN(f.recordedAt) || double.IsInfinity(f.recordedAt) || f.recordedAt < 0) ||
                value.founders.Select(f => f.player).Distinct().Count() != value.founders.Length ||
                value.recognized.Any(string.IsNullOrEmpty) ||
                value.recognized.Distinct().Count() != value.recognized.Length)
                throw new ArgumentException("Invalid legend records.");
            return value;
        }
    }
}
