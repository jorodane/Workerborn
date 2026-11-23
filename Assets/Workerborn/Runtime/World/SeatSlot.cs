using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class SeatSlot : MonoBehaviour
    {
        [SerializeField, Tooltip("Offset in world units from the seat along its facing axes.")]
        private Vector3 seatOffset;
        [SerializeField, Tooltip("Facing relative to the slot's rotation.")]
        private float facingDegrees;
        [SerializeField, Min(.1f)] private float approachWidth = .8f;
        [SerializeField, Min(.1f)] private float approachDepth = 1.1f;
        [SerializeField, Min(.1f)] private float heightTolerance = .65f;

        internal Vector3 Position => transform.position + Facing * seatOffset;
        internal Quaternion Facing => Quaternion.Euler(0f, transform.eulerAngles.y + facingDegrees, 0f);

        internal bool Covers(Vector3 position)
        {
            Vector3 local = Quaternion.Inverse(Facing) * (position - Position);
            return Mathf.Abs(local.x) <= approachWidth * .5f && Mathf.Abs(local.y) <= heightTolerance &&
                local.z >= 0f && local.z <= approachDepth;
        }

        private void OnDrawGizmosSelected()
        {
            var matrix = Gizmos.matrix;
            var color = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(Position, Facing, Vector3.one);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.forward * (approachDepth * .5f), new Vector3(approachWidth, heightTolerance * 2f, approachDepth));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * approachDepth);
            Gizmos.DrawSphere(Vector3.zero, .06f);
            Gizmos.matrix = matrix;
            Gizmos.color = color;
        }
    }
}
