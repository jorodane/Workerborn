using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Workerborn.Tests
{
    public sealed class HarvestFlowTests
    {
        private HarvestPrototype farm;

        [UnitySetUp]
        public IEnumerator LoadFarm()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Workerborn/Scenes/HarvestPrototype.unity", LoadSceneMode.Single);
            yield return null;
            farm = UnityEngine.Object.FindFirstObjectByType<HarvestPrototype>();
            Assert.That(farm, Is.Not.Null);
            Assert.That(farm.Worker, Is.Not.Null);
            Assert.That(farm.Crops.Count, Is.EqualTo(12));
        }

        [UnityTest]
        public IEnumerator ClickWalkHarvestDropAndPickUp()
        {
            var crop = farm.Crops[0];
            farm.Worker.CommandHarvest(crop);
            yield return Until(() => farm.Worker.ActiveCrop == crop, 6f);
            Assert.That(crop.Remaining, Is.EqualTo(crop.Capacity), "Walking must not consume a harvest.");
            yield return Until(() => crop.Remaining == crop.Capacity - 1, 5f);
            Assert.That(farm.GroundItemCount, Is.EqualTo(crop.Definition.ItemsPerHarvest));
            Assert.That(farm.Worker.TotalCollected, Is.Zero, "Harvesting must not add drops straight to the bag.");

            var item = UnityEngine.Object.FindFirstObjectByType<GroundPickup>();
            farm.Worker.CommandPickup(item);
            yield return Until(() => farm.Worker.TotalCollected == 1, 6f);
            Assert.That(farm.GroundItemCount, Is.EqualTo(crop.Definition.ItemsPerHarvest - 1));
            if (item != null) Assert.That(item.TryCollect(), Is.False, "A drop can only be collected once.");
        }

        [UnityTest]
        public IEnumerator ChangingTargetCancelsWithoutConsumingAHarvest()
        {
            var crop = farm.Crops[0];
            farm.Worker.CommandHarvest(crop);
            yield return Until(() => farm.Worker.ActiveCrop == crop, 6f);
            yield return new WaitForSeconds(0.35f);
            farm.Worker.CommandMove(new Vector3(0f, 0f, -4.4f));
            Assert.That(crop.Remaining, Is.EqualTo(crop.Capacity));
            Assert.That(crop.Progress, Is.Zero);
            Assert.That(farm.GroundItemCount, Is.Zero);

            farm.Worker.CommandHarvest(crop);
            yield return Until(() => crop.Remaining == crop.Capacity - 1, 10f);
            Assert.That(farm.GroundItemCount, Is.EqualTo(crop.Definition.ItemsPerHarvest));
        }

        [UnityTest]
        public IEnumerator ExhaustionBlocksExtraHarvestAndResetRestoresTheFarm()
        {
            var crop = farm.Crops[0];
            for (int remaining = crop.Capacity - 1; remaining >= 0; remaining--)
            {
                int expected = remaining;
                farm.Worker.CommandHarvest(crop);
                yield return Until(() => crop.Remaining == expected, 10f);
            }
            int drops = farm.GroundItemCount;
            farm.Worker.CommandHarvest(crop);
            yield return new WaitForSeconds(0.5f);
            Assert.That(crop.Remaining, Is.Zero);
            Assert.That(farm.GroundItemCount, Is.EqualTo(drops));
            Assert.That(farm.Worker.ActiveCrop, Is.Null);

            farm.ResetFarm();
            Assert.That(crop.Remaining, Is.EqualTo(crop.Capacity));
            Assert.That(farm.GroundItemCount, Is.Zero);
            Assert.That(farm.Worker.TotalCollected, Is.Zero);
            Assert.That(farm.Worker.Action, Is.EqualTo(WorkerAction.Idle));
        }

        private static IEnumerator Until(Func<bool> condition, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "The expected harvest interaction did not finish before the timeout.");
        }
    }
}
