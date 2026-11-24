using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class NpcSchedulePoint : MonoBehaviour
    {
        [SerializeField, Tooltip("Unique map ID referenced by NPC schedule entries.")] private string id;
        internal string Id => id;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, .35f);
        }
    }
}
