using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Workerborn
{
    internal sealed partial class VillageSession
    {
        private const int GroundChangesPerPacket = 128;
        private readonly Dictionary<string, GroundDropSave> receivedGroundDrops = new Dictionary<string, GroundDropSave>();
        private readonly List<GroundDropSave> pendingGatheredDrops = new List<GroundDropSave>();
        private long pickupClockAtSync;
        private double pickupClockSyncedAt;

        internal long PickupUtcNow => authority != null || pickupClockAtSync == 0 ? GroundDropSave.UtcNow :
            pickupClockAtSync + (long)((Time.realtimeSinceStartupAsDouble - pickupClockSyncedAt) * 1000d);

        internal IEnumerable<GroundDropSave> SharedGroundDrops => authority != null ? authority.SharedGroundDrops : receivedGroundDrops.Values;

        internal bool SubmitGroundDrop(int entryId, GroundDropSave draft) => SubmitCommand(new SessionPacket
        {
            type = SessionMessage.GroundTransfer, value = entryId, groundDrop = draft
        });

        internal bool SubmitGroundPickup(string dropId) => SubmitCommand(new SessionPacket
        {
            type = SessionMessage.GroundTransfer, argument = dropId
        });

        internal bool SubmitGatheredDrops(GroundDropSave[] drops)
        {
            if (!Online || village == null || Submitting || authority == null && host == null ||
                pendingGatheredDrops.Count + drops.Length > GroundDropSave.MaximumGatheredDrops)
                return false;
            pendingGatheredDrops.AddRange(drops);
            return true;
        }

        private bool FlushGatheredDrops(VillageSave character)
        {
            if (pendingGatheredDrops.Count == 0)
                return true;
            var drops = pendingGatheredDrops.ToArray();
            bool sent = authority != null && localIdentity != null ? PublishGatheredDrops(localIdentity, character, drops) :
                host != null && Send(host, new SessionPacket { type = SessionMessage.GatheredDrops, character = character, groundDrops = drops });
            if (sent)
                pendingGatheredDrops.Clear();
            return sent;
        }

        private bool PublishGatheredDrops(string identity, VillageSave character, GroundDropSave[] drafts)
        {
            if (!authority.TryPublishGatheredDrops(identity, character, drafts, GroundDropSave.UtcNow, out var drops))
                return false;
            QueueCreatedDrops(drops);
            return true;
        }

        private void GroundTransfer(string identity, SessionPacket packet, Connection connection)
        {
            string message = "현재 상태를 저장하지 못했어요. 다시 시도해 주세요.";
            string collected = null;
            GroundDropSave placed = null;
            GroundDropSave remainder = null;
            int collectedQuantity = 0;
            bool success = false;
            if (authority.Checkpoint(identity, packet.character))
            {
                long now = GroundDropSave.UtcNow;
                if (packet.groundDrop != null)
                    success = authority.TryPlaceGroundDrop(identity, packet.value, packet.groundDrop, now, out placed, out message);
                else if (authority.TryCollectGroundDrop(identity, packet.argument, now, out collectedQuantity, out message))
                {
                    success = true;
                    collected = packet.argument;
                    authority.TryGetGroundDrop(collected, out remainder);
                }
            }

            SendProfile(identity, connection, message, packet.request, success, transition: false,
                collectedDropId: collected, collectedQuantity: collectedQuantity, remainder: remainder);
            if (placed != null)
                QueueCreatedDrops(new[] { placed });
            if (collected != null)
            {
                if (connection != null)
                    SyncGroundChange(collected);
                else
                    QueueGroundChange(collected);
            }
        }

        private void ApplyGroundPickupReply(SessionPacket packet)
        {
            string id = packet.collectedDropId;
            if (packet.groundDrop == null)
                receivedGroundDrops.Remove(id);
            else if (authority == null)
                receivedGroundDrops[id] = packet.groundDrop;
            village.ApplySharedGroundPickup(id, packet.collectedQuantity, packet.groundDrop);
        }

        private void QueueCreatedDrops(GroundDropSave[] drops)
        {
            foreach (var drop in drops)
            {
                QueueGroundChange(drop.id);
                village?.ShowSharedGroundPickup(drop);
            }
        }

        private void QueueGroundChange(string dropId)
        {
            foreach (var connection in connections)
                if (connection.Identity != null && !connection.Leaving)
                    connection.PendingGroundDrops.Add(dropId);
        }

        private void SyncGroundChange(string dropId)
        {
            QueueGroundChange(dropId);
            if (authority.TryGetGroundDrop(dropId, out var drop))
                village?.ShowSharedGroundPickup(drop);
            else
                village?.RemoveSharedGroundPickup(dropId);
        }

        private void SendGroundChanges(Connection connection)
        {
            if (connection.PendingGroundDrops.Count == 0)
                return;
            var changed = connection.PendingGroundDrops.Take(GroundChangesPerPacket).ToArray();
            var added = new List<GroundDropSave>();
            var removed = new List<string>();
            string playerId = authority.Profile(connection.Identity).id;
            foreach (string id in changed)
            {
                if (authority.TryGetGroundDrop(id, out var drop) && drop.IsVisibleTo(playerId))
                    added.Add(drop);
                else
                    removed.Add(id);
            }
            if (Send(connection, new SessionPacket
            {
                type = SessionMessage.GroundItems, groundDrops = added.ToArray(), removedGroundDrops = removed.ToArray(),
                pickupUtc = GroundDropSave.UtcNow
            }))
                connection.PendingGroundDrops.ExceptWith(changed);
        }

        private void ReceiveGroundChanges(SessionPacket packet)
        {
            SynchronizePickupClock(packet.pickupUtc);
            foreach (string id in packet.removedGroundDrops ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(id))
                {
                    receivedGroundDrops.Remove(id);
                    village?.RemoveSharedGroundPickup(id);
                }
            foreach (var drop in packet.groundDrops ?? Array.Empty<GroundDropSave>())
                if (drop != null && !string.IsNullOrEmpty(drop.id))
                {
                    receivedGroundDrops[drop.id] = drop;
                    village?.ShowSharedGroundPickup(drop);
                }
        }

        private void SynchronizePickupClock(long now)
        {
            if (now <= 0)
                return;
            pickupClockAtSync = now;
            pickupClockSyncedAt = Time.realtimeSinceStartupAsDouble;
        }
    }
}
