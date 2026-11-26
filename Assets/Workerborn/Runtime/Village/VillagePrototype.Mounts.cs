using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private readonly Dictionary<string, VillageMount> mounts = new Dictionary<string, VillageMount>();
        private string parkedMount;
        internal string ParkedMountId => parkedMount;

        private void InitializeMounts(bool headless = false)
        {
            foreach (var mount in mapRoot.GetComponentsInChildren<VillageMount>(true))
            {
                var point = mount.transform.position;
                if (string.IsNullOrEmpty(mount.PersistentId) || !IsMountGround(point.x, point.z) ||
                    !mounts.TryAdd(mount.PersistentId, mount))
                    continue;
                if (!headless)
                    mount.transform.position = SurfacePosition(point.x, point.z);
            }
            if (IsOnline || headless)
                Session.ConfigureMounts(mounts.Values.Select(mount => (mount.Capture(), mount.InteractionDistance)), IsMountGround);
            if (headless)
                return;
            PresentMounts(Session?.StartingMounts ?? restoredWorld?.mounts);
        }

        private bool IsMountGround(float x, float z) => IsTimberGround(new TimberPoint(x, z));

        private MountSave[] CaptureSoloMounts() => IsOnline ? null : mounts.Values.Select(mount => mount.Capture()).ToArray();

        internal VillageMount FindNearbyMount(VillageMount preferred)
        {
            if (InReach(preferred))
                return preferred;
            VillageMount closest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var mount in mounts.Values)
            {
                if (!InReach(mount))
                    continue;
                float distance = (mount.transform.position - worker.StandingPosition).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    closest = mount;
                    nearestDistance = distance;
                }
            }
            return closest;

            bool InReach(VillageMount mount) => mount != null && mount.CanInteract && string.IsNullOrEmpty(mount.Rider) &&
                worker.SharesMap(mount.transform.position) && worker.InReach(mount.transform.position, mount.InteractionDistance);
        }

        internal void RequestMount(VillageMount mount)
        {
            if (!worker.AcceptsMovementCommands)
                return;
            if (mount != null)
            {
                if (!worker.CanMount || !mount.CanInteract || !worker.SharesMap(mount.transform.position) ||
                    !worker.InReach(mount.transform.position, mount.InteractionDistance))
                    return;
                if (!string.IsNullOrEmpty(mount.Rider))
                {
                    hud.ShowToast("다른 사람이 타고 있어요.");
                    return;
                }
            }
            else if (worker.CurrentMount == null)
                return;
            if (IsOnline)
            {
                Session.SubmitMount(mount != null ? mount.PersistentId : null);
                return;
            }
            if (mount == null)
            {
                worker.EndRiding(true);
                hud.ShowToast("말에서 내렸어요.");
            }
            else if (worker.BeginRiding(mount))
            {
                mount.Present(worker.CharacterId, worker.transform, default, 0f, true);
                hud.ShowToast("말에 탔어요. C를 누르면 내려요.");
            }
            else
            {
                hud.ShowToast("말 옆에 설 수 있는 곳으로 이동해 주세요.");
                return;
            }
            SaveGame(false);
        }

        internal void ParkMountForTravel(VillageMount mount)
        {
            if (IsOnline)
                parkedMount = mount.PersistentId;
        }

        internal void PresentMounts(MountSave[] states)
        {
            if (states == null)
                return;
            string localId = IsOnline ? Session.LocalPlayerId : null;
            var local = localId != null ? System.Array.Find(states, row => row != null && row.rider == localId) : null;
            if (worker.CurrentMount != null && local?.id != worker.CurrentMount.PersistentId)
                worker.EndRiding(true);

            foreach (var row in states)
            {
                if (row == null || string.IsNullOrEmpty(row.id) || !mounts.TryGetValue(row.id, out var mount) ||
                    !row.IsFinite || !IsMountGround(row.x, row.z))
                    continue;
                Vector3 point = SurfacePosition(row.x, row.z);
                bool localRider = localId != null && row.rider == localId;
                if (row.id == parkedMount)
                {
                    if (localRider)
                        continue;
                    parkedMount = null;
                }
                if (localRider)
                {
                    if (worker.CurrentMount != mount)
                    {
                        mount.Present(null, null, point, row.yaw, true);
                        if (!worker.BeginRiding(mount))
                        {
                            parkedMount = row.id;
                            hud.ShowToast("말 옆에 설 수 있는 곳으로 이동해 주세요.");
                            continue;
                        }
                    }
                    mount.Present(localId, worker.transform, point, row.yaw, true);
                }
                else
                {
                    var peer = !string.IsNullOrEmpty(row.rider) && peers.TryGetValue(row.rider, out var found) ? found : null;
                    mount.Present(row.rider, peer != null ? peer.transform : null, point, row.yaw, false);
                }
            }
            foreach (var peer in peers)
                if (peer.Value != null)
                    peer.Value.ShowMount(mounts.Values.FirstOrDefault(mount => mount.Rider == peer.Key));
        }
    }
}
