using System;
using UnityEngine;

namespace Workerborn
{
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class LootFlightTrail : MonoBehaviour
    {
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
                float t = Mathf.Clamp01(age / collectionSeconds);
                Vector3 end = receiver.position + Vector3.up * .86f;
                transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .36f);
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
            fading = true;
            fadeRemaining = trail.time;
            return fadeRemaining <= 0f;
        }

        internal void ResetTrail()
        {
            drop = default;
            receiver = null;
            onArrival = null;
            age = 0;
            fadeRemaining = 0;
            fading = false;
            trail.emitting = false;
            trail.Clear();
            if (glow != null)
                glow.SetActive(false);
        }
    }
}
