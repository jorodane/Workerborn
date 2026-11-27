using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class WindowTransition : MonoBehaviour
    {
        [SerializeField, Tooltip("For movable windows, use a child containing the artwork and controls.")]
        private RectTransform motion;
        [SerializeField] private CanvasGroup opacity;
        [Header("Opening")]
        [SerializeField, Min(0f)] private float duration = .28f;
        [SerializeField] private Vector2 startOffset;
        [SerializeField] private Vector2 startScale = Vector2.one;
        [SerializeField, Range(0f, 1f)] private float startOpacity;
        [SerializeField] private AnimationCurve easing;
        [SerializeField] private AnimationCurve popScaleEasing;
        [Header("Closing")]
        [SerializeField, Min(0f)] private float closeDuration;
        [SerializeField] private Vector2 endOffset;
        [SerializeField] private Vector2 endScale = Vector2.one;
        [SerializeField, Range(0f, 1f)] private float endOpacity;
        [SerializeField] private AnimationCurve closeEasing;

        private struct Pose
        {
            public Vector2 Position;
            public Vector3 Scale;
            public float Alpha;
        }

        private Pose rest, from, to;
        private bool hasRest;
        private float startedAt;
        private float playingDuration;
        private AnimationCurve playingCurve;
        private Vector2? motionOrigin;
        private bool openingFromOrigin;
        private bool hideAfterClosing;
        private CanvasGroup inputGate;
        private bool savedInteractable, savedBlocksRaycasts;
        internal bool IsClosing { get; private set; }
        internal bool IsPlaying { get; private set; }

        internal void PlayOpening()
        {
            if (!isActiveAndEnabled)
                return;
            bool reopening = IsClosing;
            IsClosing = false;
            RestoreInput();
            if (duration <= 0f || (motion == null && opacity == null))
            {
                ResetPose();
                return;
            }

            CaptureRest();
            openingFromOrigin = !reopening;
            var start = reopening ? ReadPose() : OffsetRest(motionOrigin ?? startOffset, startScale, startOpacity);
            Begin(start, rest, duration, easing);
        }

        internal bool PlayClosing(bool hideOnComplete = true)
        {
            if (!isActiveAndEnabled || closeDuration <= 0f || (motion == null && opacity == null))
                return false;
            if (IsClosing)
                return true;

            CaptureRest();
            IsClosing = true;
            hideAfterClosing = hideOnComplete;
            if (hideOnComplete)
                BlockInput();
            Begin(ReadPose(), OffsetRest(motionOrigin ?? endOffset, endScale, endOpacity), closeDuration, closeEasing);
            return true;
        }

        internal void SetMotionOrigin(Vector2? offset)
        {
            motionOrigin = offset;
            if (!IsPlaying)
                return;
            if (IsClosing)
                to.Position = rest.Position + (offset ?? endOffset);
            else if (openingFromOrigin)
                from.Position = rest.Position + (offset ?? startOffset);
        }

        private void Begin(Pose start, Pose end, float seconds, AnimationCurve curve)
        {
            from = start;
            to = end;
            playingDuration = seconds;
            playingCurve = curve;
            startedAt = Time.unscaledTime;
            IsPlaying = true;
            Apply(start);
        }

        private void CaptureRest()
        {
            if (hasRest)
                return;
            rest = ReadPose();
            hasRest = true;
        }

        private Pose ReadPose() => new Pose
        {
            Position = motion != null ? motion.anchoredPosition : Vector2.zero,
            Scale = motion != null ? motion.localScale : Vector3.one,
            Alpha = opacity != null ? opacity.alpha : 1f
        };

        private Pose OffsetRest(Vector2 offset, Vector2 scale, float alpha) => new Pose
        {
            Position = rest.Position + offset,
            Scale = Vector3.Scale(rest.Scale, new Vector3(scale.x, scale.y, 1f)),
            Alpha = rest.Alpha * alpha
        };

        private void LateUpdate()
        {
            if (!IsPlaying)
                return;
            float elapsed = (Time.unscaledTime - startedAt) / playingDuration;
            if (elapsed >= 1f)
            {
                Apply(to);
                IsPlaying = false;
                if (!IsClosing)
                    hasRest = false;
                else if (hideAfterClosing)
                    gameObject.SetActive(false);
                return;
            }
            float progress = playingCurve == null || playingCurve.length == 0
                ? Mathf.SmoothStep(0f, 1f, elapsed)
                : Mathf.Clamp01(playingCurve.Evaluate(elapsed));
            float scaleProgress = progress;
            if (popScaleEasing != null && popScaleEasing.length > 0)
                scaleProgress = IsClosing ? 1f - popScaleEasing.Evaluate(1f - elapsed) : popScaleEasing.Evaluate(elapsed);
            Apply(new Pose
            {
                Position = Vector2.Lerp(from.Position, to.Position, progress),
                Scale = Vector3.LerpUnclamped(from.Scale, to.Scale, scaleProgress),
                Alpha = Mathf.Lerp(from.Alpha, to.Alpha, progress)
            });
        }

        private void Apply(Pose pose)
        {
            if (motion != null)
            {
                motion.anchoredPosition = pose.Position;
                motion.localScale = pose.Scale;
            }
            if (opacity != null)
                opacity.alpha = pose.Alpha;
        }

        private void BlockInput()
        {
            inputGate = GetComponent<CanvasGroup>();
            if (inputGate == null)
                inputGate = gameObject.AddComponent<CanvasGroup>();
            savedInteractable = inputGate.interactable;
            savedBlocksRaycasts = inputGate.blocksRaycasts;
            inputGate.interactable = false;
            inputGate.blocksRaycasts = false;
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(transform))
                events.SetSelectedGameObject(null);
        }

        private void RestoreInput()
        {
            if (inputGate == null)
                return;
            inputGate.interactable = savedInteractable;
            inputGate.blocksRaycasts = savedBlocksRaycasts;
            inputGate = null;
        }

        private void ResetPose()
        {
            if (hasRest)
                Apply(rest);
            hasRest = false;
            IsPlaying = false;
        }

        private void OnDisable()
        {
            ResetPose();
            motionOrigin = null;
            IsClosing = false;
            RestoreInput();
        }
    }
}
