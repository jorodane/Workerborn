using System;

namespace Workerborn
{
    [Serializable]
    public sealed class NutritionRules
    {
        public int fullnessCapacity = 100;
        public float overeatingThreshold = .70f;
        public float digestionPerHour = 12f;
        public float nourishmentLossPerHour = .05f;
        public float hungryStaminaFraction = .55f;
        public int breadFullness = 20;
        public float breadNourishment = .33f;
    }

    public sealed partial class VillageState
    {
        private const int NutritionSaveSchema = 19;
        internal const double FedNourishment = 1.10d;
        private double nourishment = FedNourishment;
        private double nutritionAt;
        private double fullness;
        private int restedStaminaLimit;

        public int Fullness => (int)Math.Ceiling(fullness);
        public int FullnessLimit => Math.Max(1, Rules.Nutrition.fullnessCapacity);
        internal float FullnessFill => (float)(fullness / FullnessLimit);
        private double OvereatingThreshold => FullnessLimit * Math.Clamp(Rules.Nutrition.overeatingThreshold, 0f, .99f);
        internal float Overeating => (float)Math.Clamp((fullness - OvereatingThreshold) / (FullnessLimit - OvereatingThreshold), 0d, 1d);
        internal float ActionSpeed => 1f - .35f * Overeating;
        public int WellFedStaminaLimit => restedStaminaLimit;
        public int MinimumStaminaLimit => Math.Min(restedStaminaLimit, Math.Max(1,
            (int)Math.Ceiling(restedStaminaLimit * Math.Clamp(Rules.Nutrition.hungryStaminaFraction, 0f, 1f))) + Grit);

        private void RefreshUsableStaminaLimit()
        {
            int floor = MinimumStaminaLimit;
            StaminaLimit = floor + (int)Math.Round((restedStaminaLimit - floor) * Math.Min(1d, nourishment));
            SetStamina(Stamina);
        }

        private void SyncNutrition()
        {
            double elapsedHours = (WorldMinute - nutritionAt) / 60d;
            if (elapsedHours <= 0d)
                return;
            nutritionAt = WorldMinute;
            if (AwaitingIncarnation)
                return;
            var rules = Rules.Nutrition;
            fullness = Math.Max(0d, fullness - elapsedHours * Math.Max(0f, rules.digestionPerHour) * (1d + Digestion * .02d));
            nourishment = Math.Max(0d, nourishment - elapsedHours * Math.Max(0f, rules.nourishmentLossPerHour));
            RefreshUsableStaminaLimit();
        }

        private void RestoreNutritionForNewLife()
        {
            nutritionAt = WorldMinute;
            nourishment = FedNourishment;
            fullness = 0d;
        }

        public bool TryEatBread()
        {
            if (AwaitingIncarnation)
                return Result(false, "지금은 음식을 먹을 수 없어요.");
            SyncNutrition();
            double serving = Math.Max(1, Rules.Nutrition.breadFullness);
            if (fullness + serving > FullnessLimit + .0001d)
                return Result(false, "배가 불러요. 조금 소화한 뒤에 먹을 수 있어요.");
            if (!Inventory.TryRemove(ItemIds.Bread, 1))
                return Result(false, "가방에 빵이 없어요.");
            fullness = Math.Min(FullnessLimit, fullness + serving);
            int previous = StaminaLimit;
            nourishment = Math.Min(FedNourishment, nourishment + Math.Max(0f, Rules.Nutrition.breadNourishment));
            RefreshUsableStaminaLimit();
            string effect = StaminaLimit > previous ? $" 최대 스태미나가 {StaminaLimit - previous} 올랐어요." : "";
            string overeating = Overeating > 0f ? " 너무 많이 먹은 듯해요." : "";
            return Result(true, "빵을 먹었어요." + effect + overeating);
        }
    }
}
