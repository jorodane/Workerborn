using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private readonly GroundDropPiles localGroundDrops = new GroundDropPiles();
        private readonly Dictionary<string, GroundPickup> dropViews = new Dictionary<string, GroundPickup>();
        private readonly List<GroundDropSave> legacyNpcDrops = new List<GroundDropSave>();
        private Action<string, int> showAcquiredHarvest;

        internal long PickupUtcNow => IsOnline && Session != null ? Session.PickupUtcNow : GroundDropSave.UtcNow;

        private bool DeliverYield(HarvestWorker owner, string itemId, int amount, GroundPickup prefab, CropDefinition definition,
            Vector3 origin, int maxVisibleDrops, Func<int, int, Vector3> landing)
        {
            var state = owner.CharacterState;
            long now = PickupUtcNow;
            if (state.IsSkillLearned(SkillId.HarvestAcquisition))
            {
                bool accepted = state.TryAcquireHarvest(itemId, amount, now);
                if (owner.IsLocallyControlled)
                {
                    if (!accepted)
                        hud.ShowToast("수확물을 넣을 소지품 공간이 부족해요.");
                    else if (state.HarvestAcquisitionDelayMilliseconds == 0)
                        ShowAcquiredHarvest(itemId, amount);
                }
                return accepted;
            }
            if (!owner.HasPlayerOwner || amount <= 0 || !pickupPool.CanSpawn(prefab))
                return false;

            int piles = Mathf.Min(amount, maxVisibleDrops);
            var drafts = IsOnline ? new GroundDropSave[piles] : null;
            for (int i = 0; i < piles; i++)
            {
                Vector3 end = landing(i, piles);
                if (!owner.SharesMap(end))
                {
                    Vector3 position = owner.StandingPosition;
                    end = SurfacePosition(position.x, position.z) + Vector3.up * .08f;
                }
                int quantity = amount / piles + (i < amount % piles ? 1 : 0);
                var drop = CaptureGroundDrop(itemId, quantity, origin, end);
                if (drafts != null)
                    drafts[i] = drop;
                else
                    SpawnLocalPlayerDrop(drop, owner.PlayerId, now, prefab, definition, origin, end);
            }
            return drafts == null || Session?.SubmitGatheredDrops(drafts) == true;
        }

        private void ShowAcquiredHarvest(string itemId, int quantity)
        {
            string name = ItemCatalog.Get(itemId).Name;
            hud.ShowFloatingText(worker.transform.position + Vector3.up * 1.5f, $"{name} +{quantity:N0}", Color.white);
        }

        private GroundDropSave CaptureGroundDrop(string itemId, int quantity, Vector3 origin, Vector3 landing)
        {
            var room = InteriorAt(landing.x, landing.z);
            Vector3 exterior = room != null ? ExteriorPosition(room) : landing;
            return new GroundDropSave
            {
                itemId = itemId, quantity = quantity, x = exterior.x, z = exterior.z,
                interior = room != null ? room.CapturePosition(landing) : default,
                hasDropOrigin = true, originX = origin.x, originY = origin.y, originZ = origin.z
            };
        }

        private void SpawnLocalPlayerDrop(GroundDropSave drop, string playerId, long now, GroundPickup prefab,
            CropDefinition crop, Vector3 origin, Vector3 landing)
        {
            drop.id = Guid.NewGuid().ToString("N");
            drop.ReserveFor(playerId, now);
            localGroundDrops.Add(drop);
            TrackGroundPickup(SpawnPickup(prefab, crop, drop.quantity, origin, landing), drop, shared: false);
        }

        private void TrackGroundPickup(GroundPickup item, GroundDropSave drop, bool shared)
        {
            item.BindDrop(drop, shared);
            dropViews.Add(drop.id, item);
        }

        private void ForgetGroundPickup(GroundPickup pickup)
        {
            string id = pickup.DropRecord?.id;
            if (string.IsNullOrEmpty(id) || !dropViews.TryGetValue(id, out var current) || current != pickup)
                return;
            dropViews.Remove(id);
            if (pickup.SharedDropId == null)
                localGroundDrops.Remove(id);
        }

        private void ReleaseLocalGroundDrops(long now)
        {
            localGroundDrops.ReleaseExpired(now, id =>
            {
                if (!dropViews.TryGetValue(id, out var item))
                    return;
                if (localGroundDrops.TryGet(id, out var remaining))
                    item.SetQuantity(remaining.quantity);
                else
                    item.Despawn();
            });
        }

        private void RestoreLocalGroundPickups()
        {
            localGroundDrops.ReleaseExpired(PickupUtcNow);
            foreach (var drop in localGroundDrops.Active)
                SpawnRecordedGroundPickup(drop, shared: false);
        }

        private void SettleLegacyNpcDrops()
        {
            for (int i = legacyNpcDrops.Count - 1; i >= 0; i--)
            {
                var drop = legacyNpcDrops[i];
                if (villageCharacters.TryGetValue(drop.harvestedBy, out var character) && character != null &&
                    character.CharacterState.Inventory.TryAdd(drop.itemId, drop.quantity))
                    legacyNpcDrops.RemoveAt(i);
            }
        }

        internal void RefreshSharedGroundPickups()
        {
            if (!IsOnline || Session == null)
                return;
            foreach (var item in new List<GroundPickup>(dropViews.Values))
                if (item != null && item.SharedDropId != null && !SharesMap(item.LandingPosition))
                    item.Despawn();
            foreach (var drop in Session.SharedGroundDrops)
                ShowSharedGroundPickup(drop);
        }

        internal void ShowSharedGroundPickup(GroundDropSave drop)
        {
            if (!initialized || drop == null || !drop.IsVisibleTo(worker.PlayerId))
                return;
            if (dropViews.TryGetValue(drop.id, out var existing))
            {
                existing.BindDrop(drop, shared: true);
                existing.SetQuantity(drop.quantity);
            }
            else
                SpawnRecordedGroundPickup(drop, shared: true);
        }

        private void SpawnRecordedGroundPickup(GroundDropSave drop, bool shared)
        {
            Vector3 point;
            if (drop.RoomId == null)
                point = new Vector3(drop.x, 0f, drop.z);
            else if (!TryRestoreInterior(drop.interior, out _, out point))
            {
                if (shared)
                    return;
                point = new Vector3(drop.x, 0f, drop.z);
            }
            if (shared && !SharesMap(point))
                return;

            var prefab = settings.FindItem(drop.itemId)?.GroundPrefab;
            if (!pickupPool.CanSpawn(prefab))
                return;
            Vector3 end = SurfacePosition(point.x, point.z) + Vector3.up * .08f;
            bool recent = shared && drop.hasDropOrigin && drop.reservedUntilUtc - PickupUtcNow > GroundDropSave.ReservationMilliseconds - 1_000;
            Vector3 start = recent ? new Vector3(drop.originX, drop.originY, drop.originZ) : end;
            var item = SpawnPickup(prefab, drop.itemId == potato.ItemId ? potato : null, drop.quantity, start, end, settled: !recent);
            TrackGroundPickup(item, drop, shared);
        }

        internal void ApplySharedGroundPickup(string id, int quantity, GroundDropSave remainder)
        {
            if (!dropViews.TryGetValue(id, out var item))
                return;
            PlayCollection(worker, item.VisualPosition + Vector3.up * .08f, item.ItemDisplayName, quantity);
            if (remainder == null)
                item.CompleteCollection();
            else
                ShowSharedGroundPickup(remainder);
        }

        internal void RemoveSharedGroundPickup(string id)
        {
            if (dropViews.TryGetValue(id, out var item) && item.SharedDropId != null)
                item.Despawn();
        }
    }
}
