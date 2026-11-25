using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class LoggingTree : HarvestableCrop
    {
        private const float ImpactDuration = .24f;
        private const float ImpactDegrees = 4f;
        [SerializeField] private ItemDefinition branch;
        [SerializeField, Range(1, 8)] private int branchesPerStrike = 1;
        private Quaternion restRotation;
        private Vector3 impactAxis;
        private float impactStartedAt;
        private bool shaking;
        public ItemDefinition Branch => branch;
        internal override bool CanReceiveStrike => CanInteract && branch != null && branch.GroundPrefab != null;

        public override string InteractionVerbFor(HarvestWorker worker) =>
            StrikesByDefault(worker) ? "때리기" : InteractionVerb;
        internal override InteractionCursor CursorFor(HarvestWorker worker) =>
            StrikesByDefault(worker) ? InteractionCursor.Attack : InteractionCursor.Chop;

        protected override void Interact(HarvestWorker worker)
        {
            if (StrikesByDefault(worker))
                worker.CommandAttack(this);
            else
                base.Interact(worker);
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            if (CanReceiveStrike)
                actions.Add(new WorldContextAction("때리기", () =>
                {
                    if (BeginInteraction(village.Worker))
                        village.Worker.CommandAttack(this);
                }));
            actions.Add(new WorldContextAction(InteractionVerb, () =>
            {
                if (BeginInteraction(village.Worker))
                    village.Worker.CommandHarvest(this);
            }));
        }

        internal override void ReceiveStrike(HarvestWorker worker, int damage)
        {
            if (!CanReceiveStrike || worker.Village == null)
                return;
            ShowImpact(worker);
            if (worker.Village.DropStrikeLoot(transform, branch, Mathf.Clamp(branchesPerStrike, 1, 8), worker))
                NotifyInteractionCompleted();
        }

        internal void ShowImpact(HarvestWorker worker)
        {
            if (Visual == null || !Visual.gameObject.activeSelf)
                return;
            if (!shaking)
                restRotation = Visual.localRotation;
            Vector3 away = transform.position - worker.transform.position;
            away.y = 0f;
            Vector3 axis = away.sqrMagnitude > .001f ? Vector3.Cross(Vector3.up, away.normalized) : transform.right;
            impactAxis = Visual.parent != null ? Visual.parent.InverseTransformDirection(axis) : axis;
            impactStartedAt = Time.time;
            shaking = true;
        }

        private void LateUpdate()
        {
            if (!shaking)
                return;
            float phase = (Time.time - impactStartedAt) / ImpactDuration;
            if (phase >= 1f)
                ResetImpact();
            else
            {
                float degrees = ImpactDegrees * Mathf.Sin(phase * (2f * Mathf.PI)) * (1f - phase);
                Visual.localRotation = Quaternion.AngleAxis(degrees, impactAxis) * restRotation;
            }
        }

        private void ResetImpact()
        {
            if (shaking && Visual != null)
                Visual.localRotation = restRotation;
            shaking = false;
        }

        protected override void OnDisable()
        {
            ResetImpact();
            base.OnDisable();
        }

        private bool StrikesByDefault(HarvestWorker worker)
        {
            var tool = Definition?.RequiredTool;
            return CanReceiveStrike && tool != null && worker.Village != null && !worker.CharacterState.HasTool(tool.Id);
        }
    }
}
