using System;
using UnityEngine;

namespace Workerborn
{
    internal sealed partial class VillageSession
    {
        private WoodlandSave initialWoodland;
        private VillagePrototype headlessTimber;
        private int timberReplyRequest;
        private Action<TimberResult, int> finishTimberWork;
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

        internal void TimberLanded(string id, TimberPose pose, Vector3 contact)
        {
            long now = GroundDropSave.UtcNow;
            if (authority?.LandTimberTrunk(id, pose, now, out var drops) != true)
                return;

            village?.PlayTimberLanding(contact);
            QueueCreatedDrops(drops);
            var packet = new SessionPacket { type = SessionMessage.TimberLanding, timberContact = contact, groundDrops = drops, pickupUtc = now };
            foreach (var connection in connections)
            {
                if (connection.Identity == null || connection.Leaving || !Send(connection, packet))
                    continue;
                foreach (var drop in drops)
                    connection.PendingGroundDrops.Remove(drop.id);
            }
            nextSave = Mathf.Min(nextSave, Time.unscaledTime + 1f);
        }

        internal bool SubmitTimberWork(TimberOrder order, Action<TimberResult, int> completed)
        {
            if (Submitting)
                return false;
            timberReplyRequest = requestSequence + 1;
            finishTimberWork = completed;
            if (SubmitCommand(new SessionPacket { type = SessionMessage.TimberWork, timberOrder = order }))
                return true;
            ClearTimberWorkReply();
            return false;
        }

        private void ReceiveTimberWorkReply(SessionPacket packet)
        {
            if (packet.request == 0 || packet.request != timberReplyRequest)
                return;
            var completed = finishTimberWork;
            ClearTimberWorkReply();
            completed?.Invoke(packet.timberResult, packet.timberCutProgress);
        }

        private void ClearTimberWorkReply()
        {
            timberReplyRequest = 0;
            finishTimberWork = null;
        }

        private void TimberCommand(string identity, SessionPacket packet, Connection connection)
        {
            Func<TimberSite, bool> occupation = village != null ? village.TimberStumpOccupied : null;
            var result = authority.PerformTimber(identity, packet.character, packet.timberOrder, GroundDropSave.UtcNow,
                occupation, out string message, out var drops, out var acquired, out int cutProgress);
            bool changed = result != TimberResult.Rejected;
            if (changed)
            {
                QueueCreatedDrops(drops);
                if (packet.timberOrder?.action == TimberAction.Split)
                    RelayTimberImpact(connection, packet.timberOrder);
            }
            SendProfile(identity, connection, message, packet.request, changed, transition: false, timberAcquired: acquired,
                timberResult: result, timberCutProgress: cutProgress);
            if (changed && result != TimberResult.InProgress)
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
