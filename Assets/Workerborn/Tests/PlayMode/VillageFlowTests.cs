using System;
using System.Collections;
using System.Collections.Generic;
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
            // Most tests isolate the player and world action under test from concurrent farm work.
            game.FindInteraction(VillageRole.Farmer).Routine.enabled = false;
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
        public IEnumerator VillagerTakesARealBiteAtBreakfastWhilePlayingTheEatingGesture()
        {
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var worker = farmer.GetComponent<HarvestWorker>();
            var saved = worker.CharacterState.Export();
            saved.nourishment = .5d;
            worker.SetCharacterState(VillageState.Restore(saved, game.State.Rules));
            int bread = worker.CharacterState.Inventory.Count(ItemIds.Bread);
            int playerBread = game.State.Inventory.Count(ItemIds.Bread);
            Assert.That(bread, Is.GreaterThan(0));

            farmer.Routine.enabled = true;
            yield return Until(() => worker.IsEating, 3f);
            var animation = worker.GetComponent<WorkerAnimation>();
            var part = game.Settings.FindItem(ItemIds.Bread).EatingPart;
            Assert.That(animation.Gesture, Is.EqualTo(WorkerGesture.Eat));
            Assert.That(part, Is.Not.Null);
            Assert.That(worker.GetComponentsInChildren<CharacterPartInstance>().Any(item => item.Definition == part), Is.True);
            Assert.That(worker.CharacterState.Inventory.Count(ItemIds.Bread), Is.EqualTo(bread));

            yield return Until(() => worker.CharacterState.Inventory.Count(ItemIds.Bread) == bread - 1, 3f);
            Assert.That(worker.IsEating, Is.True);
            Assert.That(worker.CharacterState.Fullness, Is.Positive);
            yield return Until(() => !worker.IsEating, 3f);
            Assert.That(worker.GetComponentsInChildren<CharacterPartInstance>().Any(item => item.Definition == part), Is.False);
            Assert.That(game.State.Inventory.Count(ItemIds.Bread), Is.EqualTo(playerBread));
            var npcSave = game.CaptureSave().characters.Single(character => character.characterId == worker.CharacterId);
            Assert.That(npcSave.npcMealsIssued, Is.True);
            Assert.That(VillageState.Restore(npcSave, game.State.Rules).Inventory.Count(ItemIds.Bread), Is.EqualTo(bread - 1));
        }

        [UnityTest]
        public IEnumerator VillagerSellsProduceAndBuysAnActualMealWithoutOpeningThePlayerShop()
        {
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var merchant = game.FindInteraction(VillageRole.Merchant);
            var visitor = farmer.GetComponent<HarvestWorker>();
            var state = visitor.CharacterState;
            Assert.That(state.Inventory.TryRemove(ItemIds.Bread, state.Inventory.Count(ItemIds.Bread)), Is.True);
            Assert.That(state.Inventory.TryRemove(ItemIds.Gold, state.Gold), Is.True);
            Assert.That(state.Inventory.TryAdd(ItemIds.Potato, 4), Is.True);
            Assert.That(visitor.TryGetReachablePoint(merchant.transform.position, merchant.InteractionDistance * .7f, out var nearby), Is.True);
            Assert.That(visitor.WarpTo(nearby), Is.True);
            farmer.Routine.enabled = true;

            yield return Until(() => game.Hud.GetComponentsInChildren<NpcConversationBubbleView>(true).Any(), 10f);
            var bubble = game.Hud.GetComponentsInChildren<NpcConversationBubbleView>(true).Single();
            Assert.That(bubble.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("미라와 대화 중"));
            Assert.That(game.Hud.HasModal, Is.False);
            Assert.That(game.Hud.IsShopOpen, Is.False);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);

            yield return Until(() => state.Inventory.Count(ItemIds.Bread) == 1, 4f);
            Assert.That(state.Inventory.Count(ItemIds.Potato), Is.Zero);
            Assert.That(state.Gold, Is.EqualTo(4 * ItemCatalog.Get(ItemIds.Potato).Price - ItemCatalog.Get(ItemIds.Bread).Price));
            yield return Until(() => visitor.IsEating, 4f);
            yield return Until(() => !state.NpcMealDue, 3f);
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.Zero);
            Assert.That(state.Fullness, Is.Positive);
            Assert.That(game.Hud.GetComponentsInChildren<NpcConversationBubbleView>(true), Is.Empty);
        }

        [UnityTest]
        public IEnumerator VillagersExchangeMorningGreetingsWithoutLosingTheirWalkingRoute()
        {
            game.State.AdvanceTime((620f - game.State.Minute) / game.State.Rules.MinutesPerSecond);
            yield return null;
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var merchant = game.FindInteraction(VillageRole.Merchant);
            var passer = farmer.GetComponent<HarvestWorker>();
            var listener = merchant.GetComponent<HarvestWorker>();
            Vector3 start = game.Worker.StandingPosition;
            Vector3 endpoint = NearbyDestination(8f, direct: true);
            Vector3 close = start + (endpoint - start).normalized * 2f;
            Assert.That(NavMesh.SamplePosition(close, out var nearby, .6f, NavMesh.AllAreas), Is.True);
            Assert.That(passer.WarpTo(start), Is.True);
            Assert.That(listener.WarpTo(nearby.position), Is.True);
            Assert.That(passer.HasDirectPathTo(listener), Is.True);

            farmer.Routine.enabled = true;
            passer.CommandMove(endpoint, walk: true);
            var agent = passer.GetComponent<NavMeshAgent>();
            Vector3 originalDestination = agent.destination;
            Assert.That(farmer.Routine.TryFindConversation(passing: true, 4f), Is.True);
            Assert.That(NpcIsSaying("좋은 아침이에요!"), Is.True);
            Assert.That(passer.IsWalking, Is.True);
            Assert.That(agent.destination, Is.EqualTo(originalDestination));
            Assert.That(game.Hud.WorldActionsLocked, Is.False);

            yield return Until(() => NpcIsSaying("좋은 아침! 이따 봐요."), 2.7f);
            Assert.That(passer.IsWalking, Is.True);
            Assert.That(agent.destination, Is.EqualTo(originalDestination));
        }

        [UnityTest]
        public IEnumerator AVisitUsesTheSharedApproachAndLetsASeatedVillagerAnswerInPlace()
        {
            game.State.AdvanceTime((1280f - game.State.Minute) / game.State.Rules.MinutesPerSecond);
            yield return null;
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var merchant = game.FindInteraction(VillageRole.Merchant);
            var seated = farmer.GetComponent<HarvestWorker>();
            var visitor = merchant.GetComponent<HarvestWorker>();
            farmer.Routine.enabled = true;
            yield return null;
            Assert.That(seated.WarpTo(game.Worker.StandingPosition), Is.True);
            Assert.That(visitor.WarpTo(NearbyDestination()), Is.True);
            Assert.That(seated.ToggleSit(), Is.True);
            Vector3 seat = seated.StandingPosition;

            Assert.That(merchant.Routine.TryFindConversation(passing: false, 7f), Is.True);
            Assert.That(visitor.IsInteractingWith(farmer), Is.True);
            yield return Until(() => NpcIsSaying("여기서 쉬고 계셨군요. 잠깐 이야기해도 돼요?"), 5f);
            yield return Until(() => NpcIsSaying("그럼요. 오늘은 어떻게 보냈어요?"), 4f);
            Assert.That(seated.Action, Is.EqualTo(WorkerAction.Sitting));
            Assert.That(seated.StandingPosition, Is.EqualTo(seat));
            Assert.That(farmer.CurrentActivity, Is.EqualTo(NpcActivity.Talking));

            game.Hud.OpenDialogue(farmer);
            yield return Until(() => farmer.CurrentActivity != NpcActivity.Talking &&
                !NpcIsSaying("그럼요. 오늘은 어떻게 보냈어요?"), 1f);
            Assert.That(game.Hud.IsSpeakingWith(farmer), Is.True);
            Assert.That(seated.Action, Is.EqualTo(WorkerAction.Sitting));
        }

        [UnityTest]
        public IEnumerator InspectingAnNpcTurnsTheSeatedPlayersHeadAndDialogueMakesTheNpcLookBack()
        {
            var player = game.Worker;
            var merchant = game.FindInteraction(VillageRole.Merchant);
            merchant.Routine.enabled = false;
            var partner = merchant.GetComponent<HarvestWorker>();
            Assert.That(partner.WarpTo(NearbyDestination()), Is.True);

            Vector3 toward = partner.StandingPosition - player.StandingPosition;
            player.transform.rotation = Quaternion.AngleAxis(-50f, Vector3.up) *
                Quaternion.LookRotation(Vector3.ProjectOnPlane(toward, Vector3.up));
            Assert.That(player.ToggleSit(), Is.True);
            yield return new WaitForSeconds(1f);
            var animation = player.GetComponent<WorkerAnimation>();
            var head = player.Actor.Animator.GetBoneTransform(HumanBodyBones.Head);
            var bodyPosition = player.StandingPosition;
            var bodyRotation = player.transform.rotation;
            var headBefore = head.rotation;

            game.Hud.OpenWorldContext(merchant, new Vector2(Screen.width * .5f, Screen.height * .65f));
            Assert.That(game.Hud.ContextGazeTarget, Is.SameAs(merchant));
            yield return Until(() => animation.Gaze.weight > .8f && Quaternion.Angle(head.rotation, headBefore) > 15f, 2f);
            Assert.That(player.Action, Is.EqualTo(WorkerAction.Sitting));
            Assert.That(player.StandingPosition, Is.EqualTo(bodyPosition));
            Assert.That(Quaternion.Angle(player.transform.rotation, bodyRotation), Is.LessThan(.1f));

            partner.transform.rotation = Quaternion.AngleAxis(48f, Vector3.up) *
                Quaternion.LookRotation(Vector3.ProjectOnPlane(-toward, Vector3.up));
            game.Hud.OpenDialogue(merchant);
            var partnerAnimation = partner.GetComponent<WorkerAnimation>();
            yield return Until(() => Mathf.Abs(partnerAnimation.Gaze.yaw) > 25f && partnerAnimation.Gaze.weight > .8f, 2f);
            game.Hud.CloseDialogue();
            yield return Until(() => partnerAnimation.Gaze.weight < .04f, 1f);
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
            var announcement = game.Hud.transform.Find("Area announcement");
            var areaName = announcement.Find("Area name").GetComponent<TMP_Text>();
            var areaDescription = announcement.Find("Area description").GetComponent<TMP_Text>();
            yield return Until(() => areaName.text == game.RebirthArea.MapName && announcement.gameObject.activeSelf, 3f);
            Assert.That(areaDescription.text, Is.EqualTo(game.RebirthArea.MapDescription).And.Not.Empty);
            Assert.That(game.CaptureSave().awaitingIncarnation, Is.True);
            Assert.That(game.State.Level, Is.EqualTo(1));
            Assert.That(game.State.IsSkillLearned(SkillId.SoulGrowth), Is.False);
            Assert.That(game.State.Inventory.Entries, Is.Empty);
            yield return Until(() => game.State.Day > waiting.Day && !game.IsWorldTransitioning, 3f);
            Assert.That(game.InRebirthZone, Is.True);
            Assert.That(game.Hud.transform.Find("Normal HUD").gameObject.activeInHierarchy, Is.True);
            Assert.That(game.Hud.OpenWindow("inventory"), Is.Not.Null);
            var skills = game.Hud.OpenWindow("skills");
            var advance = skills.GetComponentsInChildren<Button>().Single(button => button.name == "Advance");
            Assert.That(advance.interactable, Is.True);
            var advanceGrade = skills.GetComponentsInChildren<Button>().Single(button => button.name == "Advance grade");
            Assert.That(advanceGrade.interactable, Is.True);
            advanceGrade.onClick.Invoke();
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
            var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
            var body = dialogue.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Dialogue");
            Assert.That(body.text, Does.Contain("영혼의 성장").And.Contain("경험치"));
            Assert.That(dialogue.GetComponentsInChildren<Button>().Any(button =>
                button.GetComponentInChildren<TMP_Text>()?.text == "환생하기"), Is.False);
            yield return Until(() => dialogue.GetComponentsInChildren<Button>().Any(button =>
                button.GetComponentInChildren<TMP_Text>()?.text == "다음"), 3f);
            FindButton(dialogue, "다음").onClick.Invoke();
            Assert.That(game.State.IsSkillLearned(SkillId.SoulGrowth), Is.False);
            var look = new CharacterLook { body = 1, hair = 7, face = 2, outfit = 4 };
            game.PreviewIncarnationLook(look);
            FindButton(dialogue, "환생하기").onClick.Invoke();
            yield return Until(() => !game.InRebirthZone, 3f);
            Assert.That(game.InRebirthZone, Is.False);
            Assert.That(game.State.LifeMinutes, Is.EqualTo(0).Within(.02));
            Assert.That(game.State.LifeLook, Is.EqualTo(look));
            Assert.That(game.State.IsSkillLearned(SkillId.SoulGrowth), Is.True);
            Assert.That(game.State.Inventory.Entries, Is.Empty);
            Assert.That(game.State.RankOf(SkillId.Harvesting), Is.EqualTo(1));
            yield return Until(() => areaName.text == "솔바람 마을" && announcement.gameObject.activeSelf, 3f);
        }

        [UnityTest]
        public IEnumerator TheWorkerStandsInPlaceBeforeMovingAwayFromASeat()
        {
            var worker = game.Worker;
            Vector3 ground = worker.transform.position;
            worker.ToggleSit();
            Assert.That(worker.Action, Is.EqualTo(WorkerAction.Sitting));
            Assert.That(worker.transform.position, Is.EqualTo(ground));
            worker.ToggleSit();
            Assert.That(worker.Action, Is.EqualTo(WorkerAction.Idle));

            var benches = new[] { GameObject.Find("Bench square"), GameObject.Find("Bench north") };
            foreach (var furniture in benches)
                foreach (var slot in furniture.GetComponentsInChildren<SeatSlot>())
                {
                    Vector3 approach = slot.ApproachPosition;
                    Assert.That(NavMesh.SamplePosition(game.SurfacePosition(approach.x, approach.z), out var reachable, .4f, NavMesh.AllAreas), Is.True);
                    Assert.That(worker.WarpTo(reachable.position), Is.True);
                    Assert.That(worker.ToggleSit(slot), Is.True, slot.transform.parent.name + " " + slot.name);
                    Assert.That(worker.OccupiedSeat, Is.SameAs(slot));
                }

            var bench = benches[0];
            var seat = bench.GetComponentInChildren<SeatSlot>();
            Assert.That(seat.Position.y - bench.transform.position.y, Is.InRange(.83f, .92f));
            Vector3 front = seat.Position + seat.Facing * Vector3.forward * .85f;
            Assert.That(seat.Covers(front), Is.True);
            Assert.That(seat.Covers(seat.Position - seat.Facing * Vector3.forward * .5f), Is.False);
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(front.x, front.z), out var standing, .35f, NavMesh.AllAreas), Is.True);
            Assert.That(worker.WarpTo(standing.position), Is.True);
            var actorVisual = worker.Actor.VisualRoot;
            Vector3 visualRest = actorVisual.localPosition;
            Assert.That(worker.ToggleSit(seat), Is.True);
            yield return new WaitForSeconds(1.1f);
            Assert.That(worker.Action, Is.EqualTo(WorkerAction.Sitting));
            Assert.That(worker.OccupiedSeat, Is.SameAs(seat));
            Assert.That(Quaternion.Angle(worker.transform.rotation, seat.Facing), Is.LessThan(.1f));
            Assert.That(actorVisual.localPosition.y, Is.GreaterThan(visualRest.y + .15f));
            var animation = worker.GetComponent<WorkerAnimation>();
            var agent = worker.GetComponent<NavMeshAgent>();
            Vector3 foothold = worker.transform.position;
            Vector3 visibleBefore = actorVisual.position;
            Vector3 destination = NearbyDestination();
            worker.CommandMove(destination);
            Assert.That(animation.IsStandingUp, Is.True);
            Assert.That(agent.isOnNavMesh, Is.True);
            yield return Until(() =>
            {
                if (!animation.IsStandingUp)
                    return true;
                Assert.That(agent.isStopped, Is.True);
                Assert.That(worker.OccupiedSeat, Is.SameAs(seat));
                Assert.That(Vector3.Distance(worker.transform.position, foothold), Is.LessThan(.01f));
                Assert.That(Vector3.ProjectOnPlane(actorVisual.position - visibleBefore, Vector3.up).magnitude, Is.LessThan(.01f));
                return false;
            }, 2f);
            yield return Until(() => Vector3.Distance(worker.transform.position, destination) < .25f, 4f);
            Assert.That(worker.OccupiedSeat, Is.Null);
            Assert.That(actorVisual.localPosition, Is.EqualTo(visualRest));
        }

        [UnityTest]
        public IEnumerator ReloadingALandedTrunkKeepsItAndItsSavedDropsInPlace()
        {
            var tree = game.Crops.OfType<LoggingTree>().First();
            var site = tree.Site();
            var midpoint = site.Point.Offset(0f, site.Section(TimberPart.Trunk).Length * .5f);
            float floor = game.SurfacePosition(midpoint.x, midpoint.z).y;
            var landed = TimberPose.AtTilt(midpoint, floor + site.BarkRadius, 0f, 90f);
            var save = game.CaptureSave();
            save.woodland = new WoodlandSave { trees = new[] { new TimberTreeSave
            {
                id = site.Id, felledDay = save.day, falling = true, landingPose = landed,
                parts = (1 << (int)TimberPart.Stump) | (1 << (int)TimberPart.Trunk)
            } } };
            var drop = new GroundDropSave
            {
                id = Guid.NewGuid().ToString("N"), itemId = ItemIds.Branch, quantity = 2,
                x = midpoint.x, z = midpoint.z, crownYield = true, harvestYield = true
            };
            drop.ReserveFor(game.Worker.PlayerId, GroundDropSave.UtcNow);
            save.drops = new[] { drop };
            Assert.That(VillageSaveStore.TrySave(save, out _), Is.True);

            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            game.FindInteraction(VillageRole.Farmer).Routine.enabled = false;
            var trunk = WorldInteractable.Active.OfType<TimberPiece>().Single(piece => piece.TargetId == site.Id && piece.Part == TimberPart.Trunk);
            Assert.That(trunk.CanInteract, Is.True);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<TimberFall>(), Is.Null);
            Assert.That(Vector3.Distance(trunk.transform.position, TimberLocation.Position(landed)), Is.LessThan(.02f));
            yield return new WaitForSeconds(1.2f);
            var record = game.CaptureSave();
            Assert.That(record.woodland.trees.Single().falling, Is.False);
            Assert.That(record.drops.Where(item => item.crownYield).Select(item => item.id), Is.EqualTo(new[] { drop.id }));
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
            var lobby = room.Lobby;
            Assert.That(lobby.MapBounds.width * lobby.MapBounds.height,
                Is.GreaterThan(2 * room.MapBounds.width * room.MapBounds.height));
            var guestDoor = lobby.GuestRoomDoor;
            Assert.That(guestDoor.Interior, Is.SameAs(room));
            room.Exit.RequestInteraction(game.Worker);
            yield return Until(() => game.CurrentInterior == lobby && !game.IsWorldTransitioning, 3f);
            Assert.That(Vector3.Distance(game.Worker.transform.position, guestDoor.Approach.position), Is.LessThan(.4f));
            guestDoor.RequestInteraction(game.Worker);
            yield return Until(() => game.CurrentInterior == room && !game.IsWorldTransitioning, 3f);
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
            double stamina = game.State.Stamina;
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
                Assert.That(game.State.Stamina, Is.LessThan(stamina));
                Assert.That(game.GroundItemCount, Is.EqualTo(drops));
                Assert.That(notice, Does.Contain("채집에 실패"));
                Assert.That(game.Worker.GetComponent<WorkerAnimation>().Gesture, Is.EqualTo(WorkerGesture.GatherFailure));
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
                var animator = game.PlayerActor.Animator;
                var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                float FootHeight() => Mathf.Min(leftFoot.position.y, rightFoot.position.y);
                float groundHeight = FootHeight();
                Vector3 original = visual.localPosition;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                yield return Until(() => FootHeight() > groundHeight + .1f, 1f);
                var animation = game.Worker.GetComponent<WorkerAnimation>();
                Assert.That(animation.Gesture, Is.EqualTo(WorkerGesture.Jump));
                Assert.That(animation.GestureProgress, Is.GreaterThan(0f));
                Assert.That(Vector3.Distance(game.Worker.transform.position, standing), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(game.Worker.GetComponent<NavMeshAgent>().nextPosition, standing), Is.LessThan(.002f));
                Assert.That(visual.localPosition, Is.EqualTo(original));
                yield return Until(() => animation.Gesture == WorkerGesture.None, 1f);
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
            Vector3 sidestep = home - Vector3.right * 1.1f;
            Vector3 escape = home + Vector3.right * 12f;
            Assert.That(rabbit.Combat.ChaseRadius, Is.EqualTo(30f));
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(start.x, start.z), out var attacker, .18f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(sidestep.x, sidestep.z), out var shifted, .18f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(escape.x, escape.z), out var retreat, .5f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(attacker.position), Is.True);
            int health = rabbit.Health;
            rabbit.RequestInteraction(game.Worker);
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Attacking));
            Assert.That(agent.Warp(shifted.position), Is.True);
            Assert.That(game.Worker.InReach(rabbit.transform.position, rabbit.InteractionDistance + .55f), Is.False);
            yield return Until(() => rabbit.Health < health, 2f);
            Assert.That(rabbit.GetComponent<HitFlash>().isActiveAndEnabled, Is.True);
            Assert.That(GameObject.Find("CombatHit(Clone)"), Is.Not.Null);

            int playerHealth = game.State.Health;
            Assert.That(game.Worker.WarpTo(retreat.position), Is.True);
            yield return Until(() => agent.hasPath && agent.velocity.sqrMagnitude > .01f, 3f);
            yield return Until(() => game.State.Health < playerHealth, 8f);
            Assert.That(game.PlayerActor.VisualRoot.GetComponent<HitFlash>().isActiveAndEnabled, Is.True);
            yield return Until(() => !rabbit.GetComponent<HitFlash>().isActiveAndEnabled, 1f);
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
            game.ReceiveAnimalHit(game.Worker, potion.HealthRestore + 10);
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

                Assert.That(state.Inventory.TryAdd(ItemIds.Bread, 1), Is.True);
                game.Hud.BeginItemDrag(state.Inventory.Entries.Single(item => item.ItemId == ItemIds.Bread).Id, Center(first.transform));
                GameObject upper = null;
                yield return Until(() => (upper = GameObject.Find("Shift shortcuts")) != null, 1f);
                var shifted = upper.GetComponentsInChildren<ItemShortcutSlotView>();
                Assert.That(shifted.Length, Is.EqualTo(10));
                Assert.That(first.Binding, Is.Zero);
                var upperFirst = shifted.Single(slot => slot.Binding == VillageState.ItemShortcutCount);
                Assert.That(upperFirst.transform.position.y, Is.GreaterThan(first.transform.position.y));
                upperFirst.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(state.ItemShortcutAt(VillageState.ItemShortcutCount), Is.EqualTo(ItemIds.Bread));
                Assert.That(state.ItemShortcutAt(0), Is.EqualTo(ItemIds.Axe));
                yield return Until(() => !upper.activeInHierarchy, 1f);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit1));
                yield return Until(() => state.Equipment.TryGetValue(EquipmentSlot.MainHand, out var id) && id == ItemIds.Axe, 1f);
                first.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                Assert.That(state.Equipment[EquipmentSlot.MainHand], Is.EqualTo(ItemIds.Axe));

                game.ReceiveAnimalHit(game.Worker, 40);
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
            Vector3 foot = game.SurfacePosition(tree.transform.position.x, tree.transform.position.z);
            var blocker = tree.GetComponentInChildren<VillageNavigationBlocker>();
            Assert.That(blocker.GetComponent<NavMeshObstacle>().carving, Is.True);
            yield return Until(() => !NavMesh.SamplePosition(foot, out _, .18f, NavMesh.AllAreas), 1.5f);
            Vector3 approach = tree.transform.position + Vector3.right * 1.75f;
            Assert.That(NavMesh.SamplePosition(approach, out var at, .4f, NavMesh.AllAreas), Is.True);
            game.Worker.WarpTo(at.position);
            var visual = tree.transform.Find("Visual");
            Quaternion rest = visual.localRotation;
            Vector3 scale = visual.localScale;
            Assert.That(tree.InteractionVerbFor(game.Worker), Is.EqualTo("나무 때리기"));
            tree.RequestInteraction(game.Worker);
            yield return Until(() => Quaternion.Angle(visual.localRotation, rest) > .2f, 3f);
            Assert.That(visual.localScale, Is.EqualTo(scale));
            yield return Until(() => Quaternion.Angle(visual.localRotation, rest) < .01f, .5f);

            Assert.That(game.State.Inventory.TryAdd(ItemIds.Axe, 1), Is.True);
            Assert.That(game.State.TryEquip(game.State.Inventory.Entries.Single(item => item.ItemId == ItemIds.Axe).Id), Is.True);
            game.State.LearnSkill(SkillId.Woodcutting, 30);
            tree.RequestInteraction(game.Worker);
            yield return Until(() => game.Worker.ActiveCrop == tree, 3f);
            Assert.That(Quaternion.Angle(visual.localRotation, rest), Is.LessThan(.01f));
            Assert.That(game.Worker.DisplayedWorkSteps.Value.Next, Is.EqualTo(.35f).Within(.001f));
            float swingTime = tree.Definition.WorkAnimation.Loop.length;
            yield return Until(() => Quaternion.Angle(visual.localRotation, rest) > .2f, swingTime + 1f);
            Assert.That(game.Worker.ActiveCrop, Is.SameAs(tree));
            Assert.That(game.Worker.DisplayedWorkSteps.Value.Confirmed, Is.EqualTo(.35f).Within(.001f));
            Assert.That(tree.Felled, Is.False);
            Assert.That(visual.localScale, Is.EqualTo(scale));
            yield return Until(() => tree.Felled, swingTime * 3f + 1f);
            Assert.That(blocker.gameObject.activeInHierarchy, Is.False);
            yield return Until(() => NavMesh.SamplePosition(foot, out _, .18f, NavMesh.AllAreas), 1.5f);
            var parts = WorldInteractable.Active.OfType<TimberPiece>().Where(piece => piece.TargetId == tree.PersistentId).ToArray();
            Assert.That(parts.Select(piece => piece.Part), Is.EquivalentTo(new[] { TimberPart.Stump, TimberPart.Trunk }));
            var trunk = parts.Single(piece => piece.Part == TimberPart.Trunk);
            Assert.That(trunk.CanInteract, Is.False);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<TimberFall>(), Is.Not.Null);
            yield return Until(() => trunk.CanInteract, 13f);
            Assert.That(Vector3.Angle(Vector3.up, trunk.transform.up), Is.GreaterThan(20f));
            Assert.That(trunk.GetComponentInChildren<MeshFilter>().sharedMesh.subMeshCount, Is.GreaterThan(2));
            AssertNoWind(trunk);

            var stump = parts.Single(piece => piece.Part == TimberPart.Stump);
            var seat = stump.Seat;
            Vector3 besideStump = seat.Position + Vector3.right * .85f;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(besideStump.x, besideStump.z), out var seatApproach, .35f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(seatApproach.position), Is.True);
            Assert.That(game.Worker.ToggleSit(seat), Is.True);
            yield return new WaitForSeconds(1f);
            Assert.That(game.Worker.transform.position.y, Is.LessThan(seat.Position.y - .35f));
            Assert.That(game.Worker.OccupiesSeat(seat.Position), Is.True);
            Vector3 seatFoothold = game.Worker.transform.position;
            game.Worker.ToggleSit();
            yield return Until(() => game.Worker.OccupiedSeat == null, 2f);
            Assert.That(Vector3.Distance(game.Worker.transform.position, seatFoothold), Is.LessThan(.02f));

            Vector3 besideTrunk = trunk.transform.position + trunk.transform.right * .85f;
            Assert.That(NavMesh.SamplePosition(besideTrunk, out var splitAt, .8f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(splitAt.position), Is.True);
            game.UseSkill(SkillId.Woodcutting);
            yield return Until(() => game.Worker.CurrentTimberSwing == TimberSwing.Split, 2f);
            float splitTime = game.Settings.Timber.Split.length;
            yield return Until(() => game.Worker.DisplayedWorkSteps is { Confirmed: > 0f }, splitTime + 1f);
            Assert.That(trunk.CanInteract, Is.True);
            Assert.That(game.Worker.DisplayedWorkSteps.Value.Confirmed, Is.EqualTo(.4f).Within(.001f));
            yield return Until(() => trunk == null, splitTime * 3f + 1f);
            Assert.That(GameObject.Find("TimberAxeImpact(Clone)"), Is.Not.Null);
            var splitPieces = WorldInteractable.Active.OfType<TimberPiece>().Where(piece => piece.TargetId == tree.PersistentId).ToArray();
            Assert.That(splitPieces.Select(piece => piece.Part), Is.EquivalentTo(new[] { TimberPart.Stump, TimberPart.Lower, TimberPart.Upper }));
            foreach (var piece in splitPieces)
            {
                Assert.That(piece.GetComponentInChildren<MeshFilter>().sharedMesh.subMeshCount, Is.EqualTo(2));
                AssertNoWind(piece);
            }

            static void AssertNoWind(TimberPiece piece)
            {
                var properties = new MaterialPropertyBlock();
                piece.GetComponentInChildren<MeshRenderer>().GetPropertyBlock(properties);
                Assert.That(properties.HasFloat("_Big_Wind_Amount"), Is.True);
                Assert.That(properties.GetFloat("_Big_Wind_Amount"), Is.Zero);
                Assert.That(properties.HasFloat("_Small_Wind_Amount"), Is.True);
                Assert.That(properties.GetFloat("_Small_Wind_Amount"), Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator TreeFallAcceleratesThenReleasesTheTrunkOntoFlatGround()
        {
            var source = game.Crops.OfType<LoggingTree>().First();
            var layout = source.Site();
            var bark = source.CutGeometry(game.Settings.Timber.CutSurface).Slice(layout.Section(TimberPart.Trunk), barkOnly: true);
            var platform = new GameObject("Tree fall test platform");
            platform.transform.position = new Vector3(-120f, 12f, -120f);
            var ground = platform.AddComponent<BoxCollider>();
            ground.size = new Vector3(24f, .5f, 24f);
            ground.center = Vector3.down * .25f;
            try
            {
                var site = new TimberSite("test tree", TimberLocation.Planar(platform.transform.position), ItemIds.Axe,
                    1, layout.Reach, 1f, platform.transform.position.y, layout.CutHeight, layout.TopHeight, layout.BarkRadius,
                    layout.Products);
                TimberPose resting = default;
                int landings = 0;
                Vector3 contact = default;
                var fall = TimberFall.Begin(site, 0f, bark, platform.transform, pose => resting = pose, (impact, point) =>
                {
                    Assert.That(impact.IsValid, Is.True);
                    Assert.That(resting.IsValid, Is.False);
                    landings++;
                    contact = point;
                });
                var body = fall.GetComponent<Rigidbody>();
                yield return Until(() => Tilt() >= 8f, 2f);
                float earlySpeed = body.angularVelocity.magnitude;
                yield return Until(() => Tilt() >= 55f, 3f);
                Assert.That(body.angularVelocity.magnitude, Is.GreaterThan(earlySpeed * 1.5f));
                yield return Until(() => fall.GetComponent<HingeJoint>() == null, 2f);
                TimberPoint releasedAt = TimberLocation.Planar(body.position);
                yield return Until(() => resting.IsValid, 10f);

                Assert.That(Mathf.Abs((TimberLocation.Rotation(resting) * Vector3.up).y), Is.LessThan(.15f));
                Assert.That(resting.height - platform.transform.position.y, Is.InRange(0f, .35f));
                Assert.That(resting.point.DistanceSquared(releasedAt), Is.GreaterThan(.0025f));
                Assert.That(landings, Is.EqualTo(1));
                Assert.That(contact.y, Is.EqualTo(platform.transform.position.y).Within(.08f));

                Vector3 direction = Vector3.ProjectOnPlane(TimberLocation.Rotation(resting) * Vector3.up, Vector3.up).normalized;
                var lower = site.Section(TimberPart.Lower);
                var upper = site.Section(TimberPart.Upper);
                var center = site.Section(TimberPart.Trunk).Center;
                var baseOfLower = resting.ShiftAlongTrunk(lower.Center - center);
                var baseOfUpper = resting.ShiftAlongTrunk(upper.Center - center);
                TimberPose lowerEnd = default, upperEnd = default;
                Push(lower, baseOfLower, -1, pose => lowerEnd = pose);
                Push(upper, baseOfUpper, 1, pose => upperEnd = pose);
                yield return Until(() => lowerEnd.IsValid && upperEnd.IsValid, 4f);
                Vector3 initialGap = TimberLocation.Position(baseOfUpper) - TimberLocation.Position(baseOfLower);
                Vector3 finalGap = TimberLocation.Position(upperEnd) - TimberLocation.Position(lowerEnd);
                Assert.That(Vector3.Dot(finalGap - initialGap, direction), Is.GreaterThan(.06f));

                float Tilt() => Vector3.Angle(Vector3.up, body.rotation * Vector3.up);
                void Push(TimberSection section, TimberPose start, int side, Action<TimberPose> settled)
                {
                    var slice = site.TrimCutFaces(section);
                    var collider = source.CutGeometry(game.Settings.Timber.CutSurface).Slice(slice, barkOnly: true);
                    TimberFall.Separate(site, section, start, 0f, collider, slice.Center - section.Center, side, platform.transform, settled);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(platform);
            }
        }

        [UnityTest]
        public IEnumerator CrownProductsUseTheSameCollisionAsTheLandingEffectAgainstAnotherTree()
        {
            var source = game.Crops.OfType<LoggingTree>().First();
            var layout = source.Site();
            var platform = new GameObject("Timber collision test platform");
            platform.transform.position = new Vector3(-120f, 12f, -120f);
            var ground = platform.AddComponent<BoxCollider>();
            ground.size = new Vector3(24f, .5f, 24f);
            ground.center = Vector3.down * .25f;
            var standingTree = new GameObject("Standing tree collision");
            standingTree.transform.SetParent(platform.transform, false);
            standingTree.transform.localPosition = new Vector3(.3f, 0f, 3.1f);
            var blocker = standingTree.AddComponent<BoxCollider>();
            blocker.size = source.StandingCollision.size;
            blocker.center = source.StandingCollision.center;

            try
            {
                var site = new TimberSite("test tree", TimberLocation.Planar(platform.transform.position), ItemIds.Axe,
                    1, layout.Reach, 1f, platform.transform.position.y, layout.CutHeight, layout.TopHeight, layout.BarkRadius, layout.Products);
                var work = new TimberWork(new[] { site }, () => 0f);
                var lumberjack = new VillageState();
                lumberjack.Inventory.TryAdd(ItemIds.Axe, 1);
                lumberjack.TryEquip(lumberjack.Inventory.Entries.Single().Id);
                var order = new TimberOrder { action = TimberAction.Fell, target = site.Id };
                for (int hit = 1; hit <= 20; hit++)
                    Assert.That(work.Perform(order, lumberjack, "test", site.Point.Offset(180f, 1f), 0f, 1,
                        _ => false, _ => true, _ => throw new InvalidOperationException("Yield before settling"), out _),
                        Is.EqualTo(hit < 20 ? TimberResult.InProgress : TimberResult.Succeeded));

                var bark = source.CutGeometry(game.Settings.Timber.CutSurface).Slice(site.Section(TimberPart.Trunk), barkOnly: true);
                TimberPose impact = default;
                List<TimberYield> products = null;
                var fall = TimberFall.Begin(site, 0f, bark, platform.transform, pose => work.SettlePiece(site.Id, TimberPart.Trunk, pose), (pose, _) =>
                {
                    impact = pose;
                    Assert.That(work.RecordTrunkLanding(site.Id, pose), Is.True);
                    work.ReleaseCrownProducts((_, _, items) => { products = items; return true; });
                });
                yield return Until(() => Vector3.Angle(Vector3.up, fall.transform.up) >= 8f, 2f);
                Assert.That(products, Is.Null);
                yield return Until(() => products != null, 13f);
                var crown = impact.ShiftAlongTrunk(site.Section(TimberPart.Trunk).Length * .25f);
                Assert.That(products.Single().point.DistanceSquared(crown.point), Is.LessThan(.0001f));
                Assert.That(products[0].crownHeight, Is.EqualTo(crown.height).Within(.001f));
                Assert.That(Vector3.Distance(TimberLocation.Position(crown), platform.transform.position + Vector3.forward * 4.7f), Is.GreaterThan(1f));
            }
            finally
            {
                UnityEngine.Object.Destroy(platform);
            }
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
        public IEnumerator MiraOffersTheFirstJobRewardOnlyAfterDeliveryAndBeforeClaiming()
        {
            const string rewardTopic = "첫 일자리 보상은요?";
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var state = game.State;
            var quests = game.Settings.GetQuests(farmer.Definition);
            var offer = quests.Introduction.DialogueOffers.Single(entry => entry.Topic.keyword == rewardTopic);
            Assert.That(offer.Recipient, Is.SameAs(farmer.Definition));
            Assert.That(farmer.Definition.Dialogue.Topics.Select(topic => topic.keyword), Does.Not.Contain(rewardTopic));
            var actions = new List<WorldContextAction>();
            bool ContextOffersReward()
            {
                actions.Clear();
                farmer.AddContextActions(game, actions);
                return actions.Any(action => action.Label == rewardTopic);
            }

            Assert.That(ContextOffersReward(), Is.False);
            game.OpenConversation(farmer);
            yield return null;
            var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
            bool DialogueOffersReward() => dialogue.GetComponentsInChildren<Button>()
                .Any(button => button.GetComponentInChildren<TMP_Text>()?.text == rewardTopic);
            Assert.That(DialogueOffersReward(), Is.False);
            Assert.That(game.Perform(state.TryAcceptJob), Is.True);
            Assert.That(DialogueOffersReward(), Is.False);
            Assert.That(ContextOffersReward(), Is.False);

            Assert.That(state.Inventory.TryAdd(state.Rules.JobItemId, state.JobRequirement), Is.True);
            Assert.That(game.Perform(state.TryDeliverJob), Is.True);
            Assert.That(DialogueOffersReward(), Is.True);
            Assert.That(ContextOffersReward(), Is.True);
            Assert.That(game.FindDialogueTopic(farmer, DialogueAction.IntroQuest), Is.SameAs(offer.Topic));
            FindButton(dialogue, rewardTopic).onClick.Invoke();
            FindButton(dialogue, "보상을 받는다").onClick.Invoke();
            Assert.That(state.IntroClaimed, Is.True);

            game.OpenConversation(farmer, DialogueAction.IntroQuest);
            Assert.That(DialogueOffersReward(), Is.False);
            Assert.That(ContextOffersReward(), Is.False);
            var body = dialogue.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Dialogue");
            Assert.That(body.text, Is.EqualTo(farmer.Definition.Dialogue.TextFor(DialogueAction.Greeting)));
        }

        [UnityTest]
        public IEnumerator DialogueTopicsWithTheSameActionKeepTheirOwnAuthoredResponses()
        {
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var quests = game.Settings.GetQuests(farmer.Definition);
            var first = quests.Introduction.DialogueOffers.Single().Topic;
            var second = quests.Expansion.DialogueOffers.Single().Topic;
            var firstAction = first.action;
            var secondAction = second.action;
            var state = game.State;
            Assert.That(state.TryAcceptJob(), Is.True);
            Assert.That(state.Inventory.TryAdd(state.Rules.JobItemId, state.JobRequirement), Is.True);
            Assert.That(state.TryDeliverJob(), Is.True);

            try
            {
                first.action = second.action = DialogueAction.PlainText;
                game.OpenConversation(farmer);
                yield return null;
                var dialogue = UnityEngine.Object.FindFirstObjectByType<ConversationWindowView>();
                var body = dialogue.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Dialogue");
                FindButton(dialogue, second.keyword).onClick.Invoke();
                Assert.That(body.text, Is.EqualTo(second.text));
                game.OpenConversation(farmer);
                FindButton(dialogue, first.keyword).onClick.Invoke();
                Assert.That(body.text, Is.EqualTo(first.text));
            }
            finally
            {
                first.action = firstAction;
                second.action = secondAction;
            }
        }

        [UnityTest]
        public IEnumerator ExhaustingStaminaStopsTheHarvestAndAllowsSitting()
        {
            var crop = game.Crops.First();
            Vector3 point = crop.transform.position + Vector3.right * .9f;
            Assert.That(NavMesh.SamplePosition(point, out var stand, .5f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(stand.position), Is.True);
            int day = game.State.Day;
            int slots = crop.Remaining;
            int drops = game.GroundItemCount;
            Assert.That(game.State.SpendWorkStamina(game.State.Stamina - 1d), Is.True);

            game.Worker.CommandHarvest(crop);
            yield return Until(() => game.Worker.ActiveCrop == crop, 2f);
            yield return Until(() => game.Worker.Action == WorkerAction.Idle, 3f);
            Assert.That(game.State.Stamina, Is.Zero);
            Assert.That(game.State.Day, Is.EqualTo(day));
            Assert.That(game.IsWorldTransitioning, Is.False);
            Assert.That(game.Worker.GetComponent<WorkerAnimation>().Gesture, Is.EqualTo(WorkerGesture.GatherFailure));
            Assert.That(crop.Remaining, Is.EqualTo(slots));
            Assert.That(game.GroundItemCount, Is.EqualTo(drops));
            Assert.That(game.Worker.ToggleSit(), Is.True);
            yield return Until(() => game.State.Stamina > 0d, 2f);
            Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Sitting));
        }

        [UnityTest]
        public IEnumerator FailedVillageHarvestKeepsTheCropAvailable()
        {
            var crop = game.Crops[0];
            var definition = crop.Definition;
            string original = JsonUtility.ToJson(definition);
            try
            {
                int charges = crop.Remaining;
                int drops = game.GroundItemCount;
                JsonUtility.FromJsonOverwrite("{\"successChance\":0}", definition);
                Assert.That(crop.TryBegin(game.Worker), Is.True);
                Assert.That(crop.Advance(game.Worker, definition.HarvestSeconds), Is.True);
                Assert.That(crop.Remaining, Is.EqualTo(charges));
                Assert.That(game.GroundItemCount, Is.EqualTo(drops));
                Assert.That(game.Worker.GetComponent<WorkerAnimation>().Gesture, Is.EqualTo(WorkerGesture.GatherFailure));

                JsonUtility.FromJsonOverwrite("{\"successChance\":1}", definition);
                Assert.That(crop.TryBegin(game.Worker), Is.True);
                Assert.That(crop.Advance(game.Worker, definition.HarvestSeconds), Is.True);
                Assert.That(crop.Remaining, Is.EqualTo(charges - 1));
                Assert.That(game.GroundItemCount, Is.GreaterThan(drops));
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(original, definition);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PracticeRankHarvestLandsAndCollectsOneItemPerSecond()
        {
            var worker = game.Worker;
            var crop = game.Crops[0];
            Vector3 start = crop.transform.position;
            Assert.That(NavMesh.SamplePosition(start, out var standing, 1.4f, NavMesh.AllAreas), Is.True);
            Assert.That(worker.WarpTo(standing.position), Is.True);
            Assert.That(game.State.LearnSkill(SkillId.HarvestAcquisition), Is.True);
            var definition = crop.Definition;
            string original = JsonUtility.ToJson(definition);
            int drops = game.GroundItemCount;
            try
            {
                JsonUtility.FromJsonOverwrite("{\"successChance\":1,\"itemsPerHarvest\":3," +
                    "\"scatterRadius\":{\"x\":0.1,\"y\":0.15}}", definition);
                Assert.That(crop.TryBegin(worker), Is.True);
                Assert.That(crop.Advance(worker, definition.HarvestSeconds), Is.True);
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.Zero);
                Assert.That(game.GroundItemCount, Is.GreaterThan(drops));
                Assert.That(game.CaptureSave().pendingHarvests, Is.Empty);
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(original, definition);
            }

            yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) == 1, 3f);
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(1));
            Assert.That(game.GroundItemCount, Is.GreaterThan(drops));
            yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) == 3, 4f);
            Assert.That(game.GroundItemCount, Is.EqualTo(drops));
        }

        [UnityTest]
        public IEnumerator MaxRankHarvestAcquisitionDeliversThroughThePlayerHarvestAction()
        {
            var crop = game.Crops[0];
            var definition = crop.Definition;
            string original = JsonUtility.ToJson(definition);
            try
            {
                JsonUtility.FromJsonOverwrite("{\"successChance\":1,\"itemsPerHarvest\":8}", definition);
                int charges = crop.Remaining;
                int drops = game.GroundItemCount;
                int trails = UnityEngine.Object.FindObjectsByType<LootFlightTrail>(FindObjectsSortMode.None).Length;
                int potatoes = game.State.Inventory.Count(ItemIds.Potato);
                Assert.That(game.State.LearnSkill(SkillId.HarvestAcquisition, SkillProgression.MaxRank), Is.True);
                Assert.That(crop.TryBegin(game.Worker), Is.True);
                Assert.That(crop.Advance(game.Worker, definition.HarvestSeconds), Is.True);
                Assert.That(crop.Remaining, Is.EqualTo(charges - 1));
                Assert.That(game.GroundItemCount, Is.EqualTo(drops));
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(potatoes + definition.ItemsPerHarvest));
                Assert.That(UnityEngine.Object.FindObjectsByType<LootFlightTrail>(FindObjectsSortMode.None).Length, Is.EqualTo(trails + 4));
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(original, definition);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HarvestPickUpDeliverClaimQuestAndRebirth()
        {
            Assert.That(game.Perform(game.State.TryAcceptJob), Is.True);
            for (int i = 0; i < game.Crops.Count && game.State.Inventory.Count(ItemIds.Potato) < game.State.JobRequirement; i++)
            {
                var crop = game.Crops[i];
                int charges = crop.Remaining;
                game.Worker.CommandHarvest(crop);
                yield return Until(() => game.Worker.ActiveCrop == crop, 12f);
                yield return Until(() => game.Worker.ActiveCrop == null, crop.Definition.HarvestSeconds + 1f);
                if (crop.Remaining == charges)
                    continue;
                var item = UnityEngine.Object.FindFirstObjectByType<GroundPickup>();
                Assert.That(item, Is.Not.Null);
                Assert.That(game.Worker.GetComponent<WorkerAnimation>().Gesture, Is.EqualTo(WorkerGesture.GatherSuccess));
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
        public IEnumerator MiraWorksNearTheFarmAndRestsOnAFreeSeatAfterClosing()
        {
            game.State.AdvanceTime((601f - game.State.Minute) / game.State.Rules.MinutesPerSecond);
            yield return null;
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var character = farmer.GetComponent<HarvestWorker>();
            Assert.That(character.CharacterState.RankOf(SkillId.HarvestAcquisition), Is.EqualTo(SkillProgression.MaxRank));
            Assert.That(character.CharacterState.IsSkillLearned(SkillId.LuckyBonus), Is.False);
            Assert.That(character.CharacterState.IsSkillLearned(SkillId.SoulGrowth), Is.False);
            long residentExperience = character.CharacterState.Experience;
            int charges = game.RemainingHarvests;
            int playerHarvests = game.State.LifetimeHarvests;
            int playerEnergy = game.State.Energy;
            int groundItems = game.GroundItemCount;
            farmer.Routine.enabled = true;
            yield return Until(() => farmer.CurrentActivity == NpcActivity.Harvesting, 3f);
            Assert.That(game.Crops.Any(crop => Vector3.Distance(crop.transform.position, farmer.transform.position) < 2f), Is.True);
            Assert.That(game.DialogueTopicsFor(farmer).Any(entry => entry.Topic.keyword == "밭일은 어때요?"), Is.True);
            yield return Until(() => farmer.GetComponent<WorkerAnimation>().CurrentAction == (int)WorkerMotion.Work, 3f);
            yield return Until(() => character.CharacterState.Inventory.Count(ItemIds.Potato) > 0, 12f);
            Assert.That(game.RemainingHarvests, Is.LessThan(charges));
            Assert.That(character.CharacterState.LifetimeHarvests, Is.GreaterThan(0));
            Assert.That(character.CharacterState.Experience, Is.EqualTo(residentExperience));
            Assert.That(game.State.LifetimeHarvests, Is.EqualTo(playerHarvests));
            Assert.That(game.State.Energy, Is.EqualTo(playerEnergy));
            Assert.That(game.GroundItemCount, Is.EqualTo(groundItems));
            Vector3 restingPosition = character.StandingPosition;
            yield return new WaitForSeconds(NpcRoutine.MinimumHarvestPause * .75f);
            Assert.That(character.Action, Is.EqualTo(WorkerAction.Idle));
            Assert.That(Vector3.Distance(character.StandingPosition, restingPosition), Is.LessThan(.01f));
            yield return Until(() => character.ActiveCrop != null, 9f);

            var state = game.State;
            state.AdvanceTime((1201f - state.Minute) / state.Rules.MinutesPerSecond);
            yield return Until(() => farmer.CurrentActivity == NpcActivity.Resting && farmer.Routine.IsSeated, 28f);
            Assert.That(game.SeatInUse(character.OccupiedSeat.Position), Is.True);
            Assert.That(game.DialogueTopicsFor(farmer).Any(entry => entry.Topic.keyword == "밭일은 어때요?"), Is.False);
            Assert.That(game.DialogueTopicsFor(farmer).Any(entry => entry.Topic.keyword == "오늘 일은 끝났나요?"), Is.True);
        }

        [UnityTest]
        public IEnumerator RestingNpcWaitsForVacantSeatsAndFollowsAvailableFacilities()
        {
            game.State.AdvanceTime((601f - game.State.Minute) / game.State.Rules.MinutesPerSecond);
            yield return null;
            var farmer = game.FindInteraction(VillageRole.Farmer);
            farmer.Routine.enabled = true;
            yield return Until(() => farmer.CurrentActivity == NpcActivity.Harvesting, 3f);
            var restType = farmer.Definition.DailySchedule.Single(entry => entry.Activity == NpcActivity.Resting).Facility;
            var benches = UnityEngine.Object.FindObjectsByType<VillageFacility>(FindObjectsSortMode.None)
                .Where(facility => facility.FacilityType == restType)
                .OrderByDescending(facility => (facility.transform.position - farmer.transform.position).sqrMagnitude)
                .ToArray();
            Assert.That(benches.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var bench in benches.Skip(1))
                bench.enabled = false;

            var seats = benches[0].GetComponentsInChildren<SeatSlot>();
            var agent = farmer.GetComponent<NavMeshAgent>();
            var path = new NavMeshPath();
            var firstSeat = seats.FirstOrDefault(seat =>
            {
                var candidateApproach = seat.ApproachPosition;
                return NavMesh.SamplePosition(game.SurfacePosition(candidateApproach.x, candidateApproach.z), out var hit, .7f, NavMesh.AllAreas) &&
                    seat.Covers(hit.position) && agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete;
            });
            Assert.That(firstSeat, Is.Not.Null);
            foreach (var seat in seats.Where(seat => seat != firstSeat))
                seat.enabled = false;
            var state = game.State;
            state.AdvanceTime((1201f - state.Minute) / state.Rules.MinutesPerSecond);
            yield return Until(() => farmer.CurrentActivity == NpcActivity.Walking && farmer.Routine.ReservesSeat(firstSeat.Position), 3f);

            Vector3 front = firstSeat.ApproachPosition;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(front.x, front.z), out var standing, .7f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(standing.position), Is.True);
            game.Worker.ToggleSit();
            Assert.That(game.Worker.OccupiesSeat(firstSeat.Position), Is.True);
            yield return Until(() => farmer.CurrentActivity == NpcActivity.Idle, 3f);

            benches[1].enabled = true;
            yield return Until(() => farmer.Routine.IsSeated, 32f);
            Assert.That(benches[1].GetComponentsInChildren<SeatSlot>()
                .Any(seat => farmer.Routine.ReservesSeat(seat.Position)), Is.True);
            Assert.That(game.Worker.OccupiesSeat(firstSeat.Position), Is.True);

            benches[1].gameObject.SetActive(false);
            yield return Until(() => farmer.CurrentActivity == NpcActivity.Idle, 3f);
            game.Worker.ToggleSit();
            yield return Until(() => farmer.Routine.IsSeated, 32f);
            Assert.That(farmer.Routine.ReservesSeat(firstSeat.Position), Is.True);
        }

        [UnityTest]
        public IEnumerator CharacterEquipmentAndWorkSurviveReloadWhileThePlayerIsInsideTheInn()
        {
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var character = farmer.GetComponent<HarvestWorker>();
            const string coat = "field_coat";
            var slot = ItemCatalog.Get(coat).EquipmentSlot;
            Assert.That(character.CharacterState.Inventory.TryAdd(coat, 1), Is.True);
            var entry = character.CharacterState.Inventory.Entries.Single(item => item.ItemId == coat);
            Assert.That(character.CharacterState.TryEquip(entry.Id), Is.True);
            Assert.That(game.State.Equipment.Values, Does.Not.Contain(coat));

            var inn = WorldInteractable.Active.OfType<VillageBuildingDoor>().Single(door => door.BuildingId == "inn");
            Vector3 approach = inn.Approach.position;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(approach.x, approach.z), out var standing, .8f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(standing.position), Is.True);
            inn.RequestInteraction(game.Worker);
            yield return Until(() => game.CurrentInterior == inn.Interior && !game.IsWorldTransitioning, 3f);

            game.State.AdvanceTime((601f - game.State.Minute) / game.State.Rules.MinutesPerSecond);
            yield return null;
            farmer.Routine.enabled = true;
            yield return Until(() => character.CharacterState.Inventory.Count(ItemIds.Potato) > 0, 12f);
            yield return Until(() => character.ActiveCrop != null && character.HarvestProgress >= .4f, 10f);
            Vector3 workPosition = character.StandingPosition;
            string cropId = character.ActiveCrop.PersistentId;
            float savedProgress = character.HarvestProgress;
            int harvested = character.CharacterState.LifetimeHarvests;
            Assert.That(game.SaveGame(false), Is.True);
            farmer.Routine.enabled = false;

            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            Assert.That(game.CurrentInterior?.Id, Is.EqualTo("inn"));
            farmer = game.FindInteraction(VillageRole.Farmer);
            character = farmer.GetComponent<HarvestWorker>();
            Assert.That(character.CharacterState.RankOf(SkillId.HarvestAcquisition), Is.EqualTo(SkillProgression.MaxRank));
            Assert.That(Vector3.Distance(character.StandingPosition, workPosition), Is.LessThan(.75f));
            Assert.That(character.CharacterState.Equipment[slot], Is.EqualTo(coat));
            Assert.That(character.CharacterState.Inventory.Count(ItemIds.Potato), Is.GreaterThan(0));
            yield return Until(() => character.ActiveCrop?.PersistentId == cropId && character.HarvestProgress >= savedProgress, 3f);
            yield return Until(() => character.CharacterState.LifetimeHarvests > harvested, 12f);
            Assert.That(game.CurrentInterior?.Id, Is.EqualTo("inn"));
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
        public IEnumerator ACarriedStackWalksIntoReachBeforeBeingPlaced()
        {
            var worker = game.Worker;
            Vector3 start = worker.StandingPosition;
            var destination = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }
                .Select(direction => start + direction * 4f)
                .Select(point => game.SurfacePosition(point.x, point.z))
                .First(point => worker.TryGetReachablePoint(point, 0f, out _));
            Assert.That(worker.InReach(destination, HarvestWorker.DefaultPickupDistance), Is.False);
            Assert.That(game.State.Inventory.TryAdd(ItemIds.Potato, 4), Is.True);
            var stack = game.State.Inventory.Entries.Single(entry => entry.ItemId == ItemIds.Potato);
            game.Hud.OpenWindow("inventory");
            game.Hud.BeginItemDrag(stack.Id, WorkerbornInput.Point);
            Assert.That(game.Hud.IsHoldingItem, Is.True);

            int drops = game.GroundItemCount;
            Assert.That(worker.CommandDrop(stack.Id, destination), Is.True);
            Assert.That(worker.HasPendingDrop, Is.True);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(4));
            Assert.That(game.GroundItemCount, Is.EqualTo(drops));
            game.Hud.CancelItemDrag();

            yield return Until(() => !worker.HasPendingDrop, 7f);
            Assert.That(worker.InReach(destination, HarvestWorker.DefaultPickupDistance), Is.True);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.Zero);
            Assert.That(game.GroundItemCount, Is.EqualTo(drops + 1));
            var drop = game.CaptureSave().drops.Single(item => item.ownerId == worker.PlayerId && item.itemId == ItemIds.Potato);
            Assert.That(drop.x, Is.EqualTo(destination.x).Within(.01f));
            Assert.That(drop.z, Is.EqualTo(destination.z).Within(.01f));
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
                Assert.That(game.Targeting.CursorKind, Is.EqualTo(InteractionCursor.PickUp));
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
        public IEnumerator RightDragKeepsOrbitingDuringAnInteriorTransition()
        {
            var inn = WorldInteractable.Active.OfType<VillageBuildingDoor>().Single(door => door.BuildingId == "inn");
            Vector3 point = inn.Approach.position;
            Assert.That(NavMesh.SamplePosition(game.SurfacePosition(point.x, point.z), out var at, .8f, NavMesh.AllAreas), Is.True);
            Assert.That(game.Worker.WarpTo(at.position), Is.True);

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var camera = game.WorldCamera;
                Vector2 center = camera.pixelRect.center;
                var delta = new Vector2(40f, 0f);
                Quaternion original = camera.transform.rotation;
                Assert.That(game.UseBuildingDoor(inn), Is.True);
                Assert.That(game.ControlsBlocked, Is.True);

                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, buttons = 2 });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center + delta, delta = delta, buttons = 2 });
                yield return null;
                Assert.That(game.IsWorldTransitioning, Is.True);
                Assert.That(game.CameraRig.IsOrbiting, Is.True);
                Assert.That(game.Targeting.CursorKind, Is.EqualTo(InteractionCursor.Orbit));
                Assert.That(Quaternion.Angle(original, camera.transform.rotation), Is.GreaterThan(4f));

                yield return Until(() => game.CurrentInterior == inn.Interior, 3f);
                Assert.That(game.IsWorldTransitioning, Is.True);
                Quaternion inRoom = camera.transform.rotation;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, delta = -delta, buttons = 2 });
                yield return null;
                Assert.That(Quaternion.Angle(inRoom, camera.transform.rotation), Is.GreaterThan(4f));

                yield return Until(() => !game.IsWorldTransitioning, 3f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
                yield return null;
                Assert.That(game.CameraRig.RightClickThisFrame, Is.False);
                Assert.That(game.Targeting.CursorKind, Is.Not.EqualTo(InteractionCursor.Orbit));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
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
            var farmType = game.FindInteraction(VillageRole.Farmer).Definition.DailySchedule
                .Single(entry => entry.Activity == NpcActivity.Harvesting).Facility;
            var builtFarm = large.GetComponentInChildren<VillageFacility>();
            Assert.That(builtFarm.FacilityType, Is.EqualTo(farmType));
            Assert.That(builtFarm.GetComponentsInChildren<HarvestableCrop>().Length, Is.EqualTo(64));
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
            Assert.That(shop.transform.IsChildOf(dialogue.ShopStock), Is.True);
            Assert.That(shop.GetComponentInChildren<ShopSellTarget>().name, Is.EqualTo("Stock viewport"));
            yield return Until(() => dialogue.Rect.rect.height >= 470f, 1f);
            Assert.That(shop.Rect.position.y, Is.GreaterThan(line.rectTransform.position.y));

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
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(7));
            Assert.That(game.State.CursorItem.Quantity, Is.EqualTo(2));
            Assert.That(game.Hud.IsHoldingItem, Is.True);
            Assert.That(game.Hud.HasQuantityPrompt, Is.False);
            FindButton(dialogue, "거래 종료").onClick.Invoke();
            Assert.That(game.Hud.IsShopOpen, Is.False);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(9));
            Assert.That(game.Hud.HasModal, Is.False);
            Assert.That(dialogue.isActiveAndEnabled, Is.True);
            Assert.That(normalHud.activeSelf, Is.False);
            Assert.That(dialogue.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            yield return Until(() => normalHud.activeSelf, 1f);
            Assert.That(dialogue.isActiveAndEnabled, Is.False);
            Assert.That(inventory.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator PurchaseArcPassesAboveAndAwayFromBothEndpoints()
        {
            var seller = new Vector3(0f, 1f, 0f);
            var buyer = new Vector3(4f, 1f, 0f);
            var center = Vector3.Lerp(seller, buyer, .5f);
            var arc = new LootFlightTrail.PurchaseArc(seller, buyer, new Vector2(.1f, .9f));
            Vector3 waypoint = arc.PositionAt(arc.WaypointProgress);
            Assert.That(waypoint.y, Is.GreaterThan(center.y + .8f));
            Assert.That(Vector3.Distance(waypoint, center), Is.EqualTo(2f).Within(.001f));
            Assert.That(Vector3.Distance(waypoint, seller), Is.GreaterThanOrEqualTo(2f));
            Assert.That(Vector3.Distance(waypoint, buyer), Is.GreaterThanOrEqualTo(2f));
            Assert.That(Vector3.Distance(arc.PositionAt(0f), seller), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(arc.PositionAt(1f), buyer), Is.LessThan(.001f));
            var overlap = new LootFlightTrail.PurchaseArc(seller, seller, Vector2.one * .5f);
            Assert.That(overlap.PositionAt(overlap.WaypointProgress).y, Is.GreaterThan(seller.y + .45f));
            yield return null;
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
                var seller = game.FindInteraction(VillageRole.Merchant);
                game.Hud.OpenShop(seller);
                yield return null;
                Canvas.ForceUpdateCanvases();

                var inventory = UnityEngine.Object.FindFirstObjectByType<InventoryWindowView>();
                var shop = UnityEngine.Object.FindFirstObjectByType<ShopWindowView>();
                bool TrailStartsAt(Transform sender)
                {
                    Vector3 origin = sender.position + Vector3.up * LootFlightTrail.ReceiverHeight;
                    return UnityEngine.Object.FindObjectsByType<LootFlightTrail>(FindObjectsSortMode.None)
                        .Any(trail => (trail.transform.position - origin).sqrMagnitude < .0001f && trail.GetComponent<TrailRenderer>().emitting);
                }
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
                Assert.That(TrailStartsAt(game.Worker.transform), Is.True);
                Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(2));
                Assert.That(game.State.Inventory.Count(ItemIds.Bread), Is.EqualTo(2));
                Assert.That(game.Hud.HasQuantityPrompt, Is.False);
                Assert.That(inventoryInput.blocksRaycasts, Is.True);
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                yield return null;
                game.Hud.ClickInventoryItem(potato.Id, saleClick);
                Assert.That(TrailStartsAt(game.Worker.transform), Is.True);
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
                Assert.That(TrailStartsAt(seller.transform), Is.True);
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
                Assert.That(TrailStartsAt(seller.transform), Is.True);
                Assert.That(game.Hud.IsHoldingItem, Is.True);
                Assert.That(game.State.Inventory.Count(ItemIds.Axe), Is.EqualTo(1));
                Assert.That(game.State.CursorItem.ItemId, Is.EqualTo(ItemIds.Axe));
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
                Assert.That(dialogue.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "거래하기" || text.text == "대화 끝내기"), Is.False);
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
                var leave = FindButton(dialogue, "대화 끝내기");
                Assert.That(leave.transform.GetSiblingIndex(), Is.Zero);
                FindButton(dialogue, "일을 맡는다").onClick.Invoke();
                Assert.That(game.State.Job, Is.EqualTo(JobStatus.Active));
                Assert.That(body.text, Does.Contain("계약서를 챙겨 가"));
                Assert.That(card.gameObject.activeSelf, Is.False);
                Assert.That(leave.transform.parent.GetComponentsInChildren<Button>(), Is.EqualTo(new[] { leave }));
                leave.onClick.Invoke();
                Assert.That(game.Hud.HasModal, Is.False);
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
            var farmer = game.FindInteraction(VillageRole.Farmer);
            var workplace = farmer.transform;
            var guide = game.Hud.GetComponentInChildren<GroundTrackingGuide>();
            Vector3 position = workplace.position;
            var routine = farmer.Routine;
            var workerAgent = farmer.GetComponent<NavMeshAgent>();
            routine.enabled = false;
            workerAgent.enabled = false;
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
                Assert.That(guide.Visible, Is.True);
                var ground = GameObject.Find("Ground tracking guide").GetComponent<MeshRenderer>();
                Assert.That(ground.GetComponentInParent<Canvas>(), Is.Null);
                Assert.That(ground.sharedMaterial.color.a, Is.LessThan(1f));
                Vector3 direction = Vector3.ProjectOnPlane(workplace.position - game.Worker.StandingPosition, Vector3.up).normalized;
                Assert.That(Vector3.Distance(guide.Direction, direction), Is.LessThan(.001f));
                Vector3 right = Vector3.ProjectOnPlane(game.WorldCamera.transform.right, Vector3.up).normalized;
                workplace.position = game.Worker.StandingPosition + right;
                yield return null;
                yield return null;
                var nameplate = names.GetComponentsInChildren<WorldNameplateView>().Single(view => view.Subject == farmer);
                Assert.That(nameplate.transform.Find("Tracking left").gameObject.activeInHierarchy, Is.True);
                Assert.That(nameplate.transform.Find("Tracking right").gameObject.activeInHierarchy, Is.True);
                Assert.That(Vector3.Distance(guide.Direction, right), Is.LessThan(.001f));
                Assert.That(fullMarker.gameObject.activeSelf, Is.True);
                fullRow.transform.Find("Job tracking").GetComponent<Button>().onClick.Invoke();
                yield return Until(() => !miniMarker.gameObject.activeSelf && !fullMarker.gameObject.activeSelf, 2f);
                Assert.That(guide.Visible, Is.False);
                Assert.That(nameplate.transform.Find("Tracking left").gameObject.activeSelf, Is.False);
                fullRow.transform.Find("Job tracking").GetComponent<Button>().onClick.Invoke();
                yield return Until(() => guide.Visible, 2f);
            }
            finally
            {
                workplace.position = position;
                workerAgent.enabled = true;
                routine.enabled = true;
            }

            var state = game.State;
            state.AdvanceTime((state.Rules.JobEnd - state.Minute + 10) / state.Rules.MinutesPerSecond);
            yield return Until(() => !guide.Visible && !miniMarker.gameObject.activeSelf && !fullMarker.gameObject.activeSelf, 2f);
            Assert.That(game.Hud.Jobs.IsPinned(game.Hud.Jobs.Jobs.Single(job => job.Id == state.Rules.JobId)), Is.True);
            Assert.That(miniRow.gameObject.activeInHierarchy, Is.False);
            Assert.That(miniSchedule.transform.Find("Empty schedule").GetComponent<TMP_Text>().text, Is.EqualTo("오늘 일정 종료"));
            Assert.That(fullRow.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Job status").text,
                Is.EqualTo("마감"));
        }

        [UnityTest]
        public IEnumerator QuestTrackingFollowsTheAvailableObjectiveAndHidesWhenWorkCloses()
        {
            var state = game.State;
            Assert.That(state.TryAcceptJob(), Is.True);
            game.Hud.OpenQuestDetail(VillageQuestPage.FarmJob);
            var detail = UnityEngine.Object.FindFirstObjectByType<QuestDetailWindowView>();
            FindButton(detail, "추적하기").onClick.Invoke();
            yield return Until(() => game.Hud.TrackedTarget is HarvestableCrop, 2f);
            Assert.That(state.Inventory.TryAdd(state.Rules.JobItemId, state.JobRequirement), Is.True);
            yield return Until(() => game.Hud.TrackedTarget == game.FindInteraction(VillageRole.Farmer), 2f);

            state.AdvanceTime((state.Rules.JobEnd - state.Minute + 10) / state.Rules.MinutesPerSecond);
            yield return Until(() => game.Hud.TrackedTarget == null, 2f);
            FindButton(detail, "추적 해제").onClick.Invoke();
            Assert.That(game.Hud.IsTrackingQuest(VillageQuestPage.FarmJob), Is.False);

            game.Hud.OpenQuestDetail(VillageQuestPage.LegendFounding);
            FindButton(detail, "추적하기").onClick.Invoke();
            yield return null;
            Assert.That(game.Hud.IsTrackingQuest(VillageQuestPage.LegendFounding), Is.True);
            Assert.That(game.Hud.TrackedTarget, Is.Null);
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
            var chatLayer = (RectTransform)chat.transform.parent;
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
                var status = (RectTransform)game.Hud.transform.Find("Normal HUD/Character status");
                float chatBottom = chatLayer.TransformPoint(chatLayer.rect.min).y;
                float chatRight = chatLayer.TransformPoint(chatLayer.rect.max).x;
                Vector3 statusBottomLeft = status.TransformPoint(status.rect.min);
                Assert.That(chatBottom, Is.EqualTo(statusBottomLeft.y).Within(.01f));
                Assert.That(chatRight, Is.LessThan(statusBottomLeft.x));
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
        public IEnumerator RidingTheVillageHorseUsesBothMovementModesAndParksItOnDismount()
        {
            var worker = game.Worker;
            var horse = UnityEngine.Object.FindFirstObjectByType<VillageMount>();
            Assert.That(horse, Is.Not.Null);
            Assert.That(horse.PersistentId, Is.EqualTo("village-chestnut-horse"));
            Assert.That(horse.RiderPose.isHumanMotion, Is.True);
            Assert.That(horse.GetComponentInChildren<Animator>().runtimeAnimatorController, Is.Not.Null);
            var skin = horse.GetComponentsInChildren<SkinnedMeshRenderer>().Single(mesh => mesh.enabled);
            var saddle = horse.GetComponentsInChildren<MeshFilter>().Single().transform;
            Assert.That(saddle.parent.name, Is.EqualTo("body"));
            Assert.That(saddle.Find("Rider root"), Is.Not.Null);

            var agent = worker.GetComponent<NavMeshAgent>();
            float onFoot = agent.speed;
            float standingHeight = worker.Actor.VisualRoot.localPosition.y;
            horse.RequestInteraction(worker);
            yield return Until(() => worker.CurrentMount == horse, 4f);
            Vector2 screen = new Vector2(Screen.width, Screen.height) * .5f;
            game.Hud.OpenWorldContext(horse, screen);
            var menu = UnityEngine.Object.FindFirstObjectByType<WorldContextView>();
            FindButton(menu, "내리기").onClick.Invoke();
            Assert.That(worker.CurrentMount, Is.Null);
            game.Hud.OpenWorldContext(horse, screen);
            FindButton(menu, "타기").onClick.Invoke();
            Assert.That(worker.CurrentMount, Is.SameAs(horse));
            game.Hud.OpenWorldContext(horse, screen);
            FindButton(menu, "내리기").onClick.Invoke();
            Assert.That(worker.CurrentMount, Is.Null);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C));
                yield return Until(() => worker.CurrentMount == horse, 2f);
                Assert.That(horse.Rider, Is.EqualTo(worker.CharacterId));
                Assert.That(worker.Actor.VisualRoot.localPosition.y, Is.GreaterThan(standingHeight));
                Assert.That(agent.speed, Is.GreaterThan(onFoot));
                horse.RequestInteraction(worker);
                Assert.That(worker.CurrentMount, Is.SameAs(horse));

                var oldRay = new Ray(skin.bounds.center + Vector3.up * 4f, Vector3.down);
                Assert.That(horse.CoversPointer(oldRay), Is.True);
                Vector3 destination = NearbyDestination();
                worker.CommandMove(destination);
                yield return Until(() => Vector3.Distance(horse.transform.position, destination) < .3f, 4f);
                Assert.That(horse.CoversPointer(oldRay), Is.False);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                yield return null;
                Assert.That(agent.speed, Is.EqualTo(horse.Speed(walk: true)));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return null;
                Assert.That(agent.speed, Is.EqualTo(horse.Speed(walk: false)));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C));
                yield return Until(() => worker.CurrentMount == null, 2f);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }

            Assert.That(agent.speed, Is.EqualTo(onFoot));
            Assert.That(worker.Actor.VisualRoot.localPosition.y, Is.EqualTo(standingHeight).Within(.01f));
            Assert.That(horse.Rider, Is.Null);
            var saved = game.CaptureSave().mounts.Single();
            Assert.That(saved.rider, Is.Null);
            Assert.That(saved.x, Is.EqualTo(horse.transform.position.x));
            Assert.That(saved.z, Is.EqualTo(horse.transform.position.z));
        }

        [UnityTest]
        public IEnumerator ThrownTimberSpinsAndStopsAtAnObstacleBeforeTheAimPoint()
        {
            Vector3 center = new Vector3(200f, 60f, 200f);
            var floor = new GameObject("Throw test floor");
            floor.transform.position = center - Vector3.up * .5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(24f, 1f, 16f);
            var wall = new GameObject("Throw test wall");
            wall.transform.position = center + new Vector3(2.4f, 2.5f, 0f);
            wall.AddComponent<BoxCollider>().size = new Vector3(.6f, 5f, 12f);
            TimberPose settled = default;
            TimberFall flight = null;
            try
            {
                var release = TimberLocation.Pose(center + Vector3.up * 2.13f, Quaternion.Euler(90f, 0f, 0f));
                Vector3 aim = center + new Vector3(7f, .20f, 0f);
                flight = TimberFall.Throw(release, aim, .20f, 1.2f, null, 0f, 0f, null,
                    pose => settled = pose, null);
                yield return new WaitForFixedUpdate();
                var body = flight.GetComponent<Rigidbody>();
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.angularVelocity.sqrMagnitude, Is.GreaterThan(1f));
                yield return Until(() => settled.IsValid, 8f);
                Assert.That(settled.point.x, Is.LessThan(wall.transform.position.x));
                Assert.That(settled.point.DistanceSquared(TimberLocation.Planar(aim)), Is.GreaterThan(1f));
            }
            finally
            {
                if (flight != null)
                    UnityEngine.Object.Destroy(flight.gameObject);
                UnityEngine.Object.Destroy(floor);
                UnityEngine.Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator WorkRingPreviewsTheNextAxeSwingAndCommitsAtImpact()
        {
            var animation = game.Worker.GetComponent<WorkerAnimation>();
            var work = game.Settings.Timber.Felling;
            animation.BeginWork(work.WorkAnimation, work.HarvestSeconds);
            var first = animation.WorkSteps(0f);
            Assert.That(first.Confirmed, Is.Zero);
            Assert.That(first.Next, Is.GreaterThan(0f));
            float hit = Enumerable.Range(1, 1000).Select(step => step / 1000f)
                .First(progress => animation.WorkSteps(progress).Confirmed > 0f);
            Assert.That(animation.WorkSteps(hit - .0011f).Confirmed, Is.Zero);
            Assert.That(animation.WorkSteps(hit).Confirmed, Is.EqualTo(first.Next).Within(.001f));

            var ring = UnityEngine.Object.Instantiate(game.Hud.Library.SkillBalloon);
            try
            {
                ring.SetWorkSteps(first.Confirmed, first.Next, 1);
                var outline = ring.transform.Find("Next action outline").GetComponent<Image>();
                Assert.That(outline.fillAmount, Is.EqualTo(first.Next).Within(.001f));
                Assert.That(ring.Progress.fillAmount, Is.Zero);
                var done = animation.WorkSteps(hit);
                yield return Until(() =>
                {
                    ring.SetWorkSteps(done.Confirmed, done.Next, 1);
                    return ring.Progress.fillAmount >= done.Confirmed && outline.fillAmount >= done.Next - .001f;
                }, .5f);
                animation.BeginWork(game.Crops.First(crop => crop.Definition.Skill == SkillId.Harvesting).Definition.WorkAnimation, 2.4f);
                var continuous = animation.WorkSteps(.27f);
                ring.SetProgress(continuous.Confirmed);
                Assert.That(ring.Progress.fillAmount, Is.EqualTo(.27f).Within(.001f));
                Assert.That(outline.fillAmount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.Destroy(ring.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator MountedPlayerCanApproachDropsTalkAndAttackWithTheHorse()
        {
            var worker = game.Worker;
            var horse = UnityEngine.Object.FindFirstObjectByType<VillageMount>();
            var saddle = horse.GetComponentsInChildren<MeshFilter>().Single().transform;
            var riderRoot = saddle.Find("Rider root");
            Vector3 onFootOffset = worker.Actor.VisualRoot.localPosition;
            Assert.That(worker.WarpTo(horse.transform.position), Is.True);
            game.RequestMount(horse);
            Assert.That(worker.CurrentMount, Is.SameAs(horse));

            var item = UnityEngine.Object.Instantiate(game.Settings.FindItem(ItemIds.Potato).GroundPrefab);
            try
            {
                Vector3 at = NearbyDestination();
                item.Initialize(game.Crops[0].Definition, at, at, true);
                Assert.That(worker.IsInPickupRange(item), Is.False);
                int quantity = game.State.Inventory.Count(ItemIds.Potato) + item.Quantity;
                item.RequestInteraction(worker);
                yield return Until(() => game.State.Inventory.Count(ItemIds.Potato) == quantity, 4f);
                Assert.That(worker.CurrentMount, Is.SameAs(horse));
            }
            finally
            {
                if (item != null)
                    UnityEngine.Object.Destroy(item.gameObject);
            }

            var farmer = game.FindInteraction(VillageRole.Farmer);
            farmer.RequestInteraction(worker);
            yield return Until(() => game.Hud.IsSpeakingWith(farmer), 6f);
            Assert.That(worker.CurrentMount, Is.SameAs(horse));
            game.Hud.CloseDialogue();
            yield return Until(() => !game.ControlsBlocked, 2f);

            var enemy = UnityEngine.Object.FindObjectsByType<MonsterInteractable>(FindObjectsSortMode.None)
                .Single(target => target.Combat != null);
            var enemyAgent = enemy.GetComponent<NavMeshAgent>();
            enemy.GetComponent<AnimalWander>().SetSuspended(true);
            Assert.That(enemyAgent.Warp(NearbyDestination()), Is.True);
            int health = enemy.Health;
            double stamina = game.State.Stamina;
            enemy.RequestInteraction(worker);
            var horseAnimation = horse.GetComponentInChildren<Animator>();
            int attack = Animator.StringToHash("Base Layer.Attack");
            yield return Until(() => horseAnimation.GetCurrentAnimatorStateInfo(0).fullPathHash == attack, 4f);
            Vector3 expectedPosition = riderRoot.position + worker.transform.TransformVector(onFootOffset);
            Assert.That(Vector3.Distance(worker.Actor.VisualRoot.position, expectedPosition), Is.LessThan(.02f));
            yield return Until(() => enemy.Health < health, 2f);
            Assert.That(enemy.Health, Is.EqualTo(Mathf.Max(0, health - horse.AttackDamage)));
            Assert.That(game.State.Stamina, Is.GreaterThanOrEqualTo(stamina));
            Assert.That(worker.CurrentMount, Is.SameAs(horse));
        }

        [UnityTest]
        public IEnumerator KeyboardMovementWalksOnlyWhileShiftIsHeld()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var agent = game.Worker.GetComponent<NavMeshAgent>();
                Vector3 right = Vector3.ProjectOnPlane(game.WorldCamera.transform.right, Vector3.up).normalized;
                Vector3 forward = Vector3.Cross(right, Vector3.up);
                Quaternion facing = Quaternion.LookRotation(forward);
                game.Worker.transform.rotation = Quaternion.LookRotation(-forward);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return null;
                Assert.That(game.Worker.Action, Is.EqualTo(WorkerAction.Moving));
                Assert.That(Quaternion.Angle(game.Worker.transform.rotation, facing),
                    Is.EqualTo(Mathf.Max(0f, 180f - agent.angularSpeed * Time.deltaTime)).Within(.5f));
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
            Assert.That(drop.DropRecord.ownerId, Is.EqualTo(game.Worker.PlayerId));
            Assert.That(drop.DropRecord.CanCollect("another-player", drop.DropRecord.reservedUntilUtc - 1), Is.False);
            Assert.That(drop.DropRecord.CanCollect("another-player", drop.DropRecord.reservedUntilUtc), Is.True);
            Assert.That(Vector2.Distance(new Vector2(drop.LandingPosition.x, drop.LandingPosition.z),
                new Vector2(destination.x, destination.z)), Is.LessThan(.01f));
        }

        [UnityTest]
        public IEnumerator ExpiredGroundDropsLoadAsOnePileWithTheCombinedName()
        {
            Vector3 position = game.Worker.StandingPosition;
            long expired = GroundDropSave.UtcNow - 1;
            var save = game.CaptureSave();
            save.drops = new[]
            {
                new GroundDropSave { id = "first-potatoes", itemId = ItemIds.Potato, quantity = 50, ownerId = game.Worker.PlayerId,
                    reservedUntilUtc = expired, x = position.x + .5f, z = position.z },
                new GroundDropSave { id = "second-potatoes", itemId = ItemIds.Potato, quantity = 75, ownerId = "another-player",
                    reservedUntilUtc = expired, x = position.x + 1f, z = position.z }
            };
            Assert.That(VillageSaveStore.TrySave(save, out _), Is.True);

            yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<VillagePrototype>();
            var pile = UnityEngine.Object.FindObjectsByType<GroundPickup>(FindObjectsSortMode.None)
                .Single(item => item.ItemId == ItemIds.Potato);
            Assert.That(pile.Quantity, Is.EqualTo(125));
            Assert.That(pile.DisplayName, Is.EqualTo("감자(125)"));
            Assert.That(game.TryCollectPickup(pile), Is.True);
            Assert.That(game.State.Inventory.Count(ItemIds.Potato), Is.EqualTo(125));
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

        private bool NpcIsSaying(string line) => game.Hud.GetComponentsInChildren<NpcConversationBubbleView>(true)
            .Any(bubble => bubble.GetComponentInChildren<TMP_Text>(true).text == line);

        private Vector3 NearbyDestination(float distance = 3.5f, bool direct = false)
        {
            var worker = game.Worker.transform;
            var agent = worker.GetComponent<NavMeshAgent>();
            var path = new NavMeshPath();
            for (int angle = 0; angle < 360; angle += 45)
            {
                var point = worker.position + Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
                if (NavMesh.SamplePosition(point, out var hit, .7f, NavMesh.AllAreas) && agent.CalculatePath(hit.position, path)
                    && path.status == NavMeshPathStatus.PathComplete && (!direct || !agent.Raycast(hit.position, out _)))
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
