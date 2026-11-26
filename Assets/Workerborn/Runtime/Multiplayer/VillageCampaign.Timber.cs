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

        internal bool LandTimberTrunk(string id, TimberPose pose, long now, out GroundDropSave[] drops)
        {
            drops = Array.Empty<GroundDropSave>();
            if (timber?.RecordTrunkLanding(id, pose) != true)
                return false;
            drops = ReleaseTimberCrownDrops(now, id);
            return true;
        }

        internal bool SettleTimberMotion(string id, TimberPart part, TimberPose pose, long now, out GroundDropSave[] drops)
        {
            drops = Array.Empty<GroundDropSave>();
            if (timber?.SettlePiece(id, part, pose) != true)
                return false;
            if (part == TimberPart.Trunk)
                drops = ReleaseTimberCrownDrops(now, id);
            return true;
        }

        internal GroundDropSave[] ReleaseTimberCrownDrops(long now, string treeId = null)
        {
            var created = new List<GroundDropSave>();
            timber?.ReleaseCrownProducts((owner, stump, rewards) =>
            {
                CreateTimberDrops(owner, stump, rewards, now, created);
                return true;
            }, treeId);
            return created.Count > 0 ? created.ToArray() : Array.Empty<GroundDropSave>();
        }

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
            timber = new TimberWork(sites, roll, storedWoodland, ground, height);
            timber.StartDay(World.day);
        }

        internal TimberResult PerformTimber(string identity, VillageSave save, TimberOrder order, long now,
            Func<TimberSite, bool> localSeatOccupied, out string message, out GroundDropSave[] drops, out TimberYield[] acquired,
            out int cutProgress)
        {
            drops = Array.Empty<GroundDropSave>();
            acquired = null;
            cutProgress = 0;
            message = "지금은 이 나무 작업을 할 수 없어요.";
            if (timber == null || !connected.Contains(identity) || !TryCheckpoint(identity, save, out var state))
                return TimberResult.Rejected;
            var player = profiles[identity];
            var point = new TimberPoint(save.playerX, save.playerZ);
            if (!string.IsNullOrEmpty(save.interior.id) || !timberGround(point))
                return TimberResult.Rejected;

            var created = new List<GroundDropSave>();
            TimberYield[] collected = null;
            var result = timber.Perform(order, state, player.id, point, save.yaw, World.day,
                IsOccupied, timberGround, Deliver, out message);
            if (result == TimberResult.Rejected)
                return result;
            SaveCharacter(player, state, teleport: false);
            drops = created.ToArray();
            acquired = collected;
            if (result == TimberResult.InProgress)
                cutProgress = timber.CutProgress(order);
            return result;

            bool IsOccupied(TimberSite site)
            {
                if (localSeatOccupied?.Invoke(site) == true)
                    return true;
                foreach (var avatar in avatars.Values)
                    if ((avatar.hasSeat || avatar.motion == (int)WorkerMotion.SeatSit) &&
                        site.Point.DistanceSquared(new TimberPoint(avatar.seatX, avatar.seatZ)) <= .075f)
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

                CreateTimberDrops(player.id, point, rewards, now, created);
                return true;
            }
        }

        private void CreateTimberDrops(string owner, TimberPoint fallback, List<TimberYield> rewards, long now, List<GroundDropSave> created)
        {
            for (int n = 0; n < rewards.Count; n++)
            {
                var reward = rewards[n];
                var landing = reward.point.Offset(n * 137.5f, .36f + n % 3 * .18f);
                if (!timberGround(landing))
                    landing = timberGround(reward.point) ? reward.point : fallback;
                created.Add(AddGroundDrop(new GroundDropSave
                {
                    itemId = reward.itemId, quantity = reward.quantity, x = landing.x, z = landing.z,
                    hasDropOrigin = !reward.settled, originX = reward.point.x, originZ = reward.point.z,
                    originY = reward.fromCrown ? reward.crownHeight + .12f : timberHeight(reward.point) + 1f,
                    flightMilliseconds = reward.settled ? 0 : 480
                }, owner, now, harvestYield: true, crownYield: reward.fromCrown));
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
