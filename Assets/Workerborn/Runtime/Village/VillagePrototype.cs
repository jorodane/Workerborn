using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    // Scene objects are authored and saved in Unity. This component binds them to session state.
    [DefaultExecutionOrder(100)]
    public sealed partial class VillagePrototype : MonoBehaviour
    {
        [SerializeField]
        private VillageSettings settings;
        [SerializeField]
        private Transform mapRoot;
        [SerializeField]
        private VillageNavigation navigation;
        [SerializeField]
        private CharacterActor player;
        [SerializeField]
        private HarvestWorker worker;
        [SerializeField]
        private Camera worldCamera;
        [SerializeField]
        private VillageCameraRig cameraRig;
        [SerializeField]
        private VillageHud hud;
        [SerializeField]
        private VillageMapRegion mapRegion;
        [SerializeField]
        private Transform spawnPoint;
        [SerializeField]
        private Transform restPoint;
        [SerializeField]
        private Transform droppedItems;
        [SerializeField, Tooltip("Shared runtime prefab with a Capsule Collider and an empty Visual pivot.")]
        private GroundPickup groundPickupTemplate;
        [SerializeField, Min(0)]
        private int prewarmGroundPickups = 32;
        [SerializeField]
        private CropDefinition potato;
        [SerializeField]
        private VillageLighting lighting;
        [SerializeField]
        private VillageEffects effects;
        [SerializeField]
        private bool loadSavedGame = true;
        [SerializeField]
        private PointerTargeting targeting;
        [SerializeField]
        private NpcPortrait npcPortrait;
        [SerializeField]
        private RenderTexture npcPortraitTexture;
        [Header("Farm foundation")]
        [SerializeField] private Transform farm;
        public PointerTargeting Targeting => targeting;
        public Terrain GroundTerrain => navigation != null ? navigation.GroundTerrain : null;
        public bool IsGround(Collider hit)
        {
            if (CurrentInterior != null)
                return hit == CurrentInterior.Floor;
            return InRebirthZone ? rebirthZone.IsGround(hit) : navigation != null && navigation.IsGround(hit);
        }
        public RenderTexture NpcPortraitTexture => npcPortraitTexture;

        public void ShowNpcPortrait(CharacterActor actor)
        {
            if (npcPortrait != null)
                npcPortrait.Show(actor);
        }

        private readonly HashSet<string> collectedPickups = new HashSet<string>();
        private readonly List<HarvestableCrop> allCrops = new List<HarvestableCrop>();
        private readonly List<HarvestableCrop> crops = new List<HarvestableCrop>();
        private MonsterInteractable[] animals;
        private readonly List<VillageInteractable> npcs = new List<VillageInteractable>();
        private readonly List<GroundPickup> pickups = new List<GroundPickup>();
        private readonly List<GroundPickup> authoredPickups = new List<GroundPickup>();
        private GroundPickupPool pickupPool;
        private Action<Vector3> playLanding;
        private VillageFarmSite[] farmSites;
        private Vector3 originalFarmPosition;
        private VillageWorldStage[] stages;
        private VillageSave restoredWorld;
        private float nextSave;
        private bool initialized;
        private bool IsAppearanceTrial { get; set; }
        private bool usesSelectedWorld;
        public VillageState State { get; private set; }
        public VillageSettings Settings => settings;
        public CharacterActor PlayerActor => player;
        public VillageHud Hud => hud;
        public HarvestWorker Worker => worker;
        public Camera WorldCamera => worldCamera;
        public VillageCameraRig CameraRig => cameraRig;
        public string WeatherName => lighting != null ? lighting.WeatherName(State.Day) : "";
        internal VillageMapRegion MapRegion => mapRegion;
        internal Transform FarmMapAnchor => farm;
        public IReadOnlyList<HarvestableCrop> Crops => crops;
        public IReadOnlyList<VillageInteractable> Interactables => npcs;
        internal int PrewarmGroundPickups => prewarmGroundPickups;
        internal bool MovementBlocked => IsWorldTransitioning || (AwaitingWorldCommand && !AwaitingAutomaticHarvest) ||
            hud != null && (hud.WorldActionsLocked || hud.IsHoldingItem || hud.IsChatTyping);
        public bool ControlsBlocked => MovementBlocked || AwaitingWorldCommand;

        public int RemainingHarvests
        {
            get
            {
                int count = 0;
                foreach (var crop in crops)
                    count += crop.Remaining;
                return count;
            }
        }

        public int GroundItemCount
        {
            get
            {
                int count = 0;
                foreach (var item in pickups)
                    if (item != null && item.isActiveAndEnabled)
                        count++;
                return count;
            }
        }

        private void Awake()
        {
            if (Session?.IsDedicated == true)
            {
                StartHeadless();
                return;
            }
            if (settings == null || mapRoot == null || navigation == null || player == null || worker == null || worldCamera == null || cameraRig == null || hud == null || spawnPoint == null || restPoint == null || droppedItems == null ||
                groundPickupTemplate == null || groundPickupTemplate.Visual == null || groundPickupTemplate.GetComponent<CapsuleCollider>() == null)
            {
                Debug.LogError("Village scene references are incomplete. Assign them in the VillagePrototype Inspector.", this);
                enabled = false;
                return;
            }

#if UNITY_EDITOR
            if (!IsOnline)
                ApplyEditorAppearanceTrial();
#endif
            if (Session?.SelectedCharacter != null && !IsAppearanceTrial)
                player.ApplyProfileLook(Session.SelectedCharacter.look);

            allCrops.AddRange(mapRoot.GetComponentsInChildren<HarvestableCrop>(true));
            pickupPool = new GroundPickupPool(groundPickupTemplate, droppedItems, worldCamera.GetComponent<CameraVisibility>(), RemovePickup);
            foreach (var item in settings.Items)
                if (item != null)
                    pickupPool.CanSpawn(item.GroundPrefab);
            if (effects != null)
                playLanding = effects.PlayLanding;
            animals = mapRoot.GetComponentsInChildren<MonsterInteractable>(true);
            npcs.AddRange(mapRoot.GetComponentsInChildren<VillageInteractable>(true));
            stages = mapRoot.GetComponentsInChildren<VillageWorldStage>(true);
            farmSites = mapRoot.GetComponentsInChildren<VillageFarmSite>(true);
            if (farm != null)
                originalFarmPosition = farm.position;
            var rules = CreateVillageRules();
            State = IsAppearanceTrial ? new VillageState(rules) : VillageState.BeforeFirstLife(rules);
            string notice = null;
            usesSelectedWorld = !IsAppearanceTrial && Session?.HasLocalWorld == true;
            bool reloading = Session != null && Session.TakeSoloReload(out restoredWorld, out notice);
            if (reloading)
                State = VillageState.Restore(restoredWorld, rules);
            else if (IsOnline || usesSelectedWorld)
            {
                if (IsOnline)
                    MarkOnline();
                Session.InitializeAuthority(rules, AuthoredSites(), spawnPoint.position, rebirthZone);
                restoredWorld = Session.InitialCharacter;
                if (restoredWorld != null)
                    State = VillageState.Restore(restoredWorld, rules);
            }
            else if (loadSavedGame && VillageSaveStore.TryLoad(out var savedState, out var savedWorld, out notice, rules, repairBackup: !IsAppearanceTrial))
            {
                State = savedState;
                restoredWorld = savedWorld;
            }
            if (!IsAppearanceTrial && State.LifeLook.HasValue)
                player.ApplyProfileLook(State.LifeLook.Value);

            foreach (var crop in allCrops)
            {
                crop.EnsureInitialized();
                pickupPool.CanSpawn(crop.Definition?.PickupPrefab);
            }
            pickupPool.Prewarm(prewarmGroundPickups);

            ApplyWorldStage();
            foreach (var plot in buildPlots.Values)
                plot.Bind(this);
            BindBuildingDoors();
            bool plotBlocksNavigation = RenderPlots();
            navigation.Activate(useBakedData: !plotBlocksNavigation && State.FarmAvailable && string.IsNullOrEmpty(State.FarmSiteId));
            rebirthZone?.Initialize(this);
            var agent = worker.GetComponent<NavMeshAgent>();
            agent.enabled = true;
            if (NavMesh.SamplePosition(SurfacePosition(spawnPoint.position.x, spawnPoint.position.z), out var initialPosition, 2f, NavMesh.AllAreas))
                agent.Warp(initialPosition.position);
            BindWorker(player, worker, State, LocalCharacterId, worldCamera, LegendPlayerId);
            BindBuiltCharacters();
            foreach (var npc in npcs)
            {
                var actor = npc.GetComponent<CharacterActor>();
                if (actor != null)
                    BindNpcCharacter(actor);
            }

            foreach (var item in FindObjectsByType<GroundPickup>(FindObjectsSortMode.None))
                if (item.gameObject.scene == gameObject.scene)
                {
                    pickups.Add(item);
                    if (!string.IsNullOrEmpty(item.PersistentId))
                    {
                        item.KeepForRespawn();
                        authoredPickups.Add(item);
                    }
                }
            hud.Initialize(this, settings.InterfaceFont);
            hud.SetIncarnationPresentation();
            targeting?.Initialize(this, hud.TargetLink);
            worldCamera.GetComponent<CameraVisibility>()?.Initialize(player.transform);
            cameraRig.Initialize(worker.transform, this);
            RestoreWorldObjects();
            RestoreVillageCharacters();
            SettleLegacyNpcDrops();
            lighting?.SetDate(State.Day, State.Minute);
            initialized = true;
            if (reloading)
                TryWorldTransition(null);
            Session?.Attach(this);
            nextSave = Time.unscaledTime + 15f;
            if (restoredWorld != null)
            {
                bool releasedLoot = false;
                foreach (var animal in animals)
                    if (animal != null)
                        releasedLoot |= animal.TryDropLoot(this);
                if (releasedLoot)
                    SaveGame(false);
            }
            if (!string.IsNullOrEmpty(notice))
                hud.ShowToast(notice);
            if (IsAppearanceTrial)
                hud.ShowToast("외형 시험 중 · 진행은 저장하지 않아요.");
        }

#if UNITY_EDITOR
        private const string AppearanceTrialKey = "Workerborn.EditorAppearanceTrial";

        public static void QueueEditorAppearanceTrial(CharacterDefinition definition)
        {
            UnityEditor.SessionState.SetString(AppearanceTrialKey, UnityEditor.AssetDatabase.GetAssetPath(definition));
        }

        private void ApplyEditorAppearanceTrial()
        {
            string path = UnityEditor.SessionState.GetString(AppearanceTrialKey, "");
            if (string.IsNullOrEmpty(path))
                return;
            UnityEditor.SessionState.EraseString(AppearanceTrialKey);
            IsAppearanceTrial = true;
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
            if (!player.TryUseTrialAppearance(definition))
                Debug.LogError($"Workerborn could not try {path}. The appearance needs a valid Humanoid Avatar.", this);
        }
#endif

        private void Start()
        {
            if (initialized && State.AwaitingIncarnation)
                EnterClouds();
        }

        internal static void BindCharacter(CharacterActor actor)
        {
            var definition = actor.Appearance;
            var driver = actor.GetComponent<WorkerAnimation>();
            if (definition == null || driver == null || actor.Animator == null)
            {
                Debug.LogError("Character appearance must be applied to the Scene before Play.", actor);
                return;
            }

            driver.Initialize(actor.Animator, definition.Idle, definition.Run, definition.Work, definition.Walk);
        }

        private void ApplyWorldStage()
        {
            if (farm != null)
            {
                var site = FindFarmSite(State.FarmSiteId);
                farm.position = site != null && State.FarmAvailable ? site.FarmOrigin.position : originalFarmPosition;
                farm.gameObject.SetActive(State.FarmAvailable);
            }
            foreach (var plot in farmSites)
                plot.gameObject.SetActive(!State.FarmAvailable);
            foreach (var stage in stages)
                stage.Apply(State.FarmLevel);
            mapRegion?.Invalidate();
            crops.Clear();
            foreach (var crop in allCrops)
                if (crop.gameObject.activeInHierarchy && crop.Definition != null && crop.Definition.ItemId == ItemIds.Potato)
                    crops.Add(crop);
        }

        private VillageFarmSite FindFarmSite(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (var site in farmSites)
                    if (site.SiteId == id)
                        return site;
            return null;
        }

        public string FarmSiteName => FindFarmSite(State.FarmSiteId)?.DisplayName ?? "";

        public bool ChooseFarmSite(VillageFarmSite site)
        {
            if (site == null || site != FindFarmSite(site.SiteId))
                return false;
            return PerformWorld(CampaignAction.SelectFarmSite, site.SiteId);
        }

        public VillageInteractable FindInteraction(VillageRole role)
        {
            foreach (var npc in npcs)
                if (npc.Role == role)
                    return npc;
            return null;
        }

        public void OpenConversation(VillageInteractable target, DialogueAction? topic = null)
        {
            PrepareConversation(worker, target);
            hud.OpenDialogue(target, topic);
        }

        internal void OpenConversationTopic(VillageInteractable target, DialogueTopicDefinition topic)
        {
            PrepareConversation(worker, target);
            hud.OpenDialogueTopic(target, topic);
        }

        internal void BeginConversation(HarvestWorker speaker, VillageInteractable target)
        {
            PrepareConversation(speaker, target);
            if (speaker.IsLocallyControlled)
                hud.OpenDialogue(target, null);
        }

        private void PrepareConversation(HarvestWorker speaker, VillageInteractable target)
        {
            speaker.Cancel();
            if (target.Role == VillageRole.Farmer)
                speaker.CharacterState.MeetFarmer();
            if (target.GetComponent<HarvestWorker>()?.OccupiedSeat == null && target.GetComponent<CharacterActor>() != null)
            {
                Vector3 forward = speaker.transform.position - target.transform.position;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f)
                    target.transform.rotation = Quaternion.LookRotation(forward);
            }
        }

        private static int BaseWorkCost(VillageState state, CropDefinition work) =>
            work.StaminaCost > 0 ? work.StaminaCost : state.Rules.HarvestEnergy;

        public bool CanHarvest(HarvestableCrop target) => CanHarvest(worker, target);
        private bool CanHarvest(HarvestableCrop target, bool showNotice) => CanHarvest(worker, target, showNotice);

        internal bool CanHarvest(HarvestWorker source, HarvestableCrop target, bool showNotice = true)
        {
            var state = source.CharacterState;
            if (state.AwaitingIncarnation || state.Health <= 0 || target.Definition == null || !source.SharesMap(target.transform.position))
                return false;
            var work = target.Definition;
            if (work.RequiredTool != null && !state.HasTool(work.RequiredTool.Id))
            {
                if (showNotice && source.IsLocallyControlled)
                    hud.ShowToast(work.RequiredTool.DisplayName + "을 주무기에 장착해 주세요.");
                return false;
            }
            if (state.Energy >= state.WorkEnergyCost(work.Skill, work.ItemId, BaseWorkCost(state, work)))
                return true;
            if (showNotice && source.IsLocallyControlled)
                hud.ShowToast("스태미나가 부족합니다.");
            return false;
        }

        internal bool TryGatherFrom(HarvestWorker sourceWorker, HarvestableCrop crop)
        {
            var state = sourceWorker.CharacterState;
            var work = crop.Definition;
            var skill = work.Skill;
            if (!CanHarvest(sourceWorker, crop))
                return false;
            var outcome = work.RollOutcome(state.RankOf(skill), UnityEngine.Random.value, UnityEngine.Random.value,
                state.SkillBonus(skill, SkillEffect.LuckyChance) * .01f,
                state.SkillBonus(skill, SkillEffect.GreatLuckyChance) * .01f);
            int level = state.Level;
            var water = crop as FishingWater;
            Vector3 source = water != null ? sourceWorker.FishingPoint ?? crop.transform.position : crop.transform.position;
            if (!state.TryGather(skill, work.ItemId, work.ItemsPerHarvest, BaseWorkCost(state, work), outcome, ReceiveYield, out _))
            {
                sourceWorker.ShowGatherResult(false);
                hud.ShowFloatingText(source + Vector3.up, "실패", new Color(1f, .65f, .55f));
                return false;
            }
            sourceWorker.ShowGatherResult(true);

            if (outcome == WorkOutcome.Lucky || outcome == WorkOutcome.GreatLucky)
                hud.ShowFloatingText(source + Vector3.up, outcome == WorkOutcome.GreatLucky ? "대박 보너스" : "럭키 보너스", new Color(1f, 0.88f, 0.5f));
            if (sourceWorker.IsLocallyControlled)
                hud.ShowFloatingText(sourceWorker.transform.position + Vector3.up * 1.8f, "+" + state.Rules.HarvestExperience + " EXP", Color.white);
            ShowLevelUp(sourceWorker, level);
            return true;

            bool ReceiveYield(int quantity)
            {
                var prefab = work.PickupPrefab != null ? work.PickupPrefab : settings.FindItem(work.ItemId)?.GroundPrefab;
                return DeliverYield(sourceWorker, work.ItemId, quantity, prefab, work,
                    source + Vector3.up * .5f, work.MaxVisibleDrops, Landing);
            }

            Vector3 Landing(int index, int piles)
            {
                Vector3 direction = sourceWorker.transform.position - source;
                Vector3 origin = water != null ? sourceWorker.transform.position : source;
                Vector3 point = origin + work.SampleDropOffset(direction, index, piles, UnityEngine.Random.Range(.18f, .82f), UnityEngine.Random.value);
                Vector3 end = SurfacePosition(point.x, point.z);
                if (water != null)
                    end = NavMesh.SamplePosition(end, out var shore, .4f, NavMesh.AllAreas) && !water.CoversGround(shore.position)
                        ? shore.position : origin;
                return end + Vector3.up * .08f;
            }
        }

        private GroundPickup SpawnPickup(GroundPickup source, CropDefinition definition, int quantity, Vector3 start, Vector3 end, bool settled = false)
        {
            var item = pickupPool.Spawn(source, definition, quantity, start, end, settled);
            pickups.Add(item);
            if (effects != null && !settled)
            {
                item.Landed += playLanding;
                effects.PlayDropTrail(item);
            }
            return item;
        }

        private void RemovePickup(GroundPickup pickup)
        {
            pickups.Remove(pickup);
            ForgetGroundPickup(pickup);
        }

        internal void PlayCorpseDespawn(Vector3 position) => effects?.PlayCorpseDespawn(position);

        internal void ShowCombatHit(Transform visual, Vector3 point)
        {
            effects?.PlayCombatHit(point);
            HitFlash.PlayOn(visual);
        }

        internal bool DropStrikeLoot(Transform origin, ItemDefinition reward, int amount, HarvestWorker sourceWorker)
        {
            if (reward == null || amount < 1)
                return false;
            Vector3 source = origin.position;
            Vector3 forward = sourceWorker.transform.position - source;
            forward.y = 0;
            forward = forward.sqrMagnitude > .001f ? forward.normalized : origin.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return DeliverYield(sourceWorker, reward.Id, amount, reward.GroundPrefab, null, source + Vector3.up * .25f, 24, Landing);

            Vector3 Landing(int index, int piles)
            {
                Vector3 point = source + forward * .65f + right * ((index - (piles - 1) * .5f) * .26f);
                return SurfacePosition(point.x, point.z) + Vector3.up * .08f;
            }
        }

        internal void ReceiveAnimalHit(HarvestWorker target, int damage)
        {
            int applied = target.CharacterState.TakeHit(damage);
            if (applied <= 0)
                return;
            ShowCombatHit(target.Actor.VisualRoot, target.transform.position + Vector3.up * 1.1f);
            hud.ShowFloatingText(target.transform.position + Vector3.up * 1.8f, "-" + applied, new Color(1f, .52f, .42f));
            if (target.CharacterState.Health <= 0)
                target.Cancel();
        }

        public void ShowClick(Vector3 position) => effects?.PlayClick(SurfacePosition(position.x, position.z) + Vector3.up * .04f);

        internal GroundPickup DropPrefabFor(InventoryEntry entry, HarvestWorker source = null)
        {
            if (entry != null &&
                settings.FindItem(entry.ItemId)?.GroundPrefab is GroundPickup prefab &&
                prefab.ItemId == entry.ItemId && pickupPool.CanSpawn(prefab))
                return prefab;
            if ((source ?? worker).IsLocallyControlled)
                hud.ShowToast("이 아이템은 바닥에 놓을 수 없어요.");
            return null;
        }

        internal bool PlaceInventoryDrop(HarvestWorker source, InventoryEntry request, Vector3 destination)
        {
            if (!source.HasPlayerOwner)
                return false;
            var state = source.CharacterState;
            var entry = state.CarriedItem(request.Id);
            if (entry == null || !entry.MatchesStack(request.ItemId, request.Condition) || entry.Quantity != request.Quantity)
            {
                if (source.IsLocallyControlled)
                    hud.ShowToast("아이템 수량이 바뀌었습니다. 다시 놓아 주세요.");
                return false;
            }
            var prefab = DropPrefabFor(entry, source);
            if (prefab == null || !source.SharesMap(destination))
                return false;

            var end = SurfacePosition(destination.x, destination.z) + Vector3.up * .08f;
            Vector3 origin = source.transform.position + Vector3.up * .45f;
            var drop = CaptureGroundDrop(entry.ItemId, entry.Quantity, origin, end, entry.Condition);
            if (IsOnline)
                return Session?.SubmitGroundDrop(entry.Id, drop) == true;
            if (!state.TryRemoveCarriedItem(entry.Id, entry.Quantity))
                return false;
            SpawnLocalPlayerDrop(drop, source.PlayerId, PickupUtcNow, prefab, null, origin, end);
            hud.RefreshWindows();
            SaveGame(false);
            return true;
        }

        public bool TryCollectPickup(GroundPickup item) => TryCollectPickup(worker, item);

        internal bool TryCollectPickup(HarvestWorker collector, GroundPickup item)
        {
            var state = collector.CharacterState;
            if (state.AwaitingIncarnation || item == null || !item.CanCollectFor(collector) ||
                !collector.SharesMap(item.LandingPosition) || !collector.IsInPickupRange(item))
                return false;
            if (item.SharedDropId != null)
                return collector.HasPlayerOwner && Session?.SubmitGroundPickup(item.SharedDropId) == true;

            Vector3 itemPosition = item.VisualPosition + Vector3.up * .08f;
            string displayName = item.ItemDisplayName;
            string persistentId = item.PersistentId;
            int quantity = item.Quantity;
            if (item.DropRecord?.combined == true)
            {
                quantity = state.Inventory.TryAddUpTo(item.ItemId, quantity, item.Condition);
                if (quantity > 0 && quantity < item.Quantity)
                {
                    item.DropRecord.quantity -= quantity;
                    item.SetQuantity(item.DropRecord.quantity);
                    PlayCollection(collector, itemPosition, displayName, quantity);
                    return true;
                }
                if (quantity > 0)
                    item.CompleteCollection();
            }
            else if (!item.TryCollect(collector, () => state.Inventory.TryAdd(item.ItemId, quantity, item.Condition)))
                quantity = 0;
            if (quantity == 0)
            {
                if (collector.IsLocallyControlled)
                    hud.ShowToast("소지품 공간이 부족합니다.");
                return false;
            }
            if (!item.IsPooled)
                RemovePickup(item);
            if (!string.IsNullOrEmpty(persistentId))
                collectedPickups.Add(persistentId);
            PlayCollection(collector, itemPosition, displayName, quantity);
            return true;
        }

        private void PlayCollection(HarvestWorker collector, Vector3 itemPosition, string displayName, int quantity)
        {
            effects?.PlayPickupTrail(itemPosition, collector.transform);
            hud.ShowFloatingText(collector.transform.position + Vector3.up * 1.5f, displayName + " +" + quantity, Color.white);
        }

        public bool Perform(Func<bool> operation) => !State.AwaitingIncarnation && ApplyLocalAction(operation);

        internal bool UpgradeSkill(SkillId skill) => ApplyLocalAction(() => State.TryUpgradeSkill(skill));
        internal bool UpgradeSkillToNextGrade(SkillId skill) => ApplyLocalAction(() => State.TryUpgradeSkillToNextGrade(skill));

        internal bool BuyFrom(VillageInteractable seller, string id, int quantity, bool placeOnCursor)
        {
            if (seller == null || !Perform(() => placeOnCursor ? State.TryBuyForPlacement(id, quantity) : State.TryBuy(id, quantity)))
                return false;
            effects?.PlayPurchaseTrail(seller.transform, worker.transform);
            return true;
        }

        internal bool SellTo(VillageInteractable buyer, int entryId, int quantity)
        {
            if (buyer == null || !Perform(() => State.TrySellStack(entryId, quantity)))
                return false;
            effects?.PlayPurchaseTrail(worker.transform, buyer.transform);
            return true;
        }

        private bool ApplyLocalAction(Func<bool> operation)
        {
            if (IsWorldTransitioning || AwaitingWorldCommand)
                return false;
            int level = State.Level;
            bool success = operation();
            ShowLevelUp(level);
            hud.ShowToast(State.LastMessage);
            hud.RefreshWindows();
            if (success)
                SaveGame(false);
            return success;
        }

        private void ShowLevelUp(int previousLevel) => ShowLevelUp(worker, previousLevel);

        private void ShowLevelUp(HarvestWorker source, int previousLevel)
        {
            if (source.CharacterState.Level <= previousLevel)
                return;
            effects?.PlayLevelUp(source.transform.position);
            hud.ShowFloatingText(source.transform.position + Vector3.up * 2.1f, "LEVEL UP", new Color(1f, 0.88f, 0.5f));
        }

        public void Sleep(bool paidRoom) => SleepInBed(CurrentInterior?.Bed, paidRoom);

        internal void SleepInBed(VillageBed bed, bool paidRoom) => SleepInBed(worker, bed, paidRoom);

        internal void SleepInBed(HarvestWorker sleeper, VillageBed bed, bool paidRoom)
        {
            var state = sleeper.CharacterState;
            if (!sleeper.AcceptsInteractionCommands || state.AwaitingIncarnation || bed == null || !bed.CanInteract ||
                !sleeper.SharesMap(bed.transform.position) || !sleeper.InReach(bed.Approach.position, .9f))
                return;
            if (!sleeper.IsLocallyControlled)
            {
                state.TrySleep(paidRoom, sharedClock: true);
                return;
            }
            if (paidRoom && State.Gold < State.Rules.RoomPrice)
            {
                State.TrySleep(paidRoom, IsOnline);
                hud.ShowToast(State.LastMessage);
                return;
            }

            if (TryWorldTransition(() =>
            {
                bool rested = State.TrySleep(paidRoom, IsOnline);
                if (rested)
                    BeginDay(resetResources: !IsOnline);
                worker.StandUpFromBed();
                hud.ShowToast(State.LastMessage);
                if (rested)
                    SaveGame(false);
            }, coverDelay: .55f))
                worker.LieOnBed(bed.Pose);
        }

        private void BeginDay(bool resetResources = true)
        {
            var room = GuestRoomForRecovery();
            var arrival = room != null ? room.Bed.Approach : restPoint;
            bool arrived = TryWarpToMap(arrival.position, room);
            if (!arrived && room != null)
            {
                arrival = restPoint;
                arrived = TryWarpToMap(arrival.position);
            }
            if (!arrived)
                worker.Cancel();
            else
            {
                worker.transform.rotation = arrival.rotation;
                RefreshAfterTravel();
            }
            if (resetResources)
                ResetDailyResources();
            lighting?.SetDate(State.Day, State.Minute);
        }

        private void ResetDailyResources()
        {
            foreach (var crop in allCrops)
                crop.ResetHarvests();
            foreach (var animal in animals)
                if (animal != null)
                    animal.ResetEncounter();
        }

        public void Rebirth() => PerformWorld(CampaignAction.Rebirth);

        public void AdvanceEra()
        {
            if (IsWorldTransitioning)
                return;
            if (IsOnline)
            {
                hud.CloseDialogue();
                hud.ToggleWindow("era");
                return;
            }
            if (State.AwaitingIncarnation)
                return;
            if (!State.CanAdvanceEra)
            {
                State.TryAdvanceEra();
                hud.ShowToast(State.LastMessage);
                return;
            }
            TryWorldTransition(() =>
            {
                bool advanced = State.TryAdvanceEra();
                if (advanced)
                    FinishTransition(reborn: true, eraChanged: true);
                hud.ShowToast(State.LastMessage);
                if (advanced)
                    SaveGame(false);
            });
        }

        private void FinishTransition(bool reborn, bool eraChanged)
        {
            hud.CancelItemDrag();
            worker.Cancel();
            effects?.Clear();
            hud.ClearFloatingText();
            if (reborn)
            {
                worker.ResetLifeActivity();
                for (int i = pickups.Count - 1; i >= 0; i--)
                    if (pickups[i] != null)
                        pickups[i].Despawn();
                pickups.Clear();
                collectedPickups.Clear();
                foreach (var pickup in authoredPickups)
                {
                    if (pickup == null || string.IsNullOrEmpty(pickup.PersistentId))
                        continue;
                    pickup.RespawnAuthored();
                    pickups.Add(pickup);
                }
            }
            if (eraChanged)
            {
                ApplyWorldStage();
                RenderPlots();
                var agent = worker.GetComponent<NavMeshAgent>();
                agent.enabled = false;
                navigation.Activate(useBakedData: false);
                agent.enabled = true;
                BindBuiltCharacters();
            }
            ResetDailyResources();
            hud.CloseDialogue();
            if (State.AwaitingIncarnation)
            {
                if (!InRebirthZone)
                    EnterClouds();
            }
            else if (InRebirthZone)
                EnterVillage();
            else if (eraChanged)
                Travel(spawnPoint.position, toClouds: false);
            lighting?.SetDate(State.Day, State.Minute);
            hud.RefreshWindows();
        }

        public void WalkTo(VillageRole role)
        {
            if (hud.WorldActionsLocked)
                return;
            hud.CloseDialogue();
            worker.CommandInteract(FindInteraction(role));
        }

        public void HarvestNearest() => UseGatheringSkill(SkillId.Harvesting);

        internal void UseGatheringSkill(SkillId skill)
        {
            if (State.AwaitingIncarnation || hud.WorldActionsLocked || !State.IsSkillLearned(skill))
                return;
            HarvestableCrop nearest = null;
            float distance = float.MaxValue;
            foreach (var crop in allCrops)
            {
                if (!crop.CanInteract || crop.Definition.Skill != skill || !SharesMap(crop.transform.position) ||
                    !State.HasTool(crop.Definition.RequiredTool?.Id))
                    continue;
                float candidate = (crop.transform.position - worker.transform.position).sqrMagnitude;
                if (candidate < distance)
                {
                    nearest = crop;
                    distance = candidate;
                }
            }

            if (nearest != null)
                worker.CommandHarvest(nearest);
        }

        internal Vector3 SurfacePosition(float x, float z)
        {
            var point = new Vector3(x, 0, z);
            var interior = InteriorAt(x, z);
            if (interior != null)
                point.y = interior.Area.max.y;
            else if (rebirthZone?.Contains(x, z) == true)
                point.y = rebirthZone.Area.max.y;
            else if (GroundTerrain != null)
                point.y = GroundTerrain.SampleHeight(point) + GroundTerrain.transform.position.y;
            return point;
        }

        internal bool TryMapGround(Vector2 mapPoint, out RaycastHit hit)
        {
            hit = default;
            Rect area = MapBounds;
            if (area.width <= 0f || area.height <= 0f)
                return false;
            Collider ground = CurrentInterior != null ? CurrentInterior.Floor : InRebirthZone ? rebirthZone.Floor :
                GroundTerrain != null ? GroundTerrain.GetComponent<TerrainCollider>() : navigation.GroundBox;
            if (ground == null)
                return false;
            Vector2 point = area.min + Vector2.Scale(mapPoint, area.size);
            Bounds bounds = ground.bounds;
            var ray = new Ray(new Vector3(point.x, bounds.max.y + 1f, point.y), Vector3.down);
            return ground.Raycast(ray, out hit, bounds.size.y + 2f);
        }

        internal Rect MapBounds
        {
            get
            {
                if (CurrentInterior != null)
                    return CurrentInterior.MapBounds;
                if (!InRebirthZone)
                    return mapRegion != null ? mapRegion.WorldBounds : default;
                Bounds clouds = rebirthZone.Area;
                return new Rect(clouds.min.x, clouds.min.z, clouds.size.x, clouds.size.z);
            }
        }

        internal void CollectMapFootprints(List<VillageMapFootprint> footprints)
        {
            foreach (var plot in BuildPlots)
            {
                if (plot == null || !plot.isActiveAndEnabled || plot.InstalledFacility == null)
                    continue;
                var facility = plot.InstalledFacility;
                if (facility.MapSurface == VillageMapSurface.None)
                    continue;
                PlotFootprint area = facility.FillsPlot ? plot.Footprint : facility.Footprint;
                footprints.Add(new VillageMapFootprint(plot.transform, Vector3.zero,
                    new Vector2(area.Width, area.Depth) * VillagePlot.CellSide, facility.MapSurface));
            }

            foreach (var prop in mapRoot.GetComponentsInChildren<VillageMapProp>())
                if (prop.TryGetMapFootprint(out var footprint))
                    footprints.Add(footprint);
        }

        private void RestoreWorldObjects()
        {
            if (restoredWorld == null)
                return;
            if (restoredWorld.collectedPickupIds != null)
                foreach (var id in restoredWorld.collectedPickupIds)
                    collectedPickups.Add(id);
            foreach (var item in pickups)
                if (item != null && !string.IsNullOrEmpty(item.PersistentId) && collectedPickups.Contains(item.PersistentId))
                    item.Despawn();
            pickups.RemoveAll(item => item == null || !item.CanCollect);

            RestoreCharacterPosition(worker, restoredWorld);
            if (CurrentInterior != null)
                cameraRig.SnapToTarget();
            if (restoredWorld.cropStates != null && restoredWorld.cropStates.Length > 0)
            {
                var saved = new Dictionary<string, int>();
                foreach (var crop in restoredWorld.cropStates)
                    if (crop != null && !string.IsNullOrEmpty(crop.id))
                        saved[crop.id] = crop.remaining;
                foreach (var crop in allCrops)
                    if (saved.TryGetValue(crop.PersistentId, out int remaining))
                        crop.RestoreRemaining(remaining);
            }
            else if (restoredWorld.cropRemaining != null)
                for (int i = 0; i < Mathf.Min(crops.Count, restoredWorld.cropRemaining.Length); i++)
                    crops[i].RestoreRemaining(restoredWorld.cropRemaining[i]);

            if (restoredWorld.animalStates != null)
                foreach (var record in restoredWorld.animalStates)
                    if (record != null && !string.IsNullOrEmpty(record.id))
                        foreach (var animal in animals)
                            if (animal != null && animal.PersistentId == record.id)
                            {
                                animal.RestoreEncounter(record.health, record.lootTaken, record.lootOwnerCharacterId);
                                break;
                            }
            if (restoredWorld.drops != null)
                foreach (var drop in restoredWorld.drops)
                {
                    if (drop == null || float.IsNaN(drop.x) || float.IsNaN(drop.z) || float.IsInfinity(drop.x) || float.IsInfinity(drop.z))
                        continue;
                    if (!string.IsNullOrEmpty(drop.harvestedBy) && drop.harvestedBy != LocalCharacterId)
                    {
                        legacyNpcDrops.Add(drop);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(drop.ownerId) && !string.IsNullOrEmpty(drop.id))
                    {
                        if (drop.RoomId != null && !TryRestoreInterior(drop.interior, out _, out _))
                            drop.interior = default;
                        localGroundDrops.Add(drop);
                        continue;
                    }
                    GroundPickup authored = null;
                    if (!string.IsNullOrEmpty(drop.id))
                        foreach (var existing in pickups)
                            if (existing != null && existing.PersistentId == drop.id)
                            {
                                authored = existing;
                                break;
                            }

                    Vector3 point = TryRestoreInterior(drop.interior, out _, out var inside)
                        ? inside : new Vector3(drop.x, 0f, drop.z);
                    var end = SurfacePosition(point.x, point.z) + Vector3.up * .08f;
                    if (authored != null)
                    {
                        authored.SetQuantity(drop.quantity);
                        authored.Initialize(authored.Definition, end, end, true);
                        continue;
                    }

                    var source = settings.FindItem(drop.itemId)?.GroundPrefab;
                    if (pickupPool.CanSpawn(source))
                        SpawnPickup(source, drop.itemId == potato.ItemId ? potato : null, drop.quantity, end, end, settled: true);
                }
            if (usesSelectedWorld && !IsOnline && Session != null)
                foreach (var drop in Session.SharedGroundDrops)
                    if (drop.IsVisibleTo(worker.PlayerId))
                        localGroundDrops.Add(drop.Copy());
            RestoreLocalGroundPickups();
            RestoreCharacterAction(worker, restoredWorld);
        }

        public bool SaveGame(bool notify)
        {
            if (!initialized)
                return false;
            if (IsOnline)
            {
                bool submitted = Session.Checkpoint(CaptureSave());
                if (notify)
                    hud.ShowToast(submitted ? "방에 현재 진행을 보냈어요." : "저장 요청을 보내지 못했어요.");
                nextSave = Time.unscaledTime + 15f;
                return submitted;
            }
            if (usesSelectedWorld)
            {
                bool saved = Session?.SaveSolo(State.ExportWorld(), CaptureSave()) == true;
                if (notify || !saved)
                    hud.ShowToast(saved ? "저장했습니다." : "월드 저장에 실패했습니다.");
                nextSave = Time.unscaledTime + 15f;
                return saved;
            }
            if (!loadSavedGame || IsAppearanceTrial)
            {
                if (notify)
                    hud.ShowToast(IsAppearanceTrial ? "외형 시험 중의 진행은 저장하지 않아요." : "이 세션은 저장을 사용하지 않습니다.");
                return false;
            }
            bool success = VillageSaveStore.TrySave(CaptureSave(), out _);
            if (notify || !success)
                hud.ShowToast(success ? "저장했습니다." : "저장에 실패했습니다.");
            nextSave = Time.unscaledTime + 15f;
            return success;
        }

        public bool CanReloadSavedGame => initialized && !State.AwaitingIncarnation && !IsOnline && !IsAppearanceTrial && Session != null &&
            (usesSelectedWorld ? Session.HasLocalSave : loadSavedGame && VillageSaveStore.HasSave);

        public void ReloadSavedGame()
        {
            if (!CanReloadSavedGame)
            {
                hud.ShowToast("불러올 저장 기록을 열지 못했어요.");
                return;
            }
            TryWorldTransition(() =>
            {
                if (Session.TryReloadSolo(State.Rules, AuthoredSites(), spawnPoint.position, usesSelectedWorld))
                    initialized = false;
                else
                    hud.ShowToast("불러올 저장 기록을 열지 못했어요.");
            });
        }

        internal VillageSave CaptureSave()
        {
            var save = CaptureCharacter<VillageSave>(worker);
            save.characters = CaptureVillageCharacters();
            if (State.AwaitingIncarnation)
                return save;
            save.cropStates = new CropStateSave[allCrops.Count];
            for (int i = 0; i < allCrops.Count; i++)
                save.cropStates[i] = new CropStateSave
                {
                    id = allCrops[i].PersistentId,
                    remaining = allCrops[i].Remaining
                };
            var animalStates = new List<AnimalStateSave>();
            foreach (var animal in animals)
                if (animal != null && animal.Combat != null && !string.IsNullOrEmpty(animal.PersistentId))
                    animalStates.Add(new AnimalStateSave
                    {
                        id = animal.PersistentId, health = animal.Health, lootTaken = animal.LootTaken,
                        lootOwnerCharacterId = animal.LootOwnerCharacterId
                    });
            save.animalStates = animalStates.ToArray();
            var ground = new List<GroundDropSave>(legacyNpcDrops);
            ground.AddRange(localGroundDrops.Active);
            foreach (var item in pickups)
                if (item != null && item.isActiveAndEnabled && item.DropRecord == null)
                {
                    Vector3 landing = item.LandingPosition;
                    var room = InteriorAt(landing.x, landing.z);
                    Vector3 exterior = room != null ? ExteriorPosition(room) : landing;
                    ground.Add(new GroundDropSave
                    {
                        id = item.PersistentId, quantity = item.Quantity, itemId = item.ItemId,
                        x = exterior.x, z = exterior.z, interior = room != null ? room.CapturePosition(landing) : default
                    });
                }
            save.drops = ground.ToArray();
            save.collectedPickupIds = new List<string>(collectedPickups).ToArray();
            return save;
        }

        private void Update()
        {
            if (!initialized)
                return;
            long now = PickupUtcNow;
            ReleaseLocalGroundDrops(now);
            hud.SetCommandPending(AwaitingWorldCommand);
            if (IsWorldTransitioning || AwaitingWorldCommand)
                return;

            State.CollectReadyHarvests(now, showAcquiredHarvest ??= ShowAcquiredHarvest);
            CollectNearbyHarvests(now);
            if (AwaitingWorldCommand)
                return;
            float elapsed = Time.deltaTime;
            bool paused = hud.HasModal;
            if (IsOnline ? State.NeedsRest : State.WillAdvanceDay(elapsed, paused))
            {
                TryWorldTransition(() => TickClock(elapsed, paused));
                return;
            }
            TickClock(elapsed, paused);
            UpdateVillageCharacters(now);

            if (Time.unscaledTime >= nextSave && (IsOnline || usesSelectedWorld || loadSavedGame && !IsAppearanceTrial))
                SaveGame(false);
            lighting?.SetDate(State.Day, State.Minute);
        }

        private void TickClock(float elapsed, bool paused)
        {
            int day = State.Day;
            bool rested = IsOnline ? State.RecoverInSharedWorld() : State.AdvanceTime(elapsed, paused);
            if (State.AwaitingIncarnation && State.Day != day)
                ResetDailyResources();
            if (rested)
            {
                BeginDay(resetResources: !IsOnline);
                hud.CloseDialogue();
                hud.ShowToast(State.LastMessage);
                SaveGame(false);
            }
        }

        private void OnDestroy()
        {
            Session?.Detach(this);
            if (worker != null)
            {
                worker.Cancel();
                worker.GetComponent<NavMeshAgent>().enabled = false;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && initialized)
                SaveGame(false);
        }

        private void OnApplicationQuit()
        {
            if (initialized && !IsOnline && !usesSelectedWorld)
                SaveGame(false);
        }
    }
}
