using System;
using System.Collections.Generic;

namespace Workerborn
{
    public enum SkillId { Harvesting = 0, Woodcutting = 1, Carving = 2, UnarmedCombat = 3, EggGathering = 4 }

    public enum SkillEffect
    {
        YieldMultiplier,
        StaminaSaving,
        MaxStamina,
        UnarmedDamage,
        LuckyChance,
        GreatLuckyChance
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
        public static IReadOnlyList<SkillId> All { get; } = Array.AsReadOnly(new[]
        {
            SkillId.Harvesting, SkillId.Woodcutting, SkillId.Carving, SkillId.UnarmedCombat, SkillId.EggGathering
        });
        private static readonly SkillProgression[] defaults =
        {
            new SkillProgression(SkillId.Harvesting, "수확", GatheringRows(1, new SkillRankEffect(20, 20, SkillEffect.MaxStamina, 5))),
            new SkillProgression(SkillId.Woodcutting, "벌목", GatheringRows(2, new SkillRankEffect(20, 20, SkillEffect.StaminaSaving, 1))),
            new SkillProgression(SkillId.Carving, "갈무리", new[] { new SkillRankEffect(1, 100, SkillEffect.YieldMultiplier, 1) }),
            new SkillProgression(SkillId.UnarmedCombat, "맨손전투", new[] { new SkillRankEffect(1, 100, SkillEffect.UnarmedDamage, 1), new SkillRankEffect(20, 20, SkillEffect.MaxStamina, 5) }),
            new SkillProgression(SkillId.EggGathering, "달걀 채집", GatheringRows(1, new SkillRankEffect(20, 20, SkillEffect.LuckyChance, 5)))
        };

        private readonly SkillRankEffect[] effects;
        private readonly int firstUpgradeCost, costIncrease;
        public SkillId Id { get; }
        public string Name { get; }

        public SkillProgression(SkillId id, string name, SkillRankEffect[] effects, int firstUpgradeCost = 1, int costIncrease = 1)
        {
            Id = id;
            Name = name;
            this.effects = effects ?? Array.Empty<SkillRankEffect>();
            this.firstUpgradeCost = firstUpgradeCost;
            this.costIncrease = costIncrease;
        }

        public static bool IsDefined(SkillId id) => (uint)id < All.Count;
        public static SkillProgression Default(SkillId id) => defaults[(int)id];
        public static SkillId ForItem(string item) => item switch
        {
            ItemIds.Timber => SkillId.Woodcutting,
            ItemIds.Egg => SkillId.EggGathering,
            _ => SkillId.Harvesting
        };
        public static bool IsGathering(SkillId id) =>
            id == SkillId.Harvesting || id == SkillId.Woodcutting || id == SkillId.EggGathering;
        public static string RankLabel(int rank) => rank == 0 ? "연습 랭크" : $"랭크 {rank}";

        public int UpgradeCost(int currentRank) =>
            (int)Math.Max(0, Math.Min(1000000, (long)firstUpgradeCost + (long)currentRank * costIncrease));

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

        private static SkillRankEffect[] GatheringRows(int laterYield, SkillRankEffect milestone) => new[]
        {
            new SkillRankEffect(1, 8, SkillEffect.YieldMultiplier, 1, 1),
            new SkillRankEffect(9, 100, SkillEffect.YieldMultiplier, laterYield),
            new SkillRankEffect(3, 3, SkillEffect.StaminaSaving, 1),
            new SkillRankEffect(6, 6, SkillEffect.StaminaSaving, 1),
            milestone
        };
    }
}
