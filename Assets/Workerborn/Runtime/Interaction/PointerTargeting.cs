using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Workerborn
{
    // A low-resolution ID/depth pass tracks visible animated silhouettes, not bounding boxes.
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
        private VillagePrototype game;
        private RenderTexture mask;
        private Texture2D synchronous;
        private Renderer[] sceneRenderers;
        private byte[] pixels;
        private WorldInteractable[] snapshot;
        private WorldInteractable[] pendingSnapshot;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
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
            sceneRenderers = game.gameObject.scene.GetRootGameObjects().Length > 0 ? FindObjectsByType<Renderer>(FindObjectsSortMode.None) : new Renderer[0];
        }

        public bool OverUi(Vector2 position)
        {
            if (EventSystem.current == null)
                return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }

        private void LateUpdate()
        {
            if (game == null || maskMaterial == null || Mouse.current == null || link == null)
                return;
            bool held = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            var cursor = Mouse.current.position.ReadValue();
            if (!held || game.ControlsBlocked || OverUi(cursor))
            {
                Select(null);
                link.Hide();
                pixels = null;
                return;
            }

            EnsureTexture();
            if (pixels != null && capturedWidth == Screen.width && capturedHeight == Screen.height && TargetMaskGeometry.Closest(pixels, mask.width, mask.height, SystemInfo.graphicsUVStartsAtTop, cursor.x, cursor.y, Screen.width, Screen.height, out var hit) && snapshot != null && hit.Id < snapshot.Length && snapshot[hit.Id] != null && snapshot[hit.Id].isActiveAndEnabled)
            {
                Select(snapshot[hit.Id]);
                var end = new Vector2(hit.X, hit.Y);
                link.Show(game.Hud.CanvasRect, cursor, end, highlightColor);
            }
            else
            {
                Select(null);
                link.Hide();
            }

            if (!waiting && Time.unscaledTime >= nextCapture)
            {
                nextCapture = Time.unscaledTime + 1f / capturesPerSecond;
                Capture();
            }
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
                if (item == null || !item.CanTarget || !item.isActiveAndEnabled)
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
            foreach (var pair in targetIds)
                Draw(command, pair.Key, pair.Value);
            Graphics.ExecuteCommandBuffer(command);
            command.Release();
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
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || (game.WorldCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                return;
            var materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || material.renderQueue >= 3000)
                    continue;
                string property = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                Texture texture = material.HasProperty(property) ? material.GetTexture(property) : null;
                var scale = material.HasProperty(property) ? material.GetTextureScale(property) : Vector2.one;
                var offset = material.HasProperty(property) ? material.GetTextureOffset(property) : Vector2.zero;
                command.SetGlobalTexture("_SelectionBaseMap", texture != null ? texture : Texture2D.whiteTexture);
                command.SetGlobalVector("_SelectionMapST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                command.SetGlobalFloat("_SelectionCutoff", material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f ? material.GetFloat("_Cutoff") : 0f);
                command.SetGlobalVector("_SelectionId", new Vector4((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f, 1));
                command.DrawRenderer(renderer, maskMaterial, index, 0);
                if (id > 0 && material.HasProperty("_OutlineWidth") && material.IsKeywordEnabled("DR_OUTLINE_ON"))
                    command.DrawRenderer(renderer, maskMaterial, index, 1);
            }
        }

        private void Select(WorldInteractable target)
        {
            if (CurrentTarget == target)
                return;
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
                highlighted.SetColor("_OutlineColor", highlightColor);
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
