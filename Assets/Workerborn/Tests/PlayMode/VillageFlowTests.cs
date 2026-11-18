using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Workerborn.Tests
{
    public sealed class VillageFlowTests
    {
        private VillagePrototype game;
        private string saveDirectory;
        private const string Scene = "Assets/Workerborn/Scenes/VillagePrototype.unity";
        [UnitySetUp]
        public IEnumerator LoadVillage()
        {
            saveDirectory = Path.Combine(Application.temporaryCachePath, "workerborn-test-" + Guid.NewGuid().ToString("N"));
            VillageSaveStore.DirectoryOverride = saveDirectory;
            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game, Is.Not.Null);
            Assert.That(game.Worker, Is.Not.Null);
            Assert.That(game.Crops.Count, Is.EqualTo(12));
            Assert.That(game.Interactables.Count, Is.EqualTo(5));
        }

        [UnityTearDown]
        public IEnumerator LeaveVillage()
        {
            // Unload the save-writing component before restoring the player's normal save path.
            if (game != null)
            {
                var scene = game.gameObject.scene;
                var empty = SceneManager.CreateScene("Workerborn test cleanup " + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }

            VillageSaveStore.DirectoryOverride = null;
            if (Directory.Exists(saveDirectory))
                Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator WalkToNpcAndPauseTimeDuringKeywordConversation()
        {
            var farmer = game.FindInteraction(VillageRole.Farmer);
            game.Worker.CommandInteract(farmer);
            yield return Until(() => game.Hud.HasModal, 8f);
            float minute = game.State.Minute;
            yield return new WaitForSeconds(0.15f);
            Assert.That(game.State.Minute, Is.EqualTo(minute));
            Assert.That(game.State.IntroAccepted, Is.True);
            Assert.That(game.Perform(game.State.TryAcceptJob), Is.True);
            game.Hud.CloseDialogue();
            yield return new WaitForSeconds(0.15f);
            Assert.That(game.State.Minute, Is.GreaterThan(minute));
        }

        [UnityTest]
        public IEnumerator HarvestPickUpDeliverClaimQuestAndRebirth()
        {
            Assert.That(game.Perform(game.State.TryAcceptJob), Is.True);
            for (int i = 0; i < 3; i++)
            {
                var crop = game.Crops[i];
                game.Worker.CommandHarvest(crop);
                yield return Until(() => crop.Remaining == crop.Capacity - 1, 12f);
            }

            while (game.State.Inventory.Count(ItemIds.Potato) < 6)
            {
                var item = UnityEngine.Object.FindFirstObjectByType<GroundPickup>();
                Assert.That(item, Is.Not.Null);
                int before = game.State.Inventory.Count(ItemIds.Potato);
                game.Worker.CommandPickup(item);
                yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) > before, 8f);
            }

            game.Worker.CommandInteract(game.FindInteraction(VillageRole.Farmer));
            yield return Until(() => game.Hud.HasModal, 10f);
            Assert.That(game.Perform(game.State.TryDeliverJob), Is.True);
            Assert.That(game.Perform(game.State.TryClaimIntroQuest), Is.True);
            Assert.That(game.State.CanRebirth, Is.True);
            Assert.That(game.Perform(game.State.TryUpgradeSkill), Is.True);
            int rank = game.State.SkillRank;
            int ap = game.State.AP;
            game.Hud.CloseDialogue();
            game.Rebirth();
            Assert.That(game.State.Life, Is.EqualTo(2));
            Assert.That(game.State.Level, Is.EqualTo(1));
            Assert.That(game.State.Gold, Is.Zero);
            Assert.That(game.State.Inventory.Entries.Count, Is.Zero);
            Assert.That(game.State.SkillRank, Is.EqualTo(rank));
            Assert.That(game.State.AP, Is.EqualTo(ap));
            Assert.That(game.State.Career, Is.EqualTo(1));
            Assert.That(game.GroundItemCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SaveReloadPreservesBagTimeAndDepletedCrops()
        {
            game.State.Inventory.TryAdd(ItemIds.Potato, 7);
            game.Crops[0].RestoreRemaining(0);
            game.Crops[1].RestoreRemaining(2);
            game.State.AdvanceTime(10f);
            Assert.That(game.SaveGame(false), Is.True);
            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(7));
            Assert.That(game.Crops[0].Remaining, Is.Zero);
            Assert.That(game.Crops[1].Remaining, Is.EqualTo(2));
            Assert.That(game.State.Minute, Is.GreaterThanOrEqualTo(510f));
            Assert.That(game.State.Minute, Is.LessThan(520f));
        }

        [UnityTest]
        public IEnumerator PendingWorldUpgradeBuildsTwentyFourPlantsAfterRebirth()
        {
            Directory.CreateDirectory(saveDirectory);
            var save = new VillageSave
            {
                level = 3,
                totalLevel = 3,
                introAccepted = true,
                introClaimed = true,
                career = 1,
                donatedGold = 100,
                donatedPotatoes = 20,
                farmUpgradePending = true
            };
            File.WriteAllText(VillageSaveStore.SavePath, JsonUtility.ToJson(save));
            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game.State.FarmUpgradePending, Is.True);
            Assert.That(game.Crops.Count, Is.EqualTo(12));
            game.Rebirth();
            yield return null;
            Assert.That(game.State.FarmLevel, Is.EqualTo(2));
            Assert.That(game.Crops.Count, Is.EqualTo(24));
            Assert.That(game.State.OfferedJobRequirement, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator PrefabWindowsBindWithoutRebuildingTheirAuthoredContent()
        {
            foreach (string id in new[]
            {
                "inventory",
                "skills",
                "quests",
                "questdetail",
                "character",
                "map",
                "system",
                "shop"
            }

            )
            {
                var view = game.Hud.OpenWindow(id);
                Assert.That(view, Is.Not.Null, id);
                view.Refresh();
                yield return null;
                Assert.That(view.transform.Find("Body"), Is.Not.Null, id);
                game.Hud.ToggleWindow(id);
            }

            var farmer = game.FindInteraction(VillageRole.Farmer);
            game.Hud.OpenDialogue(farmer);
            yield return null;
            Assert.That(game.Hud.HasModal, Is.True);
            Assert.That(game.Hud.OpenWindow("conversation").GetComponent<ConversationWindowView>(), Is.Not.Null);
            game.Hud.CloseDialogue();
        }

        [UnityTest]
        public IEnumerator ClickingAnItemNameUsesTheSamePickupCommandAsTheWorldItem()
        {
            var end = game.Worker.transform.position + Vector3.forward * .6f;
            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            item.Initialize(game.Crops[0].Definition, end, end, true);
            var prefab = game.Hud.Library.PickupName;
            var view = UnityEngine.Object.Instantiate(prefab, game.Hud.transform);
            view.Bind(item, game.Worker);
            view.SetBox(true);
            int before = game.State.Inventory.Count(ItemIds.Potato);
            view.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
            yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) > before, 4f);
            Assert.That(item == null || !item.gameObject.activeSelf, Is.True);
            UnityEngine.Object.Destroy(view.gameObject);
        }

        [UnityTest]
        public IEnumerator WheelButtonOrbitDoesNotCancelTheWorkersCommand()
        {
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            try
            {
                game.Worker.CommandHarvest(game.Crops[0]);
                var before = game.WorldCamera.transform.rotation;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = new Vector2(500, 400), buttons = 4 });
                yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = new Vector2(620, 430), delta = new Vector2(120, 30), buttons = 4 });
                yield return null;
                Assert.That(Quaternion.Angle(before, game.WorldCamera.transform.rotation), Is.GreaterThan(5f));
                Assert.That(game.Worker.Action, Is.Not.EqualTo(WorkerAction.Idle));
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            }
        }

        private static IEnumerator Until(Func<bool> condition, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, "The expected village interaction did not finish before the timeout.");
        }
    }
}
