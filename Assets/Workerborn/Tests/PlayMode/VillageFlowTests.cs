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
        public IEnumerator ClickingAnItemNameApproachesThenCollectsWithinOneMetre()
        {
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            try
            {
                Vector3 at = game.Worker.transform.position + Vector3.right * 2;
                item.Initialize(game.Crops[0].Definition, at, at, true);
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
                yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) == before + quantity, 6f);
                Assert.That(Vector3.Distance(game.Worker.transform.position, at), Is.LessThanOrEqualTo(1.001f));
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
        public IEnumerator PickupDuringHarvestDoesNotMoveOrCancelTheWorkerEvenDuringDropFlight()
        {
            var crop = game.Crops[0];
            game.Worker.CommandHarvest(crop);
            yield return Until(() => game.Worker.Action == WorkerAction.Harvesting, 10f);
            var at = game.Worker.transform.position;
            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            item.Initialize(crop.Definition, at + Vector3.up * 3, at + Vector3.right * .5f, false);
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
            crop.RestoreRemaining(0);
            Physics.SyncTransforms();
            Assert.That(crop.CanInteract, Is.False);
            Assert.That(collider.enabled, Is.False);
            foreach (var hit in Physics.RaycastAll(crop.transform.position + Vector3.up * 4, Vector3.down, 5, ~(1 << 2), QueryTriggerInteraction.Collide))
                Assert.That(hit.collider.GetComponentInParent<HarvestableCrop>(), Is.Not.SameAs(crop));
            crop.ResetHarvests();
            Assert.That(crop.CanInteract, Is.True);
            Assert.That(collider.enabled, Is.True);
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
                visibility.RefreshVisibility();
                Assert.That(visibility.IsHidden(prop.GetComponent<Renderer>()), Is.True);
                visibility.AdvanceFade(.04f);
                Assert.That(visibility.Opacity(prop.GetComponent<Renderer>()), Is.InRange(.001f, .999f));
                visibility.AdvanceFade(.2f);
                Assert.That(visibility.Opacity(prop.GetComponent<Renderer>()), Is.Zero);
                Assert.That(prop.GetComponent<Collider>().enabled, Is.True);
                Assert.That(visibility.IgnorePointerCollider(prop.GetComponent<Collider>()), Is.True);
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

        private static IEnumerator Until(Func<bool> condition, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, "The expected village interaction did not finish before the timeout.");
        }
    }
}
