using System;

namespace Workerborn
{
    [Serializable]
    public sealed class SkillRankSave
    {
        public SkillId skill;
        public int rank;
        public bool learned;
    }

    public sealed partial class VillageState
    {
        private readonly int[] skillRanks = new int[SkillProgression.All.Count];
        private readonly bool[] learnedSkills = new bool[SkillProgression.All.Count];
        private const int LearnedSkillSaveSchema = 9;
        private const int BonusSkillsSaveSchema = 14;

        public int SkillRank => RankOf(SkillId.Harvesting);
        public int NextSkillCost => NextSkillCostFor(SkillId.Harvesting);
        public int YieldMultiplier => YieldMultiplierFor(SkillId.Harvesting);
        public int StaminaLimit { get; private set; }
        public int UnarmedDamageBonus { get; private set; }
        public int GreatLuckyChancePercent => Math.Clamp(SkillBonus(SkillId.LuckyBonus, SkillEffect.GreatLuckyChance), 0, 100);
        public int LuckyChancePercent => Math.Clamp(SkillBonus(SkillId.LuckyBonus, SkillEffect.LuckyChance), 0, 100 - GreatLuckyChancePercent);
        public int ExperienceGainPercent => Math.Max(0, SkillBonus(SkillId.SoulGrowth, SkillEffect.ExperienceGainPercent));
        public float CriticalChance => Math.Clamp(SkillBonus(SkillId.CriticalHit, SkillEffect.CriticalChanceBasisPoints), 0, 10_000) / 10_000f;
        public int CriticalDamagePercent => Math.Max(100, SkillBonus(SkillId.CriticalHit, SkillEffect.CriticalDamagePercent));

        public int RankOf(SkillId skill) => skillRanks[(int)skill];
        public bool IsSkillLearned(SkillId skill) => SkillProgression.IsDefined(skill) && learnedSkills[(int)skill];
        public int NextSkillCostFor(SkillId skill) => Rules.Skill(skill).UpgradeCost(RankOf(skill));
        public int NextSkillGradeRankFor(SkillId skill) => IsSkillLearned(skill) ? SkillProgression.RankForNextGrade(RankOf(skill)) : 0;
        public int NextSkillGradeCostFor(SkillId skill) => IsSkillLearned(skill)
            ? Rules.Skill(skill).UpgradeCost(RankOf(skill), NextSkillGradeRankFor(skill)) : 0;
        public int SkillBonus(SkillId skill, SkillEffect effect) =>
            IsSkillLearned(skill) ? Rules.Skill(skill).BonusAt(RankOf(skill), effect) : 0;
        public int YieldMultiplierFor(SkillId skill) => Math.Max(1, 1 + SkillBonus(skill, SkillEffect.YieldMultiplier));

        private void RefreshSkillStats()
        {
            int maxStamina = MaxEnergy + (Level - 1) * Rules.StaminaPerLevel;
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
                (long)baseCost - Dexterity / 5 - SkillBonus(skill, SkillEffect.StaminaSaving) - (itemId == ItemIds.Potato && HasHoe ? 1 : 0)));

        public int ScaledYield(SkillId skill, int baseYield, int bonus = 1) =>
            (int)Math.Min(int.MaxValue, (long)baseYield * YieldMultiplierFor(skill) * bonus);

        internal WorkOutcome RollGatheringBonus(float roll)
        {
            int great = GreatLuckyChancePercent;
            int combined = great + LuckyChancePercent;
            if (great > 0 && (great == 100 || roll < great * .01f))
                return WorkOutcome.GreatLucky;
            return combined > 0 && (combined == 100 || roll < combined * .01f) ? WorkOutcome.Lucky : WorkOutcome.Success;
        }

        public int ScaledExperience(int baseExperience) =>
            (int)Math.Min(int.MaxValue, (long)Math.Max(0, baseExperience) * ExperienceGainPercent / 100);

        internal int ResolveCombatDamage(int baseDamage, float roll, out bool critical)
        {
            int damage = Math.Max(0, baseDamage);
            float chance = CriticalChance;
            critical = damage > 0 && chance > 0f && (chance >= 1f || roll < chance);
            return critical
                ? (int)Math.Min(int.MaxValue, ((long)damage * CriticalDamagePercent + 50) / 100)
                : damage;
        }

        public bool TryUpgradeSkill() => TryUpgradeSkill(SkillId.Harvesting);

        public bool TryUpgradeSkill(SkillId skill) => TryUpgradeSkill(skill, toNextGrade: false);
        public bool TryUpgradeSkillToNextGrade(SkillId skill) => TryUpgradeSkill(skill, toNextGrade: true);

        private bool TryUpgradeSkill(SkillId skill, bool toNextGrade)
        {
            if (!SkillProgression.IsDefined(skill))
                return Result(false, "스킬을 찾을 수 없어요.");
            var profile = Rules.Skill(skill);
            if (!IsSkillLearned(skill))
                return Result(false, $"{profile.Name} 스킬을 먼저 배워 주세요.");
            int rank = RankOf(skill);
            if (rank >= SkillProgression.MaxRank)
                return Result(false, $"{profile.Name} 최고 랭크에 도달했어요.");
            int targetRank = toNextGrade ? SkillProgression.RankForNextGrade(rank) : rank + 1;
            if (targetRank == 0)
                return Result(false, $"{profile.Name} 최고 급수에 도달했어요.");
            int cost = profile.UpgradeCost(rank, targetRank);
            if (AP < cost)
                return Result(false, $"승급하려면 AP {cost}가 필요해요.");
            AP -= cost;
            LearnSkill(skill, targetRank);
            return Result(true, $"{profile.Name} {SkillProgression.RankLabel(targetRank)} 승급!");
        }

        internal bool LearnSkill(SkillId skill, int rank = 0)
        {
            if (!SkillProgression.IsDefined(skill) || rank < 0 || rank > SkillProgression.MaxRank ||
                IsSkillLearned(skill) && RankOf(skill) >= rank)
                return false;
            learnedSkills[(int)skill] = true;
            skillRanks[(int)skill] = rank;
            RefreshSkillStats();
            Energy = Math.Min(Energy, StaminaLimit);
            return true;
        }

        public bool TryReadSkillBook(int entryId)
        {
            if (AwaitingIncarnation)
                return Result(false, "지금은 책을 읽을 수 없어요.");
            var book = Inventory.Find(entryId);
            if (!(book?.Spec.TaughtSkill is SkillId skill) || !SkillProgression.IsDefined(skill))
                return Result(false, "읽을 스킬 책을 찾을 수 없어요.");
            if (IsSkillLearned(skill))
                return Result(false, "이미 배운 스킬이에요.");
            if (!Inventory.TryRemoveAt(entryId, 1))
                return false;
            LearnSkill(skill);
            return Result(true, $"{Rules.Skill(skill).Name} 스킬을 배웠어요.");
        }

        private SkillRankSave[] ExportSkills()
        {
            var result = new SkillRankSave[skillRanks.Length];
            foreach (var skill in SkillProgression.All)
                result[(int)skill] = new SkillRankSave { skill = skill, rank = RankOf(skill), learned = IsSkillLearned(skill) };
            return result;
        }

        private void RestoreSkills(CharacterSave save)
        {
            // Older characters already had access to the practice ranks available at the time.
            if (save.schema < LearnedSkillSaveSchema)
                foreach (var skill in SkillProgression.All)
                    learnedSkills[(int)skill] = (int)skill < (int)SkillId.HarvestAcquisition;
            if (save.schema < 5)
            {
                foreach (var skill in SkillProgression.All)
                    if (skill != SkillId.Fishing && SkillProgression.IsGathering(skill))
                        skillRanks[(int)skill] = save.skillRank;
                return;
            }

            if (save.skillRanks == null)
                return;
            var seen = new bool[skillRanks.Length];
            foreach (var entry in save.skillRanks)
            {
                if (save.schema < HarvestAcquisitionSaveSchema && entry?.skill == SkillId.HarvestAcquisition)
                    continue;
                if (save.schema < BonusSkillsSaveSchema && entry != null &&
                    (int)entry.skill >= (int)SkillId.LuckyBonus && SkillProgression.IsDefined(entry.skill))
                    continue;
                if (entry == null || !SkillProgression.IsDefined(entry.skill) || entry.rank < 0 ||
                    entry.rank > SkillProgression.MaxRank || seen[(int)entry.skill] ||
                    save.schema >= LearnedSkillSaveSchema && entry.rank > 0 && !entry.learned)
                    throw new ArgumentException("Invalid saved skill rank.");
                seen[(int)entry.skill] = true;
                skillRanks[(int)entry.skill] = entry.rank;
                if (save.schema >= LearnedSkillSaveSchema)
                    learnedSkills[(int)entry.skill] = entry.learned;
            }
        }
    }
}
