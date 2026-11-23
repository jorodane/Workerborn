using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class RebirthZone : MonoBehaviour
    {
        [SerializeField] private string mapName = "구름 위";
        [SerializeField] private VillageNavigation navigation;
        [SerializeField] private Transform spawn;
        [SerializeField] private VillageInteractable guide;
        [SerializeField] private Material daySky;

        public string MapName => mapName;
        internal Transform Spawn => spawn;
        internal VillageInteractable Guide => guide;
        internal Material DaySky => daySky;
        internal MeshCollider Floor => navigation != null ? navigation.GroundMesh : null;
        internal Bounds Area => Floor.bounds;
        internal bool IsGround(Collider hit) => navigation != null && navigation.IsGround(hit);

        internal void Initialize()
        {
            if (navigation != null)
                navigation.Activate(useBakedData: false);
            if (guide != null && guide.TryGetComponent<CharacterActor>(out var actor))
                VillagePrototype.BindCharacter(actor, idleOnly: true);
        }

        internal bool Contains(float x, float z)
        {
            if (Floor == null)
                return false;
            var bounds = Area;
            return x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z;
        }

        internal float SurfaceHeight(float x, float z)
        {
            var bounds = Area;
            var top = new Vector3(x, bounds.max.y + 1f, z);
            return Floor.Raycast(new Ray(top, Vector3.down), out var hit, bounds.size.y + 2f) ? hit.point.y : bounds.center.y;
        }
    }
}
