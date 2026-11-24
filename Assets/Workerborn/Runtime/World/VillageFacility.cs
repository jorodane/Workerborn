using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class VillageFacility : MonoBehaviour
    {
        [SerializeField, Tooltip("Schedules can use any active facility of this type.")]
        private VillageFacilityType facilityType;
        [SerializeField, Tooltip("Optional standing position within this facility. Seats and crops are found among its children.")]
        private Transform standingPoint;

        internal VillageFacilityType FacilityType => facilityType;
        internal Transform StandingPoint => standingPoint != null ? standingPoint : transform;

        internal void Bind(VillageFacilityType type) => facilityType = type;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(StandingPoint.position, .35f);
        }
    }
}
