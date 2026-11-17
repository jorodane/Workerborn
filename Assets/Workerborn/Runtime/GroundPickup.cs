using UnityEngine;

namespace Workerborn
{
    public sealed class GroundPickup : MonoBehaviour
    {
        private Vector3 origin;
        private Vector3 landing;
        private Transform visual;
        private float age;
        private bool collected;
        private const float FlightSeconds = 0.48f;

        public CropDefinition Definition { get; private set; }
        public bool CanCollect => !collected && age >= FlightSeconds && isActiveAndEnabled;
        public Vector3 LandingPosition => landing;

        public void Initialize(CropDefinition definition, Transform model, Vector3 start, Vector3 end, bool settled = false)
        {
            Definition = definition;
            visual = model;
            origin = start;
            landing = end;
            age = settled ? FlightSeconds : 0f;
            transform.position = start;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / FlightSeconds);
            transform.position = Vector3.Lerp(origin, landing, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.25f);
            if (visual == null) return;
            visual.localPosition = Vector3.up * (t < 1f ? 0f : 0.04f + Mathf.Sin(age * 3f) * 0.035f);
            visual.Rotate(Vector3.up, Time.deltaTime * 28f, Space.World);
        }

        public bool TryCollect()
        {
            if (!CanCollect) return false;
            collected = true;
            // Disable immediately: repeated commands cannot collect a second time this frame.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return true;
        }
    }
}
