using System;
using System.Collections.Generic;
using System.Numerics;

namespace Workerborn
{
    public enum TimberPart { Stump, Lower, Upper, Log, Trunk }
    internal enum TimberAction { Strike, Fell, Split, Lift, Place, Throw }
    internal enum TimberResult { Rejected, Failed, Succeeded }
    internal enum TimberSwing { None, Fell, Split, Strike }

    [Serializable]
    public struct TimberPoint
    {
        public float x, z;

        internal TimberPoint(float x, float z)
        {
            this.x = x;
            this.z = z;
        }

        internal bool IsFinite => float.IsFinite(x) && float.IsFinite(z);
        internal float DistanceSquared(TimberPoint other) =>
            (x - other.x) * (x - other.x) + (z - other.z) * (z - other.z);

        internal TimberPoint Offset(float yaw, float distance)
        {
            double radians = yaw * Math.PI / 180d;
            return new TimberPoint(x + (float)Math.Sin(radians) * distance, z + (float)Math.Cos(radians) * distance);
        }

        internal float HeadingFrom(TimberPoint origin, float fallback)
        {
            if (DistanceSquared(origin) < .01f)
                return fallback;
            return (float)(Math.Atan2(x - origin.x, z - origin.z) * 180d / Math.PI);
        }
    }

    [Serializable]
    public struct TimberPose
    {
        public TimberPoint point;
        public float height, rotationX, rotationY, rotationZ, rotationW;

        internal TimberPose(TimberPoint point, float height, Quaternion rotation)
        {
            this.point = point;
            this.height = height;
            rotationX = rotation.X;
            rotationY = rotation.Y;
            rotationZ = rotation.Z;
            rotationW = rotation.W;
        }

        internal Quaternion Rotation => new Quaternion(rotationX, rotationY, rotationZ, rotationW);
        internal bool IsValid => point.IsFinite && float.IsFinite(height) && Rotation.LengthSquared() is >= .99f and <= 1.01f;
        private Vector3 Axis => Vector3.Transform(Vector3.UnitY, Rotation);

        internal TimberPose ShiftAlongTrunk(float distance)
        {
            Vector3 offset = Axis * distance;
            return new TimberPose(new TimberPoint(point.x + offset.X, point.z + offset.Z), height + offset.Y, Rotation);
        }

        internal static TimberPose AtTilt(TimberPoint point, float height, float yaw, float pitch) =>
            new TimberPose(point, height, Quaternion.CreateFromYawPitchRoll(yaw * (MathF.PI / 180f), pitch * (MathF.PI / 180f), 0f));
    }

    [Serializable]
    public sealed class TimberTreeSave
    {
        public string id;
        public int productDay, usedSlots, felledDay, parts, separatingParts;
        public float fallYaw, fallAngle;
        public TimberPose restingPose, lowerPose, upperPose;
        public bool falling;

        internal float RestAngle => fallAngle > 0f ? fallAngle : 90f;
        internal bool Has(TimberPart part) => felledDay > 0 && (parts & (1 << (int)part)) != 0;
        internal bool IsSeparating(TimberPart part) => (separatingParts & (1 << (int)part)) != 0;
        internal TimberTreeSave Copy() => (TimberTreeSave)MemberwiseClone();
    }

    [Serializable]
    public sealed class TimberLogSave
    {
        public string id, carrier, sourceTree;
        public TimberPoint point, throwOrigin;
        public float yaw, cutYaw;
        public TimberPose cutPose;
        public TimberPart sourcePart;
        public int half;
        public bool thrown, moved, separating;

        internal TimberLogSave Copy() => (TimberLogSave)MemberwiseClone();
    }

    [Serializable]
    public sealed class WoodlandSave
    {
        public int revision;
        public TimberTreeSave[] trees = Array.Empty<TimberTreeSave>();
        public TimberLogSave[] logs = Array.Empty<TimberLogSave>();
    }

    [Serializable]
    internal sealed class TimberOrder
    {
        public TimberAction action;
        public string target;
        public TimberPart part;
        public TimberPoint destination;
    }

    internal readonly struct TreeProduct
    {
        internal readonly string Item;
        internal readonly int Weight, Quantity;

        internal TreeProduct(string item, int weight, int quantity)
        {
            Item = item;
            Weight = weight;
            Quantity = quantity;
        }
    }

    internal readonly struct TimberSection
    {
        internal readonly float Start, End;

        internal TimberSection(float start, float end)
        {
            Start = start;
            End = end;
        }

        internal float Length => End - Start;
        internal float Center => (Start + End) * .5f;
        internal TimberSection Half(int half) => half == 0 ? new TimberSection(Start, Center) : new TimberSection(Center, End);
    }

    internal sealed class TimberSite
    {
        internal readonly string Id;
        internal readonly TimberPoint Point;
        internal readonly string Tool;
        internal readonly int Slots;
        internal readonly float Reach, StrikeChance, FellChance, BaseHeight, CutHeight, TopHeight, BarkRadius, CrownDropDistance;
        internal readonly TreeProduct[] Products;
        internal readonly int TotalWeight;

        internal TimberSite(string id, TimberPoint point, string tool, int slots, float reach,
            float strikeChance, float fellChance, float baseHeight, float cutHeight, float topHeight, float barkRadius,
            float crownDropDistance, TreeProduct[] products)
        {
            Id = id;
            Point = point;
            Tool = tool;
            Slots = Math.Clamp(slots, 1, 32);
            Reach = reach;
            StrikeChance = strikeChance;
            FellChance = fellChance;
            BaseHeight = baseHeight;
            CutHeight = cutHeight;
            TopHeight = topHeight;
            BarkRadius = barkRadius;
            CrownDropDistance = crownDropDistance;
            Products = Array.FindAll(products, row => ItemCatalog.Contains(row.Item) && row.Weight > 0 && row.Quantity > 0);
            foreach (var product in Products)
                TotalWeight += product.Weight;
        }

        internal TimberSection Section(TimberPart part) => part switch
        {
            TimberPart.Stump => new TimberSection(0f, CutHeight),
            TimberPart.Lower => new TimberSection(CutHeight, (CutHeight + TopHeight) * .5f),
            TimberPart.Upper => new TimberSection((CutHeight + TopHeight) * .5f, TopHeight),
            _ => new TimberSection(CutHeight, TopHeight)
        };

        internal TimberSection TrimCutFaces(TimberSection section)
        {
            const float halfKerf = .025f;
            return new TimberSection(section.Start > 0f ? section.Start + halfKerf : section.Start,
                section.End < TopHeight ? section.End - halfKerf : section.End);
        }

        internal TimberPoint RestPoint(float height, float yaw, float angle) =>
            Point.Offset(yaw, (height - CutHeight) * (float)Math.Sin(angle * Math.PI / 180d));

        internal float RestHeight(float height, float angle) =>
            BaseHeight + CutHeight + (height - CutHeight) * (float)Math.Cos(angle * Math.PI / 180d);

        internal TimberPose PoseAt(TimberTreeSave tree, float height) => tree.restingPose.IsValid
            ? tree.restingPose.ShiftAlongTrunk(height - Section(TimberPart.Trunk).Center)
            : TimberPose.AtTilt(RestPoint(height, tree.fallYaw, tree.RestAngle), RestHeight(height, tree.RestAngle), tree.fallYaw, tree.RestAngle);

        internal TimberPose PoseOf(TimberTreeSave tree, TimberPart part)
        {
            var pose = part switch
            {
                TimberPart.Lower => tree.lowerPose,
                TimberPart.Upper => tree.upperPose,
                _ => default
            };
            return pose.IsValid ? pose : PoseAt(tree, Section(part).Center);
        }
    }

    [Serializable]
    internal struct TimberYield
    {
        public string itemId;
        public int quantity;
        public TimberPoint point;

        internal TimberYield(string item, int quantity, TimberPoint point)
        {
            itemId = item;
            this.quantity = quantity;
            this.point = point;
        }
    }

    internal sealed class TimberWork
    {
        internal const float PieceReach = 1.8f;
        internal const int MaximumLogs = 512;
        private const int Trunk = 1 << (int)TimberPart.Trunk;
        private const int Stump = 1 << (int)TimberPart.Stump;
        private const int Halves = 1 << (int)TimberPart.Lower | 1 << (int)TimberPart.Upper;
        private readonly Dictionary<string, TimberSite> sites = new Dictionary<string, TimberSite>();
        private readonly Dictionary<string, TimberTreeSave> trees = new Dictionary<string, TimberTreeSave>();
        private readonly Dictionary<string, TimberLogSave> logs = new Dictionary<string, TimberLogSave>();
        private readonly Func<float> roll;
        private WoodlandSave snapshot;
        private int revision;

        internal TimberWork(IEnumerable<TimberSite> sites, Func<float> roll, WoodlandSave saved = null)
        {
            foreach (var site in sites)
                if (!string.IsNullOrEmpty(site.Id))
                    this.sites[site.Id] = site;
            this.roll = roll;
            if (saved == null)
                return;
            foreach (var tree in saved.trees ?? Array.Empty<TimberTreeSave>())
                if (tree != null && !string.IsNullOrEmpty(tree.id) && this.sites.ContainsKey(tree.id) &&
                    tree.productDay >= 0 && tree.felledDay >= 0 && tree.usedSlots is >= 0 and <= 64 &&
                    (tree.parts & ~(Stump | Trunk | Halves)) == 0 &&
                    ((tree.parts & Trunk) == 0 || (tree.parts & Halves) == 0) && float.IsFinite(tree.fallYaw) &&
                    float.IsFinite(tree.fallAngle) && tree.fallAngle is >= 0f and <= 130f)
                {
                    var copy = tree.Copy();
                    copy.separatingParts &= copy.parts & Halves;
                    trees[tree.id] = copy;
                }
            foreach (var log in saved.logs ?? Array.Empty<TimberLogSave>())
                if (log != null && !string.IsNullOrEmpty(log.id) && log.point.IsFinite &&
                    Math.Abs(log.point.x) <= 10000f && Math.Abs(log.point.z) <= 10000f &&
                    float.IsFinite(log.yaw) && logs.Count < MaximumLogs)
                {
                    var copy = log.Copy();
                    copy.carrier = null;
                    copy.thrown = false;
                    if (string.IsNullOrEmpty(copy.sourceTree) || !this.sites.ContainsKey(copy.sourceTree) ||
                        copy.sourcePart is not (TimberPart.Lower or TimberPart.Upper) || copy.half is < 0 or > 1 ||
                        !float.IsFinite(copy.cutYaw))
                        copy.sourceTree = null;
                    copy.separating &= copy.sourceTree != null && copy.cutPose.IsValid && !copy.moved;
                    logs[log.id] = copy;
                }
        }

        internal WoodlandSave Capture()
        {
            if (snapshot != null)
                return snapshot;
            var treeRecords = new TimberTreeSave[trees.Count];
            int i = 0;
            foreach (var tree in trees.Values)
                treeRecords[i++] = tree.Copy();
            var logRecords = new TimberLogSave[logs.Count];
            i = 0;
            foreach (var log in logs.Values)
                logRecords[i++] = log.Copy();
            return snapshot = new WoodlandSave { revision = revision, trees = treeRecords, logs = logRecords };
        }

        internal WoodlandSave CaptureForSave(Func<string, (TimberPoint Point, float Yaw)?> carrierPose, Func<TimberPoint, bool> canPlace)
        {
            var current = Capture();
            var carried = Array.Find(current.logs, log => log.carrier != null);
            if (carried == null)
                return current;
            var savedLogs = new TimberLogSave[current.logs.Length];
            var indices = new Dictionary<string, int>();
            for (int i = 0; i < savedLogs.Length; i++)
            {
                var log = current.logs[i];
                savedLogs[i] = log;
                if (log.carrier == null)
                    continue;
                var saved = log.Copy();
                savedLogs[i] = saved;
                saved.carrier = null;
                saved.thrown = false;
                if (carrierPose(log.carrier) is not { } pose)
                    continue;
                indices.TryGetValue(log.carrier, out int index);
                indices[log.carrier] = index + 1;
                var point = pose.Point.Offset(pose.Yaw, 1.25f)
                    .Offset(pose.Yaw + 90f, (index - (CountCarried(log.carrier) - 1) * .5f) * .48f);
                if (canPlace(point))
                    saved.point = point;
                else if (canPlace(pose.Point))
                    saved.point = pose.Point;
                saved.yaw = pose.Yaw;
                saved.moved = true;
            }
            return new WoodlandSave { revision = current.revision, trees = current.trees, logs = savedLogs };
        }

        internal void StartDay(int day)
        {
            var expired = new List<string>();
            foreach (var tree in trees.Values)
                if (tree.felledDay == 0 && tree.productDay < day || tree.felledDay > 0 && tree.felledDay < day && tree.parts == 0)
                    expired.Add(tree.id);
            if (expired.Count == 0)
                return;
            foreach (string id in expired)
                trees.Remove(id);
            Changed();
        }

        internal int CountCarried(string workerId)
        {
            int count = 0;
            foreach (var log in logs.Values)
                if (log.carrier == workerId)
                    count++;
            return count;
        }

        internal bool SettlePiece(string id, TimberPart part, TimberPose pose)
        {
            if (!pose.IsValid)
                return false;
            if (part == TimberPart.Log)
            {
                if (!logs.TryGetValue(id, out var log) || !log.separating)
                    return false;
                log.cutPose = pose;
                log.point = pose.point;
                log.separating = false;
            }
            else
            {
                if (!trees.TryGetValue(id, out var tree) || !tree.Has(part))
                    return false;
                if (part == TimberPart.Trunk && tree.falling)
                {
                    tree.restingPose = pose;
                    tree.falling = false;
                }
                else if (tree.IsSeparating(part))
                {
                    if (part == TimberPart.Lower)
                        tree.lowerPose = pose;
                    else
                        tree.upperPose = pose;
                    tree.separatingParts &= ~(1 << (int)part);
                }
                else
                    return false;
            }
            Changed();
            return true;
        }

        internal TimberResult Perform(TimberOrder order, VillageState worker, string workerId, TimberPoint at, float facing,
            int day, Func<TimberPoint, bool> occupied, Func<TimberPoint, bool> canPlace,
            Func<List<TimberYield>, bool> deliver, out string message)
        {
            message = "지금은 이 나무 작업을 할 수 없어요.";
            if (order == null || worker == null || string.IsNullOrEmpty(workerId) || !at.IsFinite || !float.IsFinite(facing) ||
                worker.AwaitingIncarnation)
                return TimberResult.Rejected;

            if (order.action == TimberAction.Place || order.action == TimberAction.Throw)
                return MoveHeld(order, worker, workerId, at, facing, canPlace, out message);
            if (worker.Health <= 0 || string.IsNullOrEmpty(order.target))
                return TimberResult.Rejected;

            // Timed strikes and felling reach this step after the worker has paid for the swing.
            if (order.action == TimberAction.Lift || order.action == TimberAction.Split && order.part == TimberPart.Log)
                return WorkLog(order, worker, workerId, at, deliver, out message);
            if (!sites.TryGetValue(order.target, out var site) || CountCarried(workerId) > 0)
                return TimberResult.Rejected;

            if (!trees.TryGetValue(site.Id, out var tree))
                tree = new TimberTreeSave { id = site.Id };
            if (tree.felledDay == 0 && (order.action == TimberAction.Strike || order.action == TimberAction.Fell))
                return WorkStanding(order, worker, site, tree, at, facing, day, deliver, out message);
            if (order.action == TimberAction.Split && order.part is >= TimberPart.Stump and <= TimberPart.Trunk &&
                order.part != TimberPart.Log && tree.Has(order.part))
                return SplitPart(order.part, worker, site, tree, at, occupied, deliver, out message);
            return TimberResult.Rejected;
        }

        private TimberResult WorkStanding(TimberOrder order, VillageState worker, TimberSite site, TimberTreeSave tree,
            TimberPoint at, float facing, int day, Func<List<TimberYield>, bool> deliver, out string message)
        {
            bool fell = order.action == TimberAction.Fell;
            int available = Math.Max(0, site.Slots + worker.TreeExtraSlots - (tree.productDay == day ? tree.usedSlots : 0));
            message = null;
            if (at.DistanceSquared(site.Point) > Square(site.Reach + .2f))
                message = "나무에 조금 더 가까이 가야 해요.";
            else if (fell && !worker.HasTool(site.Tool))
                message = "도끼를 주무기에 장착해 주세요.";
            else if (!fell && (available == 0 || site.TotalWeight == 0))
                message = "나무에 남은 생산물이 없어요.";
            if (message != null)
                return TimberResult.Rejected;

            if (!Succeeds(worker, fell ? site.FellChance : site.StrikeChance))
                return Finish(worker, false, 0, fell ? "나무를 베지 못했어요." : "이번에는 아무것도 떨어지지 않았어요.", out message);

            var products = new List<TimberYield>();
            float yaw = site.Point.HeadingFrom(at, facing);
            if (fell)
            {
                var crown = site.Point.Offset(yaw, site.CrownDropDistance);
                for (int i = 0; i < available && site.TotalWeight > 0; i++)
                    if (Succeeds(worker, site.StrikeChance))
                        AddProduct(products, worker, site, crown);
            }
            else
                AddProduct(products, worker, site, site.Point);

            if (products.Count > 0 && !deliver(products))
                return NoSpace(out message);

            if (tree.productDay != day)
                tree.usedSlots = 0;
            tree.productDay = day;
            tree.usedSlots += products.Count;
            if (fell)
            {
                tree.felledDay = day;
                tree.parts = Stump | Trunk;
                tree.fallYaw = yaw;
                tree.falling = true;
            }
            trees[site.Id] = tree;
            Changed();
            return Finish(worker, true, 0, fell ? "나무를 베었어요." : "나무에서 생산물이 떨어졌어요.", out message);
        }

        private TimberResult SplitPart(TimberPart part, VillageState worker, TimberSite site, TimberTreeSave tree,
            TimberPoint at, Func<TimberPoint, bool> occupied, Func<List<TimberYield>, bool> deliver, out string message)
        {
            var section = site.Section(part);
            var piece = part == TimberPart.Stump ? default : site.PoseOf(tree, part);
            TimberPoint point = part == TimberPart.Stump ? site.Point : piece.point;
            message = "나무 조각이 멈춘 뒤에 내려찍어 주세요.";
            if (tree.falling || tree.IsSeparating(part))
                return TimberResult.Rejected;
            if (!CanSplit(worker, site.Tool, at, point, out message))
                return TimberResult.Rejected;
            if (part == TimberPart.Stump && occupied(point))
            {
                message = "누군가 밑동에 앉아 있어요.";
                return TimberResult.Rejected;
            }

            bool success = Succeeds(worker, .85f);
            if (part == TimberPart.Trunk)
            {
                if (!success)
                    return Finish(worker, false, 0, "넘어진 줄기를 가르지 못했어요.", out message);
                tree.lowerPose = site.PoseAt(tree, site.Section(TimberPart.Lower).Center);
                tree.upperPose = site.PoseAt(tree, site.Section(TimberPart.Upper).Center);
                tree.parts = (tree.parts & ~Trunk) | Halves;
                tree.separatingParts = Halves;
                Changed();
                return Finish(worker, true, 0, "줄기를 상단과 하단으로 갈랐어요.", out message);
            }

            int count = part == TimberPart.Stump ? 1 : 2;
            if (success && logs.Count > MaximumLogs - count ||
                !success && !deliver(new List<TimberYield> { new TimberYield(ItemIds.ScrapWood, 10, point) }))
                return NoSpace(out message);

            tree.parts &= ~(1 << (int)part);
            if (success)
                for (int i = 0; i < count; i++)
                {
                    string id = Guid.NewGuid().ToString("N");
                    var cut = part == TimberPart.Stump ? default : piece.ShiftAlongTrunk(section.Half(i).Center - section.Center);
                    logs.Add(id, new TimberLogSave
                    {
                        id = id, yaw = tree.fallYaw,
                        point = part == TimberPart.Stump ? point : cut.point,
                        sourceTree = part == TimberPart.Stump ? null : site.Id,
                        sourcePart = part, half = i, cutYaw = tree.fallYaw,
                        cutPose = cut, separating = part != TimberPart.Stump
                    });
                }
            Changed();
            return Finish(worker, success, 0, success ? $"통나무 {count}개를 얻었어요." : "못 쓰게 된 목재 10개가 남았어요.", out message);
        }

        private TimberResult WorkLog(TimberOrder order, VillageState worker, string workerId, TimberPoint at,
            Func<List<TimberYield>, bool> deliver, out string message)
        {
            message = "지금은 이 통나무를 사용할 수 없어요.";
            if (!logs.TryGetValue(order.target, out var log) || log.carrier != null || log.separating ||
                at.DistanceSquared(log.point) > Square(PieceReach + .2f))
                return TimberResult.Rejected;
            int carried = CountCarried(workerId);
            if (order.action == TimberAction.Lift)
            {
                int liftCost = Math.Max(1, 4 - worker.LiftStaminaSaving);
                if (carried >= worker.LiftCapacity || worker.Stamina < liftCost)
                {
                    message = carried >= worker.LiftCapacity ? "지금은 통나무를 더 들 수 없어요." : "스태미나가 부족해요.";
                    return TimberResult.Rejected;
                }
                worker.SpendWorkStamina(liftCost);
                log.carrier = workerId;
                log.thrown = false;
                Changed();
                message = "통나무를 들어 올렸어요.";
                return TimberResult.Succeeded;
            }
            if (carried > 0 || !CanSplit(worker, ItemIds.Axe, at, log.point, out message))
                return TimberResult.Rejected;

            bool success = Succeeds(worker, .85f);
            int quantity = success ? worker.TreeYield(2, roll()) : 5;
            if (!deliver(new List<TimberYield> { new TimberYield(success ? ItemIds.Timber : ItemIds.ScrapWood, quantity, log.point) }))
                return NoSpace(out message);
            logs.Remove(log.id);
            Changed();
            return Finish(worker, success, success ? quantity : 0,
                success ? $"목재 {quantity}개를 얻었어요." : "못 쓰게 된 목재 5개가 남았어요.", out message);
        }

        private TimberResult MoveHeld(TimberOrder order, VillageState worker, string workerId, TimberPoint at, float facing,
            Func<TimberPoint, bool> canPlace, out string message)
        {
            bool throwing = order.action == TimberAction.Throw;
            int count = CountCarried(workerId);
            int cost = throwing ? Math.Max(1, count + 2 - worker.LiftStaminaSaving) : 0;
            message = "들고 있는 통나무가 없어요.";
            if (count == 0)
                return TimberResult.Rejected;
            if (throwing && (worker.ThrowRange <= 0f || !order.destination.IsFinite ||
                at.DistanceSquared(order.destination) > Square(worker.ThrowRange + .04f) || worker.Stamina < cost))
            {
                message = worker.Stamina < cost ? "스태미나가 부족해요." : "던질 수 있는 범위를 벗어났어요.";
                return TimberResult.Rejected;
            }
            var point = throwing ? order.destination : at.Offset(facing, 1.25f);
            if (!canPlace(point))
            {
                if (throwing)
                {
                    message = "통나무를 던질 수 있는 바닥을 선택해 주세요.";
                    return TimberResult.Rejected;
                }
                point = at;
            }
            float yaw = throwing ? point.HeadingFrom(at, facing) : facing;
            Release(workerId, point, at, yaw, throwing, canPlace);
            worker.SpendWorkStamina(cost);
            message = throwing ? "통나무를 던졌어요." : "통나무를 내려놓았어요.";
            return TimberResult.Succeeded;
        }

        internal void Release(string workerId, TimberPoint? point = null, float yaw = 0f)
        {
            if (point.HasValue && point.Value.IsFinite)
            {
                Release(workerId, point.Value, point.Value, yaw, false, _ => true);
                return;
            }
            bool changed = false;
            foreach (var log in logs.Values)
                if (log.carrier == workerId)
                {
                    log.carrier = null;
                    changed = true;
                }
            if (changed)
                Changed();
        }

        private void Release(string workerId, TimberPoint point, TimberPoint origin, float yaw, bool throwing, Func<TimberPoint, bool> canPlace)
        {
            int count = CountCarried(workerId);
            int index = 0;
            foreach (var log in logs.Values)
                if (log.carrier == workerId)
                {
                    var spread = point.Offset(yaw + 90f, (index++ - (count - 1) * .5f) * .48f);
                    log.carrier = null;
                    log.yaw = yaw;
                    log.point = canPlace(spread) ? spread : point;
                    log.throwOrigin = origin;
                    log.thrown = throwing;
                    log.moved = true;
                }
            if (count > 0)
                Changed();
        }

        private static bool CanSplit(VillageState worker, string tool, TimberPoint at, TimberPoint point, out string message)
        {
            message = worker.HasTool(tool) ? null : "도끼를 주무기에 장착해 주세요.";
            if (message == null && at.DistanceSquared(point) > Square(PieceReach + .2f))
                message = "나무에 조금 더 가까이 가야 해요.";
            return message == null;
        }

        private void AddProduct(List<TimberYield> rewards, VillageState worker, TimberSite site, TimberPoint point)
        {
            float pick = Math.Min(.999999f, roll()) * site.TotalWeight;
            foreach (var product in site.Products)
            {
                pick -= product.Weight;
                if (pick >= 0f)
                    continue;
                rewards.Add(new TimberYield(product.Item, worker.TreeYield(product.Quantity, roll()), point));
                return;
            }
        }

        private bool Succeeds(VillageState worker, float chance) => worker.TreeWorkSucceeds(chance, roll());
        private static float Square(float value) => value * value;

        private static TimberResult Finish(VillageState worker, bool success, int timber, string text, out string message)
        {
            if (success)
                worker.RecordTimberWork(timber);
            message = text;
            return success ? TimberResult.Succeeded : TimberResult.Failed;
        }

        private static TimberResult NoSpace(out string message)
        {
            message = "결과물을 둘 공간이 부족해요.";
            return TimberResult.Rejected;
        }

        private void Changed()
        {
            snapshot = null;
            revision++;
        }
    }
}
