using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        [Header("Building interiors")]
        [SerializeField, Tooltip("Origin of the isolated interior maps. Place it beyond view of the village and clouds.")]
        private Transform interiorsRoot;

        // Leave enough space for each room and its camera view.
        private const float InteriorSpacing = 240f;
        private const int InteriorsPerRow = 12;
        private readonly Dictionary<string, VillageInteriorMap> interiors = new Dictionary<string, VillageInteriorMap>();
        private readonly Dictionary<VillagePlot, int> plotInteriorSlots = new Dictionary<VillagePlot, int>();
        public VillageInteriorMap CurrentInterior { get; private set; }

        private void BindBuildingDoors()
        {
            var doors = mapRoot.GetComponentsInChildren<VillageBuildingDoor>(true);
            Array.Sort(doors, (a, b) => string.CompareOrdinal(a.BuildingId, b.BuildingId));
            int slot = 0;
            foreach (var door in doors)
                if (door.InteriorPrefab != null)
                {
                    var room = AddInterior(door, door.BuildingId, slot++);
                    if (room?.KeeperPoint != null)
                        PlaceInnkeeper(FindInteraction(VillageRole.Innkeeper), room);
                }
            var plots = new List<VillagePlot>(buildPlots.Values);
            plots.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var plot in plots)
                plotInteriorSlots.Add(plot, slot++);
        }

        internal VillageInteriorMap AddPlotInterior(VillagePlot plot, VillageBuildingDoor door)
        {
            if (door == null || !plotInteriorSlots.TryGetValue(plot, out int slot))
                return null;
            var lobby = AddInterior(door, $"plot/{plot.Id}/{door.BuildingId}", slot);
            if (lobby?.KeeperPoint != null)
                foreach (var npc in plot.GetComponentsInChildren<VillageInteractable>(true))
                    if (npc.Role == VillageRole.Innkeeper)
                        PlaceInnkeeper(npc, lobby);
            return lobby;
        }

        private static void PlaceInnkeeper(VillageInteractable npc, VillageInteriorMap lobby)
        {
            if (npc != null)
                npc.transform.SetPositionAndRotation(lobby.KeeperPoint.position, lobby.KeeperPoint.rotation);
        }

        private VillageInteriorMap AddInterior(VillageBuildingDoor door, string id, int slot)
        {
            var prefab = door.InteriorPrefab;
            if (prefab == null || !prefab.IsConfigured || door.Approach == null || interiorsRoot == null ||
                string.IsNullOrWhiteSpace(door.BuildingId) || interiors.ContainsKey(id))
            {
                Debug.LogError("Set a unique building ID, approach point, interior prefab and interior origin.", door);
                return null;
            }

            var offset = new Vector3(slot % InteriorsPerRow, 0f, slot / InteriorsPerRow * 2) * InteriorSpacing;
            var room = Instantiate(prefab, interiorsRoot.position + offset, Quaternion.identity, interiorsRoot);
            room.Bind(this, door, id);
            interiors.Add(id, room);
            door.Bind(this, room);
            if (room.GuestRoomPrefab is { IsConfigured: true, Bed: not null } guestPrefab)
            {
                var guest = Instantiate(guestPrefab, room.transform.position + Vector3.forward * InteriorSpacing,
                    Quaternion.identity, interiorsRoot);
                guest.Bind(this, door, id + "/guest", room);
                room.GuestRoom = guest;
                interiors.Add(guest.Id, guest);
            }
            return room;
        }

        internal bool RemovePlotInterior(VillageInteriorMap room)
        {
            if (room == null)
                return true;
            if (CurrentInterior == room || CurrentInterior?.Lobby == room)
            {
                if (!TryWarpToMap(room.Entrance.Approach.position) && !TryWarpToMap(spawnPoint.position))
                    return false;
                RefreshAfterTravel();
            }
            room.Entrance.Unbind();
            if (room.GuestRoom != null)
            {
                interiors.Remove(room.GuestRoom.Id);
                room.GuestRoom.gameObject.SetActive(false);
                Destroy(room.GuestRoom.gameObject);
            }
            interiors.Remove(room.Id);
            room.gameObject.SetActive(false);
            Destroy(room.gameObject);
            return true;
        }

        internal bool UseBuildingDoor(VillageBuildingDoor door)
        {
            if (door == null || !door.CanInteract || !door.IsOpen || ControlsBlocked ||
                State.AwaitingIncarnation || !SharesMap(door.transform.position) ||
                !worker.InReach(door.Approach.position, 1f))
                return false;

            var room = door.Interior;
            bool exiting = door.IsExit;
            if (exiting && CurrentInterior != room || !exiting && CurrentInterior != null)
                return false;
            var destination = exiting ? room.Lobby : room;
            Transform arrival = destination != null ? destination.Arrival : room.Entrance.Approach;
            return TravelToInterior(arrival, destination);
        }

        internal bool CanEnterGuestRoom(VillageInteractable keeper) => keeper != null &&
            keeper.Role == VillageRole.Innkeeper && CurrentInterior?.GuestRoom != null &&
            CurrentInterior.Contains(keeper.transform.position.x, keeper.transform.position.z);

        internal void EnterGuestRoom(VillageInteractable keeper)
        {
            if (!CanEnterGuestRoom(keeper) || !hud.IsSpeakingWith(keeper) || AwaitingWorldCommand || State.AwaitingIncarnation)
                return;
            var room = CurrentInterior.GuestRoom;
            TravelToInterior(room.Arrival, room);
        }

        private bool TravelToInterior(Transform arrival, VillageInteriorMap destination)
        {
            return TryWorldTransition(() =>
            {
                if (arrival == null || !TryWarpToMap(arrival.position, destination))
                {
                    hud.ShowToast("반대편에 설 수 있는 곳이 없어요.");
                    return;
                }
                worker.transform.rotation = arrival.rotation;
                RefreshAfterTravel();
                SaveGame(false);
            });
        }

        private VillageInteriorMap GuestRoomForRecovery()
        {
            if (CurrentInterior?.Bed != null)
                return CurrentInterior;
            if (CurrentInterior?.GuestRoom != null)
                return CurrentInterior.GuestRoom;
            if (interiors.TryGetValue("inn", out var inn) && inn.GuestRoom != null)
                return inn.GuestRoom;
            foreach (var room in interiors.Values)
                if (room.GuestRoom != null)
                    return room.GuestRoom;
            return null;
        }

        private VillageInteriorMap InteriorAt(float x, float z)
        {
            if (CurrentInterior != null && CurrentInterior.Contains(x, z))
                return CurrentInterior;
            foreach (var room in interiors.Values)
                if (room.Contains(x, z))
                    return room;
            return null;
        }

        internal bool SharesMap(Vector3 position)
        {
            if (CurrentInterior != null)
                return CurrentInterior.Contains(position.x, position.z);
            bool clouds = rebirthZone?.Contains(position.x, position.z) == true;
            return InRebirthZone ? clouds : !clouds && InteriorAt(position.x, position.z) == null;
        }

        private bool TryWarpToMap(Vector3 destination, VillageInteriorMap room = null, bool toClouds = false)
        {
            Vector3 surface = SurfacePosition(destination.x, destination.z);
            if (!NavMesh.SamplePosition(surface, out var landing, 2f, NavMesh.AllAreas))
                return false;
            Vector3 point = landing.position;
            bool cloudPosition = rebirthZone?.Contains(point.x, point.z) == true;
            if (toClouds != cloudPosition || InteriorAt(point.x, point.z) != room || !worker.WarpTo(point))
                return false;
            CurrentInterior = room;
            InRebirthZone = toClouds;
            lighting?.SetInteriorBackground(room != null);
            return true;
        }

        private bool TryRestoreInterior(InteriorPositionSave save, out VillageInteriorMap room, out Vector3 position)
        {
            room = null;
            position = default;
            if (string.IsNullOrEmpty(save.id) || !interiors.TryGetValue(save.id, out var candidate))
                return false;
            Vector3 point = candidate.RestorePosition(save);
            if (!candidate.Contains(point.x, point.z))
                return false;
            room = candidate;
            position = point;
            return true;
        }

        private Vector3 ExteriorPosition(VillageInteriorMap room) =>
            room != null && room.Entrance != null ? room.Entrance.Approach.position : spawnPoint.position;
    }
}
