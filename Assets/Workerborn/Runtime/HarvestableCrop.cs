using System;
using UnityEngine;

namespace Workerborn
{
    public sealed class HarvestableCrop : MonoBehaviour
    {
        [SerializeField] private CropDefinition definition;
        private HarvestCycle cycle;
        private HarvestWorker owner;
        private Transform visual;
        private Vector3 restScale;
        private Renderer[] charges;

        public CropDefinition Definition => definition;
        public int Remaining => cycle?.Remaining ?? 0;
        public int Capacity => cycle?.Capacity ?? 0;
        public bool IsDepleted => cycle == null || cycle.IsDepleted;
        public float Progress => cycle?.Progress ?? 0f;
        public event Action<HarvestableCrop> Harvested;

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
            if (!isActiveAndEnabled || worker == null || owner != null || cycle == null || !cycle.TryBegin())
                return false;
            owner = worker;
            return true;
        }

        public bool Advance(HarvestWorker worker, float seconds)
        {
            if (owner != worker || owner == null || !cycle.Advance(seconds)) return false;
            owner = null;
            RefreshVisuals();
            Harvested?.Invoke(this);
            return true;
        }

        public void Cancel(HarvestWorker worker)
        {
            if (owner != worker) return;
            cycle?.Cancel();
            owner = null;
            if (visual != null) visual.localScale = restScale;
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
            if (visual != null)
            {
                visual.gameObject.SetActive(!IsDepleted);
                visual.localScale = restScale;
            }
            if (charges == null) return;
            for (int i = 0; i < charges.Length; i++)
                charges[i].enabled = i < Remaining;
        }

        private void Update()
        {
            if (owner != null && visual != null)
                visual.localScale = restScale * (1f + Mathf.Sin(Time.time * 16f) * 0.025f);
        }

        private void OnDisable()
        {
            cycle?.Cancel();
            owner = null;
        }
    }
}
