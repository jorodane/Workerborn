using System;
using System.Collections;
using System.IO;
using System.Linq;
using DistantLands.Cozy;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

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
            Assert.That(VillageSaveStore.TrySave(new VillageSave { schema = 2 }, out _), Is.True);
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
        public IEnumerator SleepUsesTheNewDaysWeatherAndReloadKeepsIt()
        {
            var forecast = UnityEngine.Object.FindFirstObjectByType<VillageWeather>();
            var sky = UnityEngine.Object.FindFirstObjectByType<CozyWeather>();
            Assert.That(forecast, Is.Not.Null);
            Assert.That(sky, Is.Not.Null);
            var startingProfile = forecast.ProfileForDay(game.State.Day);
            Assert.That(sky.weatherModule.ecosystem.currentWeather, Is.SameAs(startingProfile));

            int nextChange = Enumerable.Range(game.State.Day + 1, 60)
                .First(day => forecast.ProfileForDay(day) != startingProfile);
            for (int day = game.State.Day + 1; day <= nextChange; day++)
            {
                game.Sleep(false);
                Assert.That(game.State.Day, Is.EqualTo(day));
                yield return null;
            }

            var expected = forecast.ProfileForDay(nextChange);
            Assert.That(sky.weatherModule.ecosystem.currentWeather, Is.SameAs(expected));
            Assert.That((float)sky.timeModule.currentTime * 1440f, Is.EqualTo(game.State.Minute).Within(.1f));
            Assert.That(game.SaveGame(false), Is.True);

            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            sky = UnityEngine.Object.FindFirstObjectByType<CozyWeather>();
            Assert.That(game.State.Day, Is.EqualTo(nextChange));
            Assert.That(sky.weatherModule.ecosystem.currentWeather, Is.SameAs(expected));
        }

        [UnityTest]
        public IEnumerator RabbitRequiresAFreshClickForEachStrikeAndDropsLootOnDefeat()
        {
            var rabbit = UnityEngine.Object.FindObjectsByType<MonsterInteractable>(FindObjectsSortMode.None)
                .Single(animal => animal.Combat != null);
            var approach = rabbit.transform.position + Vector3.right * .75f;
            Assert.That(NavMesh.SamplePosition(approach, out var at, .4f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(at.position);
            int health = rabbit.Health;
            int stamina = game.State.Energy;
            int drops = game.GroundItemCount;
            rabbit.RequestInteraction(game.Worker);
            yield return Until(() => rabbit.Health < health, 3f);
            int remaining = rabbit.Health;
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Attacking));
            rabbit.RequestInteraction(game.Worker);
            yield return Until(() => game.State.Energy < stamina && game.Worker.Action == WorkerAction.Idle, 3f);
            Assert.That(rabbit.Health, Is.EqualTo(remaining), "A click during the delay must not queue a strike.");

            while (!rabbit.IsDefeated)
            {
                int before = rabbit.Health;
                rabbit.RequestInteraction(game.Worker);
                yield return Until(() => rabbit.Health < before && game.Worker.Action == WorkerAction.Idle, 3f);
            }
            Assert.That(rabbit.LootTaken, Is.True);
            Assert.That(rabbit.CanInteract, Is.False);
            Assert.That(game.GroundItemCount, Is.EqualTo(drops + rabbit.Combat.LootCount));
            rabbit.RequestInteraction(game.Worker);
            Assert.That(game.GroundItemCount, Is.EqualTo(drops + rabbit.Combat.LootCount));
        }

        [UnityTest]
        public IEnumerator BareHandedTreeStrikeDropsABranchWithoutSpendingALoggingCharge()
        {
            var tree = game.Crops.OfType<LoggingTree>().First();
            Vector3 approach = tree.transform.position + Vector3.right * 1.75f;
            Assert.That(NavMesh.SamplePosition(approach, out var at, .4f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(at.position);
            int remaining = tree.Remaining;
            Assert.That(tree.InteractionVerbFor(game.Worker), Is.EqualTo("때리기"));
            tree.RequestInteraction(game.Worker);
            yield return Until(() => WorldInteractable.Active.OfType<GroundPickup>().Any(item => item.ItemId == tree.Branch.Id), 3f);
            var branch = WorldInteractable.Active.OfType<GroundPickup>().Single(item => item.ItemId == tree.Branch.Id);
            Assert.That(tree.Remaining, Is.EqualTo(remaining));
            Assert.That(branch.Quantity, Is.EqualTo(1));
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
            for (int i = 0; i < game.Crops.Count && game.State.Inventory.Count(ItemIds.Potato) < game.State.JobRequirement; i++)
            {
                var crop = game.Crops[i];
                game.Worker.CommandHarvest(crop);
                yield return Until(() => crop.Remaining == crop.Capacity - 1, 12f);
                var item = UnityEngine.Object.FindFirstObjectByType<GroundPickup>();
                if (item == null)
                    continue; // A failed work attempt yields no drop.
                int before = game.State.Inventory.Count(ItemIds.Potato);
                game.Worker.CommandPickup(item);
                yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) > before, 8f);
            }

            game.Worker.CommandInteract(game.FindInteraction(VillageRole.Farmer));
            yield return Until(() => game.Hud.HasModal, 10f);
            Assert.That(game.Perform(game.State.TryDeliverJob), Is.True);
            Assert.That(game.Perform(game.State.TryClaimIntroQuest), Is.True);
            for (int day = 0; day <= game.State.Rules.RebirthDays; day++)
                game.State.TrySleep(false);
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
            game.AdvanceEra();
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
        public IEnumerator ClickingAnItemNameApproachesAndCollectsWithinTwoMetres()
        {
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            try
            {
                Vector3 at = NearbyDestination();
                item.Initialize(game.Crops[0].Definition, at, at, true);
                Assert.That(game.Worker.IsInPickupRange(item), Is.False);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.LeftAlt));
                yield return null;
                yield return null;
                PickupNameView view = null;
                foreach (var candidate in UnityEngine.Object.FindObjectsByType<PickupNameView>(FindObjectsSortMode.None))
                    if (candidate.Target == item)
                        view = candidate;
                Assert.That(view, Is.Not.Null);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, view.Rect.TransformPoint(view.Rect.rect.center));
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = screen });
                yield return null;
                Assert.That(game.Targeting.CurrentTarget, Is.SameAs(item));
                int before = game.State.Inventory.Count(ItemIds.Potato);
                int quantity = item.Quantity;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = screen, buttons = 1 });
                yield return null;
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(before));
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
                Assert.That(GameObject.Find("WorldClick(Clone)"), Is.Null);
                yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) == before + quantity, 6f);
                Vector3 offset = game.Worker.transform.position - at;
                offset.y = 0f;
                Assert.That(offset.magnitude, Is.LessThanOrEqualTo(2.001f));
                Assert.That(item == null || !item.gameObject.activeSelf, Is.True);
            }
            finally
            {
                if (item != null)
                    UnityEngine.Object.Destroy(item.gameObject);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator AHoveredMeshUsesTheSamePixelForCollection()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Picking the rendered mesh requires a graphics device.");
            var mouse = InputSystem.AddDevice<Mouse>();
            var pickup = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Egg).GroundPrefab);
            var camera = game.WorldCamera;
            game.CameraRig.enabled = false;
            try
            {
                Vector3 at = game.Worker.transform.position + Vector3.forward;
                pickup.Initialize(null, at, at, true);
                Vector3 cameraPosition = at + new Vector3(0, 2.5f, 2f);
                camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(at + Vector3.up * .8f - cameraPosition));
                yield return null;

                Vector2 pixel = camera.WorldToScreenPoint(pickup.GetComponentInChildren<MeshRenderer>().bounds.center);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel });
                yield return Until(() => game.Targeting.CurrentTarget == pickup, 3f);
                int before = game.State.Inventory.Count(ItemIds.Egg);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel, buttons = 1 });
                yield return Until(() => game.State.Inventory.Count(ItemIds.Egg) == before + 1, 2f);
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Idle));
            }
            finally
            {
                game.CameraRig.enabled = true;
                if (pickup != null)
                    UnityEngine.Object.Destroy(pickup.gameObject);
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator HoverAssistPicksAGapAndRespectsACloserObstacle()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Picking around the rendered mesh requires a graphics device.");
            var mouse = InputSystem.AddDevice<Mouse>();
            var crop = game.Crops[0];
            var assist = crop.GetComponentsInChildren<Collider>().Single(c => c.gameObject.layer == PointerTargeting.HoverAssistLayer);
            var visual = crop.transform.Find("Plant visual");
            Vector3 originalPosition = visual.localPosition;
            var camera = game.WorldCamera;
            var obstacle = new GameObject("Hover assist test obstacle");
            var blocker = obstacle.AddComponent<BoxCollider>();
            blocker.enabled = false;
            game.CameraRig.enabled = false;
            try
            {
                // Expose a gap under the pointer while keeping the target's mesh visible beside it.
                visual.localPosition += Vector3.right * 2f;
                Physics.SyncTransforms();
                Vector3 center = assist.bounds.center;
                camera.transform.SetPositionAndRotation(center + Vector3.up * 4f,
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
                Vector2 pixel = camera.WorldToScreenPoint(center);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel });
                yield return Until(() => game.Targeting.CurrentTarget == crop, 3f);
                Assert.That(game.Targeting.TryDropPoint(pixel, out _), Is.True);

                obstacle.transform.position = center + Vector3.up * 1.3f;
                blocker.enabled = true;
                Physics.SyncTransforms();
                yield return Until(() => game.Targeting.CurrentTarget == null, 2f);

                blocker.enabled = false;
                Physics.SyncTransforms();
                yield return Until(() => game.Targeting.CurrentTarget == crop, 2f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel, buttons = 1 });
                yield return Until(() => game.Worker.LastCrop == crop, 2f);
            }
            finally
            {
                visual.localPosition = originalPosition;
                game.CameraRig.enabled = true;
                UnityEngine.Object.Destroy(obstacle);
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator AnUnreachableGroundClickKeepsTheCurrentRouteAndStaysQuiet()
        {
            game.Worker.CommandMove(NearbyDestination());
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
            Assert.That(GameObject.Find("WorldClick(Clone)"), Is.Not.Null);
            var agent = game.Worker.GetComponent<NavMeshAgent>();
            var destination = agent.destination;
            int messages = 0;
            void OnMessage(string message) => messages++;
            game.Worker.Message += OnMessage;
            try
            {
                game.Worker.CommandMove(new Vector3(10000, 0, 10000));
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
                Assert.That(agent.destination, Is.EqualTo(destination));
                Assert.That(messages, Is.Zero);
                yield return null;
            }
            finally
            {
                game.Worker.Message -= OnMessage;
            }
        }

        [UnityTest]
        public IEnumerator PickupDuringHarvestDoesNotMoveOrCancelTheWorkerEvenDuringDropFlight()
        {
            var crop = game.Crops[0];
            game.Worker.CommandHarvest(crop);
            yield return Until(() => game.Worker.Action == WorkerAction.Harvesting, 10f);
            var at = game.Worker.transform.position;
            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            Vector3 start = at + Vector3.up * 4;
            Vector3 elevated = at + Vector3.up * 3;
            item.Initialize(crop.Definition, start, elevated + Vector3.right * 2.1f);
            Assert.That(game.TryCollectPickup(item), Is.False);
            item.Initialize(crop.Definition, start, elevated + Vector3.right * 1.9f);
            int before = game.State.Inventory.Count(ItemIds.Potato);
            item.RequestInteraction(game.Worker);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(before + item.Quantity));
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Harvesting));
            Assert.That(game.Worker.ActiveCrop, Is.SameAs(crop));
            Assert.That(game.Worker.transform.position, Is.EqualTo(at));
            Assert.That(item.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator DepletedCropLeavesRaycastsAndRestoresItsInteractionAfterReset()
        {
            var crop = game.Crops[0];
            var collider = crop.GetComponent<Collider>();
            var assist = crop.GetComponentsInChildren<Collider>().Single(c => c.gameObject.layer == PointerTargeting.HoverAssistLayer);
            crop.RestoreRemaining(0);
            Physics.SyncTransforms();
            Assert.That(crop.CanInteract, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(assist.enabled, Is.False);
            foreach (var hit in Physics.RaycastAll(crop.transform.position + Vector3.up * 4, Vector3.down, 5, ~(1 << 2), QueryTriggerInteraction.Collide))
                Assert.That(hit.collider.GetComponentInParent<HarvestableCrop>(), Is.Not.SameAs(crop));
            crop.ResetHarvests();
            Assert.That(crop.CanInteract, Is.True);
            Assert.That(collider.enabled, Is.True);
            Assert.That(assist.enabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NameplateColorsAndPointerAreaOnlyActivateWithAlt()
        {
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var view = UnityEngine.Object.Instantiate(game.Hud.Library.NpcName, game.Hud.transform);
            try
            {
                view.Bind(game.FindInteraction(VillageRole.Farmer), null);
                var background = view.GetComponent<UnityEngine.UI.Image>();
                var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData.InputButton.Left
                };
                view.SetBox(true);
                view.OnPointerEnter(pointer);
                Assert.That(background.color.a, Is.Zero);
                Assert.That(background.raycastTarget, Is.False);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.LeftAlt));
                yield return null;
                view.SetBox(true);
                Color normal = background.color;
                view.OnPointerEnter(pointer);
                Color hover = background.color;
                view.OnPointerDown(pointer);
                Color pressed = background.color;
                Assert.That(hover, Is.Not.EqualTo(normal));
                Assert.That(pressed, Is.Not.EqualTo(hover));
                view.OnPointerUp(pointer);
                Assert.That(background.color, Is.EqualTo(hover));
                view.OnPointerExit(pointer);
                Assert.That(background.color, Is.EqualTo(normal));
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return null;
                Assert.That(background.color.a, Is.Zero);
                Assert.That(background.raycastTarget, Is.False);
            }
            finally
            {
                UnityEngine.Object.Destroy(view.gameObject);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator RepeatedMoveCommandsKeepTheWalkAnimationPhase()
        {
            Vector3 at = game.Worker.transform.position;
            Vector3 goal = at + Vector3.forward * 3;
            goal.y = game.GroundTerrain.SampleHeight(goal) + game.GroundTerrain.transform.position.y;
            game.Worker.CommandMove(goal);
            yield return null;
            var animation = game.Worker.GetComponent<WorkerAnimation>();
            Assert.That(animation.CurrentAction, Is.EqualTo(1));
            double phase = animation.ActionTime;
            game.Worker.CommandMove(goal + Vector3.right * .1f);
            Assert.That(animation.CurrentAction, Is.EqualTo(1));
            Assert.That(animation.ActionTime, Is.EqualTo(phase));
            yield return null;
            Assert.That(animation.CurrentAction, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OverlappingPickupNamesOnlyMoveApartWhileAltIsHeld()
        {
            var first = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            var second = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            try
            {
                Vector3 at = game.Worker.transform.position + Vector3.right;
                first.Initialize(game.Crops[0].Definition, at, at, true);
                second.Initialize(game.Crops[0].Definition, at, at, true);
                yield return null;
                yield return null;
                PickupNameView one = null, two = null;
                foreach (var view in UnityEngine.Object.FindObjectsByType<PickupNameView>(FindObjectsSortMode.None))
                {
                    if (view.Target == first)
                        one = view;
                    if (view.Target == second)
                        two = view;
                }

                Assert.That(one.Rect.anchoredPosition, Is.EqualTo(two.Rect.anchoredPosition));
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.LeftAlt));
                yield return null;
                yield return null;
                Assert.That(one.Rect.anchoredPosition.y, Is.GreaterThan(two.Rect.anchoredPosition.y));
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return null;
                yield return null;
                Assert.That(one.Rect.anchoredPosition, Is.EqualTo(two.Rect.anchoredPosition));
            }
            finally
            {
                UnityEngine.Object.Destroy(first.gameObject);
                UnityEngine.Object.Destroy(second.gameObject);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator CameraFadesSceneryAndExpandedActorVolumesWithoutZooming()
        {
            var camera = game.WorldCamera;
            var visibility = camera.GetComponent<CameraVisibility>();
            Vector3 cameraPosition = camera.transform.position;
            float distance = game.CameraRig.Distance;
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.transform.position = Vector3.Lerp(cameraPosition, game.Worker.transform.position + Vector3.up, .5f);
            prop.transform.localScale = Vector3.one * 2;
            var farmer = game.FindInteraction(VillageRole.Farmer);
            Vector3 farmerPosition = farmer.transform.position;
            try
            {
                Physics.SyncTransforms();
                visibility.RefreshVisibility();
                Assert.That(visibility.IsHidden(prop.GetComponent<Renderer>()), Is.False);
                prop.layer = VillageMapProp.PointerGeometryLayer;
                var geometry = prop.AddComponent<MeshCollider>();
                geometry.sharedMesh = prop.GetComponent<MeshFilter>().sharedMesh;
                Physics.SyncTransforms();
                visibility.RefreshVisibility();
                Assert.That(visibility.IsHidden(prop.GetComponent<Renderer>()), Is.True);
                visibility.AdvanceFade(.04f);
                Assert.That(visibility.Opacity(prop.GetComponent<Renderer>()), Is.InRange(.001f, .999f));
                visibility.AdvanceFade(.2f);
                Assert.That(visibility.Opacity(prop.GetComponent<Renderer>()), Is.Zero);
                Assert.That(geometry.enabled, Is.True);
                Assert.That(visibility.IgnorePointerCollider(geometry), Is.True);
                yield return null;
                Assert.That(game.CameraRig.Distance, Is.EqualTo(distance));
                Assert.That(Vector3.Distance(camera.transform.position, game.Worker.transform.position + Vector3.up * 1.1f), Is.EqualTo(distance).Within(.01f));
                prop.SetActive(false);
                visibility.RefreshVisibility();
                visibility.AdvanceFade(.05f);
                Assert.That(visibility.Opacity(prop.GetComponent<Renderer>()), Is.InRange(.001f, .999f));
                visibility.AdvanceFade(.4f);
                Assert.That(visibility.Opacity(prop.GetComponent<Renderer>()), Is.EqualTo(1));
                farmer.transform.position = prop.transform.position;
                Physics.SyncTransforms();
                visibility.RefreshVisibility();
                foreach (var renderer in farmer.GetComponentsInChildren<Renderer>())
                    Assert.That(visibility.IsHidden(renderer), Is.False);
                var capsule = farmer.GetComponent<CapsuleCollider>();
                camera.transform.position = farmer.transform.TransformPoint(capsule.center);
                Physics.SyncTransforms();
                visibility.RefreshVisibility();
                foreach (var renderer in farmer.GetComponentsInChildren<Renderer>())
                    Assert.That(visibility.IsHidden(renderer), Is.True);
                camera.transform.position = cameraPosition;
                visibility.RefreshVisibility();
                foreach (var renderer in farmer.GetComponentsInChildren<Renderer>())
                    Assert.That(visibility.IsHidden(renderer), Is.False);
            }
            finally
            {
                camera.transform.position = cameraPosition;
                farmer.transform.position = farmerPosition;
                UnityEngine.Object.Destroy(prop);
                visibility.RefreshVisibility();
            }
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

        [UnityTest]
        public IEnumerator RightClickOnAWorldNameOpensActionsUnlessTheCameraOrbits()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            try
            {
                Vector3 position = game.Worker.transform.position + Vector3.right * .5f;
                item.Initialize(null, position, position, true);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftAlt));
                yield return null;
                yield return null;
                var name = UnityEngine.Object.FindObjectsByType<PickupNameView>(FindObjectsSortMode.None).Single(v => v.Target == item);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, name.Rect.TransformPoint(name.Rect.rect.center));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = 2 });
                yield return null;
                Assert.That(UnityEngine.Object.FindFirstObjectByType<WorldContextView>(), Is.Null);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;
                Assert.That(UnityEngine.Object.FindFirstObjectByType<WorldContextView>(), Is.Not.Null);

                game.Hud.CloseWorldContext();
                Quaternion pose = game.WorldCamera.transform.rotation;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = 2 });
                yield return null;
                Vector2 delta = new Vector2(50, 10);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen + delta, delta = delta, buttons = 2 });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen + delta });
                yield return null;
                Assert.That(Quaternion.Angle(pose, game.WorldCamera.transform.rotation), Is.GreaterThan(4f));
                Assert.That(UnityEngine.Object.FindFirstObjectByType<WorldContextView>(), Is.Null);

                int before = game.State.Inventory.Count(ItemIds.Potato);
                int quantity = item.Quantity;
                game.Hud.OpenWorldContext(item, screen);
                var context = UnityEngine.Object.FindFirstObjectByType<WorldContextView>();
                FindButton(context, "줍기").onClick.Invoke();
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(before + quantity));
                Assert.That(GameObject.Find("WorldClick(Clone)"), Is.Null);
            }
            finally
            {
                UnityEngine.Object.Destroy(item.gameObject);
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator ConstructionSignBuildsAFullFieldAndThePondNeedsAnEquippedRod()
        {
            var plots = UnityEngine.Object.FindObjectsByType<VillagePlot>(FindObjectsSortMode.None);
            var large = plots.Single(plot => plot.name == "동쪽 큰 부지");
            var sign = large.GetComponentInChildren<VillagePlotSign>();
            var standing = sign.transform.position + sign.transform.forward * 1.4f;
            Assert.That(NavMesh.SamplePosition(standing, out var point, 2f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(point.position);
            sign.RequestInteraction(game.Worker);
            yield return Until(() => UnityEngine.Object.FindFirstObjectByType<PlotWindowView>() != null, 4f);

            var window = UnityEngine.Object.FindFirstObjectByType<PlotWindowView>();
            var options = window.GetComponentsInChildren<UiListRow>();
            Assert.That(options.Select(row => row.GetComponentsInChildren<TMP_Text>().First().text),
                Is.EquivalentTo(new[] { "감자밭", "상점", "여관", "광장" }));
            var field = options.Single(row => row.GetComponentsInChildren<TMP_Text>().First().text == "감자밭");
            field.GetComponent<Button>().onClick.Invoke();
            Assert.That(VillagePlot.CellSide, Is.EqualTo(5f));
            Assert.That(large.GetComponentsInChildren<HarvestableCrop>().Length, Is.EqualTo(64));
            Assert.That(game.Crops.Count, Is.EqualTo(76));
            Assert.That(game.State.Export().plotPlacements.Single().facilityId, Is.EqualTo("potato_field"));

            Assert.That(game.PerformWorld(CampaignAction.BuildFacility, "east_pond", 4), Is.True);
            var pond = plots.Single(plot => plot.name == "낚시터 전용 부지");
            var fishing = pond.GetComponentInChildren<FishingWater>();
            Assert.That(fishing.Definition.Skill, Is.EqualTo(SkillId.Fishing));
            Assert.That(game.CanHarvest(fishing), Is.False);
            Assert.That(game.State.Inventory.TryAdd(ItemIds.FishingRod, 1), Is.True);
            Assert.That(game.State.TryEquip(game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.FishingRod).Id), Is.True);
            Assert.That(game.CanHarvest(fishing), Is.True);
            Assert.That(game.State.Life, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CastingAtTheWaterWalksToTheNearBankAndKeepsTheClickedPoint()
        {
            Assert.That(game.PerformWorld(CampaignAction.BuildFacility, "east_pond", 4), Is.True);
            var water = UnityEngine.Object.FindFirstObjectByType<FishingWater>();
            var pond = water.GetComponentInParent<VillagePlot>();
            var surface = water.GetComponentInChildren<MeshCollider>();
            Bounds mesh = surface.sharedMesh.bounds;
            Vector3 aim = mesh.center + new Vector3(mesh.extents.x * .75f, 0f, -mesh.extents.z * .31f);
            Vector3 clicked = surface.transform.TransformPoint(aim);
            Vector3 farBank = surface.transform.TransformPoint(new Vector3(mesh.min.x, aim.y, aim.z)) - pond.transform.right * 1.3f;
            Assert.That(NavMesh.SamplePosition(farBank, out var standing, 1f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(standing.position);
            Physics.SyncTransforms();
            var ray = new Ray(clicked + Vector3.up * 5f, Vector3.down);
            water.CastFromPointer(game.Worker, ray);
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Idle));

            Assert.That(game.State.Inventory.TryAdd(ItemIds.FishingRod, 1), Is.True);
            var rod = game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.FishingRod);
            Assert.That(game.State.TryEquip(rod.Id), Is.True);
            int remaining = water.Remaining;
            water.CastFromPointer(game.Worker, ray);
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
            yield return Until(() => game.Worker.ActiveCrop == water, 12f);
            Vector3 nearEdge = surface.transform.TransformPoint(new Vector3(mesh.max.x, aim.y, aim.z));
            Assert.That(Vector3.Dot(game.Worker.transform.position - nearEdge, pond.transform.right), Is.GreaterThan(0f));

            var floatInWater = UnityEngine.Object.FindFirstObjectByType<FishingBobber>();
            Assert.That(floatInWater, Is.Not.Null);
            yield return Until(() => Vector2.Distance(new Vector2(floatInWater.transform.position.x, floatInWater.transform.position.z),
                new Vector2(clicked.x, clicked.z)) < .025f, 2f);
            Assert.That(floatInWater.transform.position.y, Is.GreaterThan(clicked.y));
            game.Worker.Cancel();
            Assert.That(floatInWater.gameObject.activeSelf, Is.False);
            Assert.That(water.Remaining, Is.EqualTo(remaining));
        }

        [UnityTest]
        public IEnumerator InventoryPlacesTheTopLeftCellUnderThePointer()
        {
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Hoe, 1), Is.True);
            var entry = game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.Hoe);
            var inventory = (InventoryWindowView)game.Hud.OpenWindow("inventory");
            inventory.Refresh();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var grid = inventory.ItemLayer;
            Vector2 Point(float x, float y)
            {
                var rect = grid.rect;
                var step = inventory.CellSize;
                return RectTransformUtility.WorldToScreenPoint(null,
                    grid.TransformPoint(new Vector3(rect.xMin + x * step.x, rect.yMax - y * step.y, 0)));
            }

            game.Hud.BeginItemDrag(entry.Id, Point(entry.X + .75f, entry.Y + entry.Spec.Height - .25f));
            Assert.That(game.Hud.IsHoldingItem, Is.True);
            Vector2 destination = Point(3.4f, 1.6f);
            game.Hud.MoveItemDrag(destination);
            game.Hud.EndItemDrag(destination);
            var placed = game.State.Inventory.Find(entry.Id);
            Assert.That((placed.X, placed.Y), Is.EqualTo((3, 1)));
            Assert.That(game.Hud.IsHoldingItem, Is.False);
        }

        [UnityTest]
        public IEnumerator HoldingAnItemKeepsEquipmentIconsAndShowsTheNoticeAboveCenter()
        {
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Hoe, 1), Is.True);
            var hoe = game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.Hoe);
            Assert.That(game.State.TryEquip(hoe.Id), Is.True);
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 3), Is.True);
            var stack = game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.Potato);
            var inventory = (InventoryWindowView)game.Hud.OpenWindow("inventory");
            inventory.Refresh();
            yield return null;

            var slots = inventory.GetComponentsInChildren<EquipmentSlotPointer>().ToDictionary(item => item.Slot);
            Vector2 Footprint(int columns, int rows) => Vector2.Scale(inventory.CellSize, new Vector2(columns, rows)) - Vector2.one * 2f;
            Assert.That(game.State.Inventory.Columns, Is.EqualTo(6));
            Assert.That(inventory.ItemViewport.rect.width, Is.EqualTo(6f * inventory.CellSize.x));
            void VerifySlot(EquipmentSlot equipmentSlot, int columns, int rows) =>
                Assert.That(((RectTransform)slots[equipmentSlot].transform).rect.size, Is.EqualTo(Footprint(columns, rows)));
            VerifySlot(EquipmentSlot.MainHand, 2, 3);
            VerifySlot(EquipmentSlot.OffHand, 2, 3);
            VerifySlot(EquipmentSlot.Body, 2, 3);
            VerifySlot(EquipmentSlot.Legs, 2, 2);
            VerifySlot(EquipmentSlot.Head, 2, 2);
            VerifySlot(EquipmentSlot.Feet, 2, 2);
            VerifySlot(EquipmentSlot.Hands, 2, 2);
            VerifySlot(EquipmentSlot.Bag, 1, 1);
            VerifySlot(EquipmentSlot.Accessory, 1, 1);
            Assert.That(((RectTransform)slots[EquipmentSlot.Hands].transform).anchoredPosition.y,
                Is.EqualTo(((RectTransform)slots[EquipmentSlot.Legs].transform).anchoredPosition.y));
            var slot = slots[EquipmentSlot.MainHand];
            var equipped = slot.GetComponentInChildren<UiItemView>();
            Assert.That(equipped, Is.Not.Null);
            Assert.That(equipped.Rect.rect.size, Is.EqualTo(Footprint(1, 3)));
            Assert.That(inventory.GetComponentsInChildren<RawImage>(), Is.Empty);
            Vector2 pointer = RectTransformUtility.WorldToScreenPoint(null, inventory.Rect.position);
            game.Hud.ShowItemTooltip(ItemIds.Hoe, pointer);
            var tooltip = UnityEngine.Object.FindFirstObjectByType<ItemTooltipView>();
            Assert.That(tooltip.GetComponentsInChildren<Button>(), Is.Empty);
            game.Hud.HideItemTooltip();
            game.Hud.ClickInventoryItem(stack.Id, pointer);
            Assert.That(game.Hud.IsHoldingItem, Is.True);
            Assert.That(slot.GetComponentInChildren<UiItemView>(), Is.SameAs(equipped));

            var held = game.Hud.GetComponentsInChildren<UiItemView>().Single(item => item.transform.parent == game.Hud.transform);
            Assert.That(held.GetComponent<Image>().enabled, Is.False);
            Assert.That(held.GetComponentsInChildren<Image>().Single(image => image.enabled).sprite,
                Is.SameAs(game.Settings.FindItem(ItemIds.Potato).Icon));
            Assert.That(held.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Count").enabled, Is.False);

            game.Hud.ClickEquipment(EquipmentSlot.MainHand, pointer);
            var notice = game.Hud.GetComponentsInChildren<CanvasGroup>(true).Single(group => group.name == "System message");
            var noticeCanvas = notice.GetComponent<Canvas>();
            Assert.That(notice.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("해당 위치에 장착할 수 없습니다."));
            Assert.That(notice.alpha, Is.Zero);
            Assert.That(noticeCanvas.overrideSorting, Is.True);
            Assert.That(noticeCanvas.sortingOrder, Is.GreaterThan(game.Hud.GetComponent<Canvas>().sortingOrder));
            var noticeRect = (RectTransform)notice.transform;
            Assert.That(noticeRect.anchorMin, Is.EqualTo(new Vector2(.5f, .64f)));
            Assert.That(noticeRect.anchorMax, Is.EqualTo(noticeRect.anchorMin));
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(notice.alpha, Is.EqualTo(1f).Within(.001f));

            game.Hud.CancelItemDrag();
            inventory.Refresh();
            Assert.That(slot.GetComponentInChildren<UiItemView>(), Is.SameAs(equipped));
        }

        [UnityTest]
        public IEnumerator MerchantOpensInOneClickAndStackSplittingUsesTheSharedQuantityControls()
        {
            var inventory = game.Hud.OpenWindow("inventory");
            var place = inventory.Rect.anchoredPosition;
            var size = inventory.Rect.rect.size;
            var scale = inventory.Rect.localScale;
            game.Hud.OpenDialogue(game.FindInteraction(VillageRole.Merchant));
            var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
            var normalHud = game.Hud.transform.Find("Normal HUD").gameObject;
            Canvas.ForceUpdateCanvases();
            Assert.That(dialogue.Rect.rect.width, Is.LessThan(game.Hud.CanvasRect.rect.width));
            Assert.That(dialogue.Rect.anchoredPosition.y, Is.GreaterThan(0f));
            Assert.That(normalHud.activeSelf, Is.False);
            Assert.That(inventory.gameObject.activeSelf, Is.False);
            var openingContent = (RectTransform)dialogue.transform.Find("Opening content");
            Assert.That(openingContent.anchoredPosition.y, Is.LessThan(0f));
            var line = dialogue.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Dialogue");
            Assert.That(line.maxVisibleCharacters, Is.Zero);
            yield return Until(() => line.maxVisibleCharacters > 0, 1f);
            int revealed = line.maxVisibleCharacters;
            dialogue.Refresh();
            Assert.That(line.maxVisibleCharacters, Is.EqualTo(revealed));
            FindButton(dialogue, "거래하기").onClick.Invoke();
            Assert.That(dialogue.GetComponentsInChildren<Button>().Select(button => button.GetComponentInChildren<TMP_Text>().text),
                Is.EqualTo(new[] { "거래 종료" }));
            Assert.That(game.Hud.HasModal, Is.True);
            Assert.That(dialogue.isActiveAndEnabled, Is.True);
            Assert.That(game.ControlsBlocked, Is.True);
            Assert.That(inventory.Rect.anchoredPosition, Is.EqualTo(place));
            Assert.That(inventory.Rect.rect.size, Is.EqualTo(size));
            Assert.That(inventory.Rect.localScale, Is.EqualTo(scale));
            var shop = UnityEngine.Object.FindFirstObjectByType<ShopWindowView>();
            Assert.That(shop.GetComponentInChildren<ShopSellTarget>().name, Is.EqualTo("Stock viewport"));

            Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 9), Is.True);
            var entry = game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.Potato);
            game.Hud.OpenItemContext(entry.Id, new Vector2(Screen.width * .5f, Screen.height * .5f));
            var context = UnityEngine.Object.FindFirstObjectByType<ItemContextView>();
            FindButton(context, "아이템 나누기").onClick.Invoke();
            var quantity = UnityEngine.Object.FindFirstObjectByType<QuantityWindowView>();
            var input = quantity.GetComponentInChildren<TMP_InputField>();
            var slider = quantity.GetComponentInChildren<Slider>();
            FindButton(quantity, "Half").onClick.Invoke();
            Assert.That(input.text, Is.EqualTo("4"));
            FindButton(quantity, "-1").onClick.Invoke();
            Assert.That(slider.value, Is.EqualTo(3));
            slider.value = 6;
            Assert.That(input.text, Is.EqualTo("6"));
            input.text = "2";
            FindButton(quantity, "나누기").onClick.Invoke();
            Assert.That(game.State.Inventory.Find(entry.Id).Quantity, Is.EqualTo(7));
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(9));
            Assert.That(game.Hud.HasQuantityPrompt, Is.False);
            FindButton(dialogue, "거래 종료").onClick.Invoke();
            Assert.That(game.Hud.IsShopOpen, Is.False);
            Assert.That(game.Hud.HasModal, Is.False);
            Assert.That(dialogue.isActiveAndEnabled, Is.True);
            Assert.That(normalHud.activeSelf, Is.False);
            Assert.That(dialogue.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            yield return Until(() => normalHud.activeSelf, 1f);
            Assert.That(dialogue.isActiveAndEnabled, Is.False);
            Assert.That(inventory.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator TradeQuantityOpensAtTheClickAndLocksBothItemGrids()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 3), Is.True);
                Assert.That(game.State.Inventory.TryAdd(ItemIds.Bread, 2), Is.True);
                var potato = game.State.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Potato);
                var bread = game.State.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Bread);
                game.Hud.OpenShop(game.FindInteraction(VillageRole.Merchant));
                yield return null;
                Canvas.ForceUpdateCanvases();

                var inventory = UnityEngine.Object.FindFirstObjectByType<InventoryWindowView>();
                var shop = UnityEngine.Object.FindFirstObjectByType<ShopWindowView>();
                var stock = shop.GetComponentInChildren<UiItemView>().GetComponent<Button>();
                var potatoCell = inventory.ItemLayer.GetComponentsInChildren<InventoryItemDrag>().Single(cell => cell.EntryId == potato.Id);
                Vector2 ScreenCenter(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                Vector2 saleClick = ScreenCenter((RectTransform)potatoCell.transform);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                yield return null;
                potatoCell.OnPointerClick(new PointerEventData(EventSystem.current)
                {
                    position = saleClick, button = PointerEventData.InputButton.Left
                });
                Assert.That(game.Hud.IsHoldingItem, Is.False);
                var prompt = UnityEngine.Object.FindFirstObjectByType<QuantityWindowView>();
                Assert.That(prompt, Is.Not.Null);
                Assert.That(stock.IsInteractable(), Is.False);
                var inventoryInput = inventory.transform.Find("Body").GetComponent<CanvasGroup>();
                Assert.That(inventoryInput.blocksRaycasts, Is.False);
                Assert.That(shop.transform.Find("Body").GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                VerifyPopupPosition(prompt.Rect, saleClick);

                game.Hud.ClickInventoryItem(bread.Id, saleClick);
                stock.onClick.Invoke();
                FindButton(prompt, "판매").onClick.Invoke();
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(2));
                Assert.That(game.State.Inventory.Count(ItemIds.Bread), Is.EqualTo(2));
                Assert.That(game.Hud.HasQuantityPrompt, Is.False);
                Assert.That(inventoryInput.blocksRaycasts, Is.True);
                stock = shop.GetComponentInChildren<UiItemView>().GetComponent<Button>();
                Assert.That(stock.IsInteractable(), Is.True);

                Vector2 buyClick = ScreenCenter((RectTransform)stock.transform);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(mouse, new MouseState { position = buyClick });
                yield return null;
                shop.GetComponentInChildren<UiItemView>().GetComponent<Button>().onClick.Invoke();
                Assert.That(game.Hud.HasQuantityPrompt, Is.True);
                VerifyPopupPosition(prompt.Rect, buyClick);
                Assert.That(inventoryInput.blocksRaycasts, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private void VerifyPopupPosition(RectTransform popup, Vector2 screen)
        {
            var parent = game.Hud.CanvasRect;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out var click);
            Vector2 origin = popup.localPosition;
            Vector2 min = origin + popup.rect.min;
            Vector2 max = origin + popup.rect.max;
            var nearest = new Vector2(Mathf.Clamp(click.x, min.x, max.x), Mathf.Clamp(click.y, min.y, max.y));
            Assert.That(Vector2.Distance(click, nearest), Is.LessThan(16f));
            Assert.That(min.x, Is.GreaterThanOrEqualTo(parent.rect.xMin - .05f));
            Assert.That(min.y, Is.GreaterThanOrEqualTo(parent.rect.yMin - .05f));
            Assert.That(max.x, Is.LessThanOrEqualTo(parent.rect.xMax + .05f));
            Assert.That(max.y, Is.LessThanOrEqualTo(parent.rect.yMax + .05f));
        }

        [UnityTest]
        public IEnumerator TradeEscapeRestoresOnlyTheWindowsThatShouldStayOpen()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var merchant = game.FindInteraction(VillageRole.Merchant);
                var quests = game.Hud.OpenWindow("quests");
                game.Hud.OpenDialogue(merchant, DialogueAction.Shop);
                Assert.That(game.Hud.IsShopOpen, Is.True);
                Assert.That(game.Hud.InventoryOpen, Is.True);
                Assert.That(quests.gameObject.activeSelf, Is.False);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(game.Hud.HasModal, Is.False);
                Assert.That(game.Hud.IsShopOpen, Is.False);
                Assert.That(game.Hud.InventoryOpen, Is.False);
                yield return Until(() => quests.gameObject.activeSelf, 1f);

                game.Hud.OpenDialogue(merchant);
                game.Hud.ToggleWindow("inventory");
                game.Hud.OpenShop(merchant);
                game.Hud.CloseWindow("shop");
                Assert.That(game.Hud.InventoryOpen, Is.True);
                Assert.That(game.Hud.HasModal, Is.False);
                yield return Until(() => quests.gameObject.activeSelf, 1f);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator AClosingWindowCanReopenAndRestoresItsInputAndAppearance()
        {
            var settings = game.Hud.OpenWindow("settings");
            var controls = settings.GetComponent<CanvasGroup>();
            yield return Until(() => controls.alpha == 1f, 1f);
            Vector2 position = settings.Rect.anchoredPosition;
            game.Hud.CloseWindow("settings");
            Assert.That(settings.gameObject.activeSelf, Is.True);
            Assert.That(controls.blocksRaycasts, Is.False);
            Assert.That(controls.interactable, Is.False);

            game.Hud.ToggleWindow("settings");
            Assert.That(settings.gameObject.activeSelf, Is.True);
            Assert.That(controls.blocksRaycasts, Is.True);
            Assert.That(controls.interactable, Is.True);
            Assert.That(controls.alpha, Is.EqualTo(1f));
            Assert.That(settings.Rect.anchoredPosition, Is.EqualTo(position));

            game.Hud.CloseWindow("settings");
            yield return Until(() => !settings.gameObject.activeSelf, 1f);
            Assert.That(controls.alpha, Is.EqualTo(1f));
            Assert.That(controls.blocksRaycasts, Is.True);
        }

        [UnityTest]
        public IEnumerator SystemMenuLoadsTheLastSoloSaveAfterConfirmation()
        {
            var menu = game.Hud.OpenWindow("system");
            menu.Refresh();
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 3), Is.True);
            FindButton(menu, "저장").onClick.Invoke();
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 4), Is.True);
            var load = FindButton(menu, "불러오기");
            Assert.That(load.interactable, Is.True);
            load.onClick.Invoke();
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(7));
            FindButton(menu, "취소").onClick.Invoke();
            load.onClick.Invoke();
            var previous = game;
            menu.GetComponentsInChildren<Button>().Single(button => button.name == "Confirm load").onClick.Invoke();
            yield return Until(() => previous == null, 10f);
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game, Is.Not.Null);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator InventoryRestoresTheLastPositionSavedByDraggingItsTitle()
        {
            const string xKey = "Workerborn.UI.Position.inventory.x";
            const string yKey = "Workerborn.UI.Position.inventory.y";
            bool hadX = PlayerPrefs.HasKey(xKey), hadY = PlayerPrefs.HasKey(yKey);
            float oldX = PlayerPrefs.GetFloat(xKey), oldY = PlayerPrefs.GetFloat(yKey);
            VillageWindowView recreated = null;
            try
            {
                PlayerPrefs.DeleteKey(xKey);
                PlayerPrefs.DeleteKey(yKey);
                var inventory = game.Hud.OpenWindow("inventory");
                Canvas.ForceUpdateCanvases();
                var startPosition = inventory.Rect.anchoredPosition;
                var handle = inventory.GetComponentInChildren<WindowDragHandle>();
                var startPointer = RectTransformUtility.WorldToScreenPoint(null, handle.transform.position);
                var drag = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    pressPosition = startPointer,
                    position = startPointer
                };
                handle.OnBeginDrag(drag);
                drag.position += new Vector2(-Screen.width * .06f, Screen.height * .08f);
                handle.OnDrag(drag);
                handle.OnEndDrag(drag);
                var moved = inventory.Rect.anchoredPosition;
                Assert.That(moved, Is.Not.EqualTo(startPosition));
                game.Hud.CloseWindow("inventory");

                recreated = UnityEngine.Object.Instantiate(game.Hud.Library.Find("inventory"), game.Hud.CanvasRect);
                recreated.Bind(game, game.Hud);
                Assert.That(recreated.Rect.anchoredPosition, Is.EqualTo(moved));
                yield return null;
            }
            finally
            {
                if (recreated != null)
                    UnityEngine.Object.Destroy(recreated.gameObject);
                if (hadX) PlayerPrefs.SetFloat(xKey, oldX); else PlayerPrefs.DeleteKey(xKey);
                if (hadY) PlayerPrefs.SetFloat(yKey, oldY); else PlayerPrefs.DeleteKey(yKey);
                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator JobSchedulesFollowTheClockAndHighlightTheWorkplaceOnEitherMap()
        {
            var miniMap = game.Hud.GetComponentInChildren<VillageMapView>();
            var miniSchedule = game.Hud.GetComponentInChildren<PartTimeScheduleView>();
            var names = game.Hud.GetComponentInChildren<PickupNames>();
            var normalHud = game.Hud.transform.Find("Normal HUD");
            Assert.That(normalHud.GetSiblingIndex(), Is.GreaterThan(names.transform.GetSiblingIndex()));
            Assert.That(miniSchedule.transform.IsChildOf(normalHud), Is.True);
            Assert.That(miniMap.transform.IsChildOf(normalHud), Is.True);

            var pointer = new PointerEventData(EventSystem.current);
            var miniRow = miniSchedule.GetComponentInChildren<PartTimeScheduleRowView>();
            miniRow.OnPointerEnter(pointer);
            var miniMarker = miniMap.transform.Find("Job location");
            Assert.That(miniMarker.gameObject.activeSelf, Is.True);
            miniRow.OnPointerExit(pointer);
            Assert.That(miniMarker.gameObject.activeSelf, Is.False);

            var map = game.Hud.OpenWindow("map");
            Assert.That(map.GetComponentInChildren<VillageClockView>(), Is.Not.Null);
            var hours = map.GetComponentsInChildren<TMP_Text>().Where(label => label.name == "Job hours").ToArray();
            Assert.That(hours.Any(label => label.text.StartsWith("오늘 ")), Is.True);
            Assert.That(hours.Any(label => label.text.StartsWith("내일 ")), Is.True);
            var fullRow = map.GetComponentInChildren<PartTimeScheduleRowView>();
            var fullMarker = map.GetComponentInChildren<VillageMapView>().transform.Find("Job location");
            var workplace = game.FindInteraction(VillageRole.Farmer).transform;
            Vector3 position = workplace.position;
            try
            {
                workplace.position += Vector3.right * 300f;
                fullRow.OnPointerEnter(pointer);
                Assert.That(fullMarker.gameObject.activeSelf, Is.True);
                Assert.That(fullMarker.Find("Direction arrow").gameObject.activeSelf, Is.True);
                Assert.That(miniMarker.gameObject.activeSelf, Is.False);
                fullRow.OnPointerExit(pointer);
                Assert.That(fullMarker.gameObject.activeSelf, Is.False);
            }
            finally
            {
                workplace.position = position;
            }

            var state = game.State;
            state.AdvanceTime((state.Rules.JobEnd - state.Minute + 10) / state.Rules.MinutesPerSecond);
            yield return null;
            Assert.That(miniRow.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Job hours").text,
                Does.StartWith("내일 "));
            Assert.That(fullRow.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Job status").text,
                Is.EqualTo("마감"));
        }

        [UnityTest]
        public IEnumerator ChatKeepsEditingAboveDialogueAndShowsTheNewestLinesInTheBubble()
        {
            var chat = UnityEngine.Object.FindFirstObjectByType<VillageChatView>();
            var input = chat.GetComponentInChildren<TMP_InputField>();
            var chatLayer = chat.transform.parent;
            var inputFrame = chat.GetComponentInChildren<CanvasGroup>();
            var historyFrame = chat.GetComponentInChildren<VerticalGradient>();
            var inputBackground = inputFrame.GetComponent<Image>();
            var historyViewport = chat.GetComponentInChildren<ScrollRect>().viewport;
            var recent = chat.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Recent messages");
            var overhead = UnityEngine.Object.FindFirstObjectByType<PlayerOverheadView>(FindObjectsInactive.Include);
            var texts = overhead.GetComponentsInChildren<TMP_Text>(true);
            var bubble = texts.Single(text => text.name == "Speech");
            var label = texts.Single(text => text.text == game.LegendPlayerName);
            Assert.That(inputFrame.alpha, Is.Zero);
            Assert.That(historyFrame.enabled, Is.False);
            Assert.That(recent.alignment, Is.EqualTo(TextAlignmentOptions.BottomLeft));
            Assert.That(recent.GetComponentInParent<RectMask2D>(), Is.Not.Null);

            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                Canvas.ForceUpdateCanvases();
                yield return MovePointer(historyViewport);
                Assert.That(inputFrame.alpha, Is.Zero);
                Assert.That(historyFrame.enabled, Is.False);
                yield return MovePointer((RectTransform)inputFrame.transform);
                Assert.That(inputFrame.alpha, Is.EqualTo(1f));
                Assert.That(historyFrame.enabled, Is.False);
                Assert.That(inputBackground.color, Is.EqualTo(historyFrame.color));
                yield return MovePointer(historyViewport);
                Assert.That(inputFrame.alpha, Is.Zero);

                game.Hud.OpenDialogue(game.FindInteraction(VillageRole.Merchant));
                var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
                Assert.That(inputFrame.gameObject.activeInHierarchy, Is.False);
                Assert.That(chatLayer.GetSiblingIndex(), Is.LessThan(dialogue.transform.GetSiblingIndex()));
                yield return TapEnter();
                Assert.That(input.isFocused, Is.True);
                Assert.That(chatLayer.GetSiblingIndex(), Is.GreaterThan(dialogue.transform.GetSiblingIndex()));
                Assert.That(inputFrame.alpha, Is.EqualTo(1f));
                Assert.That(historyFrame.enabled, Is.True);
                Assert.That(inputBackground.color.a, Is.Zero);

                foreach (string post in new[] { "첫 번째", "두 번째", "세 번째", "네 번째" })
                {
                    input.text = post;
                    input.onSubmit.Invoke(post);
                    input.DeactivateInputField(); // TMP does this as it finishes the submitted line.
                    yield return null;
                    yield return null;
                    Assert.That(input.isFocused, Is.True);
                    Assert.That(input.text, Is.Empty);
                }

                var history = chat.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Messages");
                Assert.That(history.text, Does.Contain(game.LegendPlayerName + " : 첫 번째"));
                Assert.That(bubble.text, Is.EqualTo("두 번째\n세 번째\n네 번째"));
                Assert.That(label.rectTransform.anchoredPosition.y, Is.GreaterThan(0));

                input.text = new string('W', 225) + "끝";
                yield return TapEnter();
                bubble.ForceMeshUpdate();
                Assert.That(bubble.text, Does.EndWith("끝").And.Not.Contain("네 번째"));
                Assert.That(bubble.textInfo.lineCount, Is.LessThanOrEqualTo(4));
                Assert.That(bubble.maxVisibleLines, Is.EqualTo(4));

                yield return TapEnter();
                Assert.That(input.isFocused, Is.False);
                Assert.That(historyFrame.enabled, Is.False);
                Assert.That(history.gameObject.activeInHierarchy, Is.False);
                Assert.That(inputFrame.gameObject.activeInHierarchy, Is.False);
                Assert.That(chatLayer.GetSiblingIndex(), Is.LessThan(dialogue.transform.GetSiblingIndex()));
                Assert.That(game.Hud.HasModal, Is.True);
                game.Hud.CloseDialogue();
                Assert.That(history.gameObject.activeInHierarchy, Is.False);
                Assert.That(recent.gameObject.activeInHierarchy, Is.True);
                Assert.That(recent.text, Does.Contain("네 번째").And.Not.Contain("첫 번째"));
                yield return Until(() => string.IsNullOrEmpty(recent.text), 8f);
                yield return TapEnter();
                Assert.That(history.gameObject.activeInHierarchy, Is.True);
                Assert.That(history.alignment, Is.EqualTo(TextAlignmentOptions.BottomLeft));
                Assert.That(history.text, Does.Contain("첫 번째").And.EndWith("끝"));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
            }

            IEnumerator MovePointer(RectTransform area)
            {
                Vector2 position = RectTransformUtility.WorldToScreenPoint(null, area.TransformPoint(area.rect.center));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
                yield return null;
                yield return null;
            }

            IEnumerator TapEnter()
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
            }
        }

        private static Button FindButton(Component root, string label) =>
            root.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<TMP_Text>()?.text == label);

        [UnityTest]
        public IEnumerator InventoryDropWaitsForArrivalAndShiftChangesTheGait()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 3), Is.True);
                var stack = game.State.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Potato);
                Vector3 destination = NearbyDestination();
                int before = game.GroundItemCount;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
                yield return null;

                Assert.That(game.Worker.CommandDrop(stack.Id, destination), Is.True);
                yield return Until(() => game.Worker.IsWalking, 1f);
                yield return null;
                Assert.That(game.Worker.GetComponent<WorkerAnimation>().CurrentAction, Is.EqualTo(3));
                Assert.That(game.State.Inventory.Find(stack.Id).Quantity, Is.EqualTo(3));
                Assert.That(game.GroundItemCount, Is.EqualTo(before));
                game.Worker.Cancel();
                Assert.That(game.Worker.HasPendingDrop, Is.False);
                Assert.That(game.State.Inventory.Find(stack.Id).Quantity, Is.EqualTo(3));

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Assert.That(game.Worker.CommandDrop(stack.Id, destination), Is.True);
                yield return Until(() => game.GroundItemCount == before + 1, 5f);
                var drop = UnityEngine.Object.FindObjectsByType<GroundPickup>(FindObjectsSortMode.None)
                    .Single(i => i.ItemId == ItemIds.Potato && i.Quantity == 3);
                Assert.That(game.State.Inventory.Find(stack.Id), Is.Null);
                Assert.That(Vector2.Distance(new Vector2(drop.LandingPosition.x, drop.LandingPosition.z), new Vector2(destination.x, destination.z)), Is.LessThan(.01f));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator GroundDropsReuseTheSameInstanceWithNewItemDataAndOneLandingEvent()
        {
            Vector3 destination = game.Worker.transform.position + Vector3.right * 1.9f;
            var potato = DropStack(ItemIds.Potato, 3, destination);
            var firstFilter = potato.GetComponentInChildren<MeshFilter>();
            Mesh firstMesh = firstFilter.sharedMesh;
            Assert.That(firstMesh, Is.Not.Null);
            long firstOrder = potato.NameplateOrder;
            yield return Until(() => FindPickupName(potato) != null, 2f);
            var oldName = FindPickupName(potato);
            int previousLandings = 0;
            potato.Landed += _ => previousLandings++;
            Assert.That(game.TryCollectPickup(potato), Is.True);
            Assert.That(potato.gameObject.activeSelf, Is.False);

            var egg = DropStack(ItemIds.Egg, 1, destination);
            Assert.That(egg, Is.SameAs(potato));
            Assert.That(egg.Quantity, Is.EqualTo(1));
            Assert.That(egg.DisplayName, Is.EqualTo(game.Settings.FindItem(ItemIds.Egg).DisplayName));
            Assert.That(egg.NameplateOrder, Is.GreaterThan(firstOrder));
            Assert.That(egg.IsInFlight, Is.True);
            Assert.That(egg.GetComponentInChildren<MeshFilter>(), Is.SameAs(firstFilter));
            Assert.That(firstFilter.sharedMesh, Is.Not.Null.And.Not.SameAs(firstMesh));
            Assert.That(oldName.Target, Is.Null, "The old nameplate cannot select a reused drop.");

            int landings = 0;
            egg.Landed += _ => landings++;
            yield return Until(() => FindPickupName(egg) != null && landings > 0, 3f);
            Assert.That(FindPickupName(egg), Is.SameAs(oldName));
            Assert.That(landings, Is.EqualTo(1));
            Assert.That(previousLandings, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PickupTrailAndArrivalParticlesReturnClearedAndPlayAgain()
        {
            var effects = UnityEngine.Object.FindFirstObjectByType<VillageEffects>();
            Transform worker = game.Worker.transform;
            Vector3 start = worker.position + Vector3.right;
            effects.PlayPickupTrail(start, worker);
            var flight = effects.GetComponentsInChildren<LootFlightTrail>().Single();
            var trail = flight.GetComponent<TrailRenderer>();

            yield return Until(() => !flight.gameObject.activeSelf, 3f);
            Assert.That(trail.positionCount, Is.Zero);
            Assert.That(trail.emitting, Is.False);
            var particle = effects.GetComponentsInChildren<ParticleSystem>().First();
            yield return Until(() => !particle.gameObject.activeInHierarchy, 6f);
            Assert.That(particle.particleCount, Is.Zero);

            start = worker.position + Vector3.left;
            effects.PlayPickupTrail(start, worker);
            Assert.That(effects.GetComponentsInChildren<LootFlightTrail>().Single(), Is.SameAs(flight));
            Assert.That(flight.transform.position, Is.EqualTo(start));
            Assert.That(trail.emitting, Is.True);
            yield return Until(() => particle.gameObject.activeInHierarchy, 3f);
        }

        private GroundPickup DropStack(string itemId, int quantity, Vector3 destination)
        {
            Assert.That(game.State.Inventory.TryAdd(itemId, quantity), Is.True);
            var stack = game.State.Inventory.Entries.Single(entry => entry.ItemId == itemId);
            Assert.That(game.Worker.CommandDrop(stack.Id, destination), Is.True);
            return WorldInteractable.Active.OfType<GroundPickup>().Single(item => item.ItemId == itemId);
        }

        private static PickupNameView FindPickupName(GroundPickup item) =>
            UnityEngine.Object.FindObjectsByType<PickupNameView>(FindObjectsSortMode.None).SingleOrDefault(view => view.Target == item);

        private Vector3 NearbyDestination()
        {
            var worker = game.Worker.transform;
            var agent = worker.GetComponent<NavMeshAgent>();
            var path = new NavMeshPath();
            for (int angle = 0; angle < 360; angle += 45)
            {
                var point = worker.position + Quaternion.Euler(0, angle, 0) * Vector3.forward * 3.5f;
                if (NavMesh.SamplePosition(point, out var hit, .7f, NavMesh.AllAreas) && agent.CalculatePath(hit.position, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                    return hit.position;
            }
            Assert.Fail("No reachable ground near the village spawn.");
            return default;
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
