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

        internal (MonoBehaviour Source, string Name, string Description) CurrentArea
        {
            get
            {
                if (CurrentInterior != null)
                    return (CurrentInterior, CurrentInterior.MapName, CurrentInterior.MapDescription);
                if (InRebirthZone && rebirthZone != null)
                    return (rebirthZone, rebirthZone.MapName, rebirthZone.MapDescription);
                return mapRegion != null ? (mapRegion, mapRegion.MapName, mapRegion.MapDescription) :
                    (this, VillageMapRegion.DefaultMapName, VillageMapRegion.DefaultMapDescription);
            }
        }

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
                guest.Bind(this, room.GuestRoomDoor != null ? room.GuestRoomDoor : door, id + "/guest", room);
                room.GuestRoom = guest;
                room.GuestRoomDoor?.Bind(this, guest);
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

        internal bool UseBuildingDoor(VillageBuildingDoor door) => UseBuildingDoor(worker, door);

        internal bool UseBuildingDoor(HarvestWorker traveler, VillageBuildingDoor door)
        {
            if (traveler.CarriedLogs > 0)
            {
                TimberNotice(traveler, "통나무를 내려놓은 뒤 건물에 들어갈 수 있어요.");
                return false;
            }
            if (door == null || !door.CanInteract || !door.IsOpen || !traveler.AcceptsInteractionCommands ||
                traveler.CharacterState.AwaitingIncarnation || !traveler.SharesMap(door.transform.position) ||
                !traveler.InReach(door.Approach.position, 1f))
                return false;

            var room = door.Interior;
            bool exiting = door.IsExit;
            Vector3 origin = traveler.StandingPosition;
            if (InteriorAt(origin.x, origin.z) != (exiting ? room : room.Lobby))
                return false;
            var destination = exiting ? room.Lobby : room;
            Transform arrival = room.Arrival;
            if (exiting)
                arrival = destination != null ? destination.GuestRoomDoor?.Approach ?? destination.Arrival : room.Entrance.Approach;
            return traveler.IsLocallyControlled ? TravelToInterior(arrival, destination) : ArriveAtInterior(traveler, arrival, destination);
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
                if (!ArriveAtInterior(worker, arrival, destination))
                {
                    hud.ShowToast("반대편에 설 수 있는 곳이 없어요.");
                    return;
                }
                RefreshAfterTravel();
                SaveGame(false);
            });
        }

        private bool ArriveAtInterior(HarvestWorker traveler, Transform arrival, VillageInteriorMap destination)
        {
            if (arrival == null || !TryWarpCharacter(traveler, arrival.position, destination))
                return false;
            traveler.transform.rotation = arrival.rotation;
            return true;
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

        internal bool SharesMap(Vector3 first, Vector3 second)
        {
            if (rebirthZone != null && rebirthZone.Contains(first.x, first.z) != rebirthZone.Contains(second.x, second.z))
                return false;
            return InteriorAt(first.x, first.z) == InteriorAt(second.x, second.z);
        }

        internal WorldInteractable ShopApproach(HarvestWorker visitor, VillageInteractable merchant)
        {
            Vector3 seller = merchant.transform.position;
            return visitor.SharesMap(seller) ? merchant : DoorToward(visitor.StandingPosition, seller);
        }

        internal VillageBuildingDoor DoorToward(Vector3 origin, Vector3 destination)
        {
            var from = InteriorAt(origin.x, origin.z);
            var to = InteriorAt(destination.x, destination.z);
            var door = from != null
                ? to != null && to.Lobby == from ? to.Entrance : from.Exit
                : to?.Lobby?.Entrance ?? to?.Entrance;
            return door != null && door.IsOpen && door.Approach != null ? door : null;
        }

        private bool TryWarpToMap(Vector3 destination, VillageInteriorMap room = null, bool toClouds = false) =>
            TryWarpCharacter(worker, destination, room, toClouds);

        private bool TryWarpCharacter(HarvestWorker character, Vector3 destination, VillageInteriorMap room = null, bool toClouds = false)
        {
            Vector3 surface = SurfacePosition(destination.x, destination.z);
            if (!NavMesh.SamplePosition(surface, out var landing, 2f, NavMesh.AllAreas))
                return false;
            Vector3 point = landing.position;
            bool cloudPosition = rebirthZone?.Contains(point.x, point.z) == true;
            if (toClouds != cloudPosition || InteriorAt(point.x, point.z) != room || !character.WarpTo(point))
                return false;
            if (character == worker)
            {
                if (CurrentInterior != room || InRebirthZone != toClouds)
                    effects?.Clear();
                CurrentInterior = room;
                InRebirthZone = toClouds;
                lighting?.SetInteriorBackground(room != null);
            }
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
            {
                if (candidate.Lobby == null || !float.IsFinite(point.x) || !float.IsFinite(point.z))
                    return false;
                point = candidate.Arrival.position;
            }
            room = candidate;
            position = point;
            return true;
        }

        private Vector3 ExteriorPosition(VillageInteriorMap room) =>
            (room?.Lobby ?? room)?.Entrance?.Approach.position ?? spawnPoint.position;
    }
}
