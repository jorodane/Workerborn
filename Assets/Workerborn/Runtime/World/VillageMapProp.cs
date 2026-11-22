using UnityEngine;

namespace Workerborn
{
    public sealed class VillageMapProp : MonoBehaviour
    {
        // Navigation volumes describe the footprint, not the visible picking surface.
        public const int PointerLayers = ~((1 << 2) | (1 << 8) | (1 << PointerTargeting.HoverAssistLayer));
        public const int PointerGeometryLayer = 11;
        [SerializeField]
        private Transform visual;
        [SerializeField, Tooltip("Use the model importer's exact mesh colliders for a directly imported FBX visual.")]
        private bool useImportedMeshColliders;
        public Renderer[] OcclusionRenderers { get; private set; }
        private void Awake()
        {
            OcclusionRenderers = GetComponentsInChildren<Renderer>(true);
            if (visual == null)
                return;
            foreach (var child in visual.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
            {
                var filter = collider.GetComponent<MeshFilter>();
                if (useImportedMeshColliders && collider is MeshCollider mesh && filter != null && filter.sharedMesh != null)
                {
                    mesh.sharedMesh = filter.sharedMesh;
                    mesh.convex = false;
                    mesh.enabled = true;
                    mesh.gameObject.layer = PointerGeometryLayer;
                }
                else
                    collider.enabled = false;
            }
        }
    }
}
