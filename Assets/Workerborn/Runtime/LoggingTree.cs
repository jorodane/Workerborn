using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class LoggingTree : HarvestableCrop
    {
        [Serializable]
        private struct Product
        {
            public ItemDefinition item;
            [Min(1)] public int weight;
            [Min(1)] public int quantity;
        }

        private const float ImpactDuration = .24f;
        private const float ImpactDegrees = 4f;
        [SerializeField] private Product[] products = Array.Empty<Product>();
        [SerializeField, Range(0f, 1f)] private float strikeChance = .60f;
        [SerializeField, Min(.4f)] private float lowerDistance = 1.35f;
        [SerializeField, Min(.8f)] private float upperDistance = 3.55f;
        private Quaternion restRotation;
        private Vector3 impactAxis;
        private float impactStartedAt;
        private bool shaking;
        private bool felled;

        public override bool CanInteract => base.CanInteract && !felled;
        internal override bool CanReceiveStrike => CanInteract;
        internal bool Felled => felled;

        internal TimberSite Site() => new TimberSite(PersistentId, TimberLocation.Planar(transform.position),
            Definition.RequiredTool.Id, Definition.HarvestsPerPlant, Definition.StaminaCost, InteractionDistance,
            strikeChance, Definition.SuccessChance, lowerDistance, upperDistance, Array.ConvertAll(products ?? Array.Empty<Product>(),
                product => new TreeProduct(product.item != null ? product.item.Id : "", product.weight, product.quantity)));

        public override string InteractionVerbFor(HarvestWorker worker) =>
            HasAxe(worker) ? "나무 베기" : "나무 때리기";
        internal override InteractionCursor CursorFor(HarvestWorker worker) =>
            HasAxe(worker) ? InteractionCursor.Chop : InteractionCursor.Attack;

        protected override void Interact(HarvestWorker worker)
        {
            if (HasAxe(worker))
                worker.CommandHarvest(this);
            else
                worker.CommandAttack(this);
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            if (!CanInteract)
                return;
            actions.Add(new WorldContextAction("나무 때리기", () =>
            {
                if (BeginInteraction(village.Worker))
                    village.Worker.CommandAttack(this);
            }));
            actions.Add(new WorldContextAction("나무 베기", () =>
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
            if (worker.Village.RequestTimberWork(worker, new TimberOrder { action = TimberAction.Strike, target = PersistentId }))
                NotifyInteractionCompleted();
        }

        protected override bool Gather(HarvestWorker worker)
        {
            worker.Village?.RequestTimberWork(worker, new TimberOrder { action = TimberAction.Fell, target = PersistentId });
            // The work cycle times the swing. The woodland record owns the remaining products and the cut state.
            return false;
        }

        internal void Present(bool isFelled)
        {
            if (isFelled == felled)
                return;
            felled = isFelled;
            ResetImpact();
            if (Visual != null)
                Visual.gameObject.SetActive(!felled);
            SetInteractionGeometryActive(!felled);
            RenewInteractionGeneration();
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
                Visual.localRotation = Quaternion.AngleAxis(ImpactDegrees * Mathf.Sin(phase * (2f * Mathf.PI)) * (1f - phase), impactAxis) * restRotation;
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

        private bool HasAxe(HarvestWorker worker) => worker.CharacterState?.HasTool(Definition.RequiredTool.Id) == true;
    }
}
