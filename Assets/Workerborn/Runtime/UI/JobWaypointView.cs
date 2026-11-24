using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class JobWaypointView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private Sprite pin;
        [SerializeField] private Sprite arrow;

        internal void Display(Camera camera, RectTransform canvas, WorldInteractable target)
        {
            gameObject.SetActive(target != null);
            if (target == null)
                return;

            Vector3 screen = camera.WorldToScreenPoint(target.NamePosition);
            bool finite = WorkerbornInput.IsFinite(screen);
            Vector2 point = Vector2.zero;
            if (finite)
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out point);

            Rect safe = canvas.rect;
            safe.xMin += 34f;
            safe.xMax -= 34f;
            safe.yMin += 94f;
            safe.yMax -= 34f;
            Vector2 aboveName = point + Vector2.up * 32f;
            bool atTarget = finite && screen.z >= camera.nearClipPlane && screen.z <= camera.farClipPlane &&
                safe.Contains(aboveName);
            var rect = (RectTransform)transform;
            icon.sprite = atTarget ? pin : arrow;
            if (atTarget)
            {
                rect.localPosition = aboveName;
                rect.localRotation = Quaternion.identity;
                return;
            }

            Vector2 direction = finite ? point - canvas.rect.center : Vector2.down;
            if (screen.z < 0f)
                direction = -direction;
            if (direction.sqrMagnitude < 1f)
                direction = Vector2.down;
            Vector2 edge = Vector2.Max(Vector2.one, safe.size * .5f);
            float reach = Mathf.Max(Mathf.Abs(direction.x) / edge.x, Mathf.Abs(direction.y) / edge.y);
            rect.localPosition = safe.center + direction / reach;
            rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }
    }
}
