using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private TimberWork soloTimber;
        private WoodlandSave presentedWoodland;
        private WoodlandSave presentedMotions;
        private readonly Dictionary<string, LoggingTree> timberTrees = new Dictionary<string, LoggingTree>();
        private readonly Dictionary<(string Id, TimberPart Part), TimberFall> timberMotions =
            new Dictionary<(string, TimberPart), TimberFall>();
        private Transform headlessTimberPhysics;
        private readonly Dictionary<string, BoxCollider> serverStandingTrees = new Dictionary<string, BoxCollider>();
        private float nextCrownDelivery;
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

        private void StartHeadlessTimberPhysics()
        {
            headlessTimberPhysics = new GameObject("Server timber physics").transform;
            var terrain = GroundTerrain;
            if (terrain != null)
            {
                var ground = new GameObject("Terrain collision");
                ground.transform.SetParent(headlessTimberPhysics, false);
                ground.transform.SetPositionAndRotation(terrain.transform.position, terrain.transform.rotation);
                ground.AddComponent<TerrainCollider>().terrainData = terrain.terrainData;
            }
            var standingBoxes = new HashSet<BoxCollider>();
            foreach (var pair in timberTrees)
            {
                var source = pair.Value.StandingCollision;
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy)
                    continue;
                standingBoxes.Add(source);
                serverStandingTrees.Add(pair.Key, CopyCollision(source));
            }
            foreach (var obstacle in mapRoot.GetComponentsInChildren<VillageNavigationBlocker>())
                if (obstacle.TryGetComponent<BoxCollider>(out var source) && source.enabled && !source.isTrigger && !standingBoxes.Contains(source))
                    CopyCollision(source);

            BoxCollider CopyCollision(BoxCollider source)
            {
                var collision = new GameObject(source.gameObject.name) { layer = source.gameObject.layer };
                collision.transform.SetParent(headlessTimberPhysics, false);
                collision.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                collision.transform.localScale = source.transform.lossyScale;
                var collider = collision.AddComponent<BoxCollider>();
                collider.center = source.center;
                collider.size = source.size;
                collider.sharedMaterial = source.sharedMaterial;
                return collider;
            }
        }

        internal void PresentTimberMotions(WoodlandSave snapshot)
        {
            if (snapshot == null || ReferenceEquals(presentedMotions, snapshot))
                return;
            presentedMotions = snapshot;
            var pending = new HashSet<(string Id, TimberPart Part)>();
            var standing = headlessTimberPhysics != null ? new HashSet<string>(serverStandingTrees.Keys) : null;
            bool authority = !IsOnline || Session?.OwnsTimberPhysics == true;
            foreach (var record in snapshot.trees)
            {
                if (record.felledDay <= 0 || !timberTrees.TryGetValue(record.id, out var tree))
                    continue;
                tree.Present(true);
                standing?.Remove(record.id);
                var site = tree.Site();
                if (record.falling && record.Has(TimberPart.Trunk))
                    Start(tree, record.id, TimberPart.Trunk, site.Section(TimberPart.Trunk), record.fallYaw, record.landingPose, 0);
                if (record.IsSeparating(TimberPart.Lower))
                    Start(tree, record.id, TimberPart.Lower, site.Section(TimberPart.Lower), record.fallYaw, site.PoseOf(record, TimberPart.Lower), -1);
                if (record.IsSeparating(TimberPart.Upper))
                    Start(tree, record.id, TimberPart.Upper, site.Section(TimberPart.Upper), record.fallYaw, site.PoseOf(record, TimberPart.Upper), 1);
            }
            foreach (var log in snapshot.logs)
            {
                if (log.thrown)
                    StartThrow(log, pending, authority);
                else if (log.separating && log.cutPose.IsValid && log.sourceTree != null && timberTrees.TryGetValue(log.sourceTree, out var source))
                    Start(source, log.id, TimberPart.Log, source.Site().Section(log.sourcePart).Half(log.half), log.cutYaw, log.cutPose, log.half == 0 ? -1 : 1);
            }
            if (standing != null)
                foreach (var pair in serverStandingTrees)
                    pair.Value.enabled = standing.Contains(pair.Key);
            RemoveMissing(timberMotions, pending);

            void Start(LoggingTree tree, string id, TimberPart part, TimberSection section, float cutYaw, TimberPose initial, int side)
            {
                var key = (id, part);
                pending.Add(key);
                if (timberMotions.ContainsKey(key))
                    return;
                var site = tree.Site();
                TimberSection slice = part == TimberPart.Trunk ? section : site.TrimCutFaces(section);
                var bark = tree.CutGeometry(settings.Timber?.CutSurface)?.Slice(slice, barkOnly: true);
                var parent = headlessTimberPhysics ?? droppedItems;
                Action<TimberPose> settled = authority ? pose => FinishTimberMotion(id, part, site, cutYaw, initial, pose) : null;
                Action<TimberPose, Vector3> landed = authority && part == TimberPart.Trunk ? (pose, contact) => LandTimberTrunk(id, pose, contact) : null;
                var motion = part == TimberPart.Trunk
                    ? TimberFall.Begin(site, cutYaw, bark, parent, settled, landed, initial)
                    : TimberFall.Separate(site, section, initial, cutYaw, bark, slice.Center - section.Center, side, parent, settled);
                timberMotions.Add(key, motion);
            }
        }

        private void StartThrow(TimberLogSave log, HashSet<(string Id, TimberPart Part)> pending, bool authority)
        {
            var key = (log.id, TimberPart.Log);
            pending.Add(key);
            if (timberMotions.ContainsKey(key))
                return;
            float radius = .20f;
            float length = 1.35f;
            float meshOffset = 0f;
            Mesh bark = null;
            if (!string.IsNullOrEmpty(log.sourceTree) && timberTrees.TryGetValue(log.sourceTree, out var tree))
            {
                var site = tree.Site();
                var section = site.Section(log.sourcePart).Half(log.half);
                var slice = site.TrimCutFaces(section);
                bark = tree.CutGeometry(settings.Timber?.CutSurface)?.Slice(slice, barkOnly: true);
                radius = site.BarkRadius;
                length = section.Length;
                meshOffset = slice.Center - section.Center;
            }
            Vector3 origin = SurfacePosition(log.throwOrigin.x, log.throwOrigin.z) + Quaternion.Euler(0f, log.yaw, 0f) *
                TimberLocation.CarryOffset(log.throwSlot - 1);
            var release = TimberLocation.Pose(origin, Quaternion.Euler(90f, log.yaw + 90f, 0f));
            Vector3 aim = SurfacePosition(log.point.x, log.point.z) + Vector3.up * radius;
            timberMotions.Add(key, TimberFall.Throw(release, aim, radius, length, bark, meshOffset, log.cutYaw,
                headlessTimberPhysics ?? droppedItems, authority ? Settle : null,
                headlessTimberPhysics == null ? (_, contact) => PlayTimberLanding(contact) : null));

            void Settle(TimberPose pose)
            {
                if (!pose.IsValid || !IsTimberGround(pose.point) || pose.height < TimberSurfaceHeight(pose.point) - radius)
                    pose = TimberLocation.Pose(aim, TimberLocation.Rotation(release));
                CommitTimberMotion(log.id, TimberPart.Log, pose);
            }
        }

        private void LandTimberTrunk(string id, TimberPose pose, Vector3 contact)
        {
            if (IsOnline)
            {
                Session?.TimberLanded(id, pose, contact);
                return;
            }
            if (soloTimber?.RecordTrunkLanding(id, pose) != true)
                return;
            PlayTimberLanding(contact);
            soloTimber.ReleaseCrownProducts(DeliverCrownProducts, id);
            SaveGame(false);
        }

        internal void PlayTimberLanding(Vector3 contact)
        {
            if (SharesMap(contact))
                effects?.PlayTimberLanding(contact);
        }

        private void FinishTimberMotion(string id, TimberPart part, TimberSite site, float facing, TimberPose initial, TimberPose pose)
        {
            if (!pose.IsValid || !IsTimberGround(pose.point) || pose.height < TimberSurfaceHeight(pose.point) - site.BarkRadius)
            {
                if (initial.IsValid && IsTimberGround(initial.point))
                {
                    pose = initial;
                    pose.height = Mathf.Max(initial.height, TimberSurfaceHeight(initial.point) + site.BarkRadius);
                }
                else
                {
                    var recovery = site.Point.Offset(facing, site.Section(TimberPart.Trunk).Length * .5f);
                    if (!IsTimberGround(recovery))
                        recovery = site.Point;
                    pose = TimberPose.AtTilt(recovery, TimberSurfaceHeight(recovery) + site.BarkRadius, facing, 90f);
                }
            }
            CommitTimberMotion(id, part, pose);
        }

        private void CommitTimberMotion(string id, TimberPart part, TimberPose pose)
        {
            if (IsOnline)
                Session?.FinishTimberMotion(id, part, pose);
            else if (soloTimber?.SettlePiece(id, part, pose) == true)
            {
                if (part == TimberPart.Trunk)
                    soloTimber.ReleaseCrownProducts(DeliverCrownProducts, id);
                PresentWoodland(soloTimber.Capture());
                SaveGame(false);
            }
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

        internal bool TimberStumpOccupied(TimberSite site) => felledPieces.TryGetValue((site.Id, TimberPart.Stump), out var stump) &&
            stump.Seat != null && SeatInUse(stump.Seat.Position);
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

        private void RetryCrownProducts()
        {
            if (soloTimber == null || Time.unscaledTime < nextCrownDelivery)
                return;
            nextCrownDelivery = Time.unscaledTime + 1f;
            if (soloTimber.ReleaseCrownProducts(DeliverCrownProducts))
                SaveGame(false);
        }

        private bool DeliverCrownProducts(string character, TimberPoint stump, List<TimberYield> items)
        {
            var source = FindCharacter(character);
            return source != null && DeliverTimberYield(source, items, stump);
        }

        private bool DeliverTimberYield(HarvestWorker source, List<TimberYield> items, TimberPoint? stump = null)
        {
            if (!source.HasPlayerOwner || !stump.HasValue && source.CharacterState.HasInstantHarvestAcquisition)
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
                    end = IsTimberGround(item.point) ? item.point : stump ?? TimberLocation.Planar(source.StandingPosition);
                Vector3 origin = TimberYieldOrigin(item);
                Vector3 landing = SurfacePosition(end.x, end.z) + Vector3.up * .08f;
                var prefab = settings.FindItem(item.itemId).GroundPrefab;
                var drop = CaptureGroundDrop(item.itemId, item.quantity, origin, landing);
                drop.harvestYield = true;
                drop.crownYield = item.fromCrown;
                drop.flightMilliseconds = prefab.FlightMilliseconds;
                SpawnLocalPlayerDrop(drop, source.PlayerId, PickupUtcNow, prefab, null, origin, landing);
            }
            return true;
        }

        internal void PlayTimberAcquisitions(HarvestWorker source, IEnumerable<TimberYield> items)
        {
            foreach (var item in items)
                PlayHarvestAcquisition(source, TimberYieldOrigin(item), item.itemId, item.quantity);
        }

        private Vector3 TimberYieldOrigin(TimberYield item) => item.fromCrown
            ? new Vector3(item.point.x, item.crownHeight + .12f, item.point.z)
            : SurfacePosition(item.point.x, item.point.z) + Vector3.up;

        internal void TimberNotice(HarvestWorker source, string message)
        {
            if (source.IsLocallyControlled)
                hud.ShowToast(message);
        }

        internal void PlayTimberAxeImpact(Vector3 position) => effects?.PlayTimberAxeImpact(position);

        internal void PlayRemoteTimberImpact(TimberOrder order)
        {
            if (order?.action != TimberAction.Split || string.IsNullOrEmpty(order.target))
                return;
            TimberPiece piece;
            if (order.part == TimberPart.Log)
                logModels.TryGetValue(order.target, out piece);
            else
                felledPieces.TryGetValue((order.target, order.part), out piece);
            if (piece != null && SharesMap(piece.transform.position))
                piece.PlayAxeImpact();
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
            presentedWoodland = snapshot;
            PresentTimberMotions(snapshot);
            var fallen = new HashSet<string>();
            var pieces = new HashSet<(string, TimberPart)>();
            foreach (var record in snapshot.trees)
            {
                if (record.felledDay <= 0 || !timberTrees.TryGetValue(record.id, out var tree))
                    continue;
                fallen.Add(record.id);
                var site = tree.Site();
                for (int n = 0; n <= (int)TimberPart.Trunk; n++)
                {
                    var part = (TimberPart)n;
                    if (part == TimberPart.Log || !record.Has(part))
                        continue;
                    var key = (record.id, part);
                    pieces.Add(key);
                    if (!felledPieces.TryGetValue(key, out var piece))
                    {
                        piece = Instantiate(assets.Prefab(part), droppedItems);
                        piece.Bind(this, record.id, part);
                        piece.ShowCutMesh(tree.CutGeometry(assets.CutSurface), site, site.Section(part), record.fallYaw);
                        felledPieces.Add(key, piece);
                    }
                    timberMotions.TryGetValue((record.id, part == TimberPart.Stump ? TimberPart.Trunk : part), out var body);
                    piece.PresentSection(site, record, body);
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
                LoggingTree sourceTree = null;
                if (!string.IsNullOrEmpty(log.sourceTree))
                    timberTrees.TryGetValue(log.sourceTree, out sourceTree);
                var sourceSite = sourceTree != null ? sourceTree.Site() : null;
                if (!logModels.TryGetValue(log.id, out var view))
                {
                    view = Instantiate(assets.Prefab(TimberPart.Log), droppedItems);
                    view.Bind(this, log.id, TimberPart.Log);
                    if (sourceSite != null)
                        view.ShowCutMesh(sourceTree.CutGeometry(assets.CutSurface), sourceSite,
                            sourceSite.Section(log.sourcePart).Half(log.half), log.cutYaw);
                    logModels.Add(log.id, view);
                }
                timberMotions.TryGetValue((log.id, TimberPart.Log), out var body);
                view.PresentLog(log, index, body);
            }
            RemoveMissing(logModels, logs);
            worker?.RefreshTimberSkill();
            if (initialized)
                hud.RefreshWindows();
        }

        private static void RemoveMissing<TKey, T>(Dictionary<TKey, T> objects, HashSet<TKey> current) where T : Component
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
