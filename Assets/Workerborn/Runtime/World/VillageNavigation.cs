using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed class VillageNavigation : MonoBehaviour
    {
        [SerializeField] private BoxCollider ground;
        [SerializeField] private Transform obstacleRoot;
        [SerializeField] private NavMeshData bakedData;
        private NavMeshData generated;
        private NavMeshDataInstance instance;

        public NavMeshData BuildData()
        {
            if (ground == null) { Debug.LogError("Assign the authored ground collider to VillageNavigation.", this); return null; }
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = ground.transform.localToWorldMatrix * Matrix4x4.Translate(ground.center), size = ground.size, area = 0 }
            };
            if (obstacleRoot != null)
                foreach (var marker in obstacleRoot.GetComponentsInChildren<VillageNavigationBlocker>(true))
                {
                    if (!marker.isActiveAndEnabled || marker.Volume == null || !marker.Volume.enabled) continue;
                    var volume = marker.Volume;
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox,
                        transform = volume.transform.localToWorldMatrix * Matrix4x4.Translate(volume.center), size = volume.size, area = 1 });
                }
            var bounds = ground.bounds; bounds.Expand(new Vector3(2f, 12f, 2f));
            return NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
        }

        public void Activate()
        {
            if (instance.valid) instance.Remove();
            var data = bakedData;
            if (data == null) data = generated = BuildData();
            if (data != null) instance = NavMesh.AddNavMeshData(data);
        }

        public void SetBakedData(NavMeshData data) => bakedData = data;
        private void OnDestroy()
        {
            if (instance.valid) instance.Remove();
            if (generated != null) Destroy(generated);
        }
    }
}
