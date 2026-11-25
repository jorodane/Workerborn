using System;
using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class VillageInteriorMap : MonoBehaviour
    {
        [SerializeField] private VillageNavigation navigation;
        [SerializeField] private Transform arrival;
        [SerializeField] private VillageBuildingDoor exit;
        [SerializeField] private VillageInteriorMap guestRoomPrefab;
        [SerializeField] private VillageBuildingDoor guestRoomDoor;
        [SerializeField] private Transform keeperPoint;
        [SerializeField] private VillageBed bed;
        [SerializeField] private string mapName;
        [SerializeField, TextArea(1, 3)] private string mapDescription = "마을과 이어진 실내 공간.";
        private Texture2D mapImage;

        internal VillageBuildingDoor Entrance { get; private set; }
        internal VillageInteriorMap Lobby { get; private set; }
        internal VillageInteriorMap GuestRoom { get; set; }
        internal VillageInteriorMap GuestRoomPrefab => guestRoomPrefab;
        internal VillageBuildingDoor GuestRoomDoor => guestRoomDoor;
        internal Transform KeeperPoint => keeperPoint;
        internal VillageBed Bed => bed;
        internal string Id { get; private set; }
        internal string MapName => !string.IsNullOrWhiteSpace(mapName) ? mapName :
            Lobby != null ? "내 여관방" : GuestRoom != null ? "여관 로비" : $"{Entrance.DisplayName} 내부";
        internal string MapDescription => mapDescription;
        internal BoxCollider Floor => navigation != null ? navigation.GroundBox : null;
        internal Transform Arrival => arrival;
        internal VillageBuildingDoor Exit => exit;
        internal Bounds Area { get; private set; }
        internal Rect MapBounds => new Rect(Area.min.x, Area.min.z, Area.size.x, Area.size.z);
        internal bool IsConfigured => Floor != null && arrival != null && exit != null && exit.Approach != null;

        internal void Bind(VillagePrototype game, VillageBuildingDoor entrance, string id, VillageInteriorMap lobby = null)
        {
            Id = id;
            Entrance = entrance;
            Lobby = lobby;
            Area = Floor.bounds;
            navigation.Activate(useBakedData: false);
            exit.Bind(game, this);
        }

        internal bool Contains(float x, float z) =>
            x >= Area.min.x && x <= Area.max.x && z >= Area.min.z && z <= Area.max.z;

        internal InteriorPositionSave CapturePosition(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return new InteriorPositionSave { id = Id, localX = local.x, localZ = local.z };
        }

        internal Vector3 RestorePosition(InteriorPositionSave save) =>
            transform.TransformPoint(new Vector3(save.localX, 0f, save.localZ));

        internal Texture2D MapImage()
        {
            if (mapImage != null)
                return mapImage;

            const int resolution = 96;
            Rect area = MapBounds;
            float longest = Mathf.Max(area.width, area.height);
            int columns = Mathf.Max(1, Mathf.RoundToInt(resolution * area.width / longest));
            int rows = Mathf.Max(1, Mathf.RoundToInt(resolution * area.height / longest));
            var pixels = new Color32[columns * rows];
            Array.Fill(pixels, (Color32)new Color(.84f, .76f, .60f));
            foreach (var marker in GetComponentsInChildren<VillageNavigationBlocker>())
            {
                var volume = marker.Volume;
                var size = new Vector2(volume.size.x, volume.size.z);
                new VillageMapFootprint(volume.transform, volume.center, size, VillageMapSurface.Building)
                    .Paint(pixels, columns, rows, area);
            }
            mapImage = new Texture2D(columns, rows, TextureFormat.RGBA32, false)
            {
                name = MapName, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            mapImage.SetPixels32(pixels);
            mapImage.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return mapImage;
        }

        private void OnDestroy()
        {
            if (mapImage != null)
                Destroy(mapImage);
        }
    }
}
