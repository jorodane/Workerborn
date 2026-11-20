using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Workerborn
{
    // A low-resolution ID/depth pass tracks visible animated silhouettes, not bounding boxes.
    [DefaultExecutionOrder(-25)]
    public sealed class PointerTargeting : MonoBehaviour
    {
        [SerializeField]
        private Material maskMaterial;
        [SerializeField, Range(256, 1536)]
        private int maskWidth = 768;
        [SerializeField, Range(10, 60)]
        private float capturesPerSecond = 30;
        [SerializeField]
        private Color highlightColor = new Color(1f, .88f, .36f, 1);
        [SerializeField, Range(.1f, 5f)]
        private float highlightWidth = 1.8f;
        [SerializeField]
        private TargetLinkView link;
        [SerializeField]
        private Color pressedColor = new Color(1f, .55f, .12f, 1f);
        private WorldInteractable pressTarget;
        private CameraVisibility visibility;
        private Terrain terrain;
        private Mesh terrainMask;
        public bool IsPressed => CurrentTarget != null && CurrentTarget == pressTarget && WorkerbornInput.Held("Gameplay/Interact");

        private VillagePrototype game;
        private RenderTexture mask;
        private Texture2D synchronous;
        private Texture2D clickPixel, exterior;
        private byte[] exteriorPixels;
        private int[] exteriorQueue;
        private int maskRevision, outlinedRevision = -1;
        private WorldInteractable outlinedTarget;
        private bool flipReadback;
        private byte[] pixels;
        private WorldInteractable[] snapshot;
        private readonly List<WorldInteractable> frameTargets = new List<WorldInteractable>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly RaycastHit[] worldHits = new RaycastHit[128];
        private readonly Plane[] maskFrustum = new Plane[6];
        private Renderer[] staticGeometry;
        private readonly List<Renderer> actorGeometry = new List<Renderer>();
        private readonly Dictionary<Renderer, int> targetIds = new Dictionary<Renderer, int>();
        private AsyncGPUReadbackRequest request;
        private bool waiting;
        private float nextCapture;
        private int capturedWidth, capturedHeight;
        private int generation;
        public WorldInteractable CurrentTarget { get; private set; }

        public void Initialize(VillagePrototype owner, TargetLinkView targetLink)
        {
            game = owner;
            link = targetLink;
            visibility = game.WorldCamera.GetComponent<CameraVisibility>();
            terrain = game.GroundTerrain;
            var geometry = new List<Renderer>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (renderer.gameObject.scene == game.gameObject.scene && renderer.gameObject.layer != VillageWeather.SkyLayer &&
                    renderer is MeshRenderer && renderer.GetComponentInParent<WorldInteractable>(true) == null &&
                    renderer.GetComponentInParent<HarvestWorker>(true) == null)
                    geometry.Add(renderer);
            staticGeometry = geometry.ToArray();
            RebuildTerrainMask();
            TerrainCallbacks.heightmapChanged += TerrainChanged;
        }

        private void TerrainChanged(Terrain changed, RectInt region, bool synced)
        {
            if (changed == terrain)
                RebuildTerrainMask();
        }

        // Selection-only depth proxy, derived from the authored Terrain. It never creates map objects.
        private void RebuildTerrainMask()
        {
            if (terrainMask != null)
                Destroy(terrainMask);
            if (terrain == null || terrain.terrainData == null)
                return;
            var data = terrain.terrainData;
            int steps = Mathf.Min(128, data.heightmapResolution - 1), side = steps + 1;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[steps * steps * 6];
            for (int z = 0; z <= steps; z++)
                for (int x = 0; x <= steps; x++)
                {
                    float u = (float)x / steps, v = (float)z / steps;
                    int i = z * side + x;
                    vertices[i] = new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v), v * data.size.z);
                    uv[i] = new Vector2(u, v);
                }

            int t = 0;
            for (int z = 0; z < steps; z++)
                for (int x = 0; x < steps; x++)
                {
                    int a = z * side + x, b = a + side;
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;
                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = b + 1;
                }

            terrainMask = new Mesh
            {
                name = "Authored terrain selection depth",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                uv = uv,
                triangles = triangles
            };
            terrainMask.RecalculateBounds();
        }

        private WorldInteractable rightTarget;

        public bool OverUi(Vector2 position) => UiAt(position) != null;

        public GameObject UiAt(Vector2 position)
        {
            if (EventSystem.current == null)
                return null;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0 ? uiHits[0].gameObject : null;
        }

        public bool TryDropPoint(Vector2 screen, out Vector3 point)
        {
            point = default;
            GameObject ui = UiAt(screen);
            if ((ui != null && ui.GetComponentInParent<WorldNameplateView>() == null) ||
                !TryWorldHit(screen, out var hit) || !game.IsGround(hit.collider))
                return false;
            point = hit.point;
            return true;
        }

        private void Update()
        {
            if (game == null || WorkerbornInput.Current == null)
                return;
            var cursor = WorkerbornInput.Point;
            var ui = UiAt(cursor);
            var plate = ui != null ? ui.GetComponentInParent<WorldNameplateView>() : null;
            bool overUi = ui != null && plate == null;
            bool control = WorkerbornInput.Held("Gameplay/Target");
            bool pressed = WorkerbornInput.Pressed("Gameplay/Interact");
            bool rightPressed = WorkerbornInput.Pressed("Camera/OrbitRight");
            WorldInteractable target = null;
            if (!game.ControlsBlocked)
            {
                if (plate != null)
                    target = plate.Target;
                else if (!overUi)
                {
                    if (control)
                        target = FindNearestLivingTarget(cursor, pressed || rightPressed);
                    else if (pressed || rightPressed)
                        target = ReadClickTarget(cursor);
                    else if (HasMask)
                        target = SnapshotTarget(TargetMaskGeometry.IdAt(pixels, mask.width, mask.height,
                            Mathf.FloorToInt(cursor.x * mask.width / Screen.width),
                            Mathf.FloorToInt(cursor.y * mask.height / Screen.height), flipReadback));
                }
            }

            if (target != null && !target.CanInteract)
                target = null;
            if (rightPressed)
            {
                rightTarget = target;
                game.Hud.CloseWorldContext();
            }
            if (WorkerbornInput.Released("Camera/OrbitRight"))
            {
                if (game.CameraRig.RightClickThisFrame)
                {
                    if (rightTarget != null)
                        game.Hud.OpenWorldContext(rightTarget, cursor);
                    else if (!overUi)
                        game.Worker.Cancel();
                }
                rightTarget = null;
            }

            if (pressed)
                pressTarget = target;
            if (!WorkerbornInput.Held("Gameplay/Interact"))
                pressTarget = null;
            CurrentTarget = target;
            if (!pressed)
                return;

            if (ui == null || ui.GetComponentInParent<WorldContextView>() == null)
                game.Hud.CloseWorldContext();
            if (game.Hud.HasModal)
                return;
            if (game.Hud.IsHoldingItem)
            {
                if (!overUi)
                    game.Hud.DropHeldOnGround(cursor);
                return;
            }
            if (target != null)
            {
                game.ShowClick(target.transform.position);
                target.RequestInteraction(game.Worker);
            }
            else if (!overUi && !control && TryWorldHit(cursor, out var hit))
                game.Worker.CommandMove(hit.point);
        }

        private WorldInteractable SnapshotTarget(int id) => snapshot != null && id > 0 && id < snapshot.Length ? snapshot[id] : null;

        private WorldInteractable FindNearestLivingTarget(Vector2 cursor, bool exact)
        {
            if (exact && maskMaterial != null && Screen.width > 0 && Screen.height > 0)
            {
                EnsureTexture();
                DrawMask();
                ReadWholeMask();
            }
            if (HasMask && TargetMaskGeometry.Closest(pixels, mask.width, mask.height, flipReadback, cursor.x, cursor.y,
                Screen.width, Screen.height, out var closest, IsLivingTarget))
                return SnapshotTarget(closest.Id);
            return null;
        }

        private bool IsLivingTarget(int id)
        {
            var target = SnapshotTarget(id);
            return target != null && target.CanTarget && target.CanInteract;
        }

        private WorldInteractable ReadClickTarget(Vector2 cursor)
        {
            if (maskMaterial == null || cursor.x < 0 || cursor.y < 0 || cursor.x >= Screen.width || cursor.y >= Screen.height)
                return null;
            EnsureTexture();
            DrawMask();
            if (clickPixel == null)
                clickPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            int x = Mathf.FloorToInt(cursor.x * mask.width / Screen.width), y = Mathf.FloorToInt(cursor.y * mask.height / Screen.height);
            var before = RenderTexture.active;
            RenderTexture.active = mask;
            clickPixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            RenderTexture.active = before;
            Color32 pixel = clickPixel.GetRawTextureData<Color32>()[0];
            int id = pixel.r | pixel.g << 8 | pixel.b << 16;
            return id > 0 && id < frameTargets.Count ? frameTargets[id] : null;
        }

        private bool TryWorldHit(Vector2 cursor, out RaycastHit nearest)
        {
            Ray ray = game.WorldCamera.ScreenPointToRay(cursor);
            int count = Physics.RaycastNonAlloc(ray, worldHits, 200f, VillageMapProp.PointerLayers, QueryTriggerInteraction.Ignore);
            var hits = worldHits;
            if (count == worldHits.Length)
            {
                hits = Physics.RaycastAll(ray, 200f, VillageMapProp.PointerLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            nearest = default;
            float distance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance >= distance || hits[i].collider.GetComponentInParent<WorldInteractable>() != null ||
                    visibility != null && visibility.IgnorePointerCollider(hits[i].collider))
                    continue;
                nearest = hits[i];
                distance = hits[i].distance;
                found = true;
            }

            return found;
        }

        private bool HasMask => pixels != null && mask != null && capturedWidth == Screen.width && capturedHeight == Screen.height;

        private void LateUpdate()
        {
            if (game == null || maskMaterial == null || link == null || Screen.width < 1 || Screen.height < 1)
                return;
            if (game.ControlsBlocked)
            {
                link.Hide();
                link.HideOutline();
                return;
            }

            EnsureTexture();
            if (!waiting && Time.unscaledTime >= nextCapture)
            {
                DrawMask();
                Capture();
            }

            var selected = CurrentTarget;
            if (selected == null || !selected.CanInteract)
            {
                link.Hide();
                link.HideOutline();
                return;
            }
            Color tint = IsPressed ? pressedColor : highlightColor;
            UpdateOutline(selected, tint);
            if (selected.CanTarget && WorkerbornInput.Held("Gameplay/Target"))
            {
                Vector3 center = game.WorldCamera.WorldToScreenPoint(selected.TargetCenter);
                if (center.z > 0)
                {
                    link.SetSilhouetteMask(mask, SystemInfo.graphicsUVStartsAtTop);
                    link.Show(game.Hud.CanvasRect, WorkerbornInput.Point, center, tint);
                    return;
                }
            }
            link.Hide();
        }

        private void UpdateOutline(WorldInteractable selected, Color tint)
        {
            if (HasMask && (outlinedRevision != maskRevision || outlinedTarget != selected))
            {
                outlinedTarget = selected;
                outlinedRevision = maskRevision;
                int id = System.Array.IndexOf(snapshot, selected);
                if (id < 1)
                {
                    link.HideOutline();
                    return;
                }
                if (exterior == null || exterior.width != mask.width || exterior.height != mask.height)
                {
                    if (exterior != null)
                        Destroy(exterior);
                    exterior = new Texture2D(mask.width, mask.height, TextureFormat.R8, false, true)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    exteriorPixels = new byte[mask.width * mask.height];
                    exteriorQueue = new int[exteriorPixels.Length];
                }
                TargetMaskGeometry.MarkExterior(pixels, mask.width, mask.height, id, exteriorPixels, exteriorQueue);
                exterior.LoadRawTextureData(exteriorPixels);
                exterior.Apply(false);
            }
            if (HasMask && exterior != null && System.Array.IndexOf(snapshot, selected) > 0)
                link.ShowOutline(exterior, flipReadback, tint, highlightWidth);
            else
                link.HideOutline();
        }

        private void EnsureTexture()
        {
            int w = Mathf.Min(maskWidth, Screen.width), h = Mathf.Max(1, Mathf.RoundToInt(w * (float)Screen.height / Screen.width));
            if (mask != null && mask.width == w && mask.height == h)
                return;
            if (waiting)
                return;
            if (mask != null)
            {
                mask.Release();
                Destroy(mask);
            }

            mask = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "Target silhouettes",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            mask.Create();
            pixels = null;
        }

        private void DrawMask()
        {
            frameTargets.Clear();
            frameTargets.Add(null);
            targetIds.Clear();
            foreach (var item in WorldInteractable.Active)
            {
                if (item == null || item.gameObject.scene != game.gameObject.scene)
                    continue;
                int id = 0;
                if (item.CanInteract)
                {
                    id = frameTargets.Count;
                    frameTargets.Add(item);
                }
                item.GetComponentsInChildren(false, actorGeometry);
                foreach (var renderer in actorGeometry)
                    if (renderer.enabled)
                        targetIds[renderer] = id;
            }

            using var command = new CommandBuffer { name = "Workerborn visible target mask" };
            command.SetRenderTarget(mask);
            command.ClearRenderTarget(true, true, Color.clear);
            command.SetViewport(new Rect(0, 0, mask.width, mask.height));
            command.SetGlobalMatrix("_SelectionViewProjection", GL.GetGPUProjectionMatrix(game.WorldCamera.projectionMatrix, false) * game.WorldCamera.worldToCameraMatrix);
            if (terrainMask != null && terrain != null)
            {
                command.SetGlobalTexture("_SelectionBaseMap", Texture2D.whiteTexture);
                command.SetGlobalVector("_SelectionMapST", new Vector4(1, 1, 0, 0));
                command.SetGlobalFloat("_SelectionCutoff", 0);
                command.SetGlobalVector("_SelectionId", Vector4.zero);
                command.DrawMesh(terrainMask, terrain.transform.localToWorldMatrix, maskMaterial, 0, 0);
            }

            GeometryUtility.CalculateFrustumPlanes(game.WorldCamera, maskFrustum);
            foreach (var renderer in staticGeometry)
                Draw(command, renderer, 0);
            game.Worker.GetComponentsInChildren(false, actorGeometry);
            foreach (var renderer in actorGeometry)
                Draw(command, renderer, 0);
            foreach (var pair in targetIds)
                Draw(command, pair.Key, pair.Value);
            Graphics.ExecuteCommandBuffer(command);
        }

        private void Capture()
        {
            nextCapture = Time.unscaledTime + 1f / capturesPerSecond;
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                ReadWholeMask();
                return;
            }
            int screenWidth = Screen.width, screenHeight = Screen.height, version = generation;
            var targets = frameTargets.ToArray();
            waiting = true;
            request = AsyncGPUReadback.Request(mask, 0, TextureFormat.RGBA32, result =>
            {
                waiting = false;
                if (this == null || version != generation || result.hasError)
                    return;
                var data = result.GetData<byte>();
                if (pixels == null || pixels.Length != data.Length)
                    pixels = new byte[data.Length];
                data.CopyTo(pixels);
                PublishMask(targets, screenWidth, screenHeight, SystemInfo.graphicsUVStartsAtTop);
            });
        }

        private void ReadWholeMask()
        {
            if (synchronous == null || synchronous.width != mask.width || synchronous.height != mask.height)
            {
                if (synchronous != null)
                    Destroy(synchronous);
                synchronous = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false, true);
            }

            var before = RenderTexture.active;
            RenderTexture.active = mask;
            synchronous.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0, false);
            RenderTexture.active = before;
            var data = synchronous.GetRawTextureData<byte>();
            if (pixels == null || pixels.Length != data.Length)
                pixels = new byte[data.Length];
            data.CopyTo(pixels);
            generation++;
            PublishMask(frameTargets.ToArray(), Screen.width, Screen.height, false);
        }

        private void PublishMask(WorldInteractable[] targets, int width, int height, bool flip)
        {
            snapshot = targets;
            capturedWidth = width;
            capturedHeight = height;
            flipReadback = flip;
            maskRevision++;
        }

        private void Draw(CommandBuffer command, Renderer renderer, int id)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || renderer.gameObject.layer == VillageWeather.SkyLayer ||
                (game.WorldCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                visibility != null && visibility.Opacity(renderer) <= .2f || !GeometryUtility.TestPlanesAABB(maskFrustum, renderer.bounds))
                return;
            var materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || material.renderQueue >= 3000)
                    continue;
                string property = "_BaseMap";
                foreach (string candidate in SurfaceTextures)
                    if (material.HasProperty(candidate) && material.GetTexture(candidate) != null)
                    {
                        property = candidate;
                        break;
                    }

                Texture texture = material.HasProperty(property) ? material.GetTexture(property) : null;
                var scale = material.HasProperty(property) ? material.GetTextureScale(property) : Vector2.one;
                var offset = material.HasProperty(property) ? material.GetTextureOffset(property) : Vector2.zero;
                command.SetGlobalTexture("_SelectionBaseMap", texture != null ? texture : Texture2D.whiteTexture);
                command.SetGlobalVector("_SelectionMapST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                command.SetGlobalFloat("_SelectionCutoff", SelectionCutoff(material));
                command.SetGlobalVector("_SelectionId", new Vector4((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f, id > 0 && frameTargets[id].CanTarget ? 1 : 0));
                command.DrawRenderer(renderer, maskMaterial, index, 0);
            }
        }

        private static readonly string[] SurfaceTextures =
        {
            "_BaseMap",
            "_Main_Texture",
            "_MainTex",
            "_Texture_Map"
        };
        public static float SelectionCutoff(Material material)
        {
            if (material == null || !(material.IsKeywordEnabled("_ALPHATEST_ON") || material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f))
                return 0;
            // Shader Graph foliage can use a constant threshold without exposing _Cutoff.
            if (material.HasProperty("_Cutoff"))
                return material.GetFloat("_Cutoff");
            if (material.HasProperty("_AlphaClipThreshold"))
                return material.GetFloat("_AlphaClipThreshold");
            return .5f;
        }

        private void OnDisable()
        {
            generation++;
            CurrentTarget = null;
            pixels = null;
            if (link != null)
            {
                link.Hide();
                link.HideOutline();
            }
        }

        private void OnDestroy()
        {
            TerrainCallbacks.heightmapChanged -= TerrainChanged;
            if (terrainMask != null)
                Destroy(terrainMask);
            if (waiting)
                request.WaitForCompletion();
            if (mask != null)
            {
                mask.Release();
                Destroy(mask);
            }

            if (synchronous != null)
                Destroy(synchronous);
            if (clickPixel != null)
                Destroy(clickPixel);
            if (exterior != null)
                Destroy(exterior);
        }
    }
}
