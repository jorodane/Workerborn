using System;
using System.Collections.Generic;

namespace Workerborn
{
    internal sealed partial class VillageCampaign
    {
        private TimberWork timber;
        private WoodlandSave storedWoodland;
        private Func<TimberPoint, bool> timberGround;
        private Func<TimberPoint, float> timberHeight;
        internal WoodlandSave Woodland => timber?.Capture() ?? storedWoodland;

        internal bool SettleTimberFall(string id, float angle) => timber?.SettleFall(id, angle) == true;

        private WoodlandSave CaptureWoodlandForSave() => timber?.CaptureForSave(CarrierPose, timberGround) ?? storedWoodland;

        private (TimberPoint Point, float Yaw)? CarrierPose(string id)
        {
            foreach (var avatar in avatars.Values)
                if (avatar.id == id && timberGround(new TimberPoint(avatar.x, avatar.z)))
                    return (new TimberPoint(avatar.x, avatar.z), avatar.yaw);
            return null;
        }

        internal void ConfigureTimber(TimberSite[] sites, Func<TimberPoint, bool> ground, Func<TimberPoint, float> height, Func<float> roll)
        {
            timberGround = ground;
            timberHeight = height;
            timber = new TimberWork(sites, roll, storedWoodland);
            timber.StartDay(World.day);
        }

        internal bool PerformTimber(string identity, VillageSave save, TimberOrder order, long now,
            Func<TimberPoint, bool> localSeatOccupied, out string message, out GroundDropSave[] drops, out TimberYield[] acquired)
        {
            drops = Array.Empty<GroundDropSave>();
            acquired = null;
            message = "지금은 이 나무 작업을 할 수 없어요.";
            if (timber == null || !connected.Contains(identity) || !TryCheckpoint(identity, save, out var state))
                return false;
            var player = profiles[identity];
            var point = new TimberPoint(save.playerX, save.playerZ);
            if (!string.IsNullOrEmpty(save.interior.id) || !timberGround(point))
                return false;

            var created = new List<GroundDropSave>();
            TimberYield[] collected = null;
            var result = timber.Perform(order, state, player.id, point, save.yaw, World.day,
                IsOccupied, timberGround, Deliver, out message);
            if (result == TimberResult.Rejected)
                return false;
            SaveCharacter(player, state, teleport: false);
            drops = created.ToArray();
            acquired = collected;
            return true;

            bool IsOccupied(TimberPoint seat)
            {
                if (localSeatOccupied?.Invoke(seat) == true)
                    return true;
                foreach (var avatar in avatars.Values)
                    if (avatar.motion == (int)WorkerMotion.SeatSit && seat.DistanceSquared(new TimberPoint(avatar.x, avatar.z)) <= .075f)
                        return true;
                return false;
            }

            bool Deliver(List<TimberYield> rewards)
            {
                if (state.HasInstantHarvestAcquisition)
                {
                    var updated = state.Inventory.Copy();
                    foreach (var reward in rewards)
                        if (!updated.TryAdd(reward.itemId, reward.quantity))
                            return false;
                    state.Inventory.ReplaceWith(updated);
                    collected = rewards.ToArray();
                    return true;
                }

                for (int n = 0; n < rewards.Count; n++)
                {
                    var reward = rewards[n];
                    var landing = reward.point.Offset(n * 137.5f, .36f + n % 3 * .18f);
                    if (!timberGround(landing))
                        landing = point;
                    created.Add(AddGroundDrop(new GroundDropSave
                    {
                        itemId = reward.itemId,
                        quantity = reward.quantity,
                        x = landing.x, z = landing.z,
                        hasDropOrigin = true,
                        originX = reward.point.x, originY = timberHeight(reward.point) + 1f, originZ = reward.point.z,
                        flightMilliseconds = 480
                    }, player.id, now, harvestYield: true));
                }
                return true;
            }
        }

        private void DropCarriedTimber(CampaignProfile profile)
        {
            if (timber == null)
                return;
            var point = avatars.TryGetValue(profile.identity, out var pose) && !profile.character.awaitingIncarnation
                ? new TimberPoint(pose.x, pose.z) : new TimberPoint(profile.character.playerX, profile.character.playerZ);
            if (!timberGround(point))
                timber.Release(profile.id);
            else
                timber.Release(profile.id, point, pose?.yaw ?? profile.character.yaw);
        }
    }
}
