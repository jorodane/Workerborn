using System;
using System.Collections.Generic;

namespace Workerborn
{
    public enum SkillId
    {
        Harvesting = 0, Woodcutting = 1, Carving = 2, UnarmedCombat = 3, EggGathering = 4, Fishing = 5,
        HarvestAcquisition = 6, LuckyBonus = 7, SoulGrowth = 8, CriticalHit = 9
    }

    public enum SkillEffect
    {
        YieldMultiplier,
        StaminaSaving,
        MaxStamina,
        UnarmedDamage,
        LuckyChance,
        GreatLuckyChance,
        HarvestItemsPerSecond = 7,
        ExperienceGainPercent,
        CriticalChanceBasisPoints,
        CriticalDamagePercent
    }

    [Serializable]
    public struct SkillRankEffect
    {
        public int fromRank;
        public int throughRank;
        public SkillEffect effect;
        public int amount;
        public int increasePerRank;

        public SkillRankEffect(int fromRank, int throughRank, SkillEffect effect, int amount, int increasePerRank = 0)
        {
            this.fromRank = fromRank;
            this.throughRank = throughRank;
            this.effect = effect;
            this.amount = amount;
            this.increasePerRank = increasePerRank;
        }
    }

    public sealed class SkillProgression
    {
        public const int MaxRank = 100;
        public const int RanksPerGrade = 10;
        public static IReadOnlyList<SkillId> All { get; } = Array.AsReadOnly(new[]
        {
            SkillId.Harvesting, SkillId.Woodcutting, SkillId.Carving, SkillId.UnarmedCombat, SkillId.EggGathering, SkillId.Fishing,
            SkillId.HarvestAcquisition, SkillId.LuckyBonus, SkillId.SoulGrowth, SkillId.CriticalHit
        });
        private static readonly SkillProgression[] defaults =
        {
            new SkillProgression(SkillId.Harvesting, "수확", GatheringRows(1, new SkillRankEffect(20, 20, SkillEffect.MaxStamina, 5))),
            new SkillProgression(SkillId.Woodcutting, "벌목", GatheringRows(2, new SkillRankEffect(20, 20, SkillEffect.StaminaSaving, 1))),
            new SkillProgression(SkillId.Carving, "갈무리", new[] { new SkillRankEffect(1, 100, SkillEffect.YieldMultiplier, 1) }),
            new SkillProgression(SkillId.UnarmedCombat, "맨손전투", new[] { new SkillRankEffect(1, 100, SkillEffect.UnarmedDamage, 1), new SkillRankEffect(20, 20, SkillEffect.MaxStamina, 5) }),
            new SkillProgression(SkillId.EggGathering, "달걀 채집", GatheringRows(1)),
            new SkillProgression(SkillId.Fishing, "낚시", GatheringRows(1)),
            new SkillProgression(SkillId.HarvestAcquisition, "수확물 자동 습득", GradeEffects(SkillEffect.HarvestItemsPerSecond, 1, 1)),
            new SkillProgression(SkillId.LuckyBonus, "럭키 보너스", GradeEffects(SkillEffect.LuckyChance, 5, 1,
                new SkillRankEffect(11, 11, SkillEffect.GreatLuckyChance, 1),
                new SkillRankEffect(51, 51, SkillEffect.GreatLuckyChance, 1))),
            new SkillProgression(SkillId.SoulGrowth, "영혼의 성장", new[]
            {
                new SkillRankEffect(0, 0, SkillEffect.ExperienceGainPercent, 100),
                new SkillRankEffect(1, MaxRank, SkillEffect.ExperienceGainPercent, 1)
            }),
            new SkillProgression(SkillId.CriticalHit, "치명타", GradeEffects(SkillEffect.CriticalDamagePercent, 150, 10,
                new SkillRankEffect(0, 0, SkillEffect.CriticalChanceBasisPoints, 500),
                new SkillRankEffect(1, MaxRank, SkillEffect.CriticalChanceBasisPoints, 20)))
        };

        private readonly SkillRankEffect[] effects;
        private readonly int firstUpgradeCost, costIncreasePerGrade;
        public SkillId Id { get; }
        public string Name { get; }

        public SkillProgression(SkillId id, string name, SkillRankEffect[] effects, int firstUpgradeCost = 1, int costIncrease = 1)
        {
            Id = id;
            Name = name;
            this.effects = effects ?? Array.Empty<SkillRankEffect>();
            this.firstUpgradeCost = firstUpgradeCost;
            costIncreasePerGrade = costIncrease;
        }

        public static bool IsDefined(SkillId id) => (uint)id < All.Count;
        public static SkillProgression Default(SkillId id) => defaults[(int)id];
        public static SkillId ForItem(string item) => item switch
        {
            ItemIds.Timber => SkillId.Woodcutting,
            ItemIds.Egg => SkillId.EggGathering,
            ItemIds.Fish => SkillId.Fishing,
            _ => SkillId.Harvesting
        };
        public static bool IsGathering(SkillId id) =>
            id == SkillId.Harvesting || id == SkillId.Woodcutting || id == SkillId.EggGathering || id == SkillId.Fishing;
        public static int GradeAtRank(int rank) => (rank + RanksPerGrade - 1) / RanksPerGrade;
        public static string RankLabel(int rank) => $"{GradeAtRank(rank)}급 · 랭크 {rank}";

        internal static int RankForNextGrade(int rank)
        {
            int nextRank = GradeAtRank(rank) * RanksPerGrade + 1;
            return nextRank <= MaxRank ? nextRank : 0;
        }

        public int UpgradeCost(int currentRank) =>
            (int)Math.Clamp((long)firstUpgradeCost + (long)(GradeAtRank(currentRank + 1) - 1) * costIncreasePerGrade, 0, 1_000_000);

        internal int UpgradeCost(int currentRank, int targetRank)
        {
            int total = 0;
            for (int rank = currentRank; rank < targetRank; rank++)
                total += UpgradeCost(rank);
            return total;
        }

        public int BonusAt(int rank, SkillEffect kind)
        {
            long total = 0;
            foreach (var row in effects)
            {
                int first = Math.Max(0, row.fromRank);
                int last = Math.Min(Math.Min(rank, MaxRank), row.throughRank);
                if (row.effect != kind || last < first)
                    continue;
                int count = last - first + 1;
                total += (long)count * row.amount + (long)count * (count - 1) / 2 * row.increasePerRank;
            }
            return (int)Math.Max(-1000000, Math.Min(1000000, total));
        }

        private static SkillRankEffect[] GatheringRows(int laterYield, params SkillRankEffect[] milestones)
        {
            var rows = new List<SkillRankEffect>
            {
                new SkillRankEffect(1, 8, SkillEffect.YieldMultiplier, 1, 1),
                new SkillRankEffect(9, 100, SkillEffect.YieldMultiplier, laterYield),
                new SkillRankEffect(3, 3, SkillEffect.StaminaSaving, 1),
                new SkillRankEffect(6, 6, SkillEffect.StaminaSaving, 1)
            };
            rows.AddRange(milestones);
            return rows.ToArray();
        }

        private static SkillRankEffect[] GradeEffects(SkillEffect effect, int practice, int perGrade, params SkillRankEffect[] additional)
        {
            var rows = new List<SkillRankEffect> { new SkillRankEffect(0, 0, effect, practice) };
            for (int rank = 1; rank <= MaxRank; rank += RanksPerGrade)
            {
                rows.Add(new SkillRankEffect(rank, rank, effect, perGrade));
            }
            rows.AddRange(additional);
            return rows.ToArray();
        }
    }
}
