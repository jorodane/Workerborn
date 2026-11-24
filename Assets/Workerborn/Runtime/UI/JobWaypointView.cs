using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class JobWaypointView : Graphic
    {
        private const int Segments = 32;
        private const int PointsPerLayer = Segments * 2 + 3;
        private const float Radius = .8f;
        [SerializeField] private Image icon;
        private readonly Vector2[] points = new Vector2[PointsPerLayer * 2];
        private Camera worldCamera;
        private Vector3 origin;
        internal Vector3 Direction { get; private set; }

        internal void Display(Camera camera, Vector3 feet, WorldInteractable target)
        {
            bool tracking = camera != null && target != null;
            if (gameObject.activeSelf != tracking)
                gameObject.SetActive(tracking);
            if (!tracking)
                return;

            worldCamera = camera;
            origin = feet + Vector3.up * .04f;
            Vector3 delta = target.transform.position - feet;
            delta.y = 0f;
            Direction = delta.normalized;
            SetVerticesDirty();

            bool projected = Project(target.NamePosition, out var point);
            Vector2 aboveName = point + Vector2.up * 32f;
            bool visible = projected && rectTransform.rect.Contains(point) && rectTransform.rect.Contains(aboveName);
            icon.gameObject.SetActive(visible);
            if (visible)
                icon.rectTransform.localPosition = aboveName;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (worldCamera == null)
                return;

            bool hasPointer = Direction.sqrMagnitude > .001f;
            int count = hasPointer ? PointsPerLayer : Segments * 2;
            if (!ProjectLayer(0, .09f, .03f, hasPointer) || !ProjectLayer(PointsPerLayer, .06f, 0f, hasPointer))
                return;

            Vector2 min = points[0];
            Vector2 max = min;
            for (int i = 1; i < count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }
            Rect canvas = rectTransform.rect;
            Vector2 inset = Vector2.one * 12f;
            Vector2 span = Vector2.Max(Vector2.one, max - min);
            float scale = Mathf.Clamp01(Mathf.Min((canvas.width - inset.x * 2f) / span.x,
                (canvas.height - inset.y * 2f) / span.y));
            Vector2 center = (min + max) * .5f;
            Vector2 half = (max - min) * (scale * .5f);
            Vector2 fittedMin = canvas.min + inset + half;
            Vector2 fittedMax = canvas.max - inset - half;
            // At close zoom the character's feet can fall partly below the view.
            Vector2 fittedCenter = new Vector2(Mathf.Clamp(center.x, fittedMin.x, fittedMax.x),
                Mathf.Clamp(center.y, fittedMin.y, fittedMax.y));
            Vector2 offset = fittedCenter - center * scale;

            AddLayer(mesh, 0, count, new Color32(42, 31, 25, 225), offset, scale);
            AddLayer(mesh, PointsPerLayer, count, color, offset, scale);
        }

        private bool ProjectLayer(int start, float thickness, float border, bool hasPointer)
        {
            for (int i = 0; i < Segments; i++)
            {
                float angle = 2f * Mathf.PI * i / Segments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                int index = start + i * 2;
                if (!Project(origin + radial * (Radius - thickness), out points[index]) ||
                    !Project(origin + radial * (Radius + border), out points[index + 1]))
                    return false;
            }
            if (!hasPointer)
                return true;
            int pointer = start + Segments * 2;
            Vector3 side = Vector3.Cross(Vector3.up, Direction) * (.19f + border);
            Vector3 shoulder = origin + Direction * (Radius - thickness);
            return Project(shoulder - side, out points[pointer]) && Project(shoulder + side, out points[pointer + 1]) &&
                Project(origin + Direction * (1.14f + border * 2f), out points[pointer + 2]);
        }

        private bool Project(Vector3 world, out Vector2 local)
        {
            local = default;
            Vector3 screen = worldCamera.WorldToScreenPoint(world);
            return WorkerbornInput.IsFinite(screen) && screen.z >= worldCamera.nearClipPlane &&
                screen.z <= worldCamera.farClipPlane &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out local);
        }

        private void AddLayer(VertexHelper mesh, int start, int count, Color32 tint, Vector2 offset, float scale)
        {
            int first = mesh.currentVertCount;
            for (int i = 0; i < count; i++)
                mesh.AddVert(points[start + i] * scale + offset, tint, Vector2.zero);
            for (int i = 0; i < Segments; i++)
            {
                int a = first + i * 2;
                int b = first + (i + 1) % Segments * 2;
                mesh.AddTriangle(a, a + 1, b + 1);
                mesh.AddTriangle(a, b + 1, b);
            }
            if (count == PointsPerLayer)
                mesh.AddTriangle(first + count - 3, first + count - 2, first + count - 1);
        }
    }
}
