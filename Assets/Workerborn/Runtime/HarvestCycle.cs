using System;

namespace Workerborn
{
    // One click starts one attempt. Only a successful harvest spends a charge.
    public sealed class HarvestCycle
    {
        public int Capacity { get; }
        public int Remaining { get; private set; }
        public float Duration { get; }
        public float Elapsed { get; private set; }
        public bool IsWorking { get; private set; }
        public bool IsDepleted => Remaining == 0;
        public float Progress => Math.Min(1f, Elapsed / Duration);

        public HarvestCycle(int capacity, float duration)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(duration));
            Capacity = capacity;
            Remaining = capacity;
            Duration = duration;
        }

        public bool TryBegin()
        {
            if (IsWorking || IsDepleted) return false;
            Elapsed = 0f;
            IsWorking = true;
            return true;
        }

        public bool Advance(float seconds) => Advance(seconds, null, out _);

        internal bool Advance(float seconds, Func<bool> tryGather, out bool gathered)
        {
            gathered = false;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!IsWorking) return false;
            Elapsed = Math.Min(Duration, Elapsed + seconds);
            if (Elapsed < Duration) return false;
            IsWorking = false;
            gathered = tryGather?.Invoke() ?? true;
            if (gathered)
                Remaining--;
            return true;
        }

        public void Cancel()
        {
            IsWorking = false;
            Elapsed = 0f;
        }

        public void RestoreRemaining(int remaining)
        {
            if (remaining < 0 || remaining > Capacity) throw new ArgumentOutOfRangeException(nameof(remaining));
            Cancel();
            Remaining = remaining;
        }
    }
}
