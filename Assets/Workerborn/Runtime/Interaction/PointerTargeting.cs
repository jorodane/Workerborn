using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Workerborn
{
    // Mesh picking and the display outline use separate render targets.
    [DefaultExecutionOrder(-25)]
    public sealed class PointerTargeting : MonoBehaviour
    {
        public const int HoverAssistLayer = 13;
        private const float PointerDistance = 200f;
        [SerializeField]
        private Material maskMaterial;
        [SerializeField]
        private Material outlineFilter;
        [SerializeField, Range(2f, 5f)]
        private float outlinePixels = 3f;
        private Material flatKitOutline, silhouetteMaterial;
        private RenderTexture silhouette, edges, outline;
        [SerializeField, Range(256, 1536), Tooltip("GPU readback resolution for mesh picking.")]
        private int maskWidth = 768;
        [SerializeField, Range(10, 60)]
        private float capturesPerSecond = 30;
        [SerializeField]
        private Color highlightColor = new Color(1f, .88f, .36f, 1);
        [SerializeField]
        private TargetLinkView link;
        [SerializeField]
        private Color pressedColor = new Color(1f, .55f, .12f, 1f);
        private WorldTargetReference pressTarget;
        private CameraVisibility visibility;
        private Terrain terrain;
        private Mesh terrainMask;
        public bool IsPressed => CurrentTarget != null && CurrentTarget == pressTarget.Target && WorkerbornInput.Held("Gameplay/Interact");

        private VillagePrototype game;
        private RenderTexture mask;
        private Texture2D synchronous;
        private bool flipReadback;
        private byte[] pixels;
        private WorldTargetReference[] snapshot;
        private readonly List<WorldTargetReference> frameTargets = new List<WorldTargetReference>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly RaycastHit[] worldHits = new RaycastHit[128];
        private readonly RaycastHit[] hoverHits = new RaycastHit[8];
        private readonly Plane[] maskFrustum = new Plane[6];
        private Renderer[] staticGeometry;
        private readonly List<Renderer> actorGeometry = new List<Renderer>();
        private readonly Dictionary<Renderer, int> targetIds = new Dictionary<Renderer, int>();
        private readonly List<Material> materials = new List<Material>();
        private CommandBuffer drawCommands;
        private AsyncGPUReadbackRequest request;
        private bool waiting;
        private bool useAsyncReadback;
        private float nextCapture;
        private int capturedWidth, capturedHeight;
        private int generation;
        private WorldTargetReference currentTarget;
        public WorldInteractable CurrentTarget => currentTarget.Target;

        public void Initialize(VillagePrototype owner, TargetLinkView targetLink)
        {
            game = owner;
            link = targetLink;
            visibility = game.WorldCamera.GetComponent<CameraVisibility>();
            drawCommands = new CommandBuffer();
            if (outlineFilter != null && maskMaterial != null)
            {
                flatKitOutline = new Material(outlineFilter);
                flatKitOutline.EnableKeyword("OUTLINE_USE_COLOR");
                flatKitOutline.EnableKeyword("OUTLINE_ONLY");
                flatKitOutline.SetColor("_EdgeColor", Color.white);
                flatKitOutline.SetFloat("_ColorThresholdMin", .01f);
                flatKitOutline.SetFloat("_ColorThresholdMax", .65f);
                flatKitOutline.SetFloat("_Thickness", outlinePixels);
                silhouetteMaterial = new Material(maskMaterial);
            }
            terrain = game.GroundTerrain;
            useAsyncReadback = SystemInfo.supportsAsyncGPUReadback;
            RefreshStaticGeometry();
            RebuildTerrainMask();
            TerrainCallbacks.heightmapChanged += TerrainChanged;
        }

        internal void RefreshStaticGeometry()
        {
            var geometry = new List<Renderer>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (renderer.gameObject.scene == game.gameObject.scene && renderer.gameObject.layer != VillageWeather.SkyLayer &&
                    renderer is MeshRenderer && renderer.GetComponentInParent<WorldInteractable>(true) == null &&
                    renderer.GetComponentInParent<HarvestWorker>(true) == null &&
                    renderer.GetComponentInParent<VillageEffects>(true) == null)
                    geometry.Add(renderer);
            staticGeometry = geometry.ToArray();
            generation++;
            pixels = null;
            nextCapture = 0f;
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

        private WorldTargetReference rightTarget;

        public bool OverUi(Vector2 position) => UiAt(position) != null;

        public GameObject UiAt(Vector2 position)
        {
            if (EventSystem.current == null || !WorkerbornInput.IsFinite(position))
                return null;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0 ? uiHits[0].gameObject : null;
        }

        public bool TryDropPoint(Vector2 screen, out Vector3 point)
        {
            point = default;
            if (game == null || !WorkerbornInput.IsInView(game.WorldCamera, screen))
                return false;
            GameObject ui = UiAt(screen);
            if ((ui != null && ui.GetComponentInParent<WorldNameplateView>() == null) ||
                !TryWorldHit(screen, out var hit) || !game.IsGround(hit.collider))
                return false;
            point = hit.point;
            return true;
        }

        private void Update()
        {
            if (game == null)
                return;
            if (!WorkerbornInput.TryGetPointerPosition(out var cursor) || !WorkerbornInput.IsInView(game.WorldCamera, cursor))
            {
                currentTarget = pressTarget = rightTarget = default;
                return;
            }

            var ui = UiAt(cursor);
            var plate = ui != null ? ui.GetComponentInParent<WorldNameplateView>() : null;
            bool overUi = ui != null && plate == null;
            bool control = WorkerbornInput.Held("Gameplay/Target");
            bool pressed = WorkerbornInput.Pressed("Gameplay/Interact");
            bool rightPressed = WorkerbornInput.Pressed("Camera/OrbitRight");
            WorldInteractable target = null;
            if (game.Hud.AllowsGroundItems && !game.Hud.IsHoldingItem)
            {
                if (plate != null)
                    target = plate.Target;
                else if (!overUi)
                    target = PickTarget(cursor, control && !game.ControlsBlocked);
            }

            if (target != null && (!target.CanInteract || !game.SharesMap(target.transform.position) ||
                game.ControlsBlocked && !(target is GroundPickup)))
                target = null;
            if (rightPressed)
            {
                rightTarget = new WorldTargetReference(target);
                game.Hud.CloseWorldContext();
            }
            if (WorkerbornInput.Released("Camera/OrbitRight"))
            {
                if (game.CameraRig.RightClickThisFrame)
                {
                    if (rightTarget.Target != null)
                        game.Hud.OpenWorldContext(rightTarget.Target, cursor);
                    else if (!rightTarget.IsAssigned && !overUi && !game.Hud.HasQuantityPrompt)
                        game.Worker.Cancel();
                }
                rightTarget = default;
            }

            if (pressed)
                pressTarget = new WorldTargetReference(target);
            if (!WorkerbornInput.Held("Gameplay/Interact"))
                pressTarget = default;
            currentTarget = new WorldTargetReference(target);
            if (!pressed)
                return;

            if (ui == null || ui.GetComponentInParent<WorldContextView>() == null)
                game.Hud.CloseWorldContext();
            if (!game.Hud.AllowsGroundItems)
                return;
            if (game.Hud.IsHoldingItem)
            {
                if (!overUi)
                    game.Hud.DropHeldOnGround(cursor);
                return;
            }
            if (target != null)
                target.RequestInteraction(game.Worker, game.WorldCamera.ScreenPointToRay(cursor));
            else if (!game.ControlsBlocked && !overUi && !control && (HasMask || maskMaterial == null) &&
                TryWorldHit(cursor, out var hit) && game.IsGround(hit.collider))
                game.Worker.CommandMove(hit.point, WorkerbornInput.Held("Gameplay/Walk"));
        }

        private WorldInteractable SnapshotTarget(int id) => snapshot != null && id > 0 && id < snapshot.Length ? snapshot[id].Target : null;

        private WorldInteractable PickTarget(Vector2 cursor, bool control)
        {
            if (!HasMask)
                return control ? null : PickHoverAssist(cursor, doorsOnly: true);
            if (!control)
            {
                int id = TargetMaskGeometry.IdAtScreen(pixels, mask.width, mask.height, flipReadback,
                    cursor.x, cursor.y, Screen.width, Screen.height);
                return (id != 0 ? SnapshotTarget(id) : null) ?? PickHoverAssist(cursor);
            }
            if (TargetMaskGeometry.Closest(pixels, mask.width, mask.height, flipReadback, cursor.x, cursor.y,
                Screen.width, Screen.height, out var closest, IsLivingTarget))
                return SnapshotTarget(closest.Id);
            return null;
        }

        private bool IsLivingTarget(int id)
        {
            var target = SnapshotTarget(id);
            return target != null && target.CanTarget && target.CanInteract;
        }

        private WorldInteractable PickHoverAssist(Vector2 cursor, bool doorsOnly = false)
        {
            Ray ray = game.WorldCamera.ScreenPointToRay(cursor);
            const int layer = 1 << HoverAssistLayer;
            var hits = hoverHits;
            int count = Physics.RaycastNonAlloc(ray, hits, PointerDistance, layer, QueryTriggerInteraction.Collide);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, PointerDistance, layer, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            if (count == 0)
                return null;

            float nearest = TryWorldHit(ray, out var blocker) ? blocker.distance : PointerDistance;
            WorldInteractable candidate = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= nearest || !hit.collider.isTrigger)
                    continue;
                var target = hit.collider.GetComponentInParent<WorldInteractable>();
                if (target == null || !target.CanInteract || target.gameObject.scene != game.gameObject.scene ||
                    !game.SharesMap(target.transform.position) || doorsOnly && !(target is VillageBuildingDoor))
                    continue;

                target.GetComponentsInChildren(false, actorGeometry);
                foreach (var renderer in actorGeometry)
                {
                    if (!IsPickVisible(renderer))
                        continue;
                    nearest = hit.distance;
                    candidate = target;
                    break;
                }
            }
            return candidate;
        }

        private bool TryWorldHit(Vector2 cursor, out RaycastHit nearest) =>
            TryWorldHit(game.WorldCamera.ScreenPointToRay(cursor), out nearest);

        private bool TryWorldHit(Ray ray, out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(ray, worldHits, PointerDistance, VillageMapProp.PointerLayers, QueryTriggerInteraction.Ignore);
            var hits = worldHits;
            if (count == worldHits.Length)
            {
                hits = Physics.RaycastAll(ray, PointerDistance, VillageMapProp.PointerLayers, QueryTriggerInteraction.Ignore);
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
            if (game == null)
                return;
            var selected = CurrentTarget;
            if (selected == null || !selected.CanInteract || !game.Hud.AllowsGroundItems || flatKitOutline == null)
            {
                link?.Hide();
                game.Hud.SetSelectionOutline(null);
                return;
            }

            var camera = game.WorldCamera;
            if (camera.pixelWidth < 1 || camera.pixelHeight < 1)
                return;
            EnsureOutlineTextures(camera.pixelWidth, camera.pixelHeight);
            game.Hud.SetSelectionOutline(outline);
            ShowTargetLink(selected, IsPressed ? pressedColor : highlightColor);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += RenderForWorldCamera;

        private void RenderForWorldCamera(ScriptableRenderContext context, Camera camera)
        {
            if (game == null || camera != game.WorldCamera || maskMaterial == null ||
                Screen.width < 1 || Screen.height < 1 || !game.Hud.AllowsGroundItems)
                return;

            var active = RenderTexture.active;
            if (!waiting && Time.unscaledTime >= nextCapture)
            {
                EnsureTexture();
                DrawMask(mask);
                Capture();
            }
            var selected = CurrentTarget;
            if (selected != null && selected.CanInteract && silhouette != null && flatKitOutline != null &&
                silhouette.width == camera.pixelWidth && silhouette.height == camera.pixelHeight)
                DrawOutline(selected, IsPressed ? pressedColor : highlightColor);
            RenderTexture.active = active;
        }

        private void DrawOutline(WorldInteractable selected, Color tint)
        {
            // Use the camera and animated meshes for this frame. Only picking waits for readback.
            DrawMask(silhouette, selected);
            var draw = drawCommands;
            draw.Clear();
            draw.name = "Flat Kit selected object outline";
            draw.SetGlobalTexture("_CameraColorTexture", silhouette);
            draw.SetGlobalVector("_SourceSize", new Vector4(silhouette.width, silhouette.height,
                1f / silhouette.width, 1f / silhouette.height));
            draw.Blit(silhouette, edges, flatKitOutline, 0);
            draw.SetGlobalTexture("_CameraColorTexture", Shader.GetGlobalTexture("_CameraColorTexture") ?? Texture2D.blackTexture);
            draw.SetGlobalVector("_SourceSize", Shader.GetGlobalVector("_SourceSize"));
            Graphics.ExecuteCommandBuffer(draw);
            silhouetteMaterial.SetColor("_OutlineTint", tint);
            Graphics.Blit(edges, outline, silhouetteMaterial, 1);
        }

        private void EnsureOutlineTextures(int width, int height)
        {
            if (silhouette != null && silhouette.width == width && silhouette.height == height)
                return;
            ReleaseOutlineTextures();
            silhouette = NewOutlineTexture(width, height, 24, FilterMode.Point, "Selection coverage");
            edges = NewOutlineTexture(width, height, 0, FilterMode.Bilinear, "Flat Kit selection edges");
            outline = NewOutlineTexture(width, height, 0, FilterMode.Bilinear, "Visible selection edges");
        }

        private static RenderTexture NewOutlineTexture(int width, int height, int depth, FilterMode filter, string label)
        {
            var result = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = label, filterMode = filter, wrapMode = TextureWrapMode.Clamp
            };
            result.Create();
            return result;
        }

        private void ReleaseOutlineTextures()
        {
            foreach (var texture in new[] { silhouette, edges, outline })
                if (texture != null)
                {
                    texture.Release();
                    Destroy(texture);
                }
        }

        private void ShowTargetLink(WorldInteractable selected, Color tint)
        {
            if (link == null)
                return;
            if (selected == null || !selected.CanInteract)
            {
                link.Hide();
                return;
            }
            if (selected.CanTarget && WorkerbornInput.Held("Gameplay/Target"))
            {
                Vector3 center = game.WorldCamera.WorldToScreenPoint(selected.TargetCenter);
                if (center.z > 0)
                {
                    link.SetSilhouetteMask(silhouette, SystemInfo.graphicsUVStartsAtTop);
                    link.Show(game.Hud.CanvasRect, WorkerbornInput.Point, center, tint);
                    return;
                }
            }
            link.Hide();
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

        private void DrawMask(RenderTexture destination, WorldInteractable selected = null)
        {
            bool forOutline = selected != null;
            if (!forOutline)
            {
                frameTargets.Clear();
                frameTargets.Add(default);
            }
            targetIds.Clear();
            foreach (var item in WorldInteractable.Active)
            {
                if (item == null || item.gameObject.scene != game.gameObject.scene || !game.SharesMap(item.transform.position))
                    continue;
                int id = item == selected ? 1 : 0;
                if (!forOutline && item.CanInteract)
                {
                    id = frameTargets.Count;
                    frameTargets.Add(new WorldTargetReference(item));
                }
                item.GetComponentsInChildren(false, actorGeometry);
                foreach (var renderer in actorGeometry)
                    if (renderer.enabled)
                        targetIds[renderer] = id;
            }

            var command = drawCommands;
            command.Clear();
            command.name = forOutline ? "Visible selected silhouette" : "Workerborn visible target mask";
            command.SetRenderTarget(destination);
            command.ClearRenderTarget(true, true, Color.clear);
            command.SetViewport(new Rect(0, 0, destination.width, destination.height));
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
                Draw(command, renderer, Vector4.zero);
            game.Worker.GetComponentsInChildren(false, actorGeometry);
            foreach (var renderer in actorGeometry)
                Draw(command, renderer, Vector4.zero);
            foreach (var pair in targetIds)
            {
                int id = pair.Value;
                var color = forOutline ? (id != 0 ? Vector4.one : Vector4.zero)
                    : new Vector4((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f,
                        id > 0 && frameTargets[id].Target?.CanTarget == true ? 1f : 0f);
                Draw(command, pair.Key, color);
            }
            Graphics.ExecuteCommandBuffer(command);
        }

        private void Capture()
        {
            nextCapture = Time.unscaledTime + 1f / capturesPerSecond;
            if (!useAsyncReadback)
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
                if (this == null || version != generation)
                    return;
                if (result.hasError)
                {
                    useAsyncReadback = false;
                    return;
                }
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

        private void PublishMask(WorldTargetReference[] targets, int width, int height, bool flip)
        {
            snapshot = targets;
            capturedWidth = width;
            capturedHeight = height;
            flipReadback = flip;
        }

        private bool IsPickVisible(Renderer renderer) => renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
            renderer.gameObject.activeInHierarchy && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) &&
            renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly &&
            renderer.gameObject.layer != VillageWeather.SkyLayer && renderer.gameObject.layer != HoverAssistLayer &&
            (game.WorldCamera.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
            (visibility == null || visibility.Opacity(renderer) > .2f);

        private void Draw(CommandBuffer command, Renderer renderer, Vector4 idColor)
        {
            if (!IsPickVisible(renderer) || !GeometryUtility.TestPlanesAABB(maskFrustum, renderer.bounds))
                return;
            renderer.GetSharedMaterials(materials);
            for (int index = 0; index < materials.Count; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                string property = "_BaseMap";
                foreach (string candidate in SurfaceTextures)
                    if (material.HasProperty(candidate) && material.GetTexture(candidate) != null)
                    {
                        property = candidate;
                        break;
                    }

                Texture texture = material.HasProperty(property) ? material.GetTexture(property) : null;
                float cutoff = SelectionCutoff(material);
                // A fully covered surface can render after opaque geometry to read camera textures.
                if (material.renderQueue >= (int)RenderQueue.Transparent && cutoff == 0f &&
                    material.GetTag("WorkerbornSelectionCoverage", false) != "Opaque")
                {
                    if (idColor == Vector4.zero || texture == null)
                        continue;
                    cutoff = .5f;
                }
                var scale = material.HasProperty(property) ? material.GetTextureScale(property) : Vector2.one;
                var offset = material.HasProperty(property) ? material.GetTextureOffset(property) : Vector2.zero;
                command.SetGlobalTexture("_SelectionBaseMap", texture != null ? texture : Texture2D.whiteTexture);
                command.SetGlobalVector("_SelectionMapST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                command.SetGlobalFloat("_SelectionCutoff", cutoff);
                command.SetGlobalVector("_SelectionId", idColor);
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
            if (material == null || !(material.IsKeywordEnabled("_ALPHATEST_ON") ||
                material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f ||
                material.GetTag("RenderType", false) == "TransparentCutout"))
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
            RenderPipelineManager.beginCameraRendering -= RenderForWorldCamera;
            generation++;
            currentTarget = pressTarget = rightTarget = default;
            game?.Hud.SetSelectionOutline(null);
            pixels = null;
            if (link != null)
                link.Hide();
        }

        private void OnDestroy()
        {
            TerrainCallbacks.heightmapChanged -= TerrainChanged;
            drawCommands?.Release();
            ReleaseOutlineTextures();
            if (flatKitOutline != null) Destroy(flatKitOutline);
            if (silhouetteMaterial != null) Destroy(silhouetteMaterial);
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
        }
    }
}
