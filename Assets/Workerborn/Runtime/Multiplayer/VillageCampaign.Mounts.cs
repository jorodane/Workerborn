using System;
using System.Collections.Generic;
using System.Linq;

namespace Workerborn
{
    [Serializable]
    public sealed class MountSave
    {
        public string id, rider;
        public float x, z, yaw;

        internal bool IsFinite => float.IsFinite(x) && float.IsFinite(z) && float.IsFinite(yaw);

        internal MountSave ParkedCopy() => new MountSave { id = id, x = x, z = z, yaw = yaw };
    }

    internal sealed partial class VillageCampaign
    {
        private readonly Dictionary<string, (MountSave Pose, float Reach)> mounts = new Dictionary<string, (MountSave, float)>();
        private MountSave[] savedMounts;
        private Func<float, float, bool> mountGround;

        internal void ConfigureMounts(IEnumerable<(MountSave Pose, float Reach)> sites, Func<float, float, bool> ground)
        {
            mountGround = ground;
            foreach (var site in sites)
            {
                var pose = site.Pose;
                if (string.IsNullOrEmpty(pose.id) || !pose.IsFinite || !ground(pose.x, pose.z))
                    continue;
                var saved = Array.Find(savedMounts ?? Array.Empty<MountSave>(), candidate =>
                    candidate != null && candidate.id == pose.id && candidate.IsFinite && ground(candidate.x, candidate.z));
                mounts.TryAdd(pose.id, ((saved ?? pose).ParkedCopy(), site.Reach));
            }
        }

        internal MountSave[] Mounts() => mountGround == null ? savedMounts : mounts.Values.Select(entry => entry.Pose).ToArray();

        private MountSave[] CaptureParkedMounts() => mountGround == null ? savedMounts :
            mounts.Values.Select(entry => entry.Pose.ParkedCopy()).ToArray();

        internal bool TryChangeMount(string identity, string id, out string message)
        {
            message = "지금은 탑승할 수 없어요.";
            if (!ConnectedProfile(identity, out var profile))
                return false;
            var character = profile.character;
            var current = RiddenMount(profile.id);
            if (string.IsNullOrEmpty(id))
            {
                if (current == null)
                    return false;
                if (mountGround(character.playerX, character.playerZ) && float.IsFinite(character.yaw))
                    UpdateMountPose(current, character.playerX, character.playerZ, character.yaw);
                current.rider = null;
                message = "말에서 내렸어요.";
                return true;
            }

            if (current != null || !mounts.TryGetValue(id, out var site) || character.health <= 0 ||
                !string.IsNullOrEmpty(character.interior.id) || !mountGround(character.playerX, character.playerZ) ||
                timber?.CountCarried(profile.id) > 0)
                return false;
            if (!string.IsNullOrEmpty(site.Pose.rider))
            {
                message = "다른 사람이 타고 있어요.";
                return false;
            }
            float x = character.playerX - site.Pose.x, z = character.playerZ - site.Pose.z;
            float reach = site.Reach + .2f;
            if (x * x + z * z > reach * reach)
            {
                message = "말에게 조금 더 가까이 가야 해요.";
                return false;
            }
            site.Pose.rider = profile.id;
            message = "말에 탔어요. F를 누르면 내려요.";
            return true;
        }

        private MountSave RiddenMount(string rider)
        {
            foreach (var entry in mounts.Values)
                if (entry.Pose.rider == rider)
                    return entry.Pose;
            return null;
        }

        private void MoveMount(string rider, SessionAvatar pose)
        {
            var mount = RiddenMount(rider);
            if (mount == null)
                return;
            if (pose.parkedMountId == mount.id)
                mount.rider = null;
            else if (pose.mountId == mount.id && mountGround(pose.x, pose.z))
                UpdateMountPose(mount, pose.x, pose.z, pose.yaw);
        }

        private void ReleaseMount(string rider)
        {
            var mount = RiddenMount(rider);
            if (mount != null)
                mount.rider = null;
        }

        private static void UpdateMountPose(MountSave mount, float x, float z, float yaw)
        {
            mount.x = x;
            mount.z = z;
            mount.yaw = yaw;
        }
    }
}
