using System;
using UnityEngine;

namespace Workerborn
{
    internal sealed partial class VillageSession
    {
        private WoodlandSave initialWoodland;
        private VillagePrototype headlessTimber;
        internal WoodlandSave StartingWoodland => authority?.Woodland ?? initialWoodland;
        internal bool OwnsTimberPhysics => authority != null;

        internal void ConfigureTimber(TimberSite[] sites, Func<TimberPoint, bool> ground, Func<TimberPoint, float> height,
            VillagePrototype headless = null)
        {
            headlessTimber = headless;
            authority?.ConfigureTimber(sites, ground, height, () => UnityEngine.Random.value);
        }

        internal void FinishTimberFall(string id, TimberPose pose)
        {
            if (authority?.SettleTimberFall(id, pose) == true)
                nextSave = Mathf.Min(nextSave, Time.unscaledTime + 1f);
        }

        internal bool SubmitTimberWork(TimberOrder order) => SubmitCommand(new SessionPacket
        {
            type = SessionMessage.TimberWork,
            timberOrder = order
        });

        private void TimberCommand(string identity, SessionPacket packet, Connection connection)
        {
            Func<TimberPoint, bool> occupation = village != null ? village.TimberStumpOccupied : null;
            bool changed = authority.PerformTimber(identity, packet.character, packet.timberOrder, GroundDropSave.UtcNow,
                occupation, out string message, out var drops, out var acquired);
            if (changed)
                QueueCreatedDrops(drops);
            SendProfile(identity, connection, message, packet.request, changed, transition: false, timberAcquired: acquired);
            if (changed)
                nextSave = Mathf.Min(nextSave, Time.unscaledTime + 1f);
        }
    }
}
