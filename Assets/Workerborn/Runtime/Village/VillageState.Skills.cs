using System;

namespace Workerborn
{
    [Serializable]
    public sealed class SkillRankSave
    {
        public SkillId skill;
        public int rank;
    }

    public sealed partial class VillageState
    {
        private readonly int[] skillRanks = new int[SkillProgression.All.Count];

        public int SkillRank => RankOf(SkillId.Harvesting);
        public int NextSkillCost => NextSkillCostFor(SkillId.Harvesting);
        public int YieldMultiplier => YieldMultiplierFor(SkillId.Harvesting);
        public int StaminaLimit { get; private set; }
        public int UnarmedDamageBonus { get; private set; }

        public int RankOf(SkillId skill) => skillRanks[(int)skill];
        public int NextSkillCostFor(SkillId skill) => Rules.Skill(skill).UpgradeCost(RankOf(skill));
        public int SkillBonus(SkillId skill, SkillEffect effect) => Rules.Skill(skill).BonusAt(RankOf(skill), effect);
        public int YieldMultiplierFor(SkillId skill) => Math.Max(1, 1 + SkillBonus(skill, SkillEffect.YieldMultiplier));

        private void RefreshSkillStats()
        {
            int maxStamina = MaxEnergy;
            int damage = 0;
            foreach (var skill in SkillProgression.All)
            {
                maxStamina += SkillBonus(skill, SkillEffect.MaxStamina);
                damage += SkillBonus(skill, SkillEffect.UnarmedDamage);
            }
            StaminaLimit = Math.Max(1, Math.Min(1000000, maxStamina));
            UnarmedDamageBonus = damage;
        }

        public int WorkEnergyCost(string itemId, int baseCost) => WorkEnergyCost(SkillProgression.ForItem(itemId), itemId, baseCost);
        public int WorkEnergyCost(SkillId skill, string itemId, int baseCost) =>
            (int)Math.Max(1, Math.Min(int.MaxValue,
                (long)baseCost - SkillBonus(skill, SkillEffect.StaminaSaving) - (itemId == ItemIds.Potato && HasHoe ? 1 : 0)));

        public int ScaledYield(SkillId skill, int baseYield, int bonus = 1) =>
            (int)Math.Min(int.MaxValue, (long)baseYield * YieldMultiplierFor(skill) * bonus);

        public bool TryUpgradeSkill() => TryUpgradeSkill(SkillId.Harvesting);

        public bool TryUpgradeSkill(SkillId skill)
        {
            if (!SkillProgression.IsDefined(skill))
                return Result(false, "스킬을 찾을 수 없어요.");
            var profile = Rules.Skill(skill);
            int rank = RankOf(skill);
            if (rank >= SkillProgression.MaxRank)
                return Result(false, $"{profile.Name} 최고 랭크에 도달했어요.");
            int cost = profile.UpgradeCost(rank);
            if (AP < cost)
                return Result(false, $"승급하려면 AP {cost}가 필요해요.");
            AP -= cost;
            skillRanks[(int)skill]++;
            RefreshSkillStats();
            Energy = Math.Min(Energy, StaminaLimit);
            return Result(true, $"{profile.Name} {SkillProgression.RankLabel(rank + 1)} 승급!");
        }

        private SkillRankSave[] ExportSkills()
        {
            var result = new SkillRankSave[skillRanks.Length];
            foreach (var skill in SkillProgression.All)
                result[(int)skill] = new SkillRankSave { skill = skill, rank = RankOf(skill) };
            return result;
        }

        private void RestoreSkills(VillageSave save)
        {
            if (save.schema < 5)
            {
                foreach (var skill in SkillProgression.All)
                    if (SkillProgression.IsGathering(skill))
                        skillRanks[(int)skill] = save.skillRank;
                return;
            }

            if (save.skillRanks == null)
                return;
            var seen = new bool[skillRanks.Length];
            foreach (var entry in save.skillRanks)
            {
                if (entry == null || !SkillProgression.IsDefined(entry.skill) || entry.rank < 0 ||
                    entry.rank > SkillProgression.MaxRank || seen[(int)entry.skill])
                    throw new ArgumentException("Invalid saved skill rank.");
                seen[(int)entry.skill] = true;
                skillRanks[(int)entry.skill] = entry.rank;
            }
        }
    }
}
