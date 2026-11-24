using System;
using UnityEngine;

namespace Workerborn
{
    public class HarvestableCrop : WorldInteractable
    {
        [SerializeField]
        private CropDefinition definition;
        private HarvestCycle cycle;
        private HarvestWorker owner;
        [SerializeField]
        private Transform visual;
        private Vector3 restScale;
        [SerializeField]
        private Renderer[] charges;
        [SerializeField] private bool keepVisualWhenDepleted;
        [SerializeField] private GameObject depletedVisual;
        [SerializeField] private Collider[] interactionColliders;
        private Collider[] hoverAssistColliders;
        public CropDefinition Definition => definition;
        public override string DisplayName => definition != null ? definition.DisplayName : name;
        protected override float DefaultDistance => 1.05f;

        protected override void Interact(HarvestWorker worker) => worker.CommandHarvest(this);
        public int Remaining => cycle?.Remaining ?? 0;
        public int Capacity => cycle?.Capacity ?? 0;
        public bool IsDepleted => cycle == null || cycle.IsDepleted;
        public override bool CanInteract => base.CanInteract && !IsDepleted;
        public override string InteractionVerb => definition != null ? definition.InteractionVerb : base.InteractionVerb;
        public float Progress => cycle?.Progress ?? 0f;
        protected virtual bool CanStartHarvest => true;

        public event Action<HarvestableCrop> Harvested;
        protected void Awake() => EnsureInitialized();
        public void EnsureInitialized()
        {
            if (cycle != null || definition == null)
                return;
            if (visual != null)
            {
                foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                    if (collider.gameObject.layer != PointerTargeting.HoverAssistLayer)
                        collider.enabled = false;
                foreach (var child in visual.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject.layer != PointerTargeting.HoverAssistLayer)
                        child.gameObject.layer = 2;
                restScale = visual.localScale;
            }
            ResetHarvests();
        }

        public void Initialize(CropDefinition crop, Transform plantVisual, Renderer[] chargeMarkers)
        {
            definition = crop;
            visual = plantVisual;
            restScale = visual.localScale;
            charges = chargeMarkers;
            ResetHarvests();
        }

        public bool TryBegin(HarvestWorker worker)
        {
            if (!isActiveAndEnabled || !CanStartHarvest || worker == null || owner != null || cycle == null || !cycle.TryBegin())
                return false;
            owner = worker;
            return true;
        }

        public bool Advance(HarvestWorker worker, float seconds)
        {
            if (owner != worker || owner == null || !cycle.Advance(seconds))
                return false;
            owner = null;
            RefreshVisuals();
            Harvested?.Invoke(this);
            NotifyInteractionCompleted();
            return true;
        }

        public void Cancel(HarvestWorker worker)
        {
            if (owner != worker)
                return;
            cycle?.Cancel();
            owner = null;
            if (visual != null)
                visual.localScale = restScale;
        }

        protected void InterruptHarvest(string message)
        {
            var worker = owner;
            if (worker == null)
                return;
            Cancel(worker);
            worker.HarvestInterrupted(this, message);
        }

        public void ResetHarvests()
        {
            owner = null;
            cycle = new HarvestCycle(definition.HarvestsPerPlant, definition.HarvestSeconds);
            RefreshVisuals();
        }

        public void RestoreRemaining(int remaining)
        {
            owner = null;
            cycle.RestoreRemaining(Mathf.Clamp(remaining, 0, Capacity));
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            bool available = !IsDepleted;
            var colliders = interactionColliders != null && interactionColliders.Length > 0 ? interactionColliders : GetComponents<Collider>();
            foreach (var collider in colliders)
                collider.enabled = available;
            hoverAssistColliders ??= Array.FindAll(GetComponentsInChildren<Collider>(true),
                collider => collider.enabled && collider.isTrigger && collider.gameObject.layer == PointerTargeting.HoverAssistLayer);
            foreach (var collider in hoverAssistColliders)
                if (collider != null)
                    collider.enabled = available;
            if (visual != null)
            {
                visual.gameObject.SetActive(keepVisualWhenDepleted || !IsDepleted);
                visual.localScale = restScale;
            }
            if (depletedVisual != null)
                depletedVisual.SetActive(IsDepleted);

            if (charges == null)
                return;
            for (int i = 0; i < charges.Length; i++)
                charges[i].enabled = i < Remaining;
        }

        private void Update()
        {
            if (owner != null && visual != null)
                visual.localScale = restScale * (1f + Mathf.Sin(Time.time * 16f) * 0.025f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            cycle?.Cancel();
            owner = null;
        }
    }
}
