using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class PlotFacilityPreview : MonoBehaviour
    {
        [SerializeField, Tooltip("Uses the facility's footprint. A field preview shows one repeated cell.")]
        private PlotFacilityDefinition facility;
        [SerializeField, Min(1)] private int draftWidth = 1;
        [SerializeField, Min(1)] private int draftDepth = 1;

        private void OnDrawGizmos()
        {
            if (Application.isPlaying || GetComponentInParent<VillagePlot>() != null)
                return;
            var footprint = new PlotFootprint(draftWidth, draftDepth, PlotGround.Land);
            if (facility != null)
                footprint = facility.FillsPlot ? new PlotFootprint(1, 1, facility.Footprint.Ground) : facility.Footprint;
            VillagePlot.DrawGrid(transform, footprint, new Color(1f, .62f, .24f));
        }
    }
}
