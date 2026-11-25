using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private TimberWork soloTimber;
        private WoodlandSave presentedWoodland;
        private readonly Dictionary<string, LoggingTree> timberTrees = new Dictionary<string, LoggingTree>();
        private readonly Dictionary<(string Tree, TimberPart Part), TimberPiece> felledPieces =
            new Dictionary<(string, TimberPart), TimberPiece>();
        private readonly Dictionary<string, TimberPiece> logModels = new Dictionary<string, TimberPiece>();
        private readonly Dictionary<string, int> carriedLogs = new Dictionary<string, int>();

        private TimberSite[] TimberSites()
        {
            var sites = new List<TimberSite>();
            foreach (var tree in mapRoot.GetComponentsInChildren<LoggingTree>(true))
            {
                if (tree.Definition == null || string.IsNullOrEmpty(tree.PersistentId))
                    continue;
                timberTrees[tree.PersistentId] = tree;
                sites.Add(tree.Site());
            }
            return sites.ToArray();
        }

        private void InitializeTimber()
        {
            var sites = TimberSites();
            if (IsOnline)
            {
                Session?.ConfigureTimber(sites, IsTimberGround, TimberSurfaceHeight);
                PresentWoodland(Session?.StartingWoodland);
                return;
            }
            soloTimber = new TimberWork(sites, () => UnityEngine.Random.value, restoredWorld?.woodland);
            soloTimber.StartDay(State.Day);
            PresentWoodland(soloTimber.Capture());
        }

        internal bool IsTimberGround(TimberPoint point)
        {
            var terrain = GroundTerrain;
            if (!point.IsFinite || terrain == null || terrain.terrainData == null)
                return false;
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return point.x >= origin.x && point.z >= origin.z &&
                point.x <= origin.x + size.x && point.z <= origin.z + size.z &&
                InteriorAt(point.x, point.z) == null && rebirthZone?.Contains(point.x, point.z) != true;
        }

        internal bool TimberStumpOccupied(TimberPoint point) => SeatInUse(SurfacePosition(point.x, point.z) + Vector3.up * .44f);
        private float TimberSurfaceHeight(TimberPoint point) => SurfacePosition(point.x, point.z).y;

        internal bool RequestTimberWork(HarvestWorker source, TimberOrder order)
        {
            TimberPoint location = TimberLocation.Planar(source.StandingPosition);
            if (!IsTimberGround(location) || source.CharacterState.AwaitingIncarnation)
                return false;
            if (IsOnline)
                return source == worker && Session?.SubmitTimberWork(order) == true;
            if (soloTimber == null)
                return false;

            var state = source.CharacterState;
            int level = state.Level;
            var result = soloTimber.Perform(order, state, source.CharacterId, location, source.transform.eulerAngles.y,
                State.Day, TimberStumpOccupied, IsTimberGround, rewards => DeliverTimberYield(source, rewards), out string message);
            TimberNotice(source, message);
            if (result == TimberResult.Rejected)
                return false;
            PresentWoodland(soloTimber.Capture());
            ShowLevelUp(source, level);
            if (source == worker)
                SaveGame(false);
            return true;
        }

        private bool DeliverTimberYield(HarvestWorker source, List<TimberYield> items)
        {
            if (!source.HasPlayerOwner || source.CharacterState.HasInstantHarvestAcquisition)
            {
                var updated = source.CharacterState.Inventory.Copy();
                foreach (var item in items)
                    if (!updated.TryAdd(item.itemId, item.quantity))
                        return false;
                source.CharacterState.Inventory.ReplaceWith(updated);
                PlayTimberAcquisitions(source, items);
                return true;
            }

            foreach (var item in items)
                if (!pickupPool.CanSpawn(settings.FindItem(item.itemId)?.GroundPrefab))
                    return false;

            foreach (var item in items)
            {
                TimberPoint end = item.point.Offset(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(.24f, .85f));
                if (!IsTimberGround(end))
                    end = TimberLocation.Planar(source.StandingPosition);
                Vector3 origin = SurfacePosition(item.point.x, item.point.z) + Vector3.up * 1f;
                Vector3 landing = SurfacePosition(end.x, end.z) + Vector3.up * .08f;
                var prefab = settings.FindItem(item.itemId).GroundPrefab;
                var drop = CaptureGroundDrop(item.itemId, item.quantity, origin, landing);
                drop.harvestYield = true;
                drop.flightMilliseconds = prefab.FlightMilliseconds;
                SpawnLocalPlayerDrop(drop, source.PlayerId, PickupUtcNow, prefab, null, origin, landing);
            }
            return true;
        }

        internal void PlayTimberAcquisitions(HarvestWorker source, IEnumerable<TimberYield> items)
        {
            foreach (var item in items)
                PlayHarvestAcquisition(source, SurfacePosition(item.point.x, item.point.z) + Vector3.up * .9f,
                    item.itemId, item.quantity);
        }

        internal void TimberNotice(HarvestWorker source, string message)
        {
            if (source.IsLocallyControlled)
                hud.ShowToast(message);
        }

        internal int CarriedLogCount(string id) => id != null && carriedLogs.TryGetValue(id, out int count) ? count : 0;

        internal Transform TimberCarrier(string id)
        {
            if (worker != null && worker.CharacterId == id)
                return worker.transform;
            if (peers.TryGetValue(id, out var peer) && peer != null)
                return peer.transform;
            return FindCharacter(id)?.transform;
        }

        private void RefreshTimberDay()
        {
            if (soloTimber == null)
                return;
            soloTimber.StartDay(State.Day);
            PresentWoodland(soloTimber.Capture());
        }

        private WoodlandSave CaptureSoloWoodland() => soloTimber?.CaptureForSave(id =>
        {
            var source = FindCharacter(id);
            return source != null ? (TimberLocation.Planar(source.StandingPosition), source.transform.eulerAngles.y) : null;
        }, IsTimberGround);

        internal void ReleaseSoloTimber(HarvestWorker source)
        {
            if (soloTimber == null || source.CharacterId == null || CarriedLogCount(source.CharacterId) == 0)
                return;
            TimberPoint origin = TimberLocation.Planar(source.StandingPosition);
            TimberPoint destination = origin.Offset(source.transform.eulerAngles.y, 1.25f);
            soloTimber.Release(source.CharacterId, IsTimberGround(destination) ? destination : origin, source.transform.eulerAngles.y);
            PresentWoodland(soloTimber.Capture());
        }

        internal void PresentWoodland(WoodlandSave snapshot)
        {
            var assets = settings.Timber;
            if (snapshot == null || assets == null || ReferenceEquals(presentedWoodland, snapshot) ||
                presentedWoodland != null && snapshot.revision <= presentedWoodland.revision)
                return;
            bool animate = presentedWoodland != null;
            presentedWoodland = snapshot;
            var fallen = new HashSet<string>();
            var pieces = new HashSet<(string, TimberPart)>();
            foreach (var record in snapshot.trees)
            {
                if (record.felledDay <= 0 || !timberTrees.TryGetValue(record.id, out var tree))
                    continue;
                bool firstFall = !tree.Felled;
                fallen.Add(record.id);
                tree.Present(true);
                var site = tree.Site();
                for (int n = 0; n < 3; n++)
                {
                    var part = (TimberPart)n;
                    if (!record.Has(part))
                        continue;
                    var key = (record.id, part);
                    pieces.Add(key);
                    if (felledPieces.ContainsKey(key))
                        continue;

                    var piece = Instantiate(assets.Prefab(part), droppedItems);
                    piece.Bind(this, record.id);
                    TimberPoint point = site.PartPoint(part, record.fallYaw);
                    Vector3 landing = SurfacePosition(point.x, point.z);
                    if (n > 0 && firstFall && animate)
                        piece.Fall(tree.transform.position, n == 1 ? site.LowerDistance : site.UpperDistance, landing, record.fallYaw);
                    else
                        piece.Place(landing, record.fallYaw);
                    felledPieces.Add(key, piece);
                }
            }
            foreach (var tree in timberTrees)
                if (!fallen.Contains(tree.Key))
                    tree.Value.Present(false);
            RemoveMissing(felledPieces, pieces);
            carriedLogs.Clear();
            var logs = new HashSet<string>();
            foreach (var log in snapshot.logs)
            {
                logs.Add(log.id);
                int index = 0;
                if (log.carrier != null)
                {
                    index = CarriedLogCount(log.carrier);
                    carriedLogs[log.carrier] = index + 1;
                }
                if (!logModels.TryGetValue(log.id, out var view))
                {
                    view = Instantiate(assets.Prefab(TimberPart.Log), droppedItems);
                    view.Bind(this, log.id);
                    logModels.Add(log.id, view);
                }
                view.PresentLog(log, index, animate);
            }
            RemoveMissing(logModels, logs);
            worker?.RefreshTimberSkill();
            if (initialized)
                hud.RefreshWindows();
        }

        private static void RemoveMissing<TKey>(Dictionary<TKey, TimberPiece> objects, HashSet<TKey> current)
        {
            var removed = new List<TKey>();
            foreach (var pair in objects)
                if (!current.Contains(pair.Key))
                {
                    pair.Value.gameObject.SetActive(false);
                    Destroy(pair.Value.gameObject);
                    removed.Add(pair.Key);
                }
            foreach (var key in removed)
                objects.Remove(key);
        }
    }
}
