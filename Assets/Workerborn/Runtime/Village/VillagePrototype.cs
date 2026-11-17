using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed class VillagePrototype : HarvestPrototype
    {
        [Header("Village buildings and props")]
        [SerializeField] private GameObject innPrefab;
        [SerializeField] private GameObject shopPrefab;
        [SerializeField] private GameObject wellPrefab;
        [SerializeField] private GameObject marketPrefab;
        [SerializeField] private GameObject signPrefab;
        [SerializeField] private GameObject benchPrefab;
        [SerializeField] private GameObject lampPrefab;
        [SerializeField] private GameObject forgePrefab;
        [SerializeField] private GameObject farmerPrefab;
        [SerializeField] private GameObject merchantPrefab;
        [SerializeField] private GameObject innkeeperPrefab;
        [SerializeField] private Light villageSun;
        [SerializeField] private bool loadSavedGame = true;

        private readonly List<NavMeshBuildSource> blockers = new List<NavMeshBuildSource>();
        private readonly List<VillageInteractable> interactables = new List<VillageInteractable>();
        private readonly List<Light> lamps = new List<Light>();
        private Transform field;
        private Light sun;
        private VillageSave restoredWorld;
        private string loadMessage;
        private float nextSave;
        private bool initialized;
        public VillageState State { get; private set; }
        public VillageHud Hud { get; private set; }
        public IReadOnlyList<VillageInteractable> Interactables => interactables;
        public bool ControlsBlocked => Hud != null && Hud.HasModal;
        public Camera WorldCamera => worldCamera;
        protected override bool EnableResetShortcut => false;

        protected override void Awake()
        {
            State = new VillageState();
            if (loadSavedGame)
            {
                bool loadedOk = VillageSaveStore.TryLoad(out var loaded, out var savedWorld, out var notice);
                loadMessage = notice;
                if (loadedOk) { State = loaded; restoredWorld = savedWorld; }
            }
            base.Awake();
            if (Worker == null) return;
            worldCamera.gameObject.AddComponent<VillageCameraRig>().Initialize(Worker.transform);
            sun = villageSun != null ? villageSun : RenderSettings.sun;
            RestoreWorldObjects();
            initialized = true;
            nextSave = Time.unscaledTime + 15f;
            Hud.ShowToast(loadMessage ?? "솔바람 마을에 도착했어요. 서쪽 농장의 미라에게 말을 걸어 보세요.");
        }

        protected override void BuildFarm()
        {
            world.name = "Solbaram Village";
            Primitive("Village meadow", PrimitiveType.Cube, world, new Vector3(-3f, -0.35f, 5f),
                new Vector3(55f, 0.7f, 43f), ColorFromHex(0x91AA72));
            Primitive("Village earth", PrimitiveType.Cube, world, new Vector3(-3f, -0.95f, 5f),
                new Vector3(55.4f, 0.5f, 43.4f), ColorFromHex(0x667D58));
            var ground = new GameObject("Village walkable ground");
            ground.transform.SetParent(world, false);
            var collider = ground.AddComponent<BoxCollider>();
            collider.center = new Vector3(-3f, -0.1f, 5f);
            collider.size = new Vector3(55f, 0.2f, 43f);
            Path("Village square", new Vector3(0f, 0.016f, 1f), new Vector3(11f, 0.025f, 10f));
            Path("Farm road", new Vector3(-10f, 0.015f, 1f), new Vector3(19f, 0.025f, 2.8f));
            Path("Inn lane", new Vector3(8f, 0.015f, -0.5f), new Vector3(15f, 0.025f, 2.5f));
            Path("Shrine lane", new Vector3(-1f, 0.015f, 11f), new Vector3(2.6f, 0.025f, 18f));
            Path("Market lane", new Vector3(7f, 0.015f, 10f), new Vector3(13f, 0.025f, 2.4f));

            Building(housePrefab, "Farmer's home", new Vector3(-22f, 0f, 5.7f), 4.1f, 110f, new Vector2(5f, 4.5f));
            Building(innPrefab, "Willow Inn", new Vector3(10f, 0f, 5.8f), 5.2f, 180f, new Vector2(6f, 5f));
            Building(shopPrefab, "Noah's shop", new Vector3(3f, 0f, 12.7f), 4.8f, 180f, new Vector2(5f, 4f));
            Building(housePrefab, "Village cottage", new Vector3(-9f, 0f, 20f), 4.1f, 160f, new Vector2(5f, 4f));
            Building(innPrefab, "Craft workshop", new Vector3(15f, 0f, 14.5f), 4.2f, 180f, new Vector2(5f, 4f));
            AddScenery(forgePrefab, "Village forge", new Vector3(13f, 0f, 10.6f), 1.7f, 180f);
            AddBlocker(new Vector3(13f, 1f, 10.6f), new Vector3(2f, 3f, 1.5f));
            AddScenery(marketPrefab, "Market stall", new Vector3(7.8f, 0f, 10f), 2.4f, 180f);
            AddBlocker(new Vector3(7.8f, 1f, 10f), new Vector3(2.5f, 3f, 1.4f));
            AddScenery(benchPrefab, "Square bench", new Vector3(4.7f, 0f, -2f), 0.9f, 15f);
            AddScenery(benchPrefab, "Shrine bench", new Vector3(-4f, 0f, 15.5f), 0.9f, 90f);
            AddScenery(cratePrefab, "Farm supplies", new Vector3(-20f, 0f, 0f), 0.7f, 10f);
            AddScenery(cratePrefab, "Shop stock", new Vector3(5.6f, 0f, 8.2f), 0.65f, 5f);

            CreateNpc(VillageRole.Farmer, "미라", "농장주 · 아르바이트", farmerPrefab, new Vector3(-6.7f, 0f, 2.4f));
            CreateNpc(VillageRole.Merchant, "노아", "잡화점 · 도구와 식료품", merchantPrefab, new Vector3(2.8f, 0f, 8.2f));
            CreateNpc(VillageRole.Innkeeper, "레나", "여관 · 휴식과 수면", innkeeperPrefab, new Vector3(9.2f, 0f, 1.8f));
            CreateObjectInteraction(VillageRole.NoticeBoard, "마을 게시판", "일거리와 마을 소식", signPrefab, new Vector3(-2.6f, 0f, 2f), 1.8f);
            CreateObjectInteraction(VillageRole.Shrine, "기억의 샘", "이전 삶과 다음 삶", wellPrefab, new Vector3(-1f, 0f, 18f), 1.4f);
            var orb = Primitive("Memory light", PrimitiveType.Sphere, world, new Vector3(-1f, 2f, 18f),
                Vector3.one * 0.4f, ColorFromHex(0xA6DCCB));
            CreateRing("Shrine circle", world, 1.4f, ColorFromHex(0xC3DED0)).transform.position = new Vector3(-1f, 0.07f, 18f);
            var glow = orb.AddComponent<Light>(); glow.type = LightType.Point; glow.range = 4f; glow.intensity = 1.5f; glow.color = ColorFromHex(0xA6DCCB);

            foreach (var position in new[] { new Vector3(-5f, 0f, -2f), new Vector3(6f, 0f, 2f), new Vector3(-3f, 0f, 11f), new Vector3(12f, 0f, -1f) })
            {
                AddScenery(lampPrefab, "Village lantern", position, 2.5f, 0f);
                var lamp = new GameObject("Lantern light").AddComponent<Light>();
                lamp.transform.SetParent(world, false); lamp.transform.position = position + Vector3.up * 2.3f;
                lamp.type = LightType.Point; lamp.range = 5f; lamp.intensity = 2f; lamp.color = ColorFromHex(0xFFD8A2);
                lamps.Add(lamp);
            }
            foreach (var position in new[] { new Vector3(-25f, 0f, 15f), new Vector3(-24f, 0f, -7f), new Vector3(18f, 0f, -5f), new Vector3(18f, 0f, 22f), new Vector3(-17f, 0f, 22f), new Vector3(8f, 0f, 22f) })
                AddScenery(treePrefab, "Village tree", position, 4.5f, position.x * 10f);
            BuildField();
        }

        private void Path(string label, Vector3 position, Vector3 size)
        {
            Primitive(label, PrimitiveType.Cube, world, position, size, ColorFromHex(0xC9BF95));
        }

        private void BuildField()
        {
            if (field != null) { field.gameObject.SetActive(false); Destroy(field.gameObject); }
            crops.Clear();
            field = new GameObject(State.FarmLevel == 1 ? "Small potato field" : "Expanded potato field").transform;
            field.SetParent(world, false);
            int fieldRows = State.FarmLevel == 1 ? 3 : 6;
            for (int row = 0; row < fieldRows; row++)
            {
                float z = 0.4f + row * 2.8f;
                Primitive("Tilled soil", PrimitiveType.Cube, field, new Vector3(-13f, 0.03f, z),
                    new Vector3(11.5f, 0.06f, 2f), ColorFromHex(0x806047));
                for (int column = 0; column < 4; column++)
                {
                    BuildCrop(new Vector3(-13f + (column - 1.5f) * 2.6f, 0.065f, z), row * 4 + column + 1);
                    crops[crops.Count - 1].transform.SetParent(field, true);
                }
            }
            if (State.FarmLevel > 1)
                AddScenery(marketPrefab, "Community harvest stall", new Vector3(-6.5f, 0f, 13.5f), 2.3f, 90f);
        }

        private void Building(GameObject prefab, string label, Vector3 position, float height, float yaw, Vector2 footprint)
        {
            AddScenery(prefab, label, position, height, yaw);
            AddBlocker(position + Vector3.up, new Vector3(footprint.x, 5f, footprint.y));
        }

        private void AddBlocker(Vector3 center, Vector3 size)
        {
            blockers.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one), size = size, area = 1 });
        }

        private void CreateNpc(VillageRole role, string name, string subtitle, GameObject prefab, Vector3 position)
        {
            var root = NewInteraction(role, name, subtitle, position);
            var visual = Instantiate(prefab != null ? prefab : heroPrefab, root.transform);
            PrepareVisual(visual, 1.7f);
            foreach (var child in visual.GetComponentsInChildren<Transform>())
            {
                string lower = child.name.ToLowerInvariant();
                if (lower.Contains("sword") || lower.Contains("bow_") || lower.Contains("arrow_") || lower.Contains("knife_"))
                    child.gameObject.SetActive(false);
            }
            var animator = visual.GetComponentInChildren<Animator>();
            if (animator != null) root.AddComponent<WorkerAnimation>().Initialize(animator, idleClip, idleClip, idleClip);
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = Vector3.up * 0.9f; collider.height = 1.9f; collider.radius = 0.5f; collider.isTrigger = true;
            AddBlocker(position + Vector3.up, new Vector3(0.7f, 3f, 0.7f));
        }

        private void CreateObjectInteraction(VillageRole role, string name, string subtitle, GameObject prefab, Vector3 position, float height)
        {
            var root = NewInteraction(role, name, subtitle, position);
            if (prefab != null) PrepareVisual(Instantiate(prefab, root.transform), height);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = Vector3.up * 0.8f; collider.size = new Vector3(1.3f, 1.7f, 1.3f); collider.isTrigger = true;
            AddBlocker(position + Vector3.up, new Vector3(0.6f, 3f, 0.6f));
        }

        private GameObject NewInteraction(VillageRole role, string name, string subtitle, Vector3 position)
        {
            var root = new GameObject(name);
            root.transform.SetParent(world, false); root.transform.position = position;
            var interactable = root.AddComponent<VillageInteractable>();
            interactable.Initialize(role, name, subtitle);
            interactables.Add(interactable);
            return root;
        }

        protected override void BuildNavigation()
        {
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(new Vector3(-3f, -0.1f, 5f), Quaternion.identity, Vector3.one),
                    size = new Vector3(51f, 0.2f, 39f), area = 0 }
            };
            sources.AddRange(blockers);
            navigationData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(new Vector3(-3f, 0f, 5f), new Vector3(55f, 10f, 43f)), Vector3.zero, Quaternion.identity);
            if (navigationData == null) throw new InvalidOperationException("Village navigation could not be built.");
            navigation = NavMesh.AddNavMeshData(navigationData);
        }

        protected override void BuildWorker()
        {
            base.BuildWorker();
            Worker.BindVillage(this);
        }

        protected override void BuildHud()
        {
            Hud = new GameObject("Village interface", typeof(RectTransform)).AddComponent<VillageHud>();
            Hud.transform.SetParent(transform, false);
            Hud.Initialize(this, interfaceFont);
        }

        public VillageInteractable FindInteraction(VillageRole role)
        {
            foreach (var target in interactables) if (target.Role == role) return target;
            return null;
        }

        public void OpenConversation(VillageInteractable target)
        {
            Worker.Cancel();
            if (target.Role == VillageRole.Farmer) State.MeetFarmer();
            if (target.Role == VillageRole.Farmer || target.Role == VillageRole.Merchant || target.Role == VillageRole.Innkeeper)
            {
                Vector3 direction = Worker.transform.position - target.transform.position; direction.y = 0f;
                if (direction.sqrMagnitude > 0.01f) target.transform.rotation = Quaternion.LookRotation(direction);
            }
            Hud.OpenDialogue(target);
        }

        public bool CanHarvest()
        {
            if (State.Energy >= State.HarvestEnergyCost) return true;
            Hud.ShowToast("에너지가 부족해요. 빵을 먹거나 여관에서 쉬어 주세요.");
            return false;
        }

        protected override void DropHarvest(HarvestableCrop crop)
        {
            bool lucky = UnityEngine.Random.value < 0.1f + State.SkillRank * 0.02f;
            int oldLevel = State.Level;
            if (!State.TryHarvest(crop.Definition.ItemsPerHarvest, lucky, out int amount)) return;
            SpawnDrops(crop, amount);
            if (lucky) Hud.ShowFloatingText(crop.transform.position + Vector3.up, "LUCKY BONUS!", ColorFromHex(0xFFE39A));
            Hud.ShowFloatingText(Worker.transform.position + Vector3.up * 1.8f, "+8 EXP", ColorFromHex(0xDAF1CB));
            if (State.Level > oldLevel) Hud.ShowFloatingText(Worker.transform.position + Vector3.up * 2.3f, "LEVEL UP!  AP +2", ColorFromHex(0xFFE39A));
        }

        public bool TryCollectPickup(GroundPickup item)
        {
            if (!item.CanCollect) return false;
            string id = item.Definition.ItemId;
            if (!State.Inventory.CanAdd(id, 1)) { Hud.ShowToast("가방이 가득 찼어요. 감자는 바닥에 남겨 두었어요."); return false; }
            if (!item.TryCollect()) return false;
            State.Inventory.TryAdd(id, 1);
            Hud.ShowToast($"{item.Definition.DisplayName} +1  ·  가방에 담았어요.");
            Hud.ShowFloatingText(Worker.transform.position + Vector3.up * 1.5f, $"{item.Definition.DisplayName} +1", ColorFromHex(0xF4D99A));
            return true;
        }

        public bool Perform(Func<bool> operation)
        {
            int oldLevel = State.Level;
            bool success = operation();
            if (State.Level > oldLevel) Hud.ShowFloatingText(Worker.transform.position + Vector3.up * 2.1f, "LEVEL UP!  AP +2", ColorFromHex(0xFFE39A));
            Hud.ShowToast(State.LastMessage);
            Hud.RefreshWindows();
            if (success) SaveGame(false);
            return success;
        }

        public void Sleep(bool paidRoom)
        {
            if (!State.TrySleep(paidRoom)) { Hud.ShowToast(State.LastMessage); return; }
            StartNewDay();
            Hud.CloseDialogue();
            Hud.ShowToast(State.LastMessage);
            SaveGame(false);
        }

        private void StartNewDay()
        {
            Worker.WarpTo(new Vector3(9.2f, 0f, -0.8f));
            foreach (var crop in crops) crop.ResetHarvests();
        }

        public void Rebirth()
        {
            int oldStage = State.FarmLevel;
            if (!State.TryRebirth()) { Hud.ShowToast(State.LastMessage); return; }
            Worker.Cancel();
            ResetCropsAndDrops();
            if (oldStage != State.FarmLevel) BuildField();
            Worker.ResetWorker(spawn);
            Hud.CloseDialogue();
            Hud.RefreshWindows();
            Hud.ShowToast(State.LastMessage);
            Physics.SyncTransforms();
            SaveGame(false);
        }

        public void WalkTo(VillageRole role)
        {
            Hud.CloseDialogue();
            Worker.CommandInteract(FindInteraction(role));
        }

        public void HarvestNearest()
        {
            HarvestableCrop nearest = null; float distance = float.MaxValue;
            foreach (var crop in crops)
            {
                if (crop.IsDepleted) continue;
                float candidate = (crop.transform.position - Worker.transform.position).sqrMagnitude;
                if (candidate < distance) { nearest = crop; distance = candidate; }
            }
            if (nearest != null) Worker.CommandHarvest(nearest);
            else Hud.ShowToast("오늘 수확할 작물이 없어요. 잠을 자고 나면 밭이 다시 준비됩니다.");
        }

        private void RestoreWorldObjects()
        {
            if (restoredWorld == null) return;
            if (!float.IsNaN(restoredWorld.playerX) && !float.IsNaN(restoredWorld.playerZ)
                && !float.IsInfinity(restoredWorld.playerX) && !float.IsInfinity(restoredWorld.playerZ)
                && NavMesh.SamplePosition(new Vector3(restoredWorld.playerX, 0f, restoredWorld.playerZ), out NavMeshHit playerPosition, 1f, NavMesh.AllAreas))
                Worker.WarpTo(playerPosition.position);
            if (restoredWorld.cropRemaining != null)
                for (int i = 0; i < Mathf.Min(crops.Count, restoredWorld.cropRemaining.Length); i++)
                    crops[i].RestoreRemaining(restoredWorld.cropRemaining[i]);
            if (restoredWorld.drops != null)
                for (int i = 0; i < restoredWorld.drops.Length; i++)
                {
                    var drop = restoredWorld.drops[i];
                    if (drop == null || drop.itemId != potato.ItemId || float.IsNaN(drop.x) || float.IsNaN(drop.z)
                        || float.IsInfinity(drop.x) || float.IsInfinity(drop.z) || Mathf.Abs(drop.x) > 29f || Mathf.Abs(drop.z) > 26f) continue;
                    Vector3 position = new Vector3(drop.x, 0.08f, drop.z);
                    CreatePickup(potato, position, position, true);
                }
        }

        public bool SaveGame(bool notify)
        {
            if (!initialized) return false;
            var save = State.Export();
            save.playerX = Worker.transform.position.x;
            save.playerZ = Worker.transform.position.z;
            save.cropRemaining = new int[crops.Count];
            for (int i = 0; i < crops.Count; i++) save.cropRemaining[i] = crops[i].Remaining;
            var ground = new List<GroundDropSave>();
            foreach (var item in pickups)
                if (item != null && item.isActiveAndEnabled)
                    ground.Add(new GroundDropSave { itemId = item.Definition.ItemId, x = item.LandingPosition.x, z = item.LandingPosition.z });
            save.drops = ground.ToArray();
            bool success = VillageSaveStore.TrySave(save, out _);
            if (notify || !success) Hud.ShowToast(success ? "현재 삶과 마을의 기록을 저장했어요." : "저장하지 못했어요. 저장 폴더의 여유 공간과 권한을 확인해 주세요.");
            nextSave = Time.unscaledTime + 15f;
            return success;
        }

        protected override void Update()
        {
            base.Update();
            if (!initialized) return;
            if (State.AdvanceTime(Time.deltaTime, ControlsBlocked))
            {
                StartNewDay(); Hud.CloseDialogue(); Hud.ShowToast(State.LastMessage); SaveGame(false);
            }
            if (Time.unscaledTime >= nextSave) SaveGame(false);
            float daylight = Mathf.Clamp01(Mathf.Sin((State.Minute - 360f) / 1440f * Mathf.PI * 2f) * 2f);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(15f, 150f, State.Minute / 1440f), -35f, 0f);
                sun.intensity = Mathf.Lerp(0.15f, 1.5f, daylight);
                sun.color = Color.Lerp(ColorFromHex(0xA4B9D4), ColorFromHex(0xFFF1D0), daylight);
            }
            worldCamera.backgroundColor = Color.Lerp(ColorFromHex(0x27344D), ColorFromHex(0xBFD6D1), daylight);
            RenderSettings.ambientIntensity = Mathf.Lerp(0.4f, 1f, daylight);
            RenderSettings.ambientSkyColor = Color.Lerp(ColorFromHex(0x3D4A68), ColorFromHex(0xCAD5C4), daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(ColorFromHex(0x27374A), ColorFromHex(0x8A9C7C), daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(ColorFromHex(0x1C2A33), ColorFromHex(0x576B4F), daylight);
            foreach (var lamp in lamps) lamp.enabled = daylight < 0.3f;
        }

        private void OnApplicationPause(bool paused) { if (paused && initialized) SaveGame(false); }
        private void OnApplicationQuit() { if (initialized) SaveGame(false); }
    }
}
