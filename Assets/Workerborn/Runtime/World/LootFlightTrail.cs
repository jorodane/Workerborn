using System;
using UnityEngine;

namespace Workerborn
{
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class LootFlightTrail : MonoBehaviour
    {
        internal const float ReceiverHeight = .86f;
        private const float PurchaseSeconds = .60f;
        [SerializeField] private GameObject glow;
        [SerializeField, Min(.01f)] private float collectionSeconds = .26f;
        private TrailRenderer trail;
        private WorldTargetReference drop;
        private Transform receiver;
        private Action<Vector3> onArrival;
        private Vector3 start;
        private float age;
        private float fadeRemaining;
        private bool fading;
        private PurchaseArc? purchaseArc;

        private void Awake()
        {
            trail = GetComponent<TrailRenderer>();
            trail.autodestruct = false;
        }

        public void FollowDrop(GroundPickup item)
        {
            ResetTrail();
            drop = new WorldTargetReference(item);
            trail.emitting = true;
        }

        public void FlyTo(Transform target, Action<Vector3> arrived)
        {
            ResetTrail();
            start = transform.position;
            receiver = target;
            onArrival = arrived;
            if (glow != null)
                glow.SetActive(true);
            trail.emitting = true;
        }

        internal void FlyPurchaseTo(Transform target, Action<Vector3> arrived)
        {
            FlyTo(target, arrived);
            purchaseArc = new PurchaseArc(start, target.position + Vector3.up * ReceiverHeight,
                new Vector2(UnityEngine.Random.value, UnityEngine.Random.value));
        }

        internal bool Advance(float deltaTime)
        {
            if (fading)
            {
                fadeRemaining -= deltaTime;
                return fadeRemaining <= 0f;
            }
            if (drop.Target is GroundPickup pickup && pickup.IsInFlight)
            {
                transform.position = pickup.VisualPosition;
                return false;
            }
            if (receiver != null)
            {
                age += deltaTime;
                float t = Mathf.Clamp01(age / (purchaseArc.HasValue ? PurchaseSeconds : collectionSeconds));
                Vector3 end = receiver.position + Vector3.up * ReceiverHeight;
                transform.position = purchaseArc is PurchaseArc arc
                    ? arc.PositionAt(t) + (end - arc.End) * t
                    : Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .36f);
                if (t < 1)
                    return false;
                onArrival?.Invoke(receiver.position);
            }

            trail.emitting = false;
            if (glow != null)
                glow.SetActive(false);
            drop = default;
            receiver = null;
            onArrival = null;
            purchaseArc = null;
            fading = true;
            fadeRemaining = trail.time;
            return fadeRemaining <= 0f;
        }

        internal void ResetTrail()
        {
            drop = default;
            receiver = null;
            onArrival = null;
            purchaseArc = null;
            age = 0;
            fadeRemaining = 0;
            fading = false;
            trail.emitting = false;
            trail.Clear();
            if (glow != null)
                glow.SetActive(false);
        }

        internal readonly struct PurchaseArc
        {
            private readonly Vector3 center, from, via, to;
            internal Vector3 End => center + to;
            internal float WaypointProgress { get; }

            internal PurchaseArc(Vector3 start, Vector3 end, Vector2 sample)
            {
                Vector3 span = end - start;
                float distance = span.magnitude;
                center = (start + end) * .5f;
                Vector3 axis = distance >= .01f ? span / distance : Vector3.right;
                if (distance < .01f)
                    center -= Vector3.forward * .3f;
                from = start - center;
                to = end - center;

                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
                if (up.sqrMagnitude < .001f)
                    up = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
                Vector3 side = Vector3.Cross(axis, up);
                // Sample the middle of the hemisphere, outside both caps around the endpoints.
                float along = Mathf.Lerp(-.5f, .5f, sample.x);
                float radius = Mathf.Max(.55f, distance * .5f);
                float bank = Mathf.Lerp(-60f, 60f, sample.y) * Mathf.Deg2Rad;
                Vector3 crossSection = Mathf.Sqrt(1f - along * along) * (up * Mathf.Cos(bank) + side * Mathf.Sin(bank));
                Vector3 direction = axis * along + crossSection;
                if (direction.y < 0f)
                    direction = crossSection - axis * along;
                via = direction * radius;

                float firstAngle = Vector3.Angle(from, via);
                float totalAngle = firstAngle + Vector3.Angle(via, to);
                WaypointProgress = totalAngle > 0f ? Mathf.Clamp(firstAngle / totalAngle, .15f, .85f) : .5f;
            }

            internal Vector3 PositionAt(float progress)
            {
                return center + (progress <= WaypointProgress
                    ? Vector3.Slerp(from, via, progress / WaypointProgress)
                    : Vector3.Slerp(via, to, (progress - WaypointProgress) / (1f - WaypointProgress)));
            }
        }
    }
}
