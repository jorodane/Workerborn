using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Workerborn.Tests
{
    public sealed class FoundationFlowTests
    {
        private VillagePrototype game;
        private string saveDirectory;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadNewVillage()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 3;
            saveDirectory = Path.Combine(Application.temporaryCachePath, "workerborn-foundation-test-" + Guid.NewGuid().ToString("N"));
            VillageSaveStore.DirectoryOverride = saveDirectory;
            yield return SceneManager.LoadSceneAsync("Assets/Workerborn/Scenes/VillagePrototype.unity", LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator UnloadVillage()
        {
            Time.timeScale = previousTimeScale;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                var empty = SceneManager.CreateScene("Workerborn foundation cleanup " + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            VillageSaveStore.DirectoryOverride = null;
            if (Directory.Exists(saveDirectory))
                Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator FreshSceneOpensGuideAndBuildsTheSelectedPlotAfterRebirth()
        {
            var state = game.State;
            Assert.That(game.Hud.HasModal, Is.True);
            Assert.That(game.Crops, Is.Empty);
            Assert.That(state.Inventory.Entries, Is.Empty);
            game.Hud.CloseDialogue();

            game.Worker.CommandInteract(game.FindInteraction(VillageRole.Farmer));
            yield return Until(() => game.Hud.HasModal);
            Assert.That(game.Perform(state.TryAcceptFoundation), Is.True);
            game.Hud.CloseDialogue();
            var document = state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.TimberContract);
            game.Hud.ClickInventoryItem(document.Id, Vector2.zero);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<QuestDetailWindowView>(), Is.Not.Null);
            Assert.That(game.Hud.IsHoldingItem, Is.False);

            yield return Gather(ItemIds.Egg, 3);
            Assert.That(game.Perform(() => state.TrySellItems(ItemIds.Egg, 3)), Is.True);
            Assert.That(game.Perform(() => state.TryBuy(ItemIds.Axe)), Is.True);
            Assert.That(game.Perform(() => state.TryEquip(state.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Axe).Id)), Is.True);

            yield return Gather(ItemIds.Timber, state.Rules.Foundation.TimberCount);
            game.Worker.CommandInteract(game.FindInteraction(VillageRole.Farmer));
            yield return Until(() => game.Hud.HasModal);
            Assert.That(game.Perform(state.TryDeliverTimber), Is.True);
            game.Hud.CloseDialogue();
            var site = UnityEngine.Object.FindObjectsByType<VillageFarmSite>(FindObjectsSortMode.None).Single(p => p.SiteId == "south");
            site.RequestInteraction(game.Worker);
            yield return Until(() => state.CanAdvanceEra);

            game.Worker.CommandInteract(game.FindInteraction(VillageRole.Shrine));
            yield return Until(() => game.Hud.HasModal);
            game.AdvanceEra();
            Assert.That(state.Life, Is.EqualTo(2));
            Assert.That(state.Inventory.Entries, Is.Empty);
            Assert.That(state.Equipment, Is.Empty);
            Assert.That(game.Hud.HasModal, Is.True);
            Assert.That(game.Crops.Count, Is.EqualTo(12));
            Assert.That(game.Crops.All(crop => crop.transform.position.z < -10), Is.True);
            game.Hud.CloseDialogue();
            game.Worker.CommandInteract(game.FindInteraction(VillageRole.Farmer));
            yield return Until(() => game.Hud.HasModal);
            Assert.That(game.Perform(state.TryAcceptJob), Is.True);
            Assert.That(state.Inventory.Count(ItemIds.FarmContract), Is.EqualTo(1));
        }

        private IEnumerator Gather(string id, int required)
        {
            var targets = UnityEngine.Object.FindObjectsByType<HarvestableCrop>(FindObjectsSortMode.None).Where(c => c.Definition.ItemId == id);
            foreach (var crop in targets)
                while (crop.CanInteract && !crop.IsDepleted && game.State.Inventory.Count(id) < required)
                {
                    int remaining = crop.Remaining;
                    game.Worker.CommandHarvest(crop);
                    yield return Until(() => crop.Remaining < remaining);
                    foreach (var drop in UnityEngine.Object.FindObjectsByType<GroundPickup>(FindObjectsSortMode.None).Where(p => p.ItemId == id))
                    {
                        if (game.State.Inventory.Count(id) >= required)
                            break;
                        game.Worker.CommandPickup(drop);
                        yield return Until(() => drop == null || !drop.CanCollect);
                    }
                }
            Assert.That(game.State.Inventory.Count(id), Is.GreaterThanOrEqualTo(required));
        }

        private static IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, "A village interaction did not finish.");
        }
    }
}
