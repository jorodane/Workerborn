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
            Assert.That(game.InRebirthZone, Is.False);
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
        public IEnumerator FirstLifeWaitsOnWalkableCloudsUntilTheGuideConfirmsIncarnation()
        {
            var waiting = VillageState.BeforeFirstLife(game.State.Rules);
            waiting.AdvanceTime((1439.2f - waiting.Minute) / waiting.Rules.MinutesPerSecond);
            var save = waiting.Export();
            save.ap = waiting.NextSkillCostFor(SkillId.Harvesting);
            save.skillRanks[(int)SkillId.Harvesting].learned = true;
            Assert.That(VillageSaveStore.TrySave(save, out _), Is.True);
            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game.InRebirthZone, Is.True);
            Assert.That(game.CaptureSave().awaitingIncarnation, Is.True);
            Assert.That(game.State.Level, Is.EqualTo(1));
            Assert.That(game.State.Inventory.Entries, Is.Empty);
            yield return Until(() => game.State.Day > waiting.Day && !game.IsWorldTransitioning, 3f);
            Assert.That(game.InRebirthZone, Is.True);
            Assert.That(game.Hud.transform.Find("Normal HUD").gameObject.activeInHierarchy, Is.True);
            Assert.That(game.Hud.OpenWindow("inventory"), Is.Not.Null);
            var skills = game.Hud.OpenWindow("skills");
            var advance = skills.GetComponentsInChildren<Button>().Single(button => button.name == "Advance");
            Assert.That(advance.interactable, Is.True);
            advance.onClick.Invoke();
            Assert.That(game.State.RankOf(SkillId.Harvesting), Is.EqualTo(1));
            Assert.That(game.State.AP, Is.Zero);
            Assert.That(game.Perform(() => game.State.Inventory.TryAdd(ItemIds.Potato, 1)), Is.False);
            Assert.That(game.PerformWorld(CampaignAction.Incarnate), Is.False);
            var agent = game.Worker.GetComponent<NavMeshAgent>();
            Assert.That(agent.isOnNavMesh, Is.True);
            Vector3 target = game.Worker.transform.position + Vector3.right * 2f;
            Vector3 destination = game.SurfacePosition(target.x, target.z);
            game.Worker.CommandMove(destination);
            yield return Until(() => Vector3.Distance(game.Worker.transform.position, destination) < .2f, 4f);

            var guide = game.RebirthArea.Guide;
            game.Hud.OpenDialogue(guide);
            game.Hud.CloseDialogue();
            Assert.That(game.InRebirthZone, Is.True);
            game.Hud.OpenDialogue(guide);
            var look = new CharacterLook { body = 1, hair = 7, face = 2, outfit = 4 };
            game.PreviewIncarnationLook(look);
            game.ConfirmIncarnation();
            yield return Until(() => !game.InRebirthZone, 3f);
            Assert.That(game.InRebirthZone, Is.False);
            Assert.That(game.State.LifeMinutes, Is.EqualTo(0).Within(.02));
            Assert.That(game.State.LifeLook, Is.EqualTo(look));
            Assert.That(game.State.Inventory.Entries, Is.Empty);
            Assert.That(game.State.RankOf(SkillId.Harvesting), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SittingUsesASeatOnlyFromItsFrontAndRestoresTheWalkablePosition()
        {
            Vector3 ground = game.Worker.transform.position;
            game.Worker.ToggleSit();
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Sitting));
            Assert.That(game.Worker.transform.position, Is.EqualTo(ground));
            game.Worker.ToggleSit();
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Idle));

            var seat = UnityEngine.Object.FindFirstObjectByType<SeatSlot>();
            Vector3 front = seat.Position + seat.Facing * Vector3.forward * .85f;
            Assert.That(seat.Covers(front), Is.True);
            Assert.That(seat.Covers(seat.Position - seat.Facing * Vector3.forward * .5f), Is.False);
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(front.x, front.z), out var standing, .35f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(standing.position), Is.True);
            game.Worker.ToggleSit();
            yield return null;
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Sitting));
            Assert.That(Vector3.Distance(game.Worker.transform.position, seat.Position), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(game.Worker.transform.rotation, seat.Facing), Is.LessThan(.1f));
            game.Worker.ToggleSit();
            Assert.That(Vector3.Distance(game.Worker.transform.position, standing.position), Is.LessThan(.02f));
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
            var inn = WorldInteractable.Active.OfType<VillageBuildingDoor>().Single(door => door.BuildingId == "inn");
            Vector3 atDoor = inn.Approach.position;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(atDoor.x, atDoor.z), out var at, .8f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(at.position), Is.True);
            inn.RequestInteraction(game.Worker);
            yield return Until(() => game.CurrentInterior == inn.Interior && !game.IsWorldTransitioning, 3f);
            var keeper = game.FindInteraction(VillageRole.Innkeeper);
            Assert.That(game.SharesMap(keeper.transform.position), Is.True);
            keeper.RequestInteraction(game.Worker);
            yield return Until(() => game.Hud.IsSpeakingWith(keeper), 4f);
            var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
            FindButton(dialogue, "쉬었다 가도 될까요?").onClick.Invoke();
            FindButton(dialogue, "내 방으로 들어간다").onClick.Invoke();
            yield return Until(() => game.CurrentInterior?.Lobby == inn.Interior && !game.IsWorldTransitioning, 3f);
            var room = game.CurrentInterior;
            Assert.That(game.CaptureSave().interior.id, Is.EqualTo(room.Id));
            Assert.That(NavMesh.SamplePosition(room.Bed.Approach.position, out var bedside, .35f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(bedside.position), Is.True);

            var fade = game.Hud.transform.Find("World transition").GetComponent<CanvasGroup>();
            for (int day = game.State.Day + 1; day <= nextChange; day++)
            {
                game.Sleep(false);
                Assert.That(game.State.Day, Is.EqualTo(day - 1));
                Assert.That(game.ControlsBlocked, Is.True);
                Assert.That(Vector3.Dot(game.PlayerActor.VisualRoot.up, Vector3.up), Is.EqualTo(0f).Within(.01f));
                yield return Until(() => game.State.Day == day, 3f);
                Assert.That(fade.alpha, Is.EqualTo(1f));
                yield return Until(() => !game.IsWorldTransitioning, 3f);
                Assert.That(fade.gameObject.activeSelf, Is.False);
                Assert.That(game.CurrentInterior, Is.SameAs(room));
                Assert.That(game.Worker.InReach(room.Bed.Approach.position, .9f), Is.True);
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
        public IEnumerator AHenCanShiftBeforeItMovesFarEnoughToInterruptHarvest()
        {
            var hen = UnityEngine.Object.FindFirstObjectByType<AnimalNpc>();
            var wander = hen.GetComponent<AnimalWander>();
            wander.SetSuspended(true);
            Assert.That(NavMesh.SamplePosition(hen.transform.position + Vector3.right * 1.6f, out var stand, .8f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(stand.position), Is.True);

            int charges = hen.Remaining;
            int energy = game.State.Energy;
            int drops = game.GroundItemCount;
            string notice = null;
            void Capture(string message) => notice = message;
            game.Worker.Message += Capture;
            try
            {
                game.Worker.CommandHarvest(hen);
                yield return Until(() => game.Worker.ActiveCrop == hen, 2f);
                Vector3 start = hen.transform.position;
                Vector3 away = Vector3.ProjectOnPlane(start - game.Worker.transform.position, Vector3.up).normalized;
                hen.transform.position = start + away * .55f;
                yield return null;
                Assert.That(game.Worker.ActiveCrop, Is.SameAs(hen));
                hen.transform.position = start + away * .9f;
                yield return Until(() => game.Worker.ActiveCrop == null, 1f);
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Idle));
                Assert.That(hen.Progress, Is.Zero);
                Assert.That(hen.Remaining, Is.EqualTo(charges));
                Assert.That(game.State.Energy, Is.EqualTo(energy));
                Assert.That(game.GroundItemCount, Is.EqualTo(drops));
                Assert.That(notice, Does.Contain("채집에 실패"));
            }
            finally
            {
                game.Worker.Message -= Capture;
            }
        }

        [UnityTest]
        public IEnumerator SpaceLiftsOnlyThePlayerVisual()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Vector3 standing = game.Worker.transform.position;
                var visual = game.PlayerActor.VisualRoot;
                Vector3 original = visual.localPosition;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                yield return Until(() => visual.localPosition.y > original.y + .1f, 1f);
                Assert.That(Vector3.Distance(game.Worker.transform.position, standing), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(game.Worker.GetComponent<NavMeshAgent>().nextPosition, standing), Is.LessThan(.002f));
                yield return Until(() => visual.localPosition == original, 1f);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator RabbitRequiresAFreshClickForEachStrikeAndDropsLootOnDefeat()
        {
            var rabbit = UnityEngine.Object.FindObjectsByType<MonsterInteractable>(FindObjectsSortMode.None)
                .Single(animal => animal.Combat != null);
            var approach = rabbit.transform.position + Vector3.right * .75f;
            Assert.That(NavMesh.SamplePosition(approach, out var at, .4f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(at.position);
            game.CameraRig.SnapToTarget();
            WorldNameplateView nameplate = null;
            yield return Until(() =>
            {
                nameplate = UnityEngine.Object.FindObjectsByType<WorldNameplateView>(FindObjectsSortMode.None)
                    .SingleOrDefault(view => view.Subject == rabbit);
                return nameplate != null;
            }, 2f);
            var healthBar = nameplate.transform.Find("Health bar");
            var fill = healthBar.Find("Health fill").GetComponent<Image>();
            var change = healthBar.Find("Health change").GetComponent<Image>();
            Vector2 labelSize = nameplate.Rect.rect.size;
            int health = rabbit.Health;
            int stamina = game.State.Energy;
            int drops = game.GroundItemCount;
            rabbit.RequestInteraction(game.Worker);
            yield return Until(() => healthBar.gameObject.activeSelf, 2f);
            Assert.That(nameplate.Rect.rect.size, Is.EqualTo(labelSize));
            yield return Until(() => rabbit.Health < health, 3f);
            Assert.That(rabbit.HealthVisibleUntil - Time.unscaledTime, Is.InRange(59f, 60f));
            yield return Until(() => change.fillAmount > fill.fillAmount + .05f, .2f);
            int remaining = rabbit.Health;
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Attacking));
            rabbit.RequestInteraction(game.Worker);
            yield return Until(() => game.State.Energy < stamina && game.Worker.Action == WorkerAction.Idle, 3f);
            Assert.That(rabbit.Health, Is.EqualTo(remaining), "A click during the delay must not queue a strike.");
            Assert.That(healthBar.gameObject.activeSelf, Is.True);
            Assert.That(fill.fillAmount, Is.EqualTo((float)remaining / rabbit.Combat.Health).Within(.01f));

            while (!rabbit.IsDefeated)
            {
                int before = rabbit.Health;
                rabbit.RequestInteraction(game.Worker);
                yield return Until(() => rabbit.Health < before && game.Worker.Action == WorkerAction.Idle, 3f);
            }
            Assert.That(rabbit.LootTaken, Is.True);
            Assert.That(rabbit.CanInteract, Is.False);
            Assert.That(rabbit.GetComponent<NavMeshAgent>().enabled, Is.False);
            Assert.That(rabbit.GetComponentsInChildren<Collider>(true).All(collider => !collider.enabled), Is.True);
            Assert.That(rabbit.gameObject.activeSelf, Is.True);
            Assert.That(nameplate.Target, Is.Null);
            Assert.That(fill.fillAmount, Is.Zero);
            Assert.That(game.GroundItemCount, Is.EqualTo(drops + rabbit.Combat.LootCount));
            rabbit.RequestInteraction(game.Worker);
            Assert.That(game.GroundItemCount, Is.EqualTo(drops + rabbit.Combat.LootCount));

            yield return Until(() => !rabbit.gameObject.activeSelf, rabbit.Combat.CorpseSeconds + 2f);
            Assert.That(GameObject.Find("CorpseDespawn(Clone)"), Is.Not.Null);
            Assert.That(game.CaptureSave().animalStates.Single(animal => animal.id == rabbit.PersistentId).health, Is.Zero);
            rabbit.ResetEncounter();
            Assert.That(rabbit.gameObject.activeSelf, Is.True);
            Assert.That(rabbit.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled), Is.True);
            Assert.That(rabbit.Health, Is.EqualTo(rabbit.Combat.Health));
        }

        [UnityTest]
        public IEnumerator RabbitTakesACommittedHitAndPursuesAPlayerWhoRuns()
        {
            var rabbit = UnityEngine.Object.FindObjectsByType<MonsterInteractable>(FindObjectsSortMode.None)
                .Single(animal => animal.Combat != null);
            var agent = rabbit.GetComponent<NavMeshAgent>();
            Vector3 home = rabbit.transform.position;
            Vector3 start = home + Vector3.right * (rabbit.InteractionDistance - .12f);
            Vector3 sidestep = home - Vector3.right * .36f;
            Vector3 escape = home + Vector3.right * 4f;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(start.x, start.z), out var attacker, .18f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(sidestep.x, sidestep.z), out var shifted, .18f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(escape.x, escape.z), out var retreat, .5f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(attacker.position), Is.True);
            int health = rabbit.Health;
            rabbit.RequestInteraction(game.Worker);
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Attacking));
            Assert.That(agent.Warp(shifted.position), Is.True);
            Assert.That(game.Worker.InReach(rabbit.transform.position, rabbit.InteractionDistance), Is.False);
            yield return Until(() => rabbit.Health < health, 2f);

            int playerHealth = game.State.Health;
            Assert.That(game.Worker.WarpTo(retreat.position), Is.True);
            yield return Until(() => agent.hasPath && agent.velocity.sqrMagnitude > .01f, 3f);
            yield return Until(() => game.State.Health < playerHealth, 5f);
        }

        [UnityTest]
        public IEnumerator BottomHealthBarShowsDamageAndAnInventoryPotionRestoresHealth()
        {
            var state = game.State;
            var merchant = game.FindInteraction(VillageRole.Merchant);
            var potion = merchant.Definition.ShopItems.Single(item => item.Id == ItemIds.HealthPotion).ToSpec();
            Assert.That(state.Inventory.TryAdd(ItemIds.Gold, potion.Price), Is.True);
            Assert.That(game.Perform(() => state.TryBuy(ItemIds.HealthPotion)), Is.True);
            var entry = state.Inventory.Entries.Single(item => item.ItemId == ItemIds.HealthPotion);

            var status = GameObject.Find("Character status").transform;
            var health = status.Find("Health bar");
            var fill = health.Find("Health fill").GetComponent<Image>();
            var change = health.Find("Health change").GetComponent<Image>();
            var stamina = status.Find("Stamina bar background/Stamina bar").GetComponent<Image>();
            float staminaBefore = stamina.fillAmount;
            game.ReceiveAnimalHit(potion.HealthRestore + 10);
            yield return Until(() => change.fillAmount > fill.fillAmount + .05f, 1f);
            Assert.That(change.color.r, Is.GreaterThan(change.color.g));
            float damaged = (float)state.Health / state.HealthLimit;
            Assert.That(fill.fillAmount, Is.EqualTo(damaged).Within(.01f));
            yield return Until(() => change.fillAmount == 0f, 1f);

            game.Hud.OpenItemContext(entry.Id, new Vector2(Screen.width * .5f, Screen.height * .5f));
            var context = UnityEngine.Object.FindFirstObjectByType<ItemContextView>();
            var use = FindButton(context, "사용");
            Assert.That(use.interactable, Is.True);
            use.onClick.Invoke();
            Assert.That(state.Inventory.Find(entry.Id), Is.Null);
            Assert.That(state.Health, Is.EqualTo(state.HealthLimit - 10));
            yield return Until(() => change.fillAmount > fill.fillAmount + .05f, 1f);
            Assert.That(change.color.g, Is.GreaterThan(change.color.r));
            Assert.That(fill.fillAmount, Is.LessThan((float)state.Health / state.HealthLimit));
            yield return Until(() => change.fillAmount == 0f, 1f);
            Assert.That(fill.fillAmount, Is.EqualTo((float)state.Health / state.HealthLimit).Within(.01f));
            Assert.That(stamina.fillAmount, Is.EqualTo(staminaBefore));
        }

        [UnityTest]
        public IEnumerator ItemShortcutsEquipToolsAndUsePotionsOutsideChat()
        {
            var state = game.State;
            Assert.That(state.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.HealthPotion, 2), Is.True);
            var axe = state.Inventory.Entries.Single(item => item.ItemId == ItemIds.Axe);
            var potion = state.Inventory.Entries.Single(item => item.ItemId == ItemIds.HealthPotion);
            var bar = UnityEngine.Object.FindFirstObjectByType<ItemShortcutBarView>();
            var slots = bar.GetComponentsInChildren<ItemShortcutSlotView>();
            Assert.That(slots.Length, Is.EqualTo(10));
            var first = slots.Single(slot => slot.Index == 0);
            var last = slots.Single(slot => slot.Index == 9);
            var inventory = game.Hud.OpenWindow("inventory") as InventoryWindowView;
            inventory.Refresh();
            yield return new WaitForSecondsRealtime(.35f);
            Canvas.ForceUpdateCanvases();

            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Vector2 source = Center(inventory.GetComponentsInChildren<InventoryItemDrag>().Single(item => item.EntryId == axe.Id).transform);
                Vector2 destination = Center(first.transform);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = source, buttons = 1 });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = destination, buttons = 1 });
                yield return Until(() => game.Hud.IsHoldingItem, 1f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = destination });
                yield return Until(() => state.ItemShortcutAt(0) == ItemIds.Axe, 1f);
                Assert.That(state.Inventory.Find(axe.Id), Is.SameAs(axe));

                game.Hud.BeginItemDrag(potion.Id, Center(last.transform));
                last.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(state.ItemShortcutAt(9), Is.EqualTo(ItemIds.HealthPotion));
                Assert.That(state.Inventory.Find(potion.Id).Quantity, Is.EqualTo(2));
                Assert.That(game.Hud.IsHoldingItem, Is.False);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit1));
                yield return Until(() => state.Equipment.TryGetValue(EquipmentSlot.MainHand, out var id) && id == ItemIds.Axe, 1f);
                first.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(state.Equipment[EquipmentSlot.MainHand], Is.EqualTo(ItemIds.Axe));

                game.ReceiveAnimalHit(40);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return Until(() => game.Hud.IsChatTyping, 1f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit0));
                yield return null;
                Assert.That(state.Inventory.Count(ItemIds.HealthPotion), Is.EqualTo(2));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return Until(() => !game.Hud.IsChatTyping, 1f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit0));
                yield return Until(() => state.Inventory.Count(ItemIds.HealthPotion) == 1, 1f);
                Assert.That(state.Health, Is.EqualTo(state.HealthLimit - 10));

                last.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right });
                Assert.That(state.ItemShortcutAt(9), Is.Null);
                Assert.That(state.Inventory.Count(ItemIds.HealthPotion), Is.EqualTo(1));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }

            static Vector2 Center(Transform item)
            {
                var rect = (RectTransform)item;
                return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            }
        }

        [UnityTest]
        public IEnumerator TreeReactsToBareHandedAndAxeImpacts()
        {
            var tree = game.Crops.OfType<LoggingTree>().First();
            Vector3 approach = tree.transform.position + Vector3.right * 1.75f;
            Assert.That(NavMesh.SamplePosition(approach, out var at, .4f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(at.position);
            int remaining = tree.Remaining;
            var visual = tree.transform.Find("Visual");
            Quaternion rest = visual.localRotation;
            Vector3 scale = visual.localScale;
            Assert.That(tree.InteractionVerbFor(game.Worker), Is.EqualTo("때리기"));
            tree.RequestInteraction(game.Worker);
            yield return Until(() => WorldInteractable.Active.OfType<GroundPickup>().Any(item => item.ItemId == tree.Branch.Id), 3f);
            var branch = WorldInteractable.Active.OfType<GroundPickup>().Single(item => item.ItemId == tree.Branch.Id);
            Assert.That(tree.Remaining, Is.EqualTo(remaining));
            Assert.That(branch.Quantity, Is.EqualTo(1));
            yield return Until(() => Quaternion.Angle(visual.localRotation, rest) > .2f, .25f);
            Assert.That(visual.localScale, Is.EqualTo(scale));
            yield return Until(() => Quaternion.Angle(visual.localRotation, rest) < .01f, .5f);

            Assert.That(game.State.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            Assert.That(game.State.TryEquip(game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.Axe).Id), Is.True);
            tree.RequestInteraction(game.Worker);
            yield return Until(() => game.Worker.ActiveCrop == tree, 3f);
            Assert.That(Quaternion.Angle(visual.localRotation, rest), Is.LessThan(.01f));
            yield return Until(() => Quaternion.Angle(visual.localRotation, rest) > .2f, tree.Definition.HarvestSeconds);
            Assert.That(game.Worker.ActiveCrop, Is.SameAs(tree));
            Assert.That(visual.localScale, Is.EqualTo(scale));
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
            yield return Until(() => !game.IsWorldTransitioning, 3f);
            Assert.That(game.InRebirthZone, Is.True);
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
            yield return Until(() => !game.IsWorldTransitioning, 3f);
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
                WorldNameplateView view = null;
                foreach (var candidate in UnityEngine.Object.FindObjectsByType<WorldNameplateView>(FindObjectsSortMode.None))
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
                var invalid = new Vector2(float.NaN, float.NegativeInfinity);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = invalid, buttons = 1 });
                yield return null;
                Assert.That(WorkerbornInput.TryGetPointerPosition(out var lastPosition), Is.False);
                Assert.That(lastPosition, Is.EqualTo(pixel));
                Assert.That(game.Targeting.CurrentTarget, Is.Null);
                Assert.That(game.Targeting.UiAt(invalid), Is.Null);
                Assert.That(game.Targeting.TryDropPoint(invalid, out _), Is.False);
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Idle));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel });
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
            var view = UnityEngine.Object.Instantiate(game.Hud.Library.Nameplate, game.Hud.transform);
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
        public IEnumerator AltShowsThePlayerNameBoxAndHealthForLivingCombatTargets()
        {
            var rabbit = UnityEngine.Object.FindObjectsByType<MonsterInteractable>(FindObjectsSortMode.None)
                .Single(animal => animal.Combat != null);
            Assert.That(NavMesh.SamplePosition(rabbit.transform.position + Vector3.right * .75f,
                out var at, .4f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(at.position);
            game.CameraRig.SnapToTarget();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { position = -Vector2.one });
                yield return null;
                yield return null;
                var playerName = UnityEngine.Object.FindFirstObjectByType<PlayerOverheadView>().Nameplate;
                var rabbitName = UnityEngine.Object.FindObjectsByType<WorldNameplateView>(FindObjectsSortMode.None)
                    .Single(view => view.Subject == rabbit);
                var playerBox = playerName.GetComponent<Image>();
                var playerBar = playerName.transform.Find("Health bar").gameObject;
                var rabbitBar = rabbitName.transform.Find("Health bar").gameObject;
                Assert.That(playerBox.color.a, Is.Zero);
                Assert.That(playerBar.activeInHierarchy, Is.False);
                Assert.That(rabbitBar.activeInHierarchy, Is.False);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftAlt));
                yield return null;
                yield return null;
                Assert.That(playerBox.color.a, Is.GreaterThan(0f));
                Assert.That(playerBox.raycastTarget, Is.False);
                Assert.That(playerBar.activeInHierarchy, Is.True);
                Assert.That(rabbitBar.activeInHierarchy, Is.True);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return null;
                Assert.That(playerBox.color.a, Is.Zero);
                Assert.That(playerBar.activeInHierarchy, Is.False);
                Assert.That(rabbitBar.activeInHierarchy, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator CrowdedPickupNamesStayAtTheirProjectedPositionsWithAlt()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pile = new GroundPickup[12];
            try
            {
                Vector3 position = game.Worker.transform.position + Vector3.right;
                var prefab = game.Settings.FindItem(ItemIds.Potato).GroundPrefab;
                for (int i = 0; i < pile.Length; i++)
                {
                    pile[i] = UnityEngine.Object.Instantiate(prefab);
                    Vector3 at = i == 0 ? position + game.WorldCamera.transform.forward * .75f : position;
                    pile[i].Initialize(game.Crops[0].Definition, at, at, true);
                }

                yield return null;
                yield return null;
                AssertPositions(false);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftAlt));
                yield return null;
                yield return null;
                AssertPositions(true);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return null;
                AssertPositions(false);
            }
            finally
            {
                foreach (var pickup in pile)
                    if (pickup != null)
                        UnityEngine.Object.Destroy(pickup.gameObject);
                InputSystem.RemoveDevice(keyboard);
            }

            void AssertPositions(bool boxed)
            {
                var visibleNames = UnityEngine.Object.FindObjectsByType<WorldNameplateView>(FindObjectsSortMode.None)
                    .Where(view => view.Target is GroundPickup pickup && pile.Contains(pickup)).ToArray();
                Assert.That(visibleNames, Has.Length.EqualTo(pile.Length));
                foreach (var name in visibleNames)
                {
                    var projected = (Vector2)game.WorldCamera.WorldToScreenPoint(name.Target.NamePosition);
                    var center = name.Rect.TransformPoint(name.Rect.rect.center);
                    var actual = RectTransformUtility.WorldToScreenPoint(null, center);
                    Assert.That(Vector2.Distance(projected, actual), Is.LessThan(.1f));
                    Assert.That(name.GetComponent<Image>().raycastTarget, Is.EqualTo(boxed));
                }
                var far = visibleNames.Single(name => name.Target == pile[0]);
                var near = visibleNames.Single(name => name.Target == pile[1]);
                Assert.That(far.transform.GetSiblingIndex(), Is.LessThan(near.transform.GetSiblingIndex()));
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
                var name = UnityEngine.Object.FindObjectsByType<WorldNameplateView>(FindObjectsSortMode.None).Single(v => v.Target == item);
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
        public IEnumerator PondWaterCanBePickedInTheTransparentPass()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Picking the rendered water requires a graphics device.");
            Assert.That(game.PerformWorld(CampaignAction.BuildFacility, "east_pond", 4), Is.True);
            var water = UnityEngine.Object.FindFirstObjectByType<FishingWater>();
            var surface = water.GetComponentInChildren<MeshCollider>();
            var center = surface.transform.TransformPoint(surface.sharedMesh.bounds.center);
            var camera = game.WorldCamera;
            var mouse = InputSystem.AddDevice<Mouse>();
            game.CameraRig.enabled = false;
            try
            {
                camera.transform.SetPositionAndRotation(center + Vector3.up * 6f,
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
                Vector2 pixel = camera.WorldToScreenPoint(center);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel });
                yield return Until(() => game.Targeting.CurrentTarget == water, 3f);
            }
            finally
            {
                game.CameraRig.enabled = true;
                InputSystem.RemoveDevice(mouse);
            }
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

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var slotRect = (RectTransform)slot.transform;
                var slotPoint = RectTransformUtility.WorldToScreenPoint(null, slotRect.TransformPoint(slotRect.rect.center));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = slotPoint, buttons = 1 });
                yield return null;
                Assert.That(held.transform.GetSiblingIndex(), Is.GreaterThan(inventory.transform.GetSiblingIndex()));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = slotPoint });
                yield return null;
                Assert.That(held.gameObject.activeInHierarchy, Is.True);
                Assert.That(game.Hud.IsHoldingItem, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }

            var notice = game.Hud.GetComponentsInChildren<CanvasGroup>(true).Single(group => group.name == "System message");
            var noticeCanvas = notice.GetComponent<Canvas>();
            Assert.That(notice.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("해당 위치에 장착할 수 없습니다."));
            Assert.That(notice.alpha, Is.LessThan(1f));
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
                Assert.That(game.State.Inventory.TryAdd(ItemIds.LegendAxe, 1), Is.True);
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
                game.Hud.BeginItemDrag(potato.Id, saleClick);
                game.Hud.SellHeldItem(saleClick);
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
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                yield return null;
                game.Hud.ClickInventoryItem(potato.Id, saleClick);
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.Zero);
                Assert.That(game.Hud.HasQuantityPrompt, Is.False);
                var legend = game.State.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.LegendAxe);
                game.Hud.ClickInventoryItem(legend.Id, saleClick);
                Assert.That(game.State.Inventory.Count(ItemIds.LegendAxe), Is.EqualTo(1));
                Assert.That(FindButton(prompt, "판매 확인"), Is.Not.Null);
                game.Hud.CloseWindow("quantity");

                var axe = ItemCatalog.Get(ItemIds.Axe);
                Assert.That(game.State.Inventory.TryAdd(ItemIds.Gold, axe.Price * 3), Is.True);
                yield return null;
                stock = shop.GetComponentInChildren<UiItemView>().GetComponent<Button>();
                Assert.That(stock.IsInteractable(), Is.True);

                stock.onClick.Invoke();
                Assert.That(game.State.Inventory.Count(ItemIds.Axe), Is.EqualTo(1));
                Assert.That(game.Hud.IsHoldingItem, Is.False);
                Assert.That(game.Hud.HasQuantityPrompt, Is.False);
                yield return null;
                stock = shop.GetComponentInChildren<UiItemView>().GetComponent<Button>();
                Vector2 buyClick = ScreenCenter((RectTransform)stock.transform);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(mouse, new MouseState { position = buyClick });
                yield return null;
                shop.GetComponentInChildren<UiItemView>().GetComponent<Button>().onClick.Invoke();
                Assert.That(game.Hud.HasQuantityPrompt, Is.True);
                VerifyPopupPosition(prompt.Rect, buyClick);
                Assert.That(inventoryInput.blocksRaycasts, Is.False);
                FindButton(prompt, "구입").onClick.Invoke();
                Assert.That(game.Hud.IsHoldingItem, Is.True);
                Assert.That(game.State.Inventory.Count(ItemIds.Axe), Is.EqualTo(1));
                Assert.That(game.State.PendingPurchase.ItemId, Is.EqualTo(ItemIds.Axe));
                game.Hud.ClickEquipment(EquipmentSlot.MainHand, buyClick);
                Assert.That(game.State.Equipment[EquipmentSlot.MainHand], Is.EqualTo(ItemIds.Axe));
                Assert.That(game.Hud.IsHoldingItem, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator DialoguePagesAcceptInputAndJobDetailsStayAboveTheConversation()
        {
            var merchant = game.FindInteraction(VillageRole.Merchant);
            var greeting = merchant.Definition.Dialogue.Topics.Single(topic => topic.action == DialogueAction.Greeting);
            string original = greeting.text;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                greeting.text = string.Join("\n", Enumerable.Range(1, 60).Select(line => $"대화 {line}"));
                game.Hud.OpenDialogue(merchant);
                var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
                yield return null;
                var body = dialogue.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Dialogue");
                var next = dialogue.GetComponentsInChildren<Button>(true)
                    .Single(button => button.GetComponentInChildren<TMP_Text>(true)?.text == "다음");
                Assert.That(body.textInfo.pageCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(body.pageToDisplay, Is.EqualTo(1));
                Assert.That(next.gameObject.activeInHierarchy, Is.False);
                Assert.That(dialogue.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "거래하기" || text.text == "대화 종료"), Is.False);
                game.Hud.CloseWindow("conversation");
                Assert.That(game.Hud.IsSpeakingWith(merchant), Is.True);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                yield return null;
                Assert.That(body.pageToDisplay, Is.EqualTo(1));
                Assert.That(body.maxVisibleCharacters, Is.GreaterThanOrEqualTo(body.textInfo.pageInfo[0].lastCharacterIndex + 1));
                Assert.That(next.gameObject.activeInHierarchy, Is.True);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return null;
                Assert.That(body.pageToDisplay, Is.EqualTo(2));
                Assert.That(next.gameObject.activeInHierarchy, Is.False);
                Assert.That(game.Hud.IsChatTyping, Is.False);
                yield return Until(() => next.gameObject.activeInHierarchy, 6f);
                Assert.That(body.maxVisibleCharacters, Is.GreaterThanOrEqualTo(body.textInfo.pageInfo[1].lastCharacterIndex + 1));
                next.onClick.Invoke();
                Assert.That(body.pageToDisplay, Is.EqualTo(3));
                Assert.That(next.gameObject.activeInHierarchy, Is.False);

                game.Hud.OpenDialogue(game.FindInteraction(VillageRole.Farmer));
                FindButton(dialogue, "아르바이트를 묻는다").onClick.Invoke();
                yield return null;
                var card = (RectTransform)dialogue.transform.Find("Opening content/Part-time job information");
                Assert.That(card.gameObject.activeInHierarchy, Is.True);
                Assert.That(card.anchoredPosition.y, Is.GreaterThan(0f));
                Assert.That(body.text, Does.Not.Contain("Gold"));
                var details = card.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Job details");
                Assert.That(details.text, Does.Contain("Gold").And.Contain("접수 중"));
                Assert.That(dialogue.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "대화 종료"), Is.False);
                FindButton(dialogue, "일을 맡는다").onClick.Invoke();
                Assert.That(game.State.Job, Is.EqualTo(JobStatus.Active));
                Assert.That(body.text, Does.Contain("계약서를 챙겨 가"));
            }
            finally
            {
                greeting.text = original;
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
            Assert.That(previous.IsWorldTransitioning, Is.True);
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
        public IEnumerator BothMapsUseTheRegionAndUpdateWhenItsTerrainChanges()
        {
            var region = game.MapRegion;
            var miniMap = game.Hud.GetComponentInChildren<VillageMapView>();
            var window = game.Hud.OpenWindow("map");
            yield return null;

            var miniImage = miniMap.GetComponentInChildren<RawImage>();
            var fullImage = window.GetComponentInChildren<VillageMapView>().GetComponentInChildren<RawImage>();
            Assert.That(miniImage.texture, Is.TypeOf<Texture2D>());
            Assert.That(fullImage.texture, Is.SameAs(miniImage.texture));
            Assert.That(fullImage.color, Is.EqualTo(miniImage.color));
            Assert.That(miniImage.uvRect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
            Assert.That(fullImage.uvRect, Is.EqualTo(miniImage.uvRect));
            Assert.That(miniImage.rectTransform.rect.width / miniImage.rectTransform.rect.height,
                Is.EqualTo(region.WorldBounds.width / region.WorldBounds.height).Within(.001f));
            var point = new Vector2(.35f, .66f);
            Vector2 location = region.MapToWorld(point);
            Assert.That(game.TryMapGround(point, out var hit), Is.True);
            Assert.That(hit.point.x, Is.EqualTo(location.x).Within(.01f));
            Assert.That(hit.point.z, Is.EqualTo(location.y).Within(.01f));
            Assert.That(Vector2.Distance(region.WorldToMap(hit.point), point), Is.LessThan(.001f));

            var footprints = new System.Collections.Generic.List<VillageMapFootprint>();
            game.CollectMapFootprints(footprints);
            Assert.That(footprints, Is.Not.Empty);
            Vector2 housePoint = region.WorldToMap(footprints[0].WorldCenter);
            var villageTexture = (Texture2D)miniImage.texture;
            int houseX = (int)(housePoint.x * villageTexture.width);
            int houseZ = (int)(housePoint.y * villageTexture.height);
            Color filled = villageTexture.GetPixel(houseX, houseZ);
            Assert.That(region.TerrainImage(game.GroundTerrain).GetPixel(houseX, houseZ), Is.Not.EqualTo(filled));

            // Use its own TerrainData so the authored scene can never be edited by this test.
            Rect bounds = region.WorldBounds;
            var data = new TerrainData
            {
                heightmapResolution = 33,
                alphamapResolution = 16,
                size = new Vector3(bounds.width + 8f, 30f, bounds.height + 8f),
                terrainLayers = game.GroundTerrain.terrainData.terrainLayers.Take(2).ToArray()
            };
            var sample = Terrain.CreateTerrainGameObject(data);
            sample.transform.position = new Vector3(bounds.xMin - 4f, 0f, bounds.yMin - 4f);
            try
            {
                var paint = new float[16, 16, 2];
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        paint[z, x, 0] = 1f;
                data.SetAlphamaps(0, 0, paint);
                var texture = region.TerrainImage(sample.GetComponent<Terrain>());
                int column = texture.width / 2;
                int row = texture.height / 2;
                Color original = texture.GetPixel(column, row);

                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        paint[z, x, 0] = 0f;
                        paint[z, x, 1] = 1f;
                    }
                data.SetAlphamaps(0, 0, paint);
                Assert.That(region.TerrainImage(sample.GetComponent<Terrain>()), Is.SameAs(texture));
                Color painted = texture.GetPixel(column, row);
                Assert.That(painted, Is.Not.EqualTo(original));

                var heights = new float[33, 33];
                for (int z = 0; z < 33; z++)
                    for (int x = 0; x < 33; x++)
                        heights[z, x] = x / 32f;
                data.SetHeights(0, 0, heights);
                Assert.That(region.TerrainImage(sample.GetComponent<Terrain>()), Is.SameAs(texture));
                Assert.That(texture.GetPixel(column, row), Is.Not.EqualTo(painted));
            }
            finally
            {
                UnityEngine.Object.Destroy(sample);
                UnityEngine.Object.Destroy(data);
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
            Assert.That(hours.Length, Is.EqualTo(1));
            var schedule = map.GetComponentInChildren<PartTimeScheduleView>();
            var date = schedule.transform.Find("Schedule date").GetComponent<TMP_Text>();
            var previousDay = schedule.transform.Find("Previous schedule day").GetComponent<Button>();
            var nextDay = schedule.transform.Find("Next schedule day").GetComponent<Button>();
            Assert.That(date.text, Is.EqualTo(VillageClockView.DateLabel(game.State.Day)));
            Assert.That(previousDay.interactable, Is.False);
            for (int day = 1; day <= 7; day++)
            {
                nextDay.onClick.Invoke();
                Assert.That(date.text, Is.EqualTo(VillageClockView.DateLabel(game.State.Day + day)));
            }
            Assert.That(nextDay.interactable, Is.False);
            for (int day = 1; day <= 7; day++)
                previousDay.onClick.Invoke();
            Assert.That(previousDay.interactable, Is.False);
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

                fullRow.OnPointerClick(pointer);
                yield return Until(() => miniMarker.gameObject.activeSelf && fullMarker.gameObject.activeSelf, 2f);
                Assert.That(miniMarker.Find("Direction arrow").gameObject.activeSelf, Is.True);
                var waypoint = UnityEngine.Object.FindFirstObjectByType<JobWaypointView>();
                Assert.That(waypoint.GetComponent<Image>().sprite.name, Does.Contain("Arrow"));
                workplace.position = game.Worker.transform.position;
                yield return null;
                yield return null;
                Assert.That(waypoint.GetComponent<Image>().sprite.name, Does.Contain("Mappin"));
                Assert.That(fullMarker.gameObject.activeSelf, Is.True);
                fullRow.transform.Find("Job tracking").GetComponent<Button>().onClick.Invoke();
                yield return Until(() => !miniMarker.gameObject.activeSelf && !fullMarker.gameObject.activeSelf, 2f);
                Assert.That(waypoint.gameObject.activeSelf, Is.False);
            }
            finally
            {
                workplace.position = position;
            }

            var state = game.State;
            state.AdvanceTime((state.Rules.JobEnd - state.Minute + 10) / state.Rules.MinutesPerSecond);
            yield return null;
            Assert.That(miniRow.gameObject.activeInHierarchy, Is.False);
            Assert.That(miniSchedule.transform.Find("Empty schedule").GetComponent<TMP_Text>().text, Is.EqualTo("오늘 일정 종료"));
            Assert.That(fullRow.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Job status").text,
                Is.EqualTo("마감"));
        }

        [UnityTest]
        public IEnumerator JobReminderRepeatsOnEachOpeningUntilTurnedOff()
        {
            var state = game.State;
            var job = game.Hud.Jobs.Jobs.Single(row => row.Id == state.Rules.JobId);
            string preference = "Workerborn.JobReminder." + job.Id;
            bool hadPreference = PlayerPrefs.HasKey(preference);
            int original = PlayerPrefs.GetInt(preference);
            var bell = game.Hud.GetComponentInChildren<PartTimeScheduleRowView>().GetComponentInChildren<Button>();
            var message = game.Hud.GetComponentsInChildren<TMP_Text>(true).Single(label => label.name == "Message");
            try
            {
                if (job.Reminder)
                    bell.onClick.Invoke();
                Assert.That(state.TrySleep(false), Is.True);
                bell.onClick.Invoke();
                Assert.That(PlayerPrefs.GetInt(preference), Is.EqualTo(1));
                yield return null;
                Assert.That(job.LastRemindedDay, Is.LessThan(state.Day));

                for (int opening = 0; opening < 2; opening++)
                {
                    Assert.That(state.TryWaitForWork(), Is.True);
                    yield return Until(() => job.LastRemindedDay == state.Day, 2f);
                    Assert.That(message.text, Is.EqualTo($"{job.Title} 아르바이트가 시작됐어요."));
                    game.Hud.ShowToast("다른 소식");
                    yield return null;
                    Assert.That(message.text, Is.EqualTo("다른 소식"));
                    Assert.That(state.TrySleep(false), Is.True);
                }

                bell.onClick.Invoke();
                Assert.That(PlayerPrefs.GetInt(preference), Is.Zero);
                Assert.That(state.TryWaitForWork(), Is.True);
                yield return null;
                Assert.That(job.LastRemindedDay, Is.LessThan(state.Day));
            }
            finally
            {
                if (hadPreference)
                    PlayerPrefs.SetInt(preference, original);
                else
                    PlayerPrefs.DeleteKey(preference);
                PlayerPrefs.Save();
            }
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
                var playerHealth = overhead.Nameplate.transform.Find("Health bar");
                yield return MovePointer(overhead.Nameplate.Rect);
                Assert.That(playerHealth.gameObject.activeSelf, Is.True);
                yield return MovePointer(historyViewport);
                Assert.That(playerHealth.gameObject.activeSelf, Is.False);
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
                Assert.That(label.GetComponentInParent<WorldNameplateView>().Rect.anchoredPosition.y, Is.GreaterThan(0));

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
        public IEnumerator ShiftSelectsWalkingWhenTheMapIsClicked()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Vector3 destination = NearbyDestination();
                var map = game.Hud.OpenWindow("map").GetComponentInChildren<VillageMapView>();
                yield return null;
                var image = map.GetComponentInChildren<RawImage>().rectTransform;
                var point = Rect.NormalizedToPoint(image.rect, game.MapRegion.WorldToMap(destination));
                var click = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, image.TransformPoint(point)),
                    button = PointerEventData.InputButton.Left
                };
                var mapClick = map.GetComponent<VillageMapClick>();
                mapClick.OnPointerClick(click);
                yield return null;
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
                Assert.That(game.Worker.IsWalking, Is.False);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
                yield return null;
                Assert.That(game.Worker.IsWalking, Is.False);
                mapClick.OnPointerClick(click);
                yield return null;
                Assert.That(game.Worker.IsWalking, Is.True);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(game.Worker.IsWalking, Is.True);
                Assert.That(game.Worker.GetComponent<WorkerAnimation>().CurrentAction, Is.EqualTo(3));
                mapClick.OnPointerClick(click);
                Assert.That(game.Worker.IsWalking, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator KeyboardMovementWalksOnlyWhileShiftIsHeld()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var agent = game.Worker.GetComponent<NavMeshAgent>();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return null;
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
                Assert.That(game.Worker.IsWalking, Is.False);
                float running = agent.speed;

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                yield return null;
                Assert.That(game.Worker.IsWalking, Is.True);
                Assert.That(agent.speed, Is.LessThan(running));
                Assert.That(game.Worker.GetComponent<WorkerAnimation>().CurrentAction, Is.EqualTo((int)WorkerMotion.Walk));

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return null;
                Assert.That(game.Worker.IsWalking, Is.False);
                Assert.That(agent.speed, Is.EqualTo(running));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator KeyboardMovementStopsAtUnbakedWorldGeometry()
        {
            var agent = game.Worker.GetComponent<NavMeshAgent>();
            Vector3 start = agent.nextPosition;
            Vector3 right = Vector3.ProjectOnPlane(game.WorldCamera.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            var (key, direction) = new[] { (Key.W, forward), (Key.S, -forward), (Key.A, -right), (Key.D, right) }
                .First(move => !agent.Raycast(start + move.Item2 * 1.5f, out _));
            var wall = new GameObject("Unbaked pointer geometry") { layer = VillageMapProp.PointerGeometryLayer };
            wall.transform.SetPositionAndRotation(start + direction * .85f + Vector3.up * .8f, Quaternion.LookRotation(direction));
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.5f, 1.6f, .14f);
            Physics.SyncTransforms();

            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return new WaitForSeconds(.65f);
                Assert.That(Vector3.Dot(agent.nextPosition - start, direction), Is.InRange(.15f, .5f));
                collider.enabled = false;
                Physics.SyncTransforms();
                yield return Until(() => Vector3.Dot(agent.nextPosition - start, direction) > 1f, 1f);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                UnityEngine.Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator CameraCanZoomOverTheWorldDuringDialogue()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                game.Hud.OpenDialogue(game.FindInteraction(VillageRole.Merchant));
                float start = game.CameraRig.Distance;
                Assert.That(game.ControlsBlocked, Is.True);
                InputSystem.QueueStateEvent(mouse, new MouseState
                {
                    position = new Vector2(Screen.width * .5f, Screen.height * .75f),
                    scroll = new Vector2(0f, 120f)
                });
                yield return null;
                Assert.That(game.CameraRig.Distance, Is.LessThan(start));
                Assert.That(game.Hud.HasModal, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator BuildingRestoresItsInteriorAndAlwaysLetsPlayersLeave()
        {
            var store = WorldInteractable.Active.OfType<VillageBuildingDoor>().Single(door => door.BuildingId == "general-store");
            Assert.That(store.IsOpen, Is.True);
            Assert.That(store.DisplayName, Is.EqualTo("잡화점"));
            Assert.That(store.ShowNameByDefault, Is.True);
            var hingedModel = store.transform.Find("Hinged door model");
            var frame = hingedModel.Find("Original door frame").GetComponent<MeshRenderer>();
            Assert.That(store.NamePosition.y, Is.GreaterThan(frame.bounds.max.y));
            var leaf = hingedModel.Find("Door leaf");
            Assert.That(leaf.GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("MOD_Door_part_interior"));
            Assert.That(leaf.GetComponent<BoxCollider>(), Is.Not.Null);
            var backing = hingedModel.Find("Doorway shade");
            Assert.That(backing.GetComponent<MeshRenderer>(), Is.Not.Null);
            Assert.That(backing.localPosition.z, Is.LessThan(leaf.localPosition.z));
            Assert.That(backing.GetComponent<Collider>(), Is.Null);

            var fade = game.Hud.transform.Find("World transition").GetComponent<CanvasGroup>();
            CameraClearFlags outdoorClear = game.WorldCamera.clearFlags;
            int outdoorMask = game.WorldCamera.cullingMask;
            Vector3 approach = store.Approach.position;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(approach.x, approach.z), out var outside, .8f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(outside.position), Is.True);
            store.RequestInteraction(game.Worker);
            Assert.That(game.CurrentInterior, Is.Null);
            yield return Until(() => game.CurrentInterior == store.Interior, 3f);
            Assert.That(fade.alpha, Is.EqualTo(1f));
            yield return Until(() => !game.IsWorldTransitioning, 3f);
            var inside = game.CurrentInterior;
            Assert.That(inside.Id, Is.EqualTo("general-store"));
            Assert.That(game.MapBounds, Is.EqualTo(inside.MapBounds));
            Assert.That(game.SharesMap(store.transform.position), Is.False);
            Assert.That(game.TryMapGround(Vector2.one * .5f, out var floor), Is.True);
            Assert.That(floor.collider, Is.SameAs(inside.Floor));
            Assert.That(game.WorldCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(game.WorldCamera.backgroundColor, Is.EqualTo(Color.black));
            Assert.That(game.WorldCamera.cullingMask & (1 << VillageWeather.SkyLayer), Is.Zero);

            game.State.AdvanceTime((20f * 60f + 10f - game.State.Minute) / game.State.Rules.MinutesPerSecond);
            Assert.That(store.IsOpen, Is.False);
            Assert.That(store.ShowNameByDefault, Is.True);
            Assert.That(inside.Exit.IsOpen, Is.True);
            Assert.That(inside.Exit.ShowNameByDefault, Is.True);
            Assert.That(inside.Exit.DisplayName, Is.EqualTo("솔바람 마을"));
            var save = game.CaptureSave();
            Assert.That(save.interior.id, Is.EqualTo(inside.Id));
            Assert.That(VillageSaveStore.TrySave(save, out _), Is.True);
            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            inside = game.CurrentInterior;
            Assert.That(inside.Id, Is.EqualTo("general-store"));
            Assert.That(game.WorldCamera.backgroundColor, Is.EqualTo(Color.black));
            store = inside.Entrance;
            Assert.That(store.IsOpen, Is.False);

            inside.Exit.RequestInteraction(game.Worker);
            yield return Until(() => game.CurrentInterior == null, 3f);
            yield return Until(() => !game.IsWorldTransitioning, 3f);
            Assert.That(game.WorldCamera.clearFlags, Is.EqualTo(outdoorClear));
            Assert.That(game.WorldCamera.cullingMask, Is.EqualTo(outdoorMask));
            Assert.That(game.Worker.InReach(store.Approach.position, 1f), Is.True);
            store.RequestInteraction(game.Worker);
            yield return null;
            Assert.That(game.CurrentInterior, Is.Null);
        }

        [UnityTest]
        public IEnumerator InventoryDropWaitsForArrival()
        {
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 3), Is.True);
            var stack = game.State.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Potato);
            Vector3 destination = NearbyDestination();
            int before = game.GroundItemCount;
            Assert.That(game.Worker.CommandDrop(stack.Id, destination), Is.True);
            yield return null;
            Assert.That(game.State.Inventory.Find(stack.Id).Quantity, Is.EqualTo(3));
            Assert.That(game.GroundItemCount, Is.EqualTo(before));
            game.Worker.Cancel();
            Assert.That(game.Worker.HasPendingDrop, Is.False);
            Assert.That(game.State.Inventory.Find(stack.Id).Quantity, Is.EqualTo(3));

            Assert.That(game.Worker.CommandDrop(stack.Id, destination), Is.True);
            yield return Until(() => game.GroundItemCount == before + 1, 5f);
            var drop = UnityEngine.Object.FindObjectsByType<GroundPickup>(FindObjectsSortMode.None)
                .Single(i => i.ItemId == ItemIds.Potato && i.Quantity == 3);
            Assert.That(game.State.Inventory.Find(stack.Id), Is.Null);
            Assert.That(Vector2.Distance(new Vector2(drop.LandingPosition.x, drop.LandingPosition.z),
                new Vector2(destination.x, destination.z)), Is.LessThan(.01f));
        }

        [UnityTest]
        public IEnumerator GroundDropsReuseTheSameInstanceWithNewItemDataAndOneLandingEvent()
        {
            Vector3 destination = game.Worker.transform.position + Vector3.right * 1.9f;
            var potato = DropStack(ItemIds.Potato, 3, destination);
            var firstFilter = potato.GetComponentInChildren<MeshFilter>();
            Mesh firstMesh = firstFilter.sharedMesh;
            Assert.That(firstMesh, Is.Not.Null);
            long firstGeneration = potato.InteractionGeneration;
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
            Assert.That(egg.InteractionGeneration, Is.GreaterThan(firstGeneration));
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

        private static WorldNameplateView FindPickupName(GroundPickup item) =>
            UnityEngine.Object.FindObjectsByType<WorldNameplateView>(FindObjectsSortMode.None).SingleOrDefault(view => view.Target == item);

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
