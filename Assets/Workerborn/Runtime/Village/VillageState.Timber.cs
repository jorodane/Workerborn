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

        internal int TimberCutPerHit(TimberAction action) => Math.Clamp((action == TimberAction.Fell ? 500 : 1_000) +
            SkillBonus(SkillId.Woodcutting, SkillEffect.TimberCutBasisPoints), 1, TimberWork.FullCut);

        internal int TimberCutAfter(TimberAction action, int progress) => Math.Min(TimberWork.FullCut, progress + TimberCutPerHit(action));

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

        internal void RecordTimberWork(int timber)
        {
            LifetimeHarvests++;
            LifetimeTimber = (int)Math.Min(int.MaxValue, (long)LifetimeTimber + timber);
            AddExperience(Rules.HarvestExperience);
            Version++;
        }
    }
}
