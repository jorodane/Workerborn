using UnityEngine;

namespace Workerborn
{
    public sealed class VillageMapProp : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        private void Awake()
        {
            if (visual == null) return;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
        }
    }
}
