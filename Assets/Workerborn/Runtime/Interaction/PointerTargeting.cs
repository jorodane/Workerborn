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
        private bool highlightedPressed;
        private CameraVisibility visibility;
        private Terrain terrain;
        private Mesh terrainMask;
        public bool IsPressed => CurrentTarget != null && CurrentTarget == pressTarget && WorkerbornInput.Held("Gameplay/Interact");

        private VillagePrototype game;
        private RenderTexture mask;
        private Texture2D synchronous;
        private Renderer[] sceneRenderers;
        private byte[] pixels;
        private WorldInteractable[] snapshot;
        private WorldInteractable[] pendingSnapshot;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly RaycastHit[] worldHits = new RaycastHit[128];
        private readonly Dictionary<Renderer, MaterialPropertyBlock> previous = new Dictionary<Renderer, MaterialPropertyBlock>();
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
            RebuildTerrainMask();
            TerrainCallbacks.heightmapChanged += TerrainChanged;
            sceneRenderers = game.gameObject.scene.GetRootGameObjects().Length > 0 ? FindObjectsByType<Renderer>(FindObjectsSortMode.None) : new Renderer[0];
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
            if (OverUi(screen) || !TryWorldHit(screen, out var hit) || !game.IsGround(hit.collider))
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
            bool overUi = ui != null;
            var plate = overUi ? ui.GetComponentInParent<WorldNameplateView>() : null;
            WorldInteractable target = plate != null ? plate.Target : null;
            RaycastHit hit = default;
            bool hitWorld = !overUi && TryWorldHit(cursor, out hit);
            if (hitWorld)
                target = hit.collider.GetComponentInParent<WorldInteractable>();

            if (WorkerbornInput.Pressed("Camera/OrbitRight"))
            {
                rightTarget = !game.ControlsBlocked && target != null && target.CanInteract ? target : null;
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

            bool control = WorkerbornInput.Held("Gameplay/Target");
            if (control && !overUi)
            {
                target = null;
                if (HasMask && TargetMaskGeometry.Closest(pixels, mask.width, mask.height, SystemInfo.graphicsUVStartsAtTop, cursor.x, cursor.y, Screen.width, Screen.height, out var closest)
                    && snapshot != null && closest.Id < snapshot.Length)
                    target = snapshot[closest.Id];
            }

            if (game.ControlsBlocked || target != null && !target.CanInteract)
                target = null;
            bool pressed = WorkerbornInput.Pressed("Gameplay/Interact");
            if (pressed)
                pressTarget = target;
            if (!WorkerbornInput.Held("Gameplay/Interact"))
                pressTarget = null;
            Select(target);
            if (!pressed)
                return;

            if (!overUi || ui.GetComponentInParent<WorldContextView>() == null)
                game.Hud.CloseWorldContext();
            if (game.Hud.HasModal)
                return;
            if (game.Hud.IsHoldingItem)
            {
                if (!overUi)
                    game.Hud.DropHeldOnGround(cursor);
                return;
            }
            if (CurrentTarget != null)
            {
                game.ShowClick(CurrentTarget.transform.position);
                CurrentTarget.RequestInteraction(game.Worker);
            }
            else if (!overUi && !control && hitWorld && hit.collider.GetComponentInParent<WorldInteractable>() == null)
                game.Worker.CommandMove(hit.point);
        }

        private bool TryWorldHit(Vector2 cursor, out RaycastHit nearest)
        {
            Ray ray = game.WorldCamera.ScreenPointToRay(cursor);
            int count = Physics.RaycastNonAlloc(ray, worldHits, 200f, VillageMapProp.PointerLayers, QueryTriggerInteraction.Collide);
            var hits = worldHits;
            if (count == worldHits.Length)
            {
                hits = Physics.RaycastAll(ray, 200f, VillageMapProp.PointerLayers, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }

            nearest = default;
            float distance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance >= distance || visibility != null && visibility.IgnorePointerCollider(hits[i].collider))
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
            if (game == null || maskMaterial == null || link == null)
                return;
            bool held = WorkerbornInput.Held("Gameplay/Target");
            if (!held || game.ControlsBlocked)
            {
                link.Hide();
                pixels = null;
                return;
            }

            EnsureTexture();
            // Aim at the stable capsule center; the UI material masks character silhouettes.
            Capture();
            if (CurrentTarget != null && CurrentTarget.CanTarget && CurrentTarget.CanInteract)
            {
                Vector3 center = game.WorldCamera.WorldToScreenPoint(CurrentTarget.TargetCenter);
                if (center.z > 0)
                {
                    link.SetSilhouetteMask(mask, SystemInfo.graphicsUVStartsAtTop);
                    link.Show(game.Hud.CanvasRect, WorkerbornInput.Point, center, IsPressed ? pressedColor : highlightColor);
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

        private void Capture()
        {
            var targets = new List<WorldInteractable>
            {
                null
            };
            targetIds.Clear();
            foreach (var item in WorldInteractable.Active)
            {
                if (item == null || !item.CanTarget || !item.CanInteract)
                    continue;
                int id = targets.Count;
                targets.Add(item);
                foreach (var renderer in item.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled)
                        targetIds[renderer] = id;
            }

            pendingSnapshot = targets.ToArray();
            // Re-scan on target changes (appearance rebuilds), without retaining destroyed renderers.
            sceneRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var command = new CommandBuffer
            {
                name = "Workerborn visible target mask"
            };
            command.SetGlobalFloat("_SelectionAspect", (float)Screen.width / Screen.height);
            command.SetGlobalFloat("_SelectionOutlineWidth", highlightWidth);
            command.SetRenderTarget(mask);
            command.ClearRenderTarget(true, true, Color.clear);
            command.SetViewport(new Rect(0, 0, mask.width, mask.height));
            command.SetGlobalMatrix("_SelectionViewProjection", GL.GetGPUProjectionMatrix(game.WorldCamera.projectionMatrix, false) * game.WorldCamera.worldToCameraMatrix);
            foreach (var renderer in sceneRenderers)
                Draw(command, renderer, 0);
            if (terrainMask != null && terrain != null)
            {
                command.SetGlobalTexture("_SelectionBaseMap", Texture2D.whiteTexture);
                command.SetGlobalVector("_SelectionMapST", new Vector4(1, 1, 0, 0));
                command.SetGlobalFloat("_SelectionCutoff", 0);
                command.SetGlobalVector("_SelectionId", new Vector4(0, 0, 0, 1));
                command.DrawMesh(terrainMask, terrain.transform.localToWorldMatrix, maskMaterial, 0, 0);
            }

            foreach (var pair in targetIds)
                Draw(command, pair.Key, pair.Value);
            Graphics.ExecuteCommandBuffer(command);
            command.Release();
            // Draw every frame to mask moving silhouettes; read back IDs at a lower rate.
            if (waiting || Time.unscaledTime < nextCapture)
                return;
            nextCapture = Time.unscaledTime + 1f / capturesPerSecond;
            int screenWidth = Screen.width, screenHeight = Screen.height, version = generation;
            var frameTargets = pendingSnapshot;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
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
                    snapshot = frameTargets;
                    capturedWidth = screenWidth;
                    capturedHeight = screenHeight;
                });
            }
            else
            {
                if (synchronous == null || synchronous.width != mask.width || synchronous.height != mask.height)
                {
                    if (synchronous != null)
                        Destroy(synchronous);
                    synchronous = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false, true);
                }

                var before = RenderTexture.active;
                RenderTexture.active = mask;
                synchronous.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0);
                synchronous.Apply();
                RenderTexture.active = before;
                pixels = synchronous.GetRawTextureData();
                snapshot = frameTargets;
                capturedWidth = screenWidth;
                capturedHeight = screenHeight;
            }
        }

        private void Draw(CommandBuffer command, Renderer renderer, int id)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || visibility != null && visibility.IsHidden(renderer) || !renderer.gameObject.activeInHierarchy || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || (game.WorldCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
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
                command.SetGlobalVector("_SelectionId", new Vector4((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f, 1));
                command.DrawRenderer(renderer, maskMaterial, index, 0);
                if (id > 0 && material.HasProperty("_OutlineWidth") && material.IsKeywordEnabled("DR_OUTLINE_ON"))
                    command.DrawRenderer(renderer, maskMaterial, index, 1);
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

        private void Select(WorldInteractable target)
        {
            bool pressed = target != null && target == pressTarget && WorkerbornInput.Held("Gameplay/Interact");
            if (CurrentTarget == target && highlightedPressed == pressed)
                return;
            highlightedPressed = pressed;
            foreach (var pair in previous)
                if (pair.Key != null)
                    pair.Key.SetPropertyBlock(pair.Value);
            previous.Clear();
            CurrentTarget = target;
            if (target == null)
                return;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                previous[renderer] = original;
                var highlighted = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(highlighted);
                highlighted.SetColor("_OutlineColor", pressed ? pressedColor : highlightColor);
                highlighted.SetFloat("_OutlineWidth", highlightWidth);
                renderer.SetPropertyBlock(highlighted);
            }
        }

        private void OnDisable()
        {
            generation++;
            Select(null);
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
