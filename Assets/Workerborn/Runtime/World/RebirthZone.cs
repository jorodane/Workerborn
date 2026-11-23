using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class RebirthZone : MonoBehaviour
    {
        [SerializeField] private string mapName = "구름 정원";
        [SerializeField] private VillageNavigation navigation;
        [SerializeField] private Transform spawn;
        [SerializeField] private VillageInteractable guide;
        [SerializeField] private RebirthGate exit;

        public string MapName => mapName;
        internal Transform Spawn => spawn;
        internal VillageInteractable Guide => guide;
        internal RebirthGate Exit => exit;
        internal BoxCollider Floor => navigation != null ? navigation.GroundBox : null;
        internal Bounds Area => navigation.GroundBox.bounds;
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
            if (navigation == null || navigation.GroundBox == null)
                return false;
            var bounds = Area;
            return x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z;
        }
    }
}
