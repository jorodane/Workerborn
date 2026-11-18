using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

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
        private Camera portraitCamera;
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
        private Light sun;
        [SerializeField]
        private Light[] lanterns;
        [SerializeField]
        private bool loadSavedGame = true;
        [SerializeField]
        private PointerTargeting targeting;
        [SerializeField]
        private NpcPortrait npcPortrait;
        [SerializeField]
        private RenderTexture npcPortraitTexture;
        public PointerTargeting Targeting => targeting;
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
            navigation.Activate();
            var agent = worker.GetComponent<NavMeshAgent>();
            agent.enabled = true;
            if (NavMesh.SamplePosition(spawnPoint.position, out var initialPosition, 2f, NavMesh.AllAreas))
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
            hud.Initialize(this, settings.InterfaceFont);
            targeting?.Initialize(this, hud.TargetLink);
            worldCamera.GetComponent<CameraVisibility>()?.Initialize(player.transform);
            cameraRig.Initialize(worker.transform, this);
            RestoreWorldObjects();
            initialized = true;
            nextSave = Time.unscaledTime + 15f;
            if (!string.IsNullOrEmpty(notice))
                hud.ShowToast(notice);
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

            driver.Initialize(actor.Animator, definition.Idle, idleOnly ? definition.Idle : definition.Walk, idleOnly ? definition.Idle : definition.Work);
        }

        private void ApplyWorldStage()
        {
            foreach (var stage in stages)
                stage.Apply(State.FarmLevel);
            crops.Clear();
            foreach (var crop in allCrops)
                if (crop.gameObject.activeInHierarchy)
                    crops.Add(crop);
        }

        public VillageInteractable FindInteraction(VillageRole role)
        {
            foreach (var npc in npcs)
                if (npc.Role == role)
                    return npc;
            return null;
        }

        public void OpenConversation(VillageInteractable target)
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

            hud.OpenDialogue(target);
        }

        public bool CanHarvest()
        {
            if (State.Energy >= State.HarvestEnergyCost)
                return true;
            hud.ShowToast("에너지가 부족합니다.");
            return false;
        }

        private void DropHarvest(HarvestableCrop crop)
        {
            bool lucky = UnityEngine.Random.value < 0.1f + State.SkillRank * 0.02f;
            int level = State.Level;
            if (!State.TryHarvest(crop.Definition.ItemsPerHarvest, lucky, out int count))
                return;
            pickups.RemoveAll(item => item == null);
            for (int i = 0; i < count; i++)
            {
                float angle = (-100f + (i + 0.5f) * 160f / count) * Mathf.Deg2Rad;
                Vector3 end = crop.transform.position + new Vector3(Mathf.Sin(angle) * 1.05f, 0f, -0.9f - Mathf.Cos(angle) * 0.25f);
                end.y = 0.08f;
                SpawnPickup(crop.Definition, crop.transform.position + Vector3.up * 0.5f, end, false);
            }

            if (lucky)
                hud.ShowFloatingText(crop.transform.position + Vector3.up, "LUCKY BONUS", new Color(1f, 0.88f, 0.5f));
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.8f, "+" + State.Rules.HarvestExperience + " EXP", Color.white);
            if (State.Level > level)
                hud.ShowFloatingText(worker.transform.position + Vector3.up * 2.3f, "LEVEL UP", new Color(1f, 0.88f, 0.5f));
        }

        private void SpawnPickup(CropDefinition definition, Vector3 start, Vector3 end, bool settled)
        {
            var prefab = definition.PickupPrefab != null ? definition.PickupPrefab : settings.FindItem(definition.ItemId)?.GroundPrefab;
            if (prefab == null)
            {
                Debug.LogError("Assign a ground-item prefab to " + definition.name, this);
                return;
            }

            var item = Instantiate(prefab, droppedItems);
            item.Initialize(definition, start, end, settled);
            pickups.Add(item);
        }

        public bool TryCollectPickup(GroundPickup item)
        {
            if (!item.CanCollect)
                return false;
            string id = item.ItemId;
            if (!State.Inventory.CanAdd(id, item.Quantity))
            {
                hud.ShowToast("소지품 공간이 부족합니다.");
                return false;
            }

            if (!item.TryCollect(() => State.Inventory.TryAdd(id, item.Quantity)))
                return false;
            if (!string.IsNullOrEmpty(item.PersistentId))
                collectedPickups.Add(item.PersistentId);
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.5f, item.DisplayName + " +" + item.Quantity, Color.white);
            return true;
        }

        public bool Perform(Func<bool> operation)
        {
            int level = State.Level;
            bool success = operation();
            if (State.Level > level)
                hud.ShowFloatingText(worker.transform.position + Vector3.up * 2.1f, "LEVEL UP", new Color(1f, 0.88f, 0.5f));
            hud.ShowToast(State.LastMessage);
            hud.RefreshWindows();
            if (success)
                SaveGame(false);
            return success;
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
            worker.WarpTo(restPoint.position);
            foreach (var crop in allCrops)
                crop.ResetHarvests();
        }

        public void Rebirth()
        {
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
            foreach (var crop in allCrops)
                crop.ResetHarvests();
            worker.ResetWorker(spawnPoint.position);
            hud.CloseDialogue();
            hud.RefreshWindows();
            hud.ShowToast(State.LastMessage);
            SaveGame(false);
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
            foreach (var crop in crops)
            {
                if (crop.IsDepleted)
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

            if (!float.IsNaN(restoredWorld.playerX) && !float.IsNaN(restoredWorld.playerZ) && !float.IsInfinity(restoredWorld.playerX) && !float.IsInfinity(restoredWorld.playerZ) && NavMesh.SamplePosition(new Vector3(restoredWorld.playerX, 0f, restoredWorld.playerZ), out var position, 1f, NavMesh.AllAreas))
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

                    var end = new Vector3(drop.x, .08f, drop.z);
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
                State.Equipment.TryGetValue(EquipmentSlot.MainHand, out string equipped);
                player.ShowEquipment(equipped, settings.FindItem(equipped)?.EquipmentPart);
            }

            if (portraitCamera != null)
                portraitCamera.enabled = hud.InventoryOpen;
            if (State.AdvanceTime(Time.deltaTime, hud.HasModal))
            {
                BeginDay();
                hud.CloseDialogue();
                hud.ShowToast(State.LastMessage);
                SaveGame(false);
            }

            if (Time.unscaledTime >= nextSave)
                SaveGame(false);
            float daylight = Mathf.Clamp01(Mathf.Sin((State.Minute - 360f) / 1440f * Mathf.PI * 2f) * 2f);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(15f, 150f, State.Minute / 1440f), -35f, 0f);
                sun.intensity = Mathf.Lerp(0.15f, 1.5f, daylight);
            }

            worldCamera.backgroundColor = Color.Lerp(new Color(0.15f, 0.2f, 0.3f), new Color(0.75f, 0.84f, 0.82f), daylight);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.24f, 0.29f, 0.41f), new Color(0.79f, 0.84f, 0.77f), daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.15f, 0.22f, 0.29f), new Color(0.54f, 0.61f, 0.49f), daylight);
            if (lanterns != null)
                foreach (var lamp in lanterns)
                    if (lamp != null)
                        lamp.enabled = daylight < 0.3f;
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
