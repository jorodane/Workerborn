using UnityEngine;

namespace Workerborn
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VillageNavigationBlocker : MonoBehaviour
    {
        public BoxCollider Volume => GetComponent<BoxCollider>();
        private void Reset() { gameObject.layer = 8; GetComponent<BoxCollider>().isTrigger = false; }
    }
}
