using UnityEngine;

namespace Workerborn
{
    public sealed class FishingBobber : MonoBehaviour
    {
        private Vector3 start, waterPoint;
        private float castStartedAt, flightTime;

        internal void Launch(Vector3 origin, Vector3 destination)
        {
            start = origin;
            waterPoint = destination;
            flightTime = Mathf.Clamp(Vector3.Distance(start, waterPoint) * .09f, .34f, .72f);
            castStartedAt = Time.time;
            transform.position = start;
            gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            float elapsed = Time.time - castStartedAt;
            Vector3 waterline = waterPoint + Vector3.up * .035f;
            if (elapsed < flightTime)
            {
                float t = elapsed / flightTime;
                transform.position = Vector3.Lerp(start, waterline, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .95f);
                return;
            }
            transform.position = waterline + Vector3.up * (Mathf.Sin((elapsed - flightTime) * 3f) * .012f);
        }
    }
}
