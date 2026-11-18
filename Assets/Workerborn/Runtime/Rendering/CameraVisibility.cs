using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Workerborn
{
    // Changes rendering only for the orbit camera. Other cameras, physics and navigation keep working.
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
        [SerializeField, Min(0f)]
        private float playerClearance = .35f;
        private Camera view;
        private Renderer[] renderers;
        private readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
        private readonly HashSet<VillageMapProp> hiddenProps = new HashSet<VillageMapProp>();
        private readonly Dictionary<Renderer, bool> saved = new Dictionary<Renderer, bool>();
        public bool IsHidden(Renderer renderer) => hidden.Contains(renderer);
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

        private void Awake() => view = GetComponent<Camera>();
        public void Initialize(Transform followedPlayer) => player = followedPlayer;
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void LateUpdate()
        {
            RefreshVisibility();
        }

        public void RefreshVisibility()
        {
            hidden.Clear();
            hiddenProps.Clear();
            if (player == null)
                return;
            renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            Vector3 cameraPosition = transform.position;
            Vector3 low = player.TransformPoint(lowerSightPoint), high = player.TransformPoint(upperSightPoint);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || renderer.forceRenderingOff || renderer is ParticleSystemRenderer || renderer.GetComponentInParent<WorldInteractable>() != null || renderer.GetComponentInParent<CharacterActor>() != null)
                    continue;
                if (!Blocks(renderer.bounds, cameraPosition, low) && !Blocks(renderer.bounds, cameraPosition, high))
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
            // Interactive objects may cover the player. Hide them only if the camera enters a capsule.
            foreach (var capsule in FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
            {
                if (!capsule.enabled || !capsule.gameObject.activeInHierarchy || !Contains(capsule, cameraPosition))
                    continue;
                Transform actor = null;
                var interaction = capsule.GetComponentInParent<WorldInteractable>();
                if (interaction != null)
                    actor = interaction.transform;
                else
                {
                    var character = capsule.GetComponentInParent<CharacterActor>();
                    if (character != null)
                        actor = character.transform;
                }

                if (actor != null)
                    foreach (var renderer in actor.GetComponentsInChildren<Renderer>())
                        hidden.Add(renderer);
            }
        }

        private bool Blocks(Bounds bounds, Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float length = delta.magnitude;
            return length > playerClearance && bounds.IntersectRay(new Ray(origin, delta / length), out float entry) && entry < length - playerClearance;
        }

        // Use current transforms rather than the previous physics step's collider pose.
        public static bool Contains(CapsuleCollider capsule, Vector3 point)
        {
            Vector3 scale = capsule.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            int axisIndex = capsule.direction;
            float radius = capsule.radius * Mathf.Max(scale[(axisIndex + 1) % 3], scale[(axisIndex + 2) % 3]);
            float halfSegment = Mathf.Max(0, capsule.height * scale[axisIndex] * .5f - radius);
            Vector3 axis = capsule.transform.rotation * (axisIndex == 0 ? Vector3.right : axisIndex == 1 ? Vector3.up : Vector3.forward);
            Vector3 offset = point - capsule.transform.TransformPoint(capsule.center);
            Vector3 nearest = axis * Mathf.Clamp(Vector3.Dot(offset, axis), -halfSegment, halfSegment);
            return (offset - nearest).sqrMagnitude <= radius * radius;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view)
                return;
            Restore();
            foreach (var renderer in hidden)
            {
                if (renderer == null)
                    continue;
                saved.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view)
                Restore();
        }

        private void Restore()
        {
            foreach (var pair in saved)
                if (pair.Key != null)
                    pair.Key.forceRenderingOff = pair.Value;
            saved.Clear();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Restore();
            hidden.Clear();
            hiddenProps.Clear();
        }
    }
}
