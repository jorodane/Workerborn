using System;

namespace Workerborn
{
    public sealed partial class VillageState
    {
        internal int TreeExtraSlots => Math.Clamp(SkillBonus(SkillId.Woodcutting, SkillEffect.TreeSlots), 0, 32);
        internal int TreeChanceBonus => SkillBonus(SkillId.Woodcutting, SkillEffect.TreeChanceBasisPoints);
        internal int TreeYieldBonus => Math.Clamp(SkillBonus(SkillId.Woodcutting, SkillEffect.TreeYieldPercent), 0, 1000);
        internal int LiftCapacity => Math.Clamp(SkillBonus(SkillId.Lifting, SkillEffect.LiftCapacity), 0, 12);
        internal int LiftSpeedPercent => Math.Clamp(SkillBonus(SkillId.Lifting, SkillEffect.LiftSpeedPercent), 40, 100);
        internal int LiftStaminaSaving => Math.Clamp(SkillBonus(SkillId.Lifting, SkillEffect.LiftStaminaSaving), 0, 20);
        internal float ThrowRange => Math.Clamp(SkillBonus(SkillId.Throwing, SkillEffect.ThrowRangeDecimeters), 0, 250) * .1f;

        internal bool TreeWorkSucceeds(float baseChance, float roll)
        {
            float chance = Math.Clamp(baseChance + TreeChanceBonus / 10_000f, 0f, 1f);
            return chance >= 1f || roll < chance;
        }

        internal int TreeYield(int quantity, float roll)
        {
            int scaled = quantity * (100 + TreeYieldBonus);
            return scaled / 100 + (roll < (scaled % 100) / 100f ? 1 : 0);
        }

        internal void SpendTimberEnergy(int cost)
        {
            Energy -= Math.Min(Energy, Math.Max(0, cost));
            Version++;
        }

        internal void FinishTimberWork(int cost, bool succeeded, int timber)
        {
            SpendTimberEnergy(cost);
            if (!succeeded)
                return;
            LifetimeHarvests++;
            LifetimeTimber = (int)Math.Min(int.MaxValue, (long)LifetimeTimber + timber);
            AddExperience(Rules.HarvestExperience);
        }
    }
}
