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

        public string MapName => mapName;
        internal Transform Spawn => spawn;
        internal VillageInteractable Guide => guide;
        internal BoxCollider Floor => navigation != null ? navigation.GroundBox : null;
        internal Bounds Area => Floor.bounds;
        internal bool IsGround(Collider hit) => navigation != null && navigation.IsGround(hit);

        internal void Initialize(VillagePrototype village)
        {
            if (navigation != null)
                navigation.Activate(useBakedData: false);
            if (guide != null && guide.TryGetComponent<CharacterActor>(out var actor))
                village.BindNpcCharacter(actor, inClouds: true);
        }

        internal bool Contains(float x, float z)
        {
            if (Floor == null)
                return false;
            var bounds = Area;
            return x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z;
        }
    }
}
