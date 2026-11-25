using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VillageNavigationBlocker : MonoBehaviour
    {
        internal const int Layer = 8;
        [SerializeField] private bool carvesAtRuntime;
        internal bool CarvesAtRuntime => carvesAtRuntime;
        public BoxCollider Volume => GetComponent<BoxCollider>();

        internal void EnableCarving()
        {
            var volume = Volume;
            var obstacle = GetComponent<NavMeshObstacle>() ?? gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = volume.center;
            obstacle.size = volume.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
            obstacle.enabled = volume.enabled;
        }

        private void Reset() { gameObject.layer = Layer; GetComponent<BoxCollider>().isTrigger = false; }
    }
}
