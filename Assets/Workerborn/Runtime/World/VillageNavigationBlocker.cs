using UnityEngine;

namespace Workerborn
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VillageNavigationBlocker : MonoBehaviour
    {
        internal const int Layer = 8;
        public BoxCollider Volume => GetComponent<BoxCollider>();
        private void Reset() { gameObject.layer = Layer; GetComponent<BoxCollider>().isTrigger = false; }
    }
}
