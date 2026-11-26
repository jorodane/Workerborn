using System;

namespace Workerborn
{
    public sealed partial class VillageState
    {
        private const int StaminaSaveSchema = 16;
        internal const int WorkStaminaMultiplier = 3;
        private const double RecoveryDelaySeconds = 1d;
        private const double StaminaTolerance = .000001d;
        private double staminaRecoveryDelay;

        public int Energy => (int)Math.Ceiling(Stamina);
        internal double Stamina { get; private set; } = MaxEnergy;
        internal float StaminaFill => (float)(Stamina / StaminaLimit);
        internal int SeatedRecoveryBonus => Math.Clamp(SkillBonus(SkillId.RestMaster, SkillEffect.SeatedRecoveryPercent), 0, 1000);

        internal double StaminaRecoveryPerSecond(bool sitting)
        {
            double rate = Math.Max(0f, Rules.StaminaRecoveryPerSecond);
            return sitting ? rate * Math.Max(1f, Rules.SeatedRecoveryMultiplier) * (1d + SeatedRecoveryBonus / 100d) : rate;
        }

        internal bool SpendWorkProgress(double totalCost, double previousProgress, double progress)
        {
            bool covered = SpendWorkStamina(totalCost * (progress - previousProgress));
            return covered && (progress >= 1d || Stamina > 0d);
        }

        internal bool SpendWorkStamina(double amount)
        {
            if (amount <= 0d)
                return true;
            staminaRecoveryDelay = RecoveryDelaySeconds;
            bool enough = amount <= Stamina + StaminaTolerance;
            double left = Stamina - amount;
            SetStamina(left <= StaminaTolerance ? 0d : left);
            return enough;
        }

        internal void RecoverStamina(float seconds, bool sitting)
        {
            if (AwaitingIncarnation || Health <= 0 || !float.IsFinite(seconds) || seconds <= 0f)
                return;
            double recoverySeconds = Math.Max(0d, seconds - staminaRecoveryDelay);
            staminaRecoveryDelay = Math.Max(0d, staminaRecoveryDelay - seconds);
            if (recoverySeconds > 0d && Stamina < StaminaLimit)
                SetStamina(Stamina + recoverySeconds * StaminaRecoveryPerSecond(sitting));
        }

        private void SetStamina(double amount) => Stamina = Math.Clamp(amount, 0d, StaminaLimit);

        private void RestoreStamina(double amount)
        {
            SetStamina(amount);
            staminaRecoveryDelay = 0d;
        }
    }
}
