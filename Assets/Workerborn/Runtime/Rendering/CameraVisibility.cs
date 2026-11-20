using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Workerborn
{
    // Per-camera property blocks preserve each renderer's assigned shader and material.
    [DefaultExecutionOrder(-35)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraVisibility : MonoBehaviour
    {
        [SerializeField]
        private Transform player;
        [SerializeField]
        private Vector3 lowerSightPoint = new Vector3(0, .6f, 0);
        [SerializeField]
        private Vector3 upperSightPoint = new Vector3(0, 1.5f, 0);
        [SerializeField, Min(0)]
        private float playerClearance = .35f;
        [SerializeField, Min(0)]
        private float containmentPadding = .35f;
        [SerializeField, Min(0)]
        private float fadeLeadDistance = .45f;
        [SerializeField, Min(.01f)]
        private float fadeOutSeconds = .16f;
        [SerializeField, Min(.01f)]
        private float fadeInSeconds = .28f;
        private Camera view;
        private int cameraOpacityProperty;
        private readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
        private readonly RaycastHit[] sightHits = new RaycastHit[64];
        private readonly HashSet<VillageMapProp> hiddenProps = new HashSet<VillageMapProp>();
        private readonly Dictionary<Renderer, FadeState> states = new Dictionary<Renderer, FadeState>();
        private readonly List<Renderer> removed = new List<Renderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly Stack<MaterialPropertyBlock> availableBlocks = new Stack<MaterialPropertyBlock>();
        private readonly List<(Renderer Renderer, int Index, MaterialPropertyBlock Block)> savedBlocks =
            new List<(Renderer, int, MaterialPropertyBlock)>();
        private readonly List<(Renderer Renderer, bool Value)> savedVisibility = new List<(Renderer, bool)>();
        // Native Unity objects cannot be constructed during MonoBehaviour deserialization.
        private MaterialPropertyBlock working;
        private sealed class FadeState
        {
            public float Opacity = 1;
        }

        public bool IsHidden(Renderer renderer) => hidden.Contains(renderer);
        public float Opacity(Renderer renderer) => states.TryGetValue(renderer, out var state) ? state.Opacity : 1;
        internal void ForgetFade(Renderer renderer)
        {
            if (savedBlocks.Count != 0 || savedVisibility.Count != 0)
                Restore();
            hidden.Remove(renderer);
            states.Remove(renderer);
        }

        public bool IgnorePointerCollider(Collider collider)
        {
            if (collider.GetComponentInParent<WorldInteractable>() != null)
                return false;
            var prop = collider.GetComponentInParent<VillageMapProp>();
            if (prop != null)
                return prop.OcclusionRenderers != null && System.Array.Exists(prop.OcclusionRenderers, IsPointerTransparent);
            var renderer = collider.GetComponent<Renderer>();
            return renderer != null && IsPointerTransparent(renderer);
        }

        private bool IsPointerTransparent(Renderer renderer) => renderer != null && Opacity(renderer) <= .2f;

        private void Awake()
        {
            working = new MaterialPropertyBlock();
            view = GetComponent<Camera>();
            cameraOpacityProperty = Shader.PropertyToID("_CameraOpacity");
        }

        public void Initialize(Transform followedPlayer) => player = followedPlayer;
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void LateUpdate()
        {
            RefreshVisibility();
            AdvanceFade(Time.unscaledDeltaTime);
        }

        public void RefreshVisibility()
        {
            hidden.Clear();
            hiddenProps.Clear();
            if (player == null)
                return;
            Vector3 position = transform.position;
            DetectOccluders(position, player.TransformPoint(lowerSightPoint));
            DetectOccluders(position, player.TransformPoint(upperSightPoint));
            foreach (var prop in hiddenProps)
                foreach (var renderer in prop.OcclusionRenderers)
                    if (renderer != null && renderer.enabled && !(renderer is ParticleSystemRenderer))
                        hidden.Add(renderer);

            // Visual bounds cover hats, hair, equipment and the bobbing/rotating pickup model.
            // Expanded capsules and near-plane corners begin fading before the camera cuts the mesh.
            foreach (var capsule in FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
            {
                if (!capsule.enabled || !capsule.gameObject.activeInHierarchy)
                    continue;
                var interaction = capsule.GetComponentInParent<WorldInteractable>();
                var character = capsule.GetComponentInParent<CharacterActor>();
                Transform actor = interaction != null ? interaction.transform : character != null ? character.transform : null;
                if (actor == null || actor == player || actor.IsChildOf(player))
                    continue;
                var visuals = actor.GetComponentsInChildren<Renderer>();
                Bounds bounds = default;
                bool hasBounds = false;
                foreach (var renderer in visuals)
                {
                    if (!renderer.enabled)
                        continue;
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                        bounds.Encapsulate(renderer.bounds);
                }

                // A humanoid rig's bounds include animation margins, hair and inactive equipment.
                // Its gameplay capsule is the volume the camera can enter.
                float nearPlane = view != null ? view.nearClipPlane : .08f;
                float margin = (character == null ? containmentPadding + fadeLeadDistance : .12f) + nearPlane;
                hasBounds &= character == null;
                bool inside = Contains(capsule, position, margin);
                if (hasBounds)
                {
                    bounds.Expand(margin * 2);
                    inside |= bounds.Contains(position);
                }

                if (!inside && view != null)
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector3 near = view.ViewportToWorldPoint(new Vector3(corner & 1, corner >> 1, view.nearClipPlane));
                        if (Contains(capsule, near, character == null ? containmentPadding : 0f) || hasBounds && bounds.Contains(near))
                        {
                            inside = true;
                            break;
                        }
                    }

                if (inside)
                    foreach (var renderer in visuals)
                        hidden.Add(renderer);
            }

            CheckBoundsOnlyInteractions(position);
        }

        private void CheckBoundsOnlyInteractions(Vector3 position)
        {
            float padding = containmentPadding + fadeLeadDistance + (view != null ? view.nearClipPlane : .08f);
            foreach (var target in WorldInteractable.Active)
            {
                if (target == null || !target.CanInteract || target.GetComponent<CapsuleCollider>() != null)
                    continue;
                var renderers = target.GetComponentsInChildren<Renderer>();
                Bounds bounds = default;
                bool found = false;
                foreach (var renderer in renderers)
                {
                    if (!renderer.enabled)
                        continue;
                    if (!found)
                    {
                        bounds = renderer.bounds;
                        found = true;
                    }
                    else
                        bounds.Encapsulate(renderer.bounds);
                }

                if (!found)
                    continue;
                bounds.Expand(padding * 2);
                if (bounds.Contains(position))
                    foreach (var renderer in renderers)
                        hidden.Add(renderer);
            }
        }

        public void AdvanceFade(float deltaTime)
        {
            foreach (var renderer in hidden)
                if (renderer != null && !states.ContainsKey(renderer))
                    states.Add(renderer, new FadeState());
            removed.Clear();
            foreach (var pair in states)
            {
                if (pair.Key == null)
                {
                    removed.Add(pair.Key);
                    continue;
                }

                bool hide = hidden.Contains(pair.Key);
                pair.Value.Opacity = Mathf.MoveTowards(pair.Value.Opacity, hide ? 0 : 1, Mathf.Max(0, deltaTime) / (hide ? fadeOutSeconds : fadeInSeconds));
                if (!hide && pair.Value.Opacity >= 1)
                    removed.Add(pair.Key);
            }

            foreach (var renderer in removed)
                states.Remove(renderer);
        }

        private void DetectOccluders(Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float distance = delta.magnitude - playerClearance;
            if (distance <= 0)
                return;
            Vector3 direction = delta.normalized;
            MarkOccluders(new Ray(origin, direction), distance);
            // The reverse ray also finds an exit surface when the camera is inside a mesh.
            MarkOccluders(new Ray(origin + direction * distance, -direction), distance);
        }

        private void MarkOccluders(Ray ray, float distance)
        {
            const int mask = 1 << VillageMapProp.PointerGeometryLayer;
            var hits = sightHits;
            int count = Physics.RaycastNonAlloc(ray, hits, distance, mask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (!(collider is MeshCollider) || collider.GetComponentInParent<WorldInteractable>() != null)
                    continue;
                var prop = collider.GetComponentInParent<VillageMapProp>();
                if (prop != null)
                    hiddenProps.Add(prop);
                else if (collider.TryGetComponent<Renderer>(out var renderer))
                    hidden.Add(renderer);
            }
        }

        public static bool Contains(CapsuleCollider capsule, Vector3 point, float padding = 0)
        {
            Vector3 scale = capsule.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            int direction = capsule.direction;
            float radius = capsule.radius * Mathf.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
            float halfSegment = Mathf.Max(0, capsule.height * scale[direction] * .5f - radius);
            Vector3 axis = capsule.transform.rotation * (direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward);
            Vector3 offset = point - capsule.transform.TransformPoint(capsule.center);
            Vector3 nearest = axis * Mathf.Clamp(Vector3.Dot(offset, axis), -halfSegment, halfSegment);
            return (offset - nearest).sqrMagnitude <= (radius + padding) * (radius + padding);
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view)
                return;
            Restore();
            foreach (var pair in states)
                if (pair.Key != null && pair.Value.Opacity < 1)
                    ApplyProperties(pair.Key, pair.Value.Opacity);
        }

        private void ApplyProperties(Renderer renderer, float opacity)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return;
            if (opacity <= .001f)
            {
                savedVisibility.Add((renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
                return;
            }

            renderer.GetSharedMaterials(materials);
            for (int i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null)
                    continue;
                if (!material.HasProperty(cameraOpacityProperty))
                    continue;

                var saved = availableBlocks.Count == 0 ? new MaterialPropertyBlock() : availableBlocks.Pop();
                renderer.GetPropertyBlock(saved, i);
                savedBlocks.Add((renderer, i, saved));
                if (saved.isEmpty)
                    renderer.GetPropertyBlock(working);
                else
                    renderer.GetPropertyBlock(working, i);
                working.SetFloat(cameraOpacityProperty, opacity);
                renderer.SetPropertyBlock(working, i);
            }
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view)
                Restore();
        }

        private void Restore()
        {
            foreach (var entry in savedVisibility)
                if (entry.Renderer != null)
                    entry.Renderer.forceRenderingOff = entry.Value;
            savedVisibility.Clear();
            foreach (var entry in savedBlocks)
            {
                if (entry.Renderer != null)
                    entry.Renderer.SetPropertyBlock(entry.Block.isEmpty ? null : entry.Block, entry.Index);
                availableBlocks.Push(entry.Block);
            }
            savedBlocks.Clear();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Restore();
            hidden.Clear();
            hiddenProps.Clear();
            states.Clear();
        }
    }
}
