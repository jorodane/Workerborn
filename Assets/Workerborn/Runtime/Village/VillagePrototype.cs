using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    // Scene objects are authored and saved in Unity. This component binds them to session state.
    [DefaultExecutionOrder(100)]
    public sealed class VillagePrototype : MonoBehaviour
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
        private readonly List<VillageInteractable> npcs = new List<VillageInteractable>();
        private readonly List<GroundPickup> pickups = new List<GroundPickup>();
        private VillageFarmSite[] farmSites;
        private Vector3 originalFarmPosition;
        private bool restoreCameraVisibility;
        private VillageWorldStage[] stages;
        private VillageSave restoredWorld;
        private float nextSave;
        private bool initialized;
        private int equipmentVersion = -1;
        public VillageState State { get; private set; }
        public VillageSettings Settings => settings;
        public CharacterActor PlayerActor => player;
        public VillageHud Hud => hud;
        public HarvestWorker Worker => worker;
        public Camera WorldCamera => worldCamera;
        public VillageCameraRig CameraRig => cameraRig;
        public Camera MinimapCamera => minimapCamera;
        public RenderTexture MinimapTexture => minimapTexture;
        public RenderTexture PortraitTexture => portraitTexture;
        public IReadOnlyList<HarvestableCrop> Crops => crops;
        public IReadOnlyList<VillageInteractable> Interactables => npcs;
        public bool ControlsBlocked => hud != null && (hud.HasModal || hud.IsHoldingItem);

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
            if (settings == null || mapRoot == null || navigation == null || player == null || worker == null || worldCamera == null || cameraRig == null || hud == null || spawnPoint == null || restPoint == null || droppedItems == null)
            {
                Debug.LogError("Village scene references are incomplete. Assign them in the VillagePrototype Inspector.", this);
                enabled = false;
                return;
            }

            allCrops.AddRange(mapRoot.GetComponentsInChildren<HarvestableCrop>(true));
            npcs.AddRange(mapRoot.GetComponentsInChildren<VillageInteractable>(true));
            stages = mapRoot.GetComponentsInChildren<VillageWorldStage>(true);
            farmSites = mapRoot.GetComponentsInChildren<VillageFarmSite>(true);
            if (farm != null)
                originalFarmPosition = farm.position;
            var rules = settings.CreateRules(FindInteraction(VillageRole.Farmer)?.Definition);
            State = new VillageState(rules);
            string notice = null;
            if (loadSavedGame && VillageSaveStore.TryLoad(out var savedState, out var savedWorld, out notice, rules))
            {
                State = savedState;
                restoredWorld = savedWorld;
            }

            foreach (var crop in allCrops)
            {
                crop.EnsureInitialized();
                crop.Harvested += DropHarvest;
            }

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
                    pickups.Add(item);
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
            nextSave = Time.unscaledTime + 15f;
            if (!string.IsNullOrEmpty(notice))
                hud.ShowToast(notice);
        }

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
            return Perform(() => State.TryChooseFarmSite(site.SiteId, site.DisplayName));
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
            if (State.Energy >= State.WorkEnergyCost(work.ItemId, BaseWorkCost(work)))
                return true;
            hud.ShowToast("스태미나가 부족합니다.");
            return false;
        }

        private void DropHarvest(HarvestableCrop crop)
        {
            var work = crop.Definition;
            var outcome = work.RollOutcome(State.SkillRank, UnityEngine.Random.value, UnityEngine.Random.value);
            int level = State.Level;
            if (!CanHarvest(crop))
                return;
            if (!State.TryGather(work.ItemId, work.ItemsPerHarvest, BaseWorkCost(work), outcome, out int count))
            {
                hud.ShowFloatingText(crop.transform.position + Vector3.up, "실패", new Color(1f, .65f, .55f));
                return;
            }
            var prefab = work.PickupPrefab != null ? work.PickupPrefab : settings.FindItem(work.ItemId)?.GroundPrefab;
            if (prefab != null)
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
                Debug.LogError("Assign a ground-item prefab to " + work.name, this);

            if (outcome == WorkOutcome.Lucky || outcome == WorkOutcome.GreatLucky)
                hud.ShowFloatingText(crop.transform.position + Vector3.up, outcome == WorkOutcome.GreatLucky ? "대박 보너스" : "럭키 보너스", new Color(1f, 0.88f, 0.5f));
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.8f, "+" + State.Rules.HarvestExperience + " EXP", Color.white);
            ShowLevelUp(level);
        }

        private void SpawnPickup(GroundPickup prefab, CropDefinition definition, int quantity, Vector3 start, Vector3 end)
        {
            pickups.RemoveAll(item => item == null);
            var item = Instantiate(prefab, droppedItems);
            item.SetQuantity(quantity);
            item.Initialize(definition, start, end);
            if (effects != null)
            {
                item.Landed += effects.PlayLanding;
                effects.PlayDropTrail(item);
            }
            pickups.Add(item);
        }

        public void ShowClick(Vector3 position) => effects?.PlayClick(SurfacePosition(position.x, position.z) + Vector3.up * .04f);

        internal GroundPickup DropPrefabFor(InventoryEntry entry)
        {
            if (entry != null &&
                settings.FindItem(entry.ItemId)?.GroundPrefab is GroundPickup prefab &&
                prefab.ItemId == entry.ItemId)
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
            if (!State.Inventory.CanAdd(id, item.Quantity))
            {
                hud.ShowToast("소지품 공간이 부족합니다.");
                return false;
            }

            Vector3 itemPosition = item.VisualPosition + Vector3.up * .08f;
            if (!item.TryCollect(() => State.Inventory.TryAdd(id, item.Quantity)))
                return false;
            if (!string.IsNullOrEmpty(item.PersistentId))
                collectedPickups.Add(item.PersistentId);
            effects?.PlayPickupTrail(itemPosition, worker.transform);
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.5f, item.DisplayName + " +" + item.Quantity, Color.white);
            return true;
        }

        public bool Perform(Func<bool> operation)
        {
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
            if (!State.TrySleep(paidRoom))
            {
                hud.ShowToast(State.LastMessage);
                return;
            }

            BeginDay();
            hud.CloseDialogue();
            hud.ShowToast(State.LastMessage);
            SaveGame(false);
        }

        private void BeginDay()
        {
            var rest = SurfacePosition(restPoint.position.x, restPoint.position.z);
            if (NavMesh.SamplePosition(rest, out var point, 2f, NavMesh.AllAreas))
                worker.WarpTo(point.position);
            foreach (var crop in allCrops)
                crop.ResetHarvests();
            lighting?.SetDate(State.Day, State.Minute);
        }

        public void Rebirth()
        {
            bool farmWasAvailable = State.FarmAvailable;
            int previousFarmLevel = State.FarmLevel;
            if (!State.TryRebirth())
            {
                hud.ShowToast(State.LastMessage);
                return;
            }

            hud.CancelItemDrag();
            worker.Cancel();
            foreach (var item in pickups)
                if (item != null)
                {
                    item.gameObject.SetActive(false);
                    Destroy(item.gameObject);
                }

            pickups.Clear();
            collectedPickups.Clear();
            ApplyWorldStage();
            if (farmWasAvailable != State.FarmAvailable || previousFarmLevel != State.FarmLevel)
            {
                var agent = worker.GetComponent<NavMeshAgent>();
                agent.enabled = false;
                navigation.Activate(useBakedData: false);
                agent.enabled = true;
            }
            foreach (var crop in allCrops)
                crop.ResetHarvests();
            var spawn = SurfacePosition(spawnPoint.position.x, spawnPoint.position.z);
            worker.ResetWorker(NavMesh.SamplePosition(spawn, out var point, 2f, NavMesh.AllAreas) ? point.position : spawn);
            lighting?.SetDate(State.Day, State.Minute);
            hud.CloseDialogue();
            hud.RefreshWindows();
            hud.ShowToast(State.LastMessage);
            SaveGame(false);
            OpenGuideIfNeeded();
        }

        public void WalkTo(VillageRole role)
        {
            hud.CloseDialogue();
            worker.CommandInteract(FindInteraction(role));
        }

        public void HarvestNearest()
        {
            HarvestableCrop nearest = null;
            float distance = float.MaxValue;
            foreach (var crop in allCrops)
            {
                if (!crop.CanInteract || !State.HasTool(crop.Definition.RequiredTool?.Id))
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
                {
                    item.gameObject.SetActive(false);
                    Destroy(item.gameObject);
                }

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

                    var definition = settings.FindItem(drop.itemId);
                    if (definition == null || definition.GroundPrefab == null)
                        continue;
                    var item = Instantiate(definition.GroundPrefab, droppedItems);
                    item.SetQuantity(drop.quantity);
                    item.Initialize(drop.itemId == potato.ItemId ? potato : null, end, end, true);
                    pickups.Add(item);
                }
        }

        public bool SaveGame(bool notify)
        {
            if (!initialized)
                return false;
            if (!loadSavedGame)
            {
                if (notify)
                    hud.ShowToast("이 세션은 저장을 사용하지 않습니다.");
                return false;
            }
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
            var ground = new List<GroundDropSave>();
            foreach (var item in pickups)
                if (item != null && item.isActiveAndEnabled)
                    ground.Add(new GroundDropSave { id = item.PersistentId, quantity = item.Quantity, itemId = item.ItemId, x = item.LandingPosition.x, z = item.LandingPosition.z });
            save.drops = ground.ToArray();
            save.collectedPickupIds = new List<string>(collectedPickups).ToArray();
            bool success = VillageSaveStore.TrySave(save, out _);
            if (notify || !success)
                hud.ShowToast(success ? "저장했습니다." : "저장에 실패했습니다.");
            nextSave = Time.unscaledTime + 15f;
            return success;
        }

        private void Update()
        {
            if (!initialized)
                return;
            if (equipmentVersion != State.Version)
            {
                equipmentVersion = State.Version;
                player.ShowEquipment(State.Equipment, settings);
            }

            playerPortrait?.Show(hud.InventoryOpen ? player : null);
            if (State.AdvanceTime(Time.deltaTime, hud.HasModal))
            {
                BeginDay();
                hud.CloseDialogue();
                hud.ShowToast(State.LastMessage);
                SaveGame(false);
            }

            if (Time.unscaledTime >= nextSave)
                SaveGame(false);
            lighting?.SetDate(State.Day, State.Minute);
        }

        private void OnDestroy()
        {
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
            if (initialized)
                SaveGame(false);
        }
    }
}
