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
    }

    [DisallowMultipleComponent]
    public sealed class TimberPiece : WorldInteractable
    {
        [SerializeField] private TimberPart part;
        [SerializeField] private GameObject model;
        [SerializeField] private Collider hitVolume;
        [SerializeField] private SeatSlot seat;
        private VillagePrototype village;
        private string target;
        private string carrier;
        private int carryingIndex;
        private bool moving;
        private float started, duration, dropHeight;
        private Vector3 from, landing;
        private Quaternion landingRotation;
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
        public override bool CanInteract => base.CanInteract && carrier == null && !moving && motion == null;
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
            Mesh mesh = geometry.Slice(slice);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials = geometry.Materials;
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

        private void Place(Vector3 point, Quaternion rotation)
        {
            landing = point;
            landingRotation = rotation;
            moving = false;
            motion = null;
            carrier = null;
            transform.SetPositionAndRotation(point, rotation);
            RefreshAppearance();
        }

        internal void PresentSection(TimberSite site, TimberTreeSave tree, TimberFall body)
        {
            if (part == TimberPart.Stump)
            {
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

        internal void PresentLog(TimberLogSave log, int index, bool animate, TimberFall body)
        {
            if (body != null)
            {
                Place(body.transform.position, SectionRotation(body.transform.rotation));
                motion = body;
                RefreshAppearance();
                return;
            }
            string previousCarrier = carrier;
            Vector3 point = village.SurfacePosition(log.point.x, log.point.z);
            Quaternion rotation = Quaternion.Euler(hasCutMesh ? 90f : 0f, log.yaw, 0f);
            if (hasCutMesh)
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
                moving = false;
                UpdateCarriedPose();
                RefreshAppearance();
            }
            else if (animate && log.thrown && previousCarrier != null)
            {
                from = transform.position;
                if (!model.activeSelf)
                    from = village.SurfacePosition(log.throwOrigin.x, log.throwOrigin.z) + Vector3.up * 1.15f;
                Place(point, rotation);
                dropHeight = Mathf.Clamp(Vector3.Distance(from, point) * .34f + .65f, 1.2f, 4f);
                duration = Mathf.Clamp(Vector3.Distance(from, point) * .055f + .42f, .45f, 1.18f);
                started = Time.time + .22f;
                moving = true;
                UpdateFlight(0f);
                RefreshAppearance();
            }
            else if (!moving || previousCarrier != null || (point - landing).sqrMagnitude > .0001f)
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

        internal override void ReceiveStrike(HarvestWorker worker, int damage)
        {
            if (!CanInteract)
                return;
            PlayAxeImpact();
            worker.Village.RequestTimberWork(worker, new TimberOrder { action = TimberAction.Split, target = target, part = part });
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
            else if (moving)
            {
                float progress = Mathf.Max(0f, (Time.time - started) / duration);
                if (progress >= 1f)
                    Place(landing, landingRotation);
                else
                    UpdateFlight(progress);
            }
        }

        private void UpdateFlight(float progress)
        {
            var point = Vector3.Lerp(from, landing, progress) + Vector3.up * (dropHeight * 4f * progress * (1f - progress));
            transform.SetPositionAndRotation(point, landingRotation * Quaternion.AngleAxis(Mathf.Sin(progress * Mathf.PI) * 45f, Vector3.forward));
        }

        private void UpdateCarriedPose()
        {
            Transform holder = village.TimberCarrier(carrier);
            model.SetActive(holder != null);
            if (holder == null)
                return;
            int inRow = carryingIndex % 3;
            float x = carryingIndex == 0 ? 0f : (inRow == 1 ? -.27f : .27f);
            Vector3 offset = new Vector3(x, 1.04f + (carryingIndex / 3) * .19f, .58f + (carryingIndex > 0 && inRow == 0 ? .17f : 0f));
            transform.SetPositionAndRotation(holder.TransformPoint(offset), Quaternion.Euler(hasCutMesh ? 90f : 0f, holder.eulerAngles.y + 90f, 0f));
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
