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
        private GroundPickup pickup;
        private Transform receiver;
        private Action<Vector3> onArrival;
        private Vector3 start;
        private float age;

        private void Awake() => trail = GetComponent<TrailRenderer>();

        public void FollowDrop(GroundPickup item)
        {
            pickup = item;
            if (glow != null)
                glow.SetActive(false);
            trail.Clear();
            trail.emitting = true;
        }

        public void FlyTo(Transform target, Action<Vector3> arrived)
        {
            start = transform.position;
            receiver = target;
            onArrival = arrived;
            if (glow != null)
                glow.SetActive(true);
            trail.Clear();
            trail.emitting = true;
        }

        private void LateUpdate()
        {
            if (pickup != null && pickup.IsInFlight)
            {
                transform.position = pickup.VisualPosition;
                return;
            }
            if (receiver != null)
            {
                age += Time.deltaTime;
                float t = Mathf.Clamp01(age / collectionSeconds);
                Vector3 end = receiver.position + Vector3.up * .86f;
                transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .36f);
                if (t < 1)
                    return;
                onArrival?.Invoke(receiver.position);
            }

            trail.emitting = false;
            if (glow != null)
                glow.SetActive(false);
            enabled = false;
            Destroy(gameObject, trail.time);
        }
    }
}
