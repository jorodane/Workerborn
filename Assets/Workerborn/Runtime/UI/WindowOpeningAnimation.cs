using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class WindowOpeningAnimation : MonoBehaviour
    {
        [SerializeField, Tooltip("For movable windows, use a child containing the artwork and controls.")]
        private RectTransform motion;
        [SerializeField] private CanvasGroup opacity;
        [SerializeField, Min(0f)] private float duration = .28f;
        [SerializeField] private Vector2 startOffset;
        [SerializeField] private Vector2 startScale = Vector2.one;
        [SerializeField, Range(0f, 1f)] private float startOpacity;
        [SerializeField] private AnimationCurve easing;
        private Vector2 position;
        private Vector3 scale;
        private float alpha;
        private float startedAt;
        private bool playing;

        internal void Play()
        {
            Finish();
            if (!isActiveAndEnabled || duration <= 0f)
                return;
            if (motion != null)
            {
                position = motion.anchoredPosition;
                scale = motion.localScale;
            }
            if (opacity != null)
                alpha = opacity.alpha;
            startedAt = Time.unscaledTime;
            playing = true;
            Apply(0f);
        }

        private void LateUpdate()
        {
            if (!playing)
                return;
            float elapsed = (Time.unscaledTime - startedAt) / duration;
            if (elapsed >= 1f)
            {
                Finish();
                return;
            }
            float progress = easing == null || easing.length == 0
                ? Mathf.SmoothStep(0f, 1f, elapsed)
                : Mathf.Clamp01(easing.Evaluate(elapsed));
            Apply(progress);
        }

        private void Apply(float progress)
        {
            if (motion != null)
            {
                motion.anchoredPosition = position + startOffset * (1f - progress);
                Vector2 factor = Vector2.Lerp(startScale, Vector2.one, progress);
                motion.localScale = new Vector3(scale.x * factor.x, scale.y * factor.y, scale.z);
            }
            if (opacity != null)
                opacity.alpha = alpha * Mathf.Lerp(startOpacity, 1f, progress);
        }

        private void OnDisable() => Finish();

        private void Finish()
        {
            if (!playing)
                return;
            Apply(1f);
            playing = false;
        }
    }
}
