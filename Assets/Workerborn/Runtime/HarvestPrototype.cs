using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Workerborn
{
    // All prototype content is scoped to the dedicated scene; imported assets stay untouched.
    public class HarvestPrototype : MonoBehaviour
    {
        [SerializeField] protected CropDefinition potato;
        [SerializeField] protected GameObject heroPrefab;
        [SerializeField] protected AnimationClip idleClip;
        [SerializeField] protected AnimationClip walkClip;
        [SerializeField] protected AnimationClip workClip;
        [SerializeField] protected GameObject cratePrefab;
        [SerializeField] protected GameObject treePrefab;
        [SerializeField] protected GameObject housePrefab;
        [SerializeField] protected Material surfaceMaterial;
        [SerializeField] protected Font interfaceFont;
        [SerializeField, Range(1, 6)] private int columns = 4;
        [SerializeField, Range(1, 4)] private int rows = 3;

        protected readonly List<HarvestableCrop> crops = new List<HarvestableCrop>();
        protected readonly List<GroundPickup> pickups = new List<GroundPickup>();
        private readonly Dictionary<Color, Material> palette = new Dictionary<Color, Material>();
        protected readonly Vector3 spawn = new Vector3(0f, 0f, -4.4f);
        protected NavMeshData navigationData;
        protected NavMeshDataInstance navigation;
        protected Transform world;
        protected Camera worldCamera;
        private HarvestHud hud;
        private LineRenderer selection;
        private float cameraAspect;

        public HarvestWorker Worker { get; private set; }
        public IReadOnlyList<HarvestableCrop> Crops => crops;
        public int RemainingHarvests
        {
            get
            {
                int count = 0;
                foreach (var crop in crops) if (crop != null) count += crop.Remaining;
                return count;
            }
        }
        public int GroundItemCount
        {
            get
            {
                int count = 0;
                foreach (var item in pickups) if (item != null && item.isActiveAndEnabled) count++;
                return count;
            }
        }

        protected virtual bool EnableResetShortcut => true;

        protected virtual void Awake()
        {
            if (potato == null || potato.PlantPrefab == null || potato.DropPrefab == null || heroPrefab == null
                || surfaceMaterial == null || interfaceFont == null || idleClip == null || walkClip == null || workClip == null)
            {
                Debug.LogError("Workerborn: the harvest scene is missing a configured asset reference.", this);
                enabled = false;
                return;
            }
            worldCamera = Camera.main;
            if (worldCamera == null)
            {
                Debug.LogError("Workerborn: the harvest scene needs its Main Camera.", this);
                enabled = false;
                return;
            }
            world = new GameObject("Farm").transform;
            world.SetParent(transform, false);
            ConfigureCamera();
            BuildFarm();
            BuildNavigation();
            BuildWorker();
            BuildHud();
            selection = CreateRing("Selected crop", transform, 0.9f, ColorFromHex(0xF1CF80));
            selection.gameObject.SetActive(false);
            Physics.SyncTransforms();
        }

        protected virtual void BuildHud()
        {
            hud = new GameObject("Harvest interface", typeof(RectTransform)).AddComponent<HarvestHud>();
            hud.transform.SetParent(transform, false);
            hud.Initialize(this, worldCamera, interfaceFont);
        }

        protected virtual void ConfigureCamera()
        {
            worldCamera.orthographic = true;
            worldCamera.transform.position = new Vector3(11.5f, 16f, -14.5f);
            worldCamera.transform.LookAt(new Vector3(0f, 0f, 1.1f));
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = ColorFromHex(0xB9D3CF);
            worldCamera.nearClipPlane = 0.1f;
            worldCamera.farClipPlane = 100f;
            ResizeCamera();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.83f, 0.77f);
            RenderSettings.ambientEquatorColor = new Color(0.53f, 0.61f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.39f, 0.31f);
        }

        private void ResizeCamera()
        {
            cameraAspect = worldCamera.aspect;
            worldCamera.orthographicSize = Mathf.Max(8.6f, 13.5f / Mathf.Max(0.3f, cameraAspect));
        }

        protected virtual void BuildFarm()
        {
            Primitive("Meadow island", PrimitiveType.Cube, world, new Vector3(0f, -0.35f, 1f),
                new Vector3(24f, 0.7f, 21f), ColorFromHex(0x91A76A));
            Primitive("Island base", PrimitiveType.Cube, world, new Vector3(0f, -0.9f, 1f),
                new Vector3(24.2f, 0.55f, 21.2f), ColorFromHex(0x667E57));
            var ground = new GameObject("Walkable ground");
            ground.transform.SetParent(world, false);
            var collider = ground.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, -0.1f, 1f);
            collider.size = new Vector3(24f, 0.2f, 21f);

            Primitive("Farm approach", PrimitiveType.Cube, world, new Vector3(0f, 0.005f, -4.1f),
                new Vector3(3.5f, 0.025f, 4.2f), ColorFromHex(0xC7BA8C));
            int safeRows = Mathf.Clamp(rows, 1, 4);
            int safeColumns = Mathf.Clamp(columns, 1, 6);
            for (int row = 0; row < safeRows; row++)
            {
                float z = -1.1f + row * 2.8f;
                Primitive($"Soil bed {row + 1}", PrimitiveType.Cube, world, new Vector3(0f, 0.025f, z),
                    new Vector3(safeColumns * 2.6f + 0.6f, 0.05f, 1.9f), ColorFromHex(0x806047));
                for (int column = 0; column < safeColumns; column++)
                {
                    float x = (column - (safeColumns - 1) * 0.5f) * 2.6f;
                    BuildCrop(new Vector3(x, 0.06f, z), row * safeColumns + column + 1);
                }
            }

            // These decorative pieces are outside the navigation rectangle.
            for (int i = 0; i <= 9; i++)
            {
                float x = -9f + i * 2f;
                FencePost(new Vector3(x, 0f, 8.4f));
                if (i < 9) FenceRail(new Vector3(x + 1f, 0f, 8.4f), false);
            }
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 7; i++)
                {
                    Vector3 position = new Vector3(side * 9f, 0f, -5.6f + i * 2f);
                    FencePost(position);
                    if (i < 6) FenceRail(position + Vector3.forward, true);
                }
            AddScenery(housePrefab, "Farm cottage", new Vector3(-6.5f, 0f, 9.5f), 3.8f, 180f);
            AddScenery(treePrefab, "Apple tree", new Vector3(8f, 0f, 9.8f), 4.6f, 0f);
            AddScenery(treePrefab, "Shade tree", new Vector3(-10.5f, 0f, -0.5f), 3.3f, 30f);
            AddScenery(cratePrefab, "Harvest crate", new Vector3(-6.7f, 0f, -4.6f), 0.65f, 15f);
            AddScenery(cratePrefab, "Spare crate", new Vector3(-7.5f, 0f, -4.1f), 0.5f, -10f);
        }

        protected void BuildCrop(Vector3 position, int number)
        {
            var root = new GameObject($"Potato {number:00}");
            root.transform.SetParent(world, false);
            root.transform.position = position;
            var visual = Instantiate(potato.PlantPrefab, root.transform);
            PrepareVisual(visual, 0.95f);
            var hit = root.AddComponent<BoxCollider>();
            hit.center = new Vector3(0f, 0.45f, 0f);
            hit.size = new Vector3(1.5f, 1f, 1.4f);
            hit.isTrigger = true;
            var markers = new Renderer[potato.HarvestsPerPlant];
            for (int i = 0; i < markers.Length; i++)
            {
                var dot = Primitive("Remaining harvest", PrimitiveType.Sphere, root.transform,
                    new Vector3((i - (markers.Length - 1) * 0.5f) * 0.18f, 0.05f, -0.75f),
                    Vector3.one * 0.11f, ColorFromHex(0xE9C77D));
                markers[i] = dot.GetComponent<Renderer>();
            }
            var crop = root.AddComponent<HarvestableCrop>();
            crop.Initialize(potato, visual.transform, markers);
            crop.Harvested += DropHarvest;
            crops.Add(crop);
        }

        protected virtual void BuildNavigation()
        {
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(new Vector3(0f, -0.1f, 0.8f), Quaternion.identity, Vector3.one),
                    size = new Vector3(17.2f, 0.2f, 14.4f), area = 0
                }
            };
            var settings = NavMesh.GetSettingsByID(0);
            navigationData = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(new Vector3(0f, 0f, 1f), new Vector3(24f, 8f, 22f)), Vector3.zero, Quaternion.identity);
            if (navigationData == null) throw new System.InvalidOperationException("Workerborn farm navigation could not be built.");
            navigation = NavMesh.AddNavMeshData(navigationData);
        }

        protected virtual void BuildWorker()
        {
            var root = new GameObject("Worker");
            root.transform.SetParent(transform, false);
            root.transform.position = spawn;
            var model = Instantiate(heroPrefab, root.transform);
            PrepareVisual(model, 1.7f);
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            var driver = root.AddComponent<WorkerAnimation>();
            driver.Initialize(animator, idleClip, walkClip, workClip);
            root.AddComponent<NavMeshAgent>();
            Worker = root.AddComponent<HarvestWorker>();
            Worker.Initialize(worldCamera, driver);
            var ring = CreateRing("Worker footing", root.transform, 0.35f, ColorFromHex(0xF0E9CA));
            ring.transform.localPosition = Vector3.up * 0.04f;
        }

        protected virtual void DropHarvest(HarvestableCrop crop)
        {
            SpawnDrops(crop, crop.Definition.ItemsPerHarvest);
        }

        protected void SpawnDrops(HarvestableCrop crop, int count)
        {
            pickups.RemoveAll(item => item == null);
            for (int i = 0; i < count; i++)
            {
                // Scatter into the space between rows, so drops can be selected independently of the plant.
                float angle = (-100f + (i + 0.5f) * 160f / count) * Mathf.Deg2Rad;
                Vector3 landing = crop.transform.position
                    + new Vector3(Mathf.Sin(angle) * 1.05f, 0f, -0.9f - Mathf.Cos(angle) * 0.25f);
                landing.y = 0.08f;
                CreatePickup(crop.Definition, crop.transform.position + Vector3.up * 0.5f, landing);
            }
        }

        protected GroundPickup CreatePickup(CropDefinition definition, Vector3 origin, Vector3 landing, bool settled = false)
        {
            var root = new GameObject("Dropped potato");
            root.transform.SetParent(world, false);
            var model = Instantiate(definition.DropPrefab, root.transform);
            PrepareVisual(model, 0.32f);
            var hit = root.AddComponent<SphereCollider>();
            hit.radius = 0.38f;
            hit.center = Vector3.up * 0.15f;
            hit.isTrigger = true;
            var item = root.AddComponent<GroundPickup>();
            item.Initialize(definition, model.transform, origin, landing, settled);
            var ring = CreateRing("Pickup marker", root.transform, 0.25f, ColorFromHex(0xF4D390));
            ring.transform.localPosition = Vector3.up * 0.01f;
            pickups.Add(item);
            return item;
        }

        public void ResetFarm()
        {
            if (Worker == null) return;
            Worker.ResetWorker(spawn);
            ResetCropsAndDrops();
            hud?.ShowMessage("밭을 다시 준비했어요. 감자를 클릭해 수확을 시작해 보세요.");
        }

        protected void ResetCropsAndDrops()
        {
            foreach (var item in pickups)
            {
                if (item == null) continue;
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            pickups.Clear();
            foreach (var crop in crops) crop.ResetHarvests();
        }

        protected virtual void Update()
        {
            if (Worker == null) return;
            if (EnableResetShortcut && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetFarm();
            if (!Mathf.Approximately(cameraAspect, worldCamera.aspect)) ResizeCamera();
            var crop = Worker.FocusCrop;
            selection.gameObject.SetActive(crop != null && !crop.IsDepleted);
            if (crop != null) selection.transform.position = crop.transform.position + Vector3.up * 0.03f;
        }

        protected GameObject Primitive(string label, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var result = GameObject.CreatePrimitive(type);
            result.name = label;
            result.layer = 2;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = position;
            result.transform.localScale = scale;
            result.GetComponent<Collider>().enabled = false;
            result.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return result;
        }

        protected Material MaterialFor(Color color)
        {
            if (palette.TryGetValue(color, out Material material)) return material;
            material = new Material(surfaceMaterial) { name = "Farm surface", color = color };
            palette.Add(color, material);
            return material;
        }

        protected LineRenderer CreateRing(string label, Transform parent, float radius, Color color)
        {
            var ring = new GameObject(label).AddComponent<LineRenderer>();
            ring.gameObject.layer = 2;
            ring.transform.SetParent(parent, false);
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 48;
            ring.widthMultiplier = 0.045f;
            ring.sharedMaterial = MaterialFor(color);
            ring.generateLightingData = true;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2f / 48f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            return ring;
        }

        protected void FencePost(Vector3 position)
        {
            Primitive("Fence post", PrimitiveType.Cube, world, position + Vector3.up * 0.5f,
                new Vector3(0.18f, 1f, 0.18f), ColorFromHex(0xD6BF88));
        }

        protected void FenceRail(Vector3 position, bool alongZ)
        {
            for (int i = 0; i < 2; i++)
                Primitive("Fence rail", PrimitiveType.Cube, world, position + Vector3.up * (0.35f + i * 0.35f),
                    alongZ ? new Vector3(0.1f, 0.12f, 2f) : new Vector3(2f, 0.12f, 0.1f), ColorFromHex(0xD6BF88));
        }

        protected void AddScenery(GameObject prefab, string label, Vector3 position, float height, float yaw)
        {
            if (prefab == null) return;
            var root = new GameObject(label);
            root.transform.SetParent(world, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            PrepareVisual(Instantiate(prefab, root.transform), height);
        }

        protected static void PrepareVisual(GameObject visual, float height)
        {
            foreach (var child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = default;
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found || bounds.size.y < 0.001f) return;
            Vector3 localBottom = visual.transform.parent.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            float scale = height / bounds.size.y;
            visual.transform.localScale *= scale;
            visual.transform.localPosition -= localBottom * scale;
        }

        protected static Color ColorFromHex(uint value) => new Color(((value >> 16) & 255) / 255f,
            ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1f);

        protected virtual void OnDestroy()
        {
            // Stop the agent before removing the NavMesh it belongs to.
            if (Worker != null)
            {
                Worker.Cancel();
                Worker.GetComponent<NavMeshAgent>().enabled = false;
            }
            if (navigation.valid) navigation.Remove();
            if (navigationData != null) Destroy(navigationData);
            foreach (var material in palette.Values) Destroy(material);
        }
    }
}
