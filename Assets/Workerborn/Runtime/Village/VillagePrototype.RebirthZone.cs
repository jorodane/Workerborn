using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        [Header("Rebirth map")]
        [SerializeField] private RebirthZone rebirthZone;

        private Vector3 villageMinimapPosition;
        private float villageMinimapSize;
        internal CharacterLook IncarnationLook { get; private set; }

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

        private void EnterClouds()
        {
            if (!State.AwaitingIncarnation)
                return;
            equipmentVersion = -1;
            player.ShowEquipment(State.Equipment, settings);
            IncarnationLook = player.CurrentLook.Validated();
            if (rebirthZone != null && rebirthZone.Spawn != null && Travel(rebirthZone.Spawn.position, toClouds: true))
            {
                SaveGame(false);
                return;
            }
            Debug.LogError("The rebirth area needs a walkable cloud surface and an arrival point.", this);
        }

        private void EnterVillage()
        {
            if (State.AwaitingIncarnation)
                return;
            player.ApplyProfileLook(State.LifeLook ?? IncarnationLook);
            if (!Travel(spawnPoint.position, toClouds: false))
                Debug.LogError("The village arrival point must be on walkable ground.", this);
        }

        internal void PreviewIncarnationLook(CharacterLook look)
        {
            if (!InRebirthZone || !State.AwaitingIncarnation || !hud.IsSpeakingWith(rebirthZone.Guide))
                return;
            IncarnationLook = look.Validated();
            player.ApplyProfileLook(IncarnationLook);
        }

        internal void ConfirmIncarnation() => PerformWorld(CampaignAction.Incarnate, value: IncarnationLook.ToSelection());

        private bool Travel(Vector3 destination, bool toClouds)
        {
            if (State.AwaitingIncarnation != toClouds)
                return false;
            Vector3 surface = SurfacePosition(destination.x, destination.z);
            if (!NavMesh.SamplePosition(surface, out var landing, 2f, NavMesh.AllAreas) || !worker.WarpTo(landing.position))
                return false;
            InRebirthZone = toClouds;
            if (toClouds)
                worker.transform.rotation = rebirthZone.Spawn.rotation;
            hud.CloseDialogue();
            hud.CloseWorldContext();
            hud.CancelItemDrag();
            hud.HideItemTooltip();
            hud.SetIncarnationPresentation();
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

    }
}
