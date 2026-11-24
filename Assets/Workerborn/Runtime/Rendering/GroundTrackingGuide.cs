using UnityEngine;
using UnityEngine.Rendering;

namespace Workerborn
{
    public sealed class GroundTrackingGuide : MonoBehaviour
    {
        [SerializeField] private Material material;
        private const int GridSize = 17;
        private const float Extent = 1.15f;
        private const float SurfaceOffset = .018f;
        private static readonly int FloorBounds = Shader.PropertyToID("_FloorBounds");
        private static readonly int HasDirection = Shader.PropertyToID("_HasDirection");
        private readonly Vector3[] vertices = new Vector3[GridSize * GridSize];
        private Mesh surface;
        private MeshRenderer surfaceRenderer;
        private MaterialPropertyBlock properties;
        private Rect floor;
        private Vector3 origin;
        internal Vector3 Direction { get; private set; }
        internal bool Visible => surfaceRenderer != null && surfaceRenderer.enabled;

        internal void Display(VillagePrototype game, WorldInteractable target)
        {
            if (!isActiveAndEnabled || target == null || material == null)
            {
                if (surfaceRenderer != null)
                    surfaceRenderer.enabled = false;
                return;
            }

            Rect bounds = game.TrackingGroundBounds;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                if (surfaceRenderer != null)
                    surfaceRenderer.enabled = false;
                return;
            }

            Vector3 feet = game.Worker.StandingPosition;
            Vector3 toward = target.transform.position - feet;
            toward.y = 0f;
            Vector3 direction = toward.normalized;
            if (Visible && origin == feet && Direction == direction && floor == bounds)
                return;
            if (surfaceRenderer == null)
                CreateSurface(game.transform);

            origin = feet;
            floor = bounds;
            Direction = direction;
            Quaternion heading = direction != Vector3.zero ? Quaternion.LookRotation(direction) : Quaternion.identity;
            surfaceRenderer.transform.SetPositionAndRotation(feet, heading);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = feet + heading * new Vector3(vertices[i].x, 0f, vertices[i].z);
                float x = Mathf.Clamp(point.x, floor.xMin, floor.xMax);
                float z = Mathf.Clamp(point.z, floor.yMin, floor.yMax);
                vertices[i].y = game.SurfacePosition(x, z).y - feet.y + SurfaceOffset;
            }
            surface.vertices = vertices;
            surface.RecalculateBounds();
            properties.SetVector(FloorBounds, new Vector4(floor.xMin, floor.yMin, floor.xMax, floor.yMax));
            properties.SetFloat(HasDirection, direction == Vector3.zero ? 0f : 1f);
            surfaceRenderer.SetPropertyBlock(properties);
            surfaceRenderer.enabled = true;
        }

        private void CreateSurface(Transform parent)
        {
            surface = new Mesh { name = "Ground tracking guide" };
            surface.MarkDynamic();
            var plane = new Vector2[vertices.Length];
            var triangles = new int[(GridSize - 1) * (GridSize - 1) * 6];
            int next = 0;
            for (int z = 0; z < GridSize; z++)
                for (int x = 0; x < GridSize; x++)
                {
                    int i = z * GridSize + x;
                    float u = Mathf.Lerp(-Extent, Extent, (float)x / (GridSize - 1));
                    float v = Mathf.Lerp(-Extent, Extent, (float)z / (GridSize - 1));
                    vertices[i] = new Vector3(u, 0f, v);
                    plane[i] = new Vector2(u, v);
                    if (x == GridSize - 1 || z == GridSize - 1)
                        continue;
                    triangles[next++] = i;
                    triangles[next++] = i + GridSize;
                    triangles[next++] = i + 1;
                    triangles[next++] = i + 1;
                    triangles[next++] = i + GridSize;
                    triangles[next++] = i + GridSize + 1;
                }
            surface.vertices = vertices;
            surface.uv = plane;
            surface.triangles = triangles;

            var guide = new GameObject("Ground tracking guide", typeof(MeshFilter), typeof(MeshRenderer));
            guide.transform.SetParent(parent, false);
            guide.GetComponent<MeshFilter>().sharedMesh = surface;
            surfaceRenderer = guide.GetComponent<MeshRenderer>();
            surfaceRenderer.sharedMaterial = material;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;
            surfaceRenderer.lightProbeUsage = LightProbeUsage.Off;
            surfaceRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            properties = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            if (surfaceRenderer != null)
                surfaceRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (surfaceRenderer != null)
                Destroy(surfaceRenderer.gameObject);
            if (surface != null)
                Destroy(surface);
        }
    }
}
