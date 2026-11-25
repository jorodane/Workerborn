using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    internal static class TimberLocation
    {
        internal static TimberPoint Planar(Vector3 point) => new TimberPoint(point.x, point.z);
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
        private float started, yaw, duration, dropHeight;
        private Vector3 from, landing;
        private bool falling;
        private Vector3 pivot;
        private float fallDistance;
        public TimberPart Part => part;
        internal string Carrier => carrier;
        internal string TargetId => target;
        internal SeatSlot Seat => seat;
        public override string DisplayName => part switch
        {
            TimberPart.Stump => "나무 밑동",
            TimberPart.Lower => "나무 하단",
            TimberPart.Upper => "나무 상단",
            _ => "통나무"
        };

        protected override float DefaultDistance => TimberWork.PieceReach;
        public override string InteractionVerb => "내려찍기";
        public override string InteractionVerbFor(HarvestWorker worker) =>
            part == TimberPart.Log && worker.CanSelectLog ? "들어 올리기" : InteractionVerb;
        internal override InteractionCursor CursorFor(HarvestWorker worker) =>
            part == TimberPart.Log && worker.CanSelectLog ? InteractionCursor.PickUp : InteractionCursor.Chop;
        public override bool CanInteract => base.CanInteract && carrier == null && !moving;
        internal override bool CanReceiveStrike => CanInteract;
        internal override AnimationClip StrikeClip(HarvestWorker worker) => worker.Village.Settings.Timber.Split;
        internal override float StrikeImpact => .52f;

        internal void Bind(VillagePrototype owner, string id)
        {
            village = owner;
            target = id;
            AssignPersistentId(id);
        }

        internal void Place(Vector3 point, float facing)
        {
            landing = point;
            yaw = facing;
            moving = false;
            carrier = null;
            transform.SetPositionAndRotation(point, Quaternion.Euler(0f, yaw, 0f));
            RefreshAppearance();
        }

        internal void Fall(Vector3 foot, float distance, Vector3 point, float facing)
        {
            Place(point, facing);
            falling = true;
            moving = true;
            pivot = foot;
            fallDistance = distance;
            duration = .58f;
            started = Time.time;
            UpdateFlight(0f);
            RefreshAppearance();
        }

        internal void PresentLog(TimberLogSave log, int index, bool animate)
        {
            string previousCarrier = carrier;
            Vector3 point = village.SurfacePosition(log.point.x, log.point.z);
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
                Place(point, log.yaw);
                falling = false;
                dropHeight = Mathf.Clamp(Vector3.Distance(from, point) * .34f + .65f, 1.2f, 4f);
                duration = Mathf.Clamp(Vector3.Distance(from, point) * .055f + .42f, .45f, 1.18f);
                started = Time.time + .22f;
                moving = true;
                UpdateFlight(0f);
                RefreshAppearance();
            }
            else if (!moving || previousCarrier != null || (point - landing).sqrMagnitude > .0001f)
                Place(point, log.yaw);
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
            worker.Village.RequestTimberWork(worker, new TimberOrder { action = TimberAction.Split, target = target, part = part });
        }

        private void LateUpdate()
        {
            if (carrier != null)
                UpdateCarriedPose();
            else if (moving)
            {
                float progress = Mathf.Max(0f, (Time.time - started) / duration);
                if (progress >= 1f)
                    Place(landing, yaw);
                else
                    UpdateFlight(progress);
            }
        }

        private void UpdateFlight(float progress)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            if (falling)
            {
                float angle = progress * progress * Mathf.PI * .5f;
                var point = pivot + rotation * Vector3.forward * (Mathf.Sin(angle) * fallDistance) +
                    Vector3.up * (Mathf.Cos(angle) * fallDistance);
                point.y += (landing.y - pivot.y) * progress;
                transform.SetPositionAndRotation(point, rotation * Quaternion.Euler(-90f * (1f - progress * progress), 0f, 0f));
            }
            else
            {
                var point = Vector3.Lerp(from, landing, progress) + Vector3.up * (dropHeight * 4f * progress * (1f - progress));
                transform.SetPositionAndRotation(point, rotation * Quaternion.Euler(0f, 0f, Mathf.Sin(progress * Mathf.PI) * 45f));
            }
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
            transform.SetPositionAndRotation(holder.TransformPoint(offset), Quaternion.Euler(0f, holder.eulerAngles.y + 90f, 0f));
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
