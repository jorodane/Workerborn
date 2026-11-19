using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed class VillageNavigation : MonoBehaviour
    {
        [SerializeField]
        private BoxCollider ground;
        [SerializeField]
        private Terrain terrain;
        public Terrain GroundTerrain => terrain;

        [SerializeField]
        private Transform obstacleRoot;
        [SerializeField, Tooltip("World space volume for the playable region. Zero size uses the full terrain.")]
        private Bounds buildBounds;
        [SerializeField]
        private NavMeshData bakedData;
        private NavMeshData generated;
        private NavMeshDataInstance instance;
        public NavMeshData BuildData()
        {
            if (ground == null && terrain == null)
            {
                Debug.LogError("Assign the authored Terrain or ground collider to VillageNavigation.", this);
                return null;
            }

            var sources = new List<NavMeshBuildSource>();
            Bounds bounds;
            if (terrain != null && terrain.terrainData != null)
            {
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain, sourceObject = terrain.terrainData, transform = terrain.transform.localToWorldMatrix, area = 0 });
                bounds = new Bounds(terrain.transform.position + terrain.terrainData.size * .5f, terrain.terrainData.size);
            }
            else
            {
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = ground.transform.localToWorldMatrix * Matrix4x4.Translate(ground.center), size = ground.size, area = 0 });
                bounds = ground.bounds;
            }

            if (obstacleRoot != null)
                foreach (var marker in obstacleRoot.GetComponentsInChildren<VillageNavigationBlocker>(true))
                {
                    if (!marker.isActiveAndEnabled || marker.Volume == null || !marker.Volume.enabled)
                        continue;
                    var volume = marker.Volume;
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, transform = volume.transform.localToWorldMatrix * Matrix4x4.Translate(volume.center), size = volume.size, area = 1 });
                }

            if (buildBounds.size.x > 0 && buildBounds.size.y > 0 && buildBounds.size.z > 0)
                bounds = buildBounds;
            else
                bounds.Expand(new Vector3(2f, 12f, 2f));
            return NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
        }

        public void Activate(bool useBakedData = true)
        {
            if (instance.valid)
                instance.Remove();
            if (generated != null)
            {
                Destroy(generated);
                generated = null;
            }
            var data = useBakedData ? bakedData : null;
            if (data == null)
                data = generated = BuildData();
            if (data != null)
                instance = NavMesh.AddNavMeshData(data);
        }

        public void SetBakedData(NavMeshData data) => bakedData = data;

        private void OnDrawGizmosSelected()
        {
            if (buildBounds.size == Vector3.zero)
                return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(buildBounds.center, buildBounds.size);
        }
        private void OnDestroy()
        {
            if (instance.valid)
                instance.Remove();
            if (generated != null)
                Destroy(generated);
        }
    }
}
