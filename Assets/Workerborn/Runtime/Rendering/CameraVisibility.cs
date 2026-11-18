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
        private readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
        private readonly HashSet<VillageMapProp> hiddenProps = new HashSet<VillageMapProp>();
        private readonly Dictionary<Renderer, FadeState> states = new Dictionary<Renderer, FadeState>();
        private readonly List<Renderer> removed = new List<Renderer>();
        private readonly List<Renderer> applied = new List<Renderer>();
        // Native Unity objects cannot be constructed during MonoBehaviour deserialization.
        private MaterialPropertyBlock working;
        private sealed class FadeState
        {
            public float Opacity = 1;
            public MaterialPropertyBlock[] Blocks;
            public bool ForceOff;
        }

        public bool IsHidden(Renderer renderer) => hidden.Contains(renderer);
        public float Opacity(Renderer renderer) => states.TryGetValue(renderer, out var state) ? state.Opacity : 1;
        public bool IgnorePointerCollider(Collider collider)
        {
            if (collider.GetComponentInParent<WorldInteractable>() != null)
                return false;
            var prop = collider.GetComponentInParent<VillageMapProp>();
            if (prop != null)
                return hiddenProps.Contains(prop);
            var renderer = collider.GetComponent<Renderer>();
            return renderer != null && hidden.Contains(renderer);
        }

        private void Awake()
        {
            working = new MaterialPropertyBlock();
            view = GetComponent<Camera>();
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
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            Vector3 low = player.TransformPoint(lowerSightPoint), high = player.TransformPoint(upperSightPoint);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || renderer.forceRenderingOff || renderer is ParticleSystemRenderer || renderer.GetComponentInParent<WorldInteractable>() != null || renderer.GetComponentInParent<CharacterActor>() != null)
                    continue;
                if (!Blocks(renderer.bounds, position, low) && !Blocks(renderer.bounds, position, high))
                    continue;
                var prop = renderer.GetComponentInParent<VillageMapProp>();
                if (prop != null)
                    hiddenProps.Add(prop);
                else
                    hidden.Add(renderer);
            }

            foreach (var prop in hiddenProps)
                foreach (var renderer in prop.GetComponentsInChildren<Renderer>())
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
                if (actor == null)
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

                float margin = containmentPadding + fadeLeadDistance + (view != null ? view.nearClipPlane : .08f);
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
                        if (Contains(capsule, near, containmentPadding) || hasBounds && bounds.Contains(near))
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

        private bool Blocks(Bounds bounds, Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float length = delta.magnitude;
            return length > playerClearance && bounds.IntersectRay(new Ray(origin, delta / length), out float entry) && entry < length - playerClearance;
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
            {
                var renderer = pair.Key;
                var state = pair.Value;
                if (renderer == null || state.Opacity >= 1)
                    continue;
                state.ForceOff = renderer.forceRenderingOff;
                var materials = renderer.sharedMaterials;
                state.Blocks = new MaterialPropertyBlock[materials.Length];
                applied.Add(renderer);
                if (state.Opacity <= .001f)
                {
                    renderer.forceRenderingOff = true;
                    continue;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !materials[i].HasProperty("_CameraOpacity"))
                        continue;
                    var saved = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(saved, i);
                    state.Blocks[i] = saved;
                    working.Clear();
                    renderer.GetPropertyBlock(working, i);
                    if (working.isEmpty)
                        renderer.GetPropertyBlock(working);
                    working.SetFloat("_CameraOpacity", state.Opacity);
                    renderer.SetPropertyBlock(working, i);
                }
            }
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view)
                Restore();
        }

        private void Restore()
        {
            foreach (var renderer in applied)
            {
                if (renderer == null || !states.TryGetValue(renderer, out var state))
                    continue;
                renderer.forceRenderingOff = state.ForceOff;
                for (int i = 0; i < state.Blocks.Length; i++)
                    if (state.Blocks[i] != null)
                        renderer.SetPropertyBlock(state.Blocks[i].isEmpty ? null : state.Blocks[i], i);
            }

            applied.Clear();
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
