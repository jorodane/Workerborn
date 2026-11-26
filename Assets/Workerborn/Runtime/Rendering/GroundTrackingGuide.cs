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
        private static readonly Color ValidAimColor = new Color(.96f, .98f, 1f, .87f);
        private static readonly Color InvalidAimColor = new Color(1f, .25f, .22f, .92f);
        private readonly Vector3[] vertices = new Vector3[GridSize * GridSize];
        private Mesh surface;
        private MeshRenderer surfaceRenderer;
        private MaterialPropertyBlock properties;
        private Rect floor;
        private Vector3 origin;
        private AimRing throwRange, throwDestination;
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
                CreateSurface(game.GroundGuideRoot);

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

        internal void DisplayThrow(VillagePrototype game, bool visible, TimberThrowAim aim)
        {
            if (!visible || !isActiveAndEnabled || material == null)
            {
                throwRange?.Hide();
                throwDestination?.Hide();
                return;
            }

            bool outOfRange = aim == TimberThrowAim.OutOfRange;
            Color tint = outOfRange || aim == TimberThrowAim.NoGround ? InvalidAimColor : ValidAimColor;
            throwRange ??= new AimRing(game.GroundGuideRoot, material, "Throw range", .048f);
            throwRange.Show(game, game.Worker.StandingPosition, game.State.ThrowRange, tint);
            if (aim is TimberThrowAim.Ready or TimberThrowAim.OutOfRange)
            {
                throwDestination ??= new AimRing(game.GroundGuideRoot, material, "Throw destination", .035f);
                throwDestination.Show(game, game.Targeting.ThrowDestination, .29f, tint);
            }
            else
                throwDestination?.Hide();
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

            surfaceRenderer = CreateRenderer(parent, surface, material);
            properties = new MaterialPropertyBlock();
        }

        private static MeshRenderer CreateRenderer(Transform parent, Mesh mesh, Material material)
        {
            var guide = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            guide.transform.SetParent(parent, false);
            guide.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = guide.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private void OnDisable()
        {
            if (surfaceRenderer != null)
                surfaceRenderer.enabled = false;
            throwRange?.Hide();
            throwDestination?.Hide();
        }

        private void OnDestroy()
        {
            if (surfaceRenderer != null)
                Destroy(surfaceRenderer.gameObject);
            if (surface != null)
                Destroy(surface);
            throwRange?.Destroy();
            throwDestination?.Destroy();
        }

        private sealed class AimRing
        {
            private static readonly int Radius = Shader.PropertyToID("_GuideRadius");
            private static readonly int HalfWidth = Shader.PropertyToID("_GuideHalfWidth");
            private static readonly int Tint = Shader.PropertyToID("_Color");
            private readonly Mesh mesh;
            private readonly MeshRenderer renderer;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private readonly float halfWidth;
            private Vector3[] samples;
            private Vector3 center;
            private Rect floor;
            private float radius;
            private Color tint;

            internal AimRing(Transform parent, Material material, string name, float halfWidth)
            {
                this.halfWidth = halfWidth;
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
                renderer = CreateRenderer(parent, mesh, material);
                renderer.enabled = false;
                properties.SetFloat(HasDirection, 0f);
                properties.SetFloat(HalfWidth, halfWidth);
            }

            internal void Show(VillagePrototype game, Vector3 point, float reach, Color color)
            {
                Rect bounds = game.TrackingGroundBounds;
                if (reach <= 0f || bounds.width <= 0f || bounds.height <= 0f)
                {
                    Hide();
                    return;
                }
                bool changedRadius = radius != reach;
                if (changedRadius)
                    BuildRing(reach);
                if (changedRadius || !renderer.enabled || center != point || floor != bounds)
                {
                    center = point;
                    floor = bounds;
                    renderer.transform.position = center;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float x = Mathf.Clamp(point.x + samples[i].x, bounds.xMin, bounds.xMax);
                        float z = Mathf.Clamp(point.z + samples[i].z, bounds.yMin, bounds.yMax);
                        samples[i].y = game.SurfacePosition(x, z).y - point.y + SurfaceOffset;
                    }
                    mesh.vertices = samples;
                    mesh.RecalculateBounds();
                    properties.SetVector(FloorBounds, new Vector4(bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax));
                    properties.SetFloat(Radius, reach);
                    tint = default;
                }
                if (tint != color)
                {
                    tint = color;
                    properties.SetColor(Tint, color);
                    renderer.SetPropertyBlock(properties);
                }
                renderer.enabled = true;
            }

            private void BuildRing(float reach)
            {
                radius = reach;
                int segments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * reach / .27f), 24, 600);
                samples = new Vector3[segments * 2];
                var plane = new Vector2[samples.Length];
                var triangles = new int[segments * 6];
                float margin = halfWidth + .10f;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * (2f * Mathf.PI / segments);
                    var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                    int vertex = i * 2, next = (vertex + 2) % samples.Length, triangle = i * 6;
                    plane[vertex] = direction * Mathf.Max(0f, reach - margin);
                    plane[vertex + 1] = direction * (reach + margin);
                    samples[vertex] = new Vector3(plane[vertex].x, 0f, plane[vertex].y);
                    samples[vertex + 1] = new Vector3(plane[vertex + 1].x, 0f, plane[vertex + 1].y);
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 1;
                    triangles[triangle + 2] = next;
                    triangles[triangle + 3] = next;
                    triangles[triangle + 4] = vertex + 1;
                    triangles[triangle + 5] = next + 1;
                }
                mesh.Clear();
                mesh.vertices = samples;
                mesh.uv = plane;
                mesh.triangles = triangles;
            }

            internal void Hide()
            {
                if (renderer != null)
                    renderer.enabled = false;
            }

            internal void Destroy()
            {
                if (renderer != null)
                    UnityEngine.Object.Destroy(renderer.gameObject);
                UnityEngine.Object.Destroy(mesh);
            }
        }
    }
}
