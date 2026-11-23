using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        [Header("Rebirth map")]
        [SerializeField] private RebirthZone rebirthZone;

        private Vector3 villageReturnPosition;
        private Vector3 villageMinimapPosition;
        private float villageMinimapSize;
        private bool reloadingSavedGame;

        public bool InRebirthZone { get; private set; }
        internal RebirthZone RebirthArea => rebirthZone;
        internal bool SharesMap(Vector3 position) => InRebirthZone == (rebirthZone?.Contains(position.x, position.z) == true);

        internal bool TryMapGround(Ray ray, out RaycastHit hit)
        {
            Collider ground = InRebirthZone ? rebirthZone.Floor : navigation.GroundBox;
            if (!InRebirthZone && GroundTerrain != null)
                ground = GroundTerrain.GetComponent<TerrainCollider>();
            hit = default;
            return ground != null && ground.Raycast(ray, out hit, 300f);
        }

        public void VisitRebirthZone()
        {
            if (!initialized || InRebirthZone || rebirthZone == null || rebirthZone.Spawn == null)
                return;
            Vector3 returnPosition = worker.StandingPosition;
            if (Travel(rebirthZone.Spawn.position, toRebirthZone: true))
            {
                villageReturnPosition = returnPosition;
                SaveGame(false);
            }
        }

        public void LeaveRebirthZone()
        {
            if (!InRebirthZone)
                return;
            if (Travel(villageReturnPosition, toRebirthZone: false) || Travel(spawnPoint.position, toRebirthZone: false))
                SaveGame(false);
        }

        private bool Travel(Vector3 destination, bool toRebirthZone)
        {
            Vector3 surface = SurfacePosition(destination.x, destination.z);
            if (!NavMesh.SamplePosition(surface, out var landing, 2f, NavMesh.AllAreas) || !worker.WarpTo(landing.position))
                return false;
            InRebirthZone = toRebirthZone;
            if (toRebirthZone)
                worker.transform.rotation = rebirthZone.Spawn.rotation;
            hud.CloseDialogue();
            hud.CloseWorldContext();
            hud.CancelItemDrag();
            hud.HideItemTooltip();
            targeting?.RefreshStaticGeometry();
            cameraRig.SnapToTarget();
            UpdateRegionMap();
            return true;
        }

        private void UpdateRegionMap()
        {
            if (minimapCamera == null)
                return;
            if (InRebirthZone)
            {
                Bounds area = rebirthZone.Area;
                minimapCamera.transform.position = area.center + Vector3.up * 32f;
                minimapCamera.orthographicSize = Mathf.Max(area.extents.x, area.extents.z) + 2f;
            }
            else
            {
                minimapCamera.transform.position = villageMinimapPosition;
                minimapCamera.orthographicSize = villageMinimapSize;
            }
        }

        public void FinishGuideVisit()
        {
            if (!State.NeedsGuide)
                return;
            State.FinishGuideVisit();
            SaveGame(false);
        }
    }
}
