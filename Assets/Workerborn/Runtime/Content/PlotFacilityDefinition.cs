using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Plot Facility", fileName = "PlotFacility")]
    public sealed class PlotFacilityDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string title;
        [SerializeField] private PlotGround ground;
        [SerializeField, Min(1)] private int width = 1;
        [SerializeField, Min(1)] private int depth = 1;
        [SerializeField, Tooltip("The prefab is placed at the center of the plot, or repeated in each cell for fields. Its local +Z faces the sign.")]
        private GameObject prefab;
        [SerializeField, Tooltip("Assign a crop prefab for a field that occupies the entire plot and can be replaced later.")]
        private HarvestableCrop fieldCrop;
        [SerializeField, Tooltip("Number of plants across and along each cell of a field.")]
        private Vector2Int plantsPerCell = new Vector2Int(2, 2);
        [SerializeField, Tooltip("Schedule facility type for the entire generated field. For other facilities, add Village Facility directly to the prefab.")]
        private VillageFacilityType fieldFacilityType;
        [SerializeField, Tooltip("The ground drawn on the map for this facility.")]
        private VillageMapSurface mapSurface;

        public string Id => id;
        public string Title => title;
        public GameObject Prefab => prefab;
        public HarvestableCrop FieldCrop => fieldCrop;
        public bool FillsPlot => fieldCrop != null;
        internal VillageMapSurface MapSurface => mapSurface;
        internal VillageFacilityType FieldFacilityType => fieldFacilityType;
        internal Vector2Int PlantsPerCell => new Vector2Int(Mathf.Clamp(plantsPerCell.x, 1, 4), Mathf.Clamp(plantsPerCell.y, 1, 4));
        internal PlotFootprint Footprint => new PlotFootprint(width, depth, ground);
        internal bool IsConfigured => !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title) && prefab != null &&
            Footprint.IsValid && (!FillsPlot || fieldCrop.Definition != null);
    }
}
