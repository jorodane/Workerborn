using UnityEngine;

namespace Workerborn
{
    public sealed partial class HarvestWorker
    {
        // The hand reaches the mouth a third of the way through the authored eat clip.
        internal const float MealBitePoint = .35f;
        private int mealEntryId;
        private float mealStartedAt;
        private bool tookBite;
        internal string EatingItemId { get; private set; }
        internal bool IsEating => EatingItemId != null;

        internal bool CommandEat(int entryId)
        {
            if (IsEating || village == null || village.IsWorldTransitioning || !AcceptsMovementCommands ||
                CarriedLogs > 0 || restingVisual != null)
                return false;
            var entry = CharacterState.Inventory.Find(entryId);
            if (!CharacterState.CanEatFood(entry, out string message))
            {
                if (IsLocallyControlled)
                    village.Hud.ShowToast(message);
                return false;
            }
            if (IsStandingUp)
                return DeferUntilStanding(() => CommandEat(entryId));

            if (Action != WorkerAction.Sitting)
                Cancel();
            FinishVisualJump();
            mealEntryId = entryId;
            EatingItemId = entry.ItemId;
            mealStartedAt = Time.time;
            tookBite = false;
            if (animationDriver != null)
            {
                animationDriver.SetGesture(WorkerGesture.Eat);
                animationDriver.SampleMeal(0f);
                animationDriver.ShowMeal(village.Settings.FindItem(EatingItemId), actor?.VisualRoot, actor?.Appearance);
                actor?.SetMealInHand(animationDriver.Gesture == WorkerGesture.Eat);
            }
            return true;
        }

        private void UpdateEating()
        {
            if (!IsEating)
                return;
            if (CharacterState is not { Health: > 0, AwaitingIncarnation: false } || village.IsWorldTransitioning)
            {
                StopEating();
                return;
            }
            float phase = (Time.time - mealStartedAt) / (animationDriver?.EatingSeconds ?? 2f);
            if (!tookBite && phase >= MealBitePoint)
            {
                if (IsLocallyControlled && village.AwaitingWorldCommand)
                {
                    mealStartedAt += Time.deltaTime;
                    return;
                }
                tookBite = true;
                var entry = CharacterState.Inventory.Find(mealEntryId);
                if (entry?.ItemId != EatingItemId || !(IsLocallyControlled
                        ? village.Perform(() => CharacterState.TryEatFood(mealEntryId))
                        : CharacterState.TryEatFood(mealEntryId)))
                {
                    StopEating();
                    return;
                }
            }

            if (phase >= 1f)
                StopEating();
            else
                animationDriver?.SampleMeal(phase);
        }

        private void StopEating()
        {
            if (!IsEating)
                return;
            EatingItemId = null;
            if (animationDriver?.Gesture == WorkerGesture.Eat)
                animationDriver.SetGesture(WorkerGesture.None);
            actor?.SetMealInHand(false);
        }
    }
}
