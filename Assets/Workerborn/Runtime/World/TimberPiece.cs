using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    internal static class TimberLocation
    {
        internal static TimberPoint Planar(Vector3 point) => new TimberPoint(point.x, point.z);
        internal static TimberPose Pose(Vector3 point, Quaternion rotation) => new TimberPose(Planar(point), point.y,
            new System.Numerics.Quaternion(rotation.x, rotation.y, rotation.z, rotation.w));
        internal static Vector3 Position(TimberPose pose) => new Vector3(pose.point.x, pose.height, pose.point.z);
        internal static Quaternion Rotation(TimberPose pose) => new Quaternion(pose.rotationX, pose.rotationY, pose.rotationZ, pose.rotationW);
        internal static Vector3 CarryOffset(int index)
        {
            float depth = (index % 3) switch { 1 => -.40f, 2 => .40f, _ => 0f };
            return new Vector3(0f, 2.13f + index / 3 * .40f, .13f + depth);
        }
    }

    [DisallowMultipleComponent]
    public sealed class TimberPiece : WorldInteractable
    {
        private static readonly int BigWindAmount = Shader.PropertyToID("_Big_Wind_Amount");
        private static readonly int SmallWindAmount = Shader.PropertyToID("_Small_Wind_Amount");

        [SerializeField] private TimberPart part;
        [SerializeField] private GameObject model;
        [SerializeField] private Collider hitVolume;
        [SerializeField] private SeatSlot seat;
        private VillagePrototype village;
        private string target;
        private string carrier;
        private int carryingIndex;
        private TimberFall motion;
        private float groundClearance;
        private bool hasCutMesh;
        public TimberPart Part => part;
        internal string Carrier => carrier;
        internal string TargetId => target;
        internal SeatSlot Seat => seat;
        public override string DisplayName => part switch
        {
            TimberPart.Stump => "나무 밑동",
            TimberPart.Lower => "나무 하단",
            TimberPart.Upper => "나무 상단",
            TimberPart.Trunk => "넘어진 나무 줄기",
            _ => "통나무"
        };

        protected override float DefaultDistance => TimberWork.PieceReach;
        public override string InteractionVerb => "내려찍기";
        public override string InteractionVerbFor(HarvestWorker worker) =>
            part == TimberPart.Log && worker.CanSelectLog ? "들어 올리기" : InteractionVerb;
        internal override InteractionCursor CursorFor(HarvestWorker worker) =>
            part == TimberPart.Log && worker.CanSelectLog ? InteractionCursor.PickUp : InteractionCursor.Chop;
        public override bool CanInteract => base.CanInteract && carrier == null && motion == null;
        internal override bool CanReceiveStrike => CanInteract;
        internal override AnimationClip StrikeClip(HarvestWorker worker) => worker.Village.Settings.Timber.Split;
        internal override float StrikeImpact => .52f;
        internal override int StrikeStaminaCost(VillageState state, int baseCost) =>
            state.WorkEnergyCost(SkillId.Woodcutting, ItemIds.Timber, 3);

        internal void Bind(VillagePrototype owner, string id, TimberPart piece)
        {
            village = owner;
            target = id;
            part = piece;
            AssignPersistentId(part == TimberPart.Log ? id : $"{id}:{part}");
            SuppressWind(model.GetComponent<MeshRenderer>());
        }

        internal void ShowCutMesh(TimberGeometry geometry, TimberSite site, TimberSection section, float cutYaw)
        {
            if (geometry == null)
                return;
            var slice = site.TrimCutFaces(section);
            model.SetActive(false);
            model = new GameObject("Cut tree mesh") { layer = 2 };
            model.transform.SetParent(transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, -cutYaw, 0f);
            model.transform.localPosition = Vector3.up * (slice.Center - (part == TimberPart.Stump ? 0f : section.Center));
            bool barkOnly = part != TimberPart.Trunk;
            Mesh mesh = geometry.Slice(slice, barkOnly);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = geometry.MaterialsFor(barkOnly);
            SuppressWind(renderer);
            hasCutMesh = true;
            groundClearance = site.BarkRadius;
            if (part != TimberPart.Stump && hitVolume is CapsuleCollider capsule)
            {
                capsule.direction = 1;
                capsule.center = Vector3.zero;
                capsule.radius = Mathf.Max(capsule.radius, site.BarkRadius);
                capsule.height = section.Length + .08f;
            }
        }

        private static void SuppressWind(MeshRenderer renderer)
        {
            if (renderer == null)
                return;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetFloat(BigWindAmount, 0f);
            properties.SetFloat(SmallWindAmount, 0f);
            renderer.SetPropertyBlock(properties);
        }

        private void Place(Vector3 point, Quaternion rotation)
        {
            motion = null;
            carrier = null;
            transform.SetPositionAndRotation(point, rotation);
            RefreshAppearance();
        }

        internal void PresentSection(TimberSite site, TimberTreeSave tree, TimberFall body)
        {
            if (part == TimberPart.Stump)
            {
                if (seat != null && hasCutMesh)
                    seat.SetSurfaceHeight(site.TrimCutFaces(site.Section(TimberPart.Stump)).End);
                Place(new Vector3(site.Point.x, site.BaseHeight, site.Point.z), Quaternion.Euler(0f, tree.fallYaw, 0f));
            }
            else if (body != null)
            {
                Place(body.transform.position, SectionRotation(body.transform.rotation));
            }
            else
            {
                var pose = site.PoseOf(tree, part);
                Place(TimberLocation.Position(pose), SectionRotation(TimberLocation.Rotation(pose)));
            }
            motion = body;
            RefreshAppearance();
        }

        private Quaternion SectionRotation(Quaternion rotation) => hasCutMesh ? rotation : rotation * Quaternion.Euler(-90f, 0f, 0f);

        internal void PresentLog(TimberLogSave log, int index, TimberFall body)
        {
            if (body != null)
            {
                Place(body.transform.position, SectionRotation(body.transform.rotation));
                motion = body;
                RefreshAppearance();
                return;
            }
            Vector3 point = village.SurfacePosition(log.point.x, log.point.z);
            Quaternion rotation = Quaternion.Euler(hasCutMesh ? 90f : 0f, log.yaw, 0f);
            if (log.throwRest.IsValid)
            {
                point = TimberLocation.Position(log.throwRest);
                rotation = SectionRotation(TimberLocation.Rotation(log.throwRest));
            }
            else if (hasCutMesh)
            {
                if (!log.moved && log.cutPose.IsValid)
                {
                    point = TimberLocation.Position(log.cutPose);
                    rotation = TimberLocation.Rotation(log.cutPose);
                }
                else
                    point.y += groundClearance;
            }
            carryingIndex = index;
            if (log.carrier != null)
            {
                carrier = log.carrier;
                UpdateCarriedPose();
                RefreshAppearance();
            }
            else
                Place(point, rotation);
        }

        protected override void Interact(HarvestWorker worker) => RequestSplit(worker);

        public override void AddContextActions(VillagePrototype owner, List<WorldContextAction> actions)
        {
            actions.Add(new WorldContextAction(InteractionVerb, () => RequestSplit(owner.Worker)));
            if (seat != null)
                actions.Add(new WorldContextAction("앉기", () => owner.Worker.CommandApproach(this, () => owner.Worker.ToggleSit(seat))));
            if (part == TimberPart.Log && owner.State.IsSkillLearned(SkillId.Lifting))
                actions.Add(new WorldContextAction("들어 올리기", () => owner.Worker.LiftLog(this)));
        }

        private void RequestSplit(HarvestWorker worker)
        {
            if (!CanInteract || !worker.AcceptsInteractionCommands)
                return;
            if (worker.CarriedLogs > 0)
                worker.Village.TimberNotice(worker, "들고 있는 통나무를 먼저 내려놓아 주세요.");
            else if (!worker.CharacterState.HasTool(ItemIds.Axe))
                worker.Village.TimberNotice(worker, "도끼를 주무기에 장착해 주세요.");
            else if (seat != null && village.SeatInUse(seat.Position))
                worker.Village.TimberNotice(worker, "누군가 밑동에 앉아 있어요.");
            else
                worker.CommandAttack(this);
        }

        internal void PlayAxeImpact()
        {
            Vector3 above = TargetCenter + Vector3.up * (hitVolume.bounds.extents.y + .15f);
            village.PlayTimberAxeImpact(hitVolume.ClosestPoint(above));
        }

        private void LateUpdate()
        {
            if (carrier != null)
                UpdateCarriedPose();
            else if (motion != null && part != TimberPart.Stump)
                transform.SetPositionAndRotation(motion.transform.position, SectionRotation(motion.transform.rotation));
        }

        private void UpdateCarriedPose()
        {
            Transform holder = village.TimberCarrier(carrier);
            model.SetActive(holder != null);
            if (holder == null)
                return;
            transform.SetPositionAndRotation(holder.TransformPoint(TimberLocation.CarryOffset(carryingIndex)),
                Quaternion.Euler(hasCutMesh ? 90f : 0f, holder.eulerAngles.y + 90f, 0f));
        }

        private void RefreshAppearance()
        {
            if (carrier == null)
                model.SetActive(true);
            hitVolume.enabled = CanInteract;
            if (seat != null)
                seat.enabled = CanInteract;
        }
    }
}
