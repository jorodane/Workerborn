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
        [SerializeField, Min(.2f)] private float cutHeight = .44f;
        [SerializeField, Min(.04f)] private float barkRadius = .19f;
        [SerializeField] private GameObject standingNavigation;
        private TimberSite site;
        private MeshFilter cutSource;
        private TimberGeometry cuts;
        private Quaternion restRotation;
        private Vector3 impactAxis;
        private float impactStartedAt;
        private bool shaking;
        private bool felled;

        public override bool CanInteract => base.CanInteract && !felled;
        internal override bool CanReceiveStrike => CanInteract;
        internal override int StrikeStaminaCost(VillageState state, int baseCost) =>
            state.WorkEnergyCost(SkillId.Woodcutting, ItemIds.Timber, 2);
        internal bool Felled => felled;
        internal BoxCollider StandingCollision => standingNavigation != null ? standingNavigation.GetComponent<BoxCollider>() : null;

        internal TimberSite Site()
        {
            if (site != null)
                return site;
            var lod = Visual != null ? Visual.GetComponentInChildren<LODGroup>(true) : null;
            var levels = lod != null ? lod.GetLODs() : Array.Empty<LOD>();
            cutSource = levels.Length > 0 && levels[0].renderers.Length > 0 && levels[0].renderers[0] != null
                ? levels[0].renderers[0].GetComponent<MeshFilter>() : Visual?.GetComponentInChildren<MeshFilter>(true);
            float top = cutHeight + 6f;
            if (cutSource != null && cutSource.sharedMesh != null)
            {
                Bounds bounds = cutSource.sharedMesh.bounds;
                Matrix4x4 world = cutSource.transform.localToWorldMatrix;
                top = world.MultiplyPoint3x4(bounds.center).y - transform.position.y +
                    Mathf.Abs(world.m10) * bounds.extents.x + Mathf.Abs(world.m11) * bounds.extents.y +
                    Mathf.Abs(world.m12) * bounds.extents.z;
            }
            return site = new TimberSite(PersistentId, TimberLocation.Planar(transform.position),
                Definition.RequiredTool.Id, Definition.HarvestsPerPlant, InteractionDistance, strikeChance, Definition.SuccessChance,
                transform.position.y, cutHeight, Mathf.Max(cutHeight + .4f, top), barkRadius,
                Array.ConvertAll(products ?? Array.Empty<Product>(),
                    product => new TreeProduct(product.item != null ? product.item.Id : "", product.weight, product.quantity)));
        }

        internal TimberGeometry CutGeometry(Material surface)
        {
            if (cuts == null && cutSource != null && cutSource.sharedMesh != null && cutSource.sharedMesh.isReadable)
                cuts = new TimberGeometry(cutSource, transform.position, surface);
            return cuts;
        }

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
            if (standingNavigation != null)
                standingNavigation.SetActive(!felled);
            SetInteractionGeometryActive(!felled);
            RenewInteractionGeneration();
        }

        internal void ShowImpact(HarvestWorker worker, bool withAxe = false)
        {
            if (Visual == null || !Visual.gameObject.activeSelf)
                return;
            if (!shaking)
                restRotation = Visual.localRotation;
            Vector3 away = transform.position - worker.transform.position;
            away.y = 0f;
            if (withAxe)
                worker.Village?.PlayTimberAxeImpact(transform.position + Vector3.up * (cutHeight + .14f) - away.normalized * barkRadius);
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

        private void OnDestroy() => cuts?.Dispose();

        private bool HasAxe(HarvestWorker worker) => worker.CharacterState?.HasTool(Definition.RequiredTool.Id) == true;
    }
}
