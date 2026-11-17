using System;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class HarvestCycleTests
    {
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        public void SavedRemainingCountDoesNotReplayAHarvest(int remaining)
        {
            var cycle = new HarvestCycle(3, 2f);
            cycle.RestoreRemaining(remaining);
            Assert.That(cycle.Remaining, Is.EqualTo(remaining));
            Assert.That(cycle.Progress, Is.Zero);
            Assert.That(cycle.IsWorking, Is.False);
            Assert.That(cycle.TryBegin(), Is.EqualTo(remaining > 0));
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void InvalidSavedChargesAreRejected(int remaining)
        {
            var cycle = new HarvestCycle(3, 2f);
            Assert.Throws<ArgumentOutOfRangeException>(() => cycle.RestoreRemaining(remaining));
            Assert.That(cycle.Remaining, Is.EqualTo(3));
        }

        [Test]
        public void PartialWorkDoesNotSpendAHarvest()
        {
            var cycle = new HarvestCycle(3, 2f);
            Assert.That(cycle.TryBegin(), Is.True);
            Assert.That(cycle.Advance(0.75f), Is.False);
            Assert.That(cycle.Remaining, Is.EqualTo(3));
            Assert.That(cycle.Progress, Is.EqualTo(0.375f).Within(0.0001f));
        }

        [Test]
        public void CancelPreservesChargesAndNextAttemptStartsFresh()
        {
            var cycle = new HarvestCycle(3, 2f);
            cycle.TryBegin();
            cycle.Advance(1.9f);
            cycle.Cancel();
            Assert.That(cycle.Remaining, Is.EqualTo(3));
            Assert.That(cycle.Progress, Is.Zero);
            Assert.That(cycle.IsWorking, Is.False);
            Assert.That(cycle.TryBegin(), Is.True);
            Assert.That(cycle.Advance(0.2f), Is.False);
            Assert.That(cycle.Advance(1.8f), Is.True);
            Assert.That(cycle.Remaining, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedClickDoesNotRestartWork()
        {
            var cycle = new HarvestCycle(3, 2f);
            cycle.TryBegin();
            cycle.Advance(1f);
            Assert.That(cycle.TryBegin(), Is.False);
            Assert.That(cycle.Progress, Is.EqualTo(0.5f));
            Assert.That(cycle.Advance(1f), Is.True);
        }

        [Test]
        public void CompletionIsReportedOnlyOnce()
        {
            var cycle = new HarvestCycle(3, 2f);
            cycle.TryBegin();
            Assert.That(cycle.Advance(2f), Is.True);
            Assert.That(cycle.Advance(2f), Is.False);
            Assert.That(cycle.Remaining, Is.EqualTo(2));
            Assert.That(cycle.IsWorking, Is.False);
        }

        [Test]
        public void LargeFrameCompletesOnlyTheClickedHarvest()
        {
            var cycle = new HarvestCycle(3, 2f);
            cycle.TryBegin();
            Assert.That(cycle.Advance(100f), Is.True);
            Assert.That(cycle.Remaining, Is.EqualTo(2));
            Assert.That(cycle.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void DepletedPlantCannotStartAndNeverGoesNegative()
        {
            var cycle = new HarvestCycle(3, 2f);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(cycle.TryBegin(), Is.True);
                Assert.That(cycle.Advance(2f), Is.True);
            }
            Assert.That(cycle.IsDepleted, Is.True);
            Assert.That(cycle.TryBegin(), Is.False);
            Assert.That(cycle.Advance(100f), Is.False);
            cycle.Cancel();
            Assert.That(cycle.TryBegin(), Is.False);
            Assert.That(cycle.Remaining, Is.Zero);
        }

        [Test]
        public void PlantsHaveIndependentRemainingHarvests()
        {
            var first = new HarvestCycle(1, 1f);
            var second = new HarvestCycle(4, 3f);
            first.TryBegin();
            first.Advance(1f);
            Assert.That(first.IsDepleted, Is.True);
            Assert.That(second.Remaining, Is.EqualTo(4));
            Assert.That(second.IsWorking, Is.False);
        }

        [Test]
        public void WorkCannotAdvanceBeforeAClick()
        {
            var cycle = new HarvestCycle(3, 2f);
            Assert.That(cycle.Advance(100f), Is.False);
            Assert.That(cycle.Remaining, Is.EqualTo(3));
            Assert.That(cycle.Progress, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidCapacityIsRejected(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HarvestCycle(capacity, 1f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDurationIsRejected(float duration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HarvestCycle(3, duration));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidFrameTimeDoesNotCorruptTheCrop(float seconds)
        {
            var cycle = new HarvestCycle(3, 1f);
            cycle.TryBegin();
            Assert.Throws<ArgumentOutOfRangeException>(() => cycle.Advance(seconds));
            Assert.That(cycle.Remaining, Is.EqualTo(3));
            Assert.That(cycle.Progress, Is.Zero);
        }
    }
}
