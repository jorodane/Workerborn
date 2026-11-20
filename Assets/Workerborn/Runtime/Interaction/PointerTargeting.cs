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
        private readonly Plane[] maskFrustum = new Plane[6];
        private Renderer[] staticGeometry;
        private readonly List<Renderer> actorGeometry = new List<Renderer>();
        private readonly Dictionary<Renderer, int> targetIds = new Dictionary<Renderer, int>();
        private readonly List<Material> materials = new List<Material>();
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
            terrain = game.GroundTerrain;
            useAsyncReadback = SystemInfo.supportsAsyncGPUReadback;
            var geometry = new List<Renderer>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (renderer.gameObject.scene == game.gameObject.scene && renderer.gameObject.layer != VillageWeather.SkyLayer &&
                    renderer is MeshRenderer && renderer.GetComponentInParent<WorldInteractable>(true) == null &&
                    renderer.GetComponentInParent<HarvestWorker>(true) == null &&
                    renderer.GetComponentInParent<VillageEffects>(true) == null)
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

        private WorldTargetReference rightTarget;

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
                    target = PickTarget(cursor, control);
            }

            if (target != null && !target.CanInteract)
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
            if (game.Hud.HasModal || game.Hud.HasQuantityPrompt)
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
            else if (!overUi && !control && (HasMask || maskMaterial == null) &&
                TryWorldHit(cursor, out var hit) && game.IsGround(hit.collider))
                game.Worker.CommandMove(hit.point);
        }

        private WorldInteractable SnapshotTarget(int id) => snapshot != null && id > 0 && id < snapshot.Length ? snapshot[id].Target : null;

        private WorldInteractable PickTarget(Vector2 cursor, bool control)
        {
            if (!HasMask)
                return null;
            if (!control)
                return SnapshotTarget(TargetMaskGeometry.IdAtScreen(pixels, mask.width, mask.height, flipReadback,
                    cursor.x, cursor.y, Screen.width, Screen.height));
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
            if (game == null)
                return;
            var selected = game.ControlsBlocked ? null : CurrentTarget;
            Color tint = IsPressed ? pressedColor : highlightColor;
            visibility?.Select(selected, tint);
            if (maskMaterial == null || Screen.width < 1 || Screen.height < 1)
                return;
            if (game.ControlsBlocked)
            {
                link?.Hide();
                return;
            }

            EnsureTexture();
            ShowTargetLink(selected, tint);
            if (!waiting && Time.unscaledTime >= nextCapture)
            {
                DrawMask();
                Capture();
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
                    link.SetSilhouetteMask(mask, SystemInfo.graphicsUVStartsAtTop);
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

        private void DrawMask()
        {
            frameTargets.Clear();
            frameTargets.Add(default);
            targetIds.Clear();
            foreach (var item in WorldInteractable.Active)
            {
                if (item == null || item.gameObject.scene != game.gameObject.scene)
                    continue;
                int id = 0;
                if (item.CanInteract)
                {
                    id = frameTargets.Count;
                    frameTargets.Add(new WorldTargetReference(item));
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

        private void Draw(CommandBuffer command, Renderer renderer, int id)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || renderer.gameObject.layer == VillageWeather.SkyLayer ||
                (game.WorldCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                visibility != null && visibility.Opacity(renderer) <= .2f || !GeometryUtility.TestPlanesAABB(maskFrustum, renderer.bounds))
                return;
            renderer.GetSharedMaterials(materials);
            for (int index = 0; index < materials.Count; index++)
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
                command.SetGlobalVector("_SelectionId", new Vector4((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f, id > 0 && frameTargets[id].Target?.CanTarget == true ? 1 : 0));
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
            currentTarget = pressTarget = rightTarget = default;
            visibility?.Select(null, default);
            pixels = null;
            if (link != null)
                link.Hide();
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
        }
    }
}
