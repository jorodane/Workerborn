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
            if (authority != null)
                QueueCreatedDrops(authority.ReleaseTimberCrownDrops(GroundDropSave.UtcNow));
        }

        internal void FinishTimberMotion(string id, TimberPart part, TimberPose pose)
        {
            if (authority?.SettleTimberMotion(id, part, pose, GroundDropSave.UtcNow, out var drops) != true)
                return;
            QueueCreatedDrops(drops);
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
            {
                QueueCreatedDrops(drops);
                if (packet.timberOrder?.action == TimberAction.Split)
                    RelayTimberImpact(connection, packet.timberOrder);
            }
            SendProfile(identity, connection, message, packet.request, changed, transition: false, timberAcquired: acquired);
            if (changed)
                nextSave = Mathf.Min(nextSave, Time.unscaledTime + 1f);
        }

        private void RelayTimberImpact(Connection source, TimberOrder order)
        {
            if (source != null)
                village?.PlayRemoteTimberImpact(order);
            var impact = new SessionPacket { type = SessionMessage.TimberImpact, timberOrder = order };
            foreach (var observer in connections)
                if (observer != source && observer.Identity != null && !observer.Leaving)
                    Send(observer, impact);
        }
    }
}
