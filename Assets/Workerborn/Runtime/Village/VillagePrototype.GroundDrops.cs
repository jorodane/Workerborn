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
        private readonly List<(GroundPickup Item, float Distance)> nearbyHarvests = new List<(GroundPickup, float)>();
        private readonly List<string> autoPickupIds = new List<string>();
        private Action<HarvestAcquisitionSave> showAcquiredHarvest;
        private float nextHarvestPickupScan;
        private long nextHarvestPickupAt;

        internal long PickupUtcNow => IsOnline && Session != null ? Session.PickupUtcNow : GroundDropSave.UtcNow;

        private bool DeliverYield(HarvestWorker owner, string itemId, int amount, GroundPickup prefab, CropDefinition definition,
            Vector3 origin, int maxVisibleDrops, Func<int, int, Vector3> landing)
        {
            var state = owner.CharacterState;
            long now = PickupUtcNow;
            if (state.HasInstantHarvestAcquisition)
            {
                if (!state.TryAcquireHarvestImmediately(itemId, amount))
                {
                    if (owner.IsLocallyControlled)
                        hud.ShowToast("수확물을 넣을 소지품 공간이 부족해요.");
                    return false;
                }
                PlayHarvestAcquisition(owner, origin, itemId, amount);
                return true;
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
                drop.harvestYield = true;
                drop.flightMilliseconds = prefab.FlightMilliseconds;
                if (drafts != null)
                    drafts[i] = drop;
                else
                    SpawnLocalPlayerDrop(drop, owner.PlayerId, now, prefab, definition, origin, end);
            }
            return drafts == null || Session?.SubmitGatheredDrops(drafts) == true;
        }

        private void CollectNearbyHarvests(long now)
        {
            if (State.AwaitingIncarnation || Time.unscaledTime < nextHarvestPickupScan ||
                (IsOnline && worker.IsApproachingInteraction))
                return;
            bool instant = State.HasInstantHarvestAcquisition;
            if (!instant && now < nextHarvestPickupAt)
                return;
            int budget = instant ? int.MaxValue : State.HarvestItemsPerSecond;
            if (budget <= 0)
                return;
            nextHarvestPickupScan = Time.unscaledTime + .1f;
            nearbyHarvests.Clear();
            foreach (var item in pickups)
            {
                if (item == null || !item.CanCollect || !item.ReadyForAutoPickup ||
                    item.DropRecord?.CanAutoCollect(worker.PlayerId, now) != true ||
                    !worker.SharesMap(item.LandingPosition) ||
                    !worker.IsInPickupRange(item) && !(instant && item.DropRecord.CanAcquireFromCrown(worker.PlayerId, now)))
                    continue;
                Vector3 delta = worker.StandingPosition - item.LandingPosition;
                nearbyHarvests.Add((item, delta.x * delta.x + delta.z * delta.z));
            }
            if (nearbyHarvests.Count == 0)
                return;
            nearbyHarvests.Sort((first, second) => first.Distance.CompareTo(second.Distance));
            autoPickupIds.Clear();
            GridInventory preview = IsOnline ? State.Inventory.Copy() : null;
            foreach (var (item, _) in nearbyHarvests)
            {
                if (preview != null)
                {
                    int quantity = preview.TryAddUpTo(item.ItemId, Math.Min(budget, item.Quantity), item.Condition);
                    if (quantity <= 0)
                        continue;
                    budget -= quantity;
                }
                autoPickupIds.Add(item.DropRecord.id);
                if (budget == 0 || preview != null && autoPickupIds.Count == GroundDropSave.MaximumGatheredDrops)
                    break;
            }
            if (autoPickupIds.Count == 0)
                return;
            if (IsOnline)
            {
                if (Session?.SubmitAutomaticHarvestPickup(autoPickupIds.ToArray()) == true && AwaitingWorldCommand)
                    hud.SetCommandPending(true);
            }
            else
                FinishAutomaticHarvestPickup(localGroundDrops.CollectHarvests(State, worker.PlayerId, now, autoPickupIds));
        }

        internal void FinishAutomaticHarvestPickup(HarvestPickupResult[] collected)
        {
            if (collected.Length == 0)
                return;
            nextHarvestPickupAt = PickupUtcNow + GroundDropSave.AutoPickupPeriodMilliseconds;
            var acquired = new Dictionary<string, (Vector3? Position, int Quantity)>();
            foreach (var result in collected)
            {
                Vector3? position = null;
                if (dropViews.TryGetValue(result.id, out var item))
                {
                    position = item.VisualPosition;
                    if (result.remaining > 0)
                    {
                        item.DropRecord.quantity = result.remaining;
                        item.SetQuantity(result.remaining);
                    }
                    else
                        item.CompleteCollection();
                }
                acquired.TryGetValue(result.itemId, out var total);
                acquired[result.itemId] = (total.Position ?? position,
                    (int)Math.Min(int.MaxValue, (long)total.Quantity + result.quantity));
            }
            foreach (var item in acquired)
                PlayHarvestAcquisition(worker, item.Value.Position, item.Key, item.Value.Quantity);
        }

        private void ShowAcquiredHarvest(HarvestAcquisitionSave harvest)
        {
            Vector3? origin = harvest.hasSource && float.IsFinite(harvest.sourceX) && float.IsFinite(harvest.sourceY) &&
                float.IsFinite(harvest.sourceZ) ? new Vector3(harvest.sourceX, harvest.sourceY, harvest.sourceZ) : null;
            PlayHarvestAcquisition(worker, origin, harvest.itemId, harvest.quantity);
        }

        private void PlayHarvestAcquisition(HarvestWorker collector, Vector3? origin, string itemId, int quantity)
        {
            Vector3 position = collector.StandingPosition;
            if (!SharesMap(position))
                return;
            Vector3 start = origin is Vector3 source && (source - position).sqrMagnitude <= 12f * 12f && collector.SharesMap(source)
                ? source : position + collector.transform.forward * .85f + Vector3.up * .2f;
            effects?.PlayHarvestTrails(start, collector.transform, quantity);
            if (collector.IsLocallyControlled)
                hud.ShowFloatingText(position + Vector3.up * 1.5f, $"{ItemCatalog.Get(itemId).Name} +{quantity:N0}", Color.white);
        }

        private GroundDropSave CaptureGroundDrop(string itemId, int quantity, Vector3 origin, Vector3 landing,
            EquipmentCondition condition = default)
        {
            var room = InteriorAt(landing.x, landing.z);
            Vector3 exterior = room != null ? ExteriorPosition(room) : landing;
            return new GroundDropSave
            {
                itemId = itemId, quantity = quantity, condition = condition, x = exterior.x, z = exterior.z,
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
                    character.CharacterState.Inventory.TryAdd(drop.itemId, drop.quantity, drop.condition))
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
