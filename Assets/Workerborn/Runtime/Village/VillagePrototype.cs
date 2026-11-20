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
        private Camera minimapCamera;
        [SerializeField]
        private RenderTexture minimapTexture;
        [SerializeField]
        private NpcPortrait playerPortrait;
        [SerializeField]
        private RenderTexture portraitTexture;
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
        [SerializeField] private GameObject guideStage;
        [SerializeField] private VillageInteractable fateGuide;
        [SerializeField] private Transform guideCameraPose;
        public PointerTargeting Targeting => targeting;
        public Terrain GroundTerrain => navigation != null ? navigation.GroundTerrain : null;
        public bool IsGround(Collider hit) => navigation != null && navigation.IsGround(hit);
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
        private bool restoreCameraVisibility;
        private VillageWorldStage[] stages;
        private VillageSave restoredWorld;
        private float nextSave;
        private bool initialized;
        private bool IsAppearanceTrial { get; set; }
        private bool usesSelectedWorld;
        private int equipmentVersion = -1;
        public VillageState State { get; private set; }
        public VillageSettings Settings => settings;
        public CharacterActor PlayerActor => player;
        public VillageHud Hud => hud;
        public HarvestWorker Worker => worker;
        public Camera WorldCamera => worldCamera;
        public VillageCameraRig CameraRig => cameraRig;
        public string WeatherName => lighting != null ? lighting.WeatherName(State.Day) : "";
        public Camera MinimapCamera => minimapCamera;
        public RenderTexture MinimapTexture => minimapTexture;
        public RenderTexture PortraitTexture => portraitTexture;
        public IReadOnlyList<HarvestableCrop> Crops => crops;
        public IReadOnlyList<VillageInteractable> Interactables => npcs;
        internal int PrewarmGroundPickups => prewarmGroundPickups;
        public bool ControlsBlocked => AwaitingWorldCommand || hud != null && (hud.WorldActionsLocked || hud.IsHoldingItem);

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
            var rules = settings.CreateRules(FindInteraction(VillageRole.Farmer)?.Definition);
            State = new VillageState(rules);
            string notice = null;
            usesSelectedWorld = !IsAppearanceTrial && Session?.HasLocalWorld == true;
            if (IsOnline || usesSelectedWorld)
            {
                if (IsOnline)
                    MarkOnline();
                Session.InitializeAuthority(rules, AuthoredSites(), spawnPoint.position);
                restoredWorld = Session.InitialCharacter;
                if (restoredWorld != null)
                    State = VillageState.Restore(restoredWorld, rules);
            }
            else if (loadSavedGame && VillageSaveStore.TryLoad(out var savedState, out var savedWorld, out notice, rules, repairBackup: !IsAppearanceTrial))
            {
                State = savedState;
                restoredWorld = savedWorld;
            }

            foreach (var crop in allCrops)
            {
                crop.EnsureInitialized();
                pickupPool.CanSpawn(crop.Definition?.PickupPrefab);
                crop.Harvested += DropHarvest;
            }
            pickupPool.Prewarm(prewarmGroundPickups);

            ApplyWorldStage();
            navigation.Activate(useBakedData: State.FarmAvailable && string.IsNullOrEmpty(State.FarmSiteId));
            var agent = worker.GetComponent<NavMeshAgent>();
            agent.enabled = true;
            if (NavMesh.SamplePosition(SurfacePosition(spawnPoint.position.x, spawnPoint.position.z), out var initialPosition, 2f, NavMesh.AllAreas))
                agent.Warp(initialPosition.position);
            BindCharacter(player, false);
            worker.Initialize(worldCamera, player.GetComponent<WorkerAnimation>());
            worker.BindVillage(this);
            foreach (var npc in npcs)
            {
                var actor = npc.GetComponent<CharacterActor>();
                if (actor != null)
                    BindCharacter(actor, true);
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
            if (minimapCamera != null && minimapTexture != null)
            {
                minimapCamera.targetTexture = minimapTexture;
                minimapCamera.enabled = true;
            }
            hud.Initialize(this, settings.InterfaceFont);
            targeting?.Initialize(this, hud.TargetLink);
            worldCamera.GetComponent<CameraVisibility>()?.Initialize(player.transform);
            cameraRig.Initialize(worker.transform, this);
            RestoreWorldObjects();
            lighting?.SetDate(State.Day, State.Minute);
            initialized = true;
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
            if (initialized)
                OpenGuideIfNeeded();
        }

        private void OpenGuideIfNeeded()
        {
            if (!State.NeedsGuide || guideStage == null || fateGuide == null || guideCameraPose == null)
                return;
            guideStage.SetActive(true);
            var actor = fateGuide.GetComponent<CharacterActor>();
            if (actor != null)
                BindCharacter(actor, true);
            var visibility = worldCamera.GetComponent<CameraVisibility>();
            restoreCameraVisibility = visibility != null && visibility.enabled;
            if (restoreCameraVisibility)
                visibility.enabled = false;
            cameraRig.SetViewOverride(guideCameraPose);
            hud.OpenDialogue(fateGuide);
        }

        public void FinishGuideVisit()
        {
            cameraRig.SetViewOverride(null);
            if (guideStage != null)
                guideStage.SetActive(false);
            if (restoreCameraVisibility && worldCamera.TryGetComponent<CameraVisibility>(out var visibility))
                visibility.enabled = true;
            State.FinishGuideVisit();
            SaveGame(false);
        }

        private static void BindCharacter(CharacterActor actor, bool idleOnly)
        {
            var definition = actor.Appearance;
            var driver = actor.GetComponent<WorkerAnimation>();
            if (definition == null || driver == null || actor.Animator == null)
            {
                Debug.LogError("Character appearance must be applied to the Scene before Play.", actor);
                return;
            }

            driver.Initialize(actor.Animator, definition.Idle, idleOnly ? definition.Idle : definition.Run,
                idleOnly ? definition.Idle : definition.Work, idleOnly ? definition.Idle : definition.Walk);
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
            worker.Cancel();
            if (target.Role == VillageRole.Farmer)
                State.MeetFarmer();
            if (target.GetComponent<CharacterActor>() != null)
            {
                Vector3 forward = worker.transform.position - target.transform.position;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f)
                    target.transform.rotation = Quaternion.LookRotation(forward);
            }

            hud.OpenDialogue(target, topic);
        }

        private int BaseWorkCost(CropDefinition work) => work.StaminaCost > 0 ? work.StaminaCost : State.Rules.HarvestEnergy;

        public bool CanHarvest(HarvestableCrop target)
        {
            var work = target.Definition;
            if (work.RequiredTool != null && !State.HasTool(work.RequiredTool.Id))
            {
                hud.ShowToast(work.RequiredTool.DisplayName + "을 주무기에 장착해 주세요.");
                return false;
            }
            if (State.Energy >= State.WorkEnergyCost(work.Skill, work.ItemId, BaseWorkCost(work)))
                return true;
            hud.ShowToast("스태미나가 부족합니다.");
            return false;
        }

        private void DropHarvest(HarvestableCrop crop)
        {
            var work = crop.Definition;
            var skill = work.Skill;
            var outcome = work.RollOutcome(State.RankOf(skill), UnityEngine.Random.value, UnityEngine.Random.value,
                State.SkillBonus(skill, SkillEffect.LuckyChance) * .01f,
                State.SkillBonus(skill, SkillEffect.GreatLuckyChance) * .01f);
            int level = State.Level;
            if (!CanHarvest(crop))
                return;
            if (!State.TryGather(skill, work.ItemId, work.ItemsPerHarvest, BaseWorkCost(work), outcome, out int count))
            {
                hud.ShowFloatingText(crop.transform.position + Vector3.up, "실패", new Color(1f, .65f, .55f));
                return;
            }
            var prefab = work.PickupPrefab != null ? work.PickupPrefab : settings.FindItem(work.ItemId)?.GroundPrefab;
            if (pickupPool.CanSpawn(prefab))
            {
                Vector3 source = crop.transform.position;
                Vector3 direction = worker.transform.position - source;
                int piles = Mathf.Min(count, work.MaxVisibleDrops);
                for (int i = 0; i < piles; i++)
                {
                    Vector3 point = source + work.SampleDropOffset(direction, i, piles, UnityEngine.Random.Range(.18f, .82f), UnityEngine.Random.value);
                    Vector3 end = SurfacePosition(point.x, point.z) + Vector3.up * .08f;
                    int quantity = count / piles + (i < count % piles ? 1 : 0);
                    SpawnPickup(prefab, work, quantity, source + Vector3.up * .5f, end);
                }
            }
            else
                Debug.LogError("Assign a ground-item prefab with a mesh to " + work.name, this);

            if (outcome == WorkOutcome.Lucky || outcome == WorkOutcome.GreatLucky)
                hud.ShowFloatingText(crop.transform.position + Vector3.up, outcome == WorkOutcome.GreatLucky ? "대박 보너스" : "럭키 보너스", new Color(1f, 0.88f, 0.5f));
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.8f, "+" + State.Rules.HarvestExperience + " EXP", Color.white);
            ShowLevelUp(level);
        }

        private void SpawnPickup(GroundPickup source, CropDefinition definition, int quantity, Vector3 start, Vector3 end, bool settled = false)
        {
            var item = pickupPool.Spawn(source, definition, quantity, start, end, settled);
            pickups.Add(item);
            if (effects != null && !settled)
            {
                item.Landed += playLanding;
                effects.PlayDropTrail(item);
            }
        }

        private void RemovePickup(GroundPickup pickup) => pickups.Remove(pickup);

        internal bool DropStrikeLoot(Transform origin, ItemDefinition reward, int amount)
        {
            var prefab = reward != null ? reward.GroundPrefab : null;
            if (amount < 1 || !pickupPool.CanSpawn(prefab) || prefab.ItemId != reward.Id)
                return false;
            Vector3 source = origin.position;
            Vector3 forward = worker.transform.position - source;
            forward.y = 0;
            forward = forward.sqrMagnitude > .001f ? forward.normalized : origin.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int piles = Mathf.Min(amount, 24);
            for (int i = 0; i < piles; i++)
            {
                Vector3 point = source + forward * .65f + right * ((i - (piles - 1) * .5f) * .26f);
                Vector3 end = SurfacePosition(point.x, point.z) + Vector3.up * .08f;
                int quantity = amount / piles + (i < amount % piles ? 1 : 0);
                SpawnPickup(prefab, null, quantity, source + Vector3.up * .25f, end);
            }
            return true;
        }

        internal void ReceiveAnimalHit(int damage)
        {
            int applied = State.TakeHit(damage);
            if (applied <= 0)
                return;
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.8f, "-" + applied, new Color(1f, .52f, .42f));
            if (State.Health <= 0)
                worker.Cancel();
        }

        public void ShowClick(Vector3 position) => effects?.PlayClick(SurfacePosition(position.x, position.z) + Vector3.up * .04f);

        internal GroundPickup DropPrefabFor(InventoryEntry entry)
        {
            if (entry != null &&
                settings.FindItem(entry.ItemId)?.GroundPrefab is GroundPickup prefab &&
                prefab.ItemId == entry.ItemId && pickupPool.CanSpawn(prefab))
                return prefab;
            hud.ShowToast("이 아이템은 바닥에 놓을 수 없어요.");
            return null;
        }

        internal bool PlaceInventoryDrop(InventoryEntry request, Vector3 destination)
        {
            var entry = State.Inventory.Find(request.Id);
            if (entry == null || entry.ItemId != request.ItemId || entry.Quantity != request.Quantity)
            {
                hud.ShowToast("아이템 수량이 바뀌었습니다. 다시 놓아 주세요.");
                return false;
            }
            var prefab = DropPrefabFor(entry);
            if (prefab == null || !State.Inventory.TryRemoveAt(entry.Id, entry.Quantity))
                return false;

            var end = SurfacePosition(destination.x, destination.z) + Vector3.up * .08f;
            SpawnPickup(prefab, null, request.Quantity,
                worker.transform.position + Vector3.up * .45f, end);
            hud.RefreshWindows();
            SaveGame(false);
            return true;
        }

        public bool TryCollectPickup(GroundPickup item)
        {
            if (item == null || !item.CanCollect || !worker.IsInPickupRange(item))
                return false;
            string id = item.ItemId;
            int quantity = item.Quantity;
            if (!State.Inventory.CanAdd(id, quantity))
            {
                hud.ShowToast("소지품 공간이 부족합니다.");
                return false;
            }

            Vector3 itemPosition = item.VisualPosition + Vector3.up * .08f;
            string displayName = item.DisplayName;
            string persistentId = item.PersistentId;
            if (!item.TryCollect(() => State.Inventory.TryAdd(id, quantity)))
                return false;
            if (!item.IsPooled)
                RemovePickup(item);
            if (!string.IsNullOrEmpty(persistentId))
                collectedPickups.Add(persistentId);
            effects?.PlayPickupTrail(itemPosition, worker.transform);
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.5f, displayName + " +" + quantity, Color.white);
            return true;
        }

        public bool Perform(Func<bool> operation)
        {
            if (AwaitingWorldCommand)
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

        private void ShowLevelUp(int previousLevel)
        {
            if (State.Level <= previousLevel)
                return;
            effects?.PlayLevelUp(worker.transform.position);
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 2.1f, "LEVEL UP", new Color(1f, 0.88f, 0.5f));
        }

        public void Sleep(bool paidRoom)
        {
            if (AwaitingWorldCommand)
                return;
            if (!State.TrySleep(paidRoom, IsOnline))
            {
                hud.ShowToast(State.LastMessage);
                return;
            }

            BeginDay(resetResources: !IsOnline);
            hud.CloseDialogue();
            hud.ShowToast(State.LastMessage);
            SaveGame(false);
        }

        private void BeginDay(bool resetResources = true)
        {
            var rest = SurfacePosition(restPoint.position.x, restPoint.position.z);
            if (NavMesh.SamplePosition(rest, out var point, 2f, NavMesh.AllAreas))
                worker.WarpTo(point.position);
            else
                worker.Cancel();
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
            if (IsOnline)
            {
                hud.CloseDialogue();
                hud.ToggleWindow("era");
                return;
            }
            if (!State.TryAdvanceEra())
            {
                hud.ShowToast(State.LastMessage);
                return;
            }
            FinishTransition(reborn: true, eraChanged: true);
            hud.ShowToast(State.LastMessage);
            SaveGame(false);
        }

        private void FinishTransition(bool reborn, bool eraChanged)
        {
            hud.CancelItemDrag();
            worker.Cancel();
            effects?.Clear();
            hud.ClearFloatingText();
            if (reborn)
            {
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
                var agent = worker.GetComponent<NavMeshAgent>();
                agent.enabled = false;
                navigation.Activate(useBakedData: false);
                agent.enabled = true;
            }
            ResetDailyResources();
            var spawn = SurfacePosition(spawnPoint.position.x, spawnPoint.position.z);
            worker.ResetWorker(NavMesh.SamplePosition(spawn, out var point, 2f, NavMesh.AllAreas) ? point.position : spawn);
            lighting?.SetDate(State.Day, State.Minute);
            hud.CloseDialogue();
            hud.RefreshWindows();
            if (reborn)
                OpenGuideIfNeeded();
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
            HarvestableCrop nearest = null;
            float distance = float.MaxValue;
            foreach (var crop in allCrops)
            {
                if (!crop.CanInteract || crop.Definition.Skill != skill || !State.HasTool(crop.Definition.RequiredTool?.Id))
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
            if (GroundTerrain != null)
                point.y = GroundTerrain.SampleHeight(point) + GroundTerrain.transform.position.y;
            return point;
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

            if (!float.IsNaN(restoredWorld.playerX) && !float.IsNaN(restoredWorld.playerZ) && !float.IsInfinity(restoredWorld.playerX) && !float.IsInfinity(restoredWorld.playerZ) && NavMesh.SamplePosition(SurfacePosition(restoredWorld.playerX, restoredWorld.playerZ), out var position, 2f, NavMesh.AllAreas))
                worker.WarpTo(position.position);
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
                                animal.RestoreEncounter(record.health, record.lootTaken);
                                break;
                            }
            if (restoredWorld.drops != null)
                foreach (var drop in restoredWorld.drops)
                {
                    if (drop == null || float.IsNaN(drop.x) || float.IsNaN(drop.z) || float.IsInfinity(drop.x) || float.IsInfinity(drop.z))
                        continue;
                    GroundPickup authored = null;
                    if (!string.IsNullOrEmpty(drop.id))
                        foreach (var existing in pickups)
                            if (existing != null && existing.PersistentId == drop.id)
                            {
                                authored = existing;
                                break;
                            }

                    var end = SurfacePosition(drop.x, drop.z) + Vector3.up * .08f;
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

        internal VillageSave CaptureSave()
        {
            var save = State.Export();
            save.playerX = worker.transform.position.x;
            save.playerZ = worker.transform.position.z;
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
                    animalStates.Add(new AnimalStateSave { id = animal.PersistentId, health = animal.Health, lootTaken = animal.LootTaken });
            save.animalStates = animalStates.ToArray();
            var ground = new List<GroundDropSave>();
            foreach (var item in pickups)
                if (item != null && item.isActiveAndEnabled)
                    ground.Add(new GroundDropSave { id = item.PersistentId, quantity = item.Quantity, itemId = item.ItemId, x = item.LandingPosition.x, z = item.LandingPosition.z });
            save.drops = ground.ToArray();
            save.collectedPickupIds = new List<string>(collectedPickups).ToArray();
            return save;
        }

        private void Update()
        {
            if (!initialized)
                return;
            hud.SetCommandPending(AwaitingWorldCommand);
            if (AwaitingWorldCommand)
                return;
            if (equipmentVersion != State.Version)
            {
                equipmentVersion = State.Version;
                player.ShowEquipment(State.Equipment, settings);
            }

            playerPortrait?.Show(hud.InventoryOpen ? player : null);
            bool rested = IsOnline ? State.RecoverInSharedWorld() : State.AdvanceTime(Time.deltaTime, hud.HasModal);
            if (rested)
            {
                BeginDay(resetResources: !IsOnline);
                hud.CloseDialogue();
                hud.ShowToast(State.LastMessage);
                SaveGame(false);
            }

            if (Time.unscaledTime >= nextSave && (IsOnline || usesSelectedWorld || loadSavedGame && !IsAppearanceTrial))
                SaveGame(false);
            lighting?.SetDate(State.Day, State.Minute);
        }

        private void OnDestroy()
        {
            Session?.Detach(this);
            if (worker != null)
            {
                worker.Cancel();
                worker.GetComponent<NavMeshAgent>().enabled = false;
            }

            foreach (var crop in allCrops)
                if (crop != null)
                    crop.Harvested -= DropHarvest;
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
