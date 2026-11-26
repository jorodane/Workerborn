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
        [SerializeField, Tooltip("Offset from the seat surface to the root of the seated pose, in the character's facing axes.")]
        private Vector3 seatedRootOffset;
        [SerializeField, Min(.1f)] private float approachWidth = .8f;
        [SerializeField, Min(.1f)] private float approachDepth = 1.1f;
        [SerializeField, Min(.1f)] private float heightTolerance = .65f;
        [SerializeField, Tooltip("Allows sitting from any side and faces the direction of approach.")]
        private bool radial;

        internal Vector3 Position => transform.position + Facing * seatOffset;
        internal Quaternion Facing => Quaternion.Euler(0f, transform.eulerAngles.y + facingDegrees, 0f);
        internal Vector3 ApproachPosition => Position + Facing * new Vector3(0f, seatedRootOffset.y, approachDepth * .75f);
        internal Vector3 AnimationRoot(Quaternion facing) => Position + facing * seatedRootOffset;
        internal void SetSurfaceHeight(float height) => seatOffset.y = height;

        internal Quaternion FacingFor(Vector3 approach, Quaternion fallback)
        {
            if (!radial)
                return Facing;
            Vector3 direction = approach - Position;
            direction.y = 0f;
            return direction.sqrMagnitude > .0025f ? Quaternion.LookRotation(direction) : fallback;
        }

        internal bool Covers(Vector3 position)
        {
            Vector3 local = Quaternion.Inverse(Facing) * (position - Position);
            local.y -= seatedRootOffset.y;
            if (radial)
                return Mathf.Abs(local.y) <= heightTolerance && new Vector2(local.x, local.z).sqrMagnitude <= approachDepth * approachDepth;
            return Mathf.Abs(local.x) <= approachWidth * .5f && Mathf.Abs(local.y) <= heightTolerance &&
                local.z >= 0f && local.z <= approachDepth;
        }

        private void OnDrawGizmosSelected()
        {
            var matrix = Gizmos.matrix;
            var color = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(Position, Facing, Vector3.one);
            Gizmos.color = Color.yellow;
            var approachBase = Vector3.up * seatedRootOffset.y;
            if (radial)
                Gizmos.DrawWireSphere(approachBase, approachDepth);
            else
            {
                Gizmos.DrawWireCube(approachBase + Vector3.forward * (approachDepth * .5f),
                    new Vector3(approachWidth, heightTolerance * 2f, approachDepth));
                Gizmos.DrawLine(approachBase, approachBase + Vector3.forward * approachDepth);
            }
            Gizmos.DrawSphere(Vector3.zero, .06f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Vector3.zero, seatedRootOffset);
            Gizmos.DrawWireSphere(seatedRootOffset, .06f);
            Gizmos.matrix = matrix;
            Gizmos.color = color;
        }
    }
}
