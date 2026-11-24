using UnityEngine;

namespace Workerborn
{
    [DisallowMultipleComponent]
    public sealed class VillagePlot : MonoBehaviour
    {
        public const float CellSide = 5f;
        public const float CellArea = CellSide * CellSide;

        [SerializeField, Tooltip("Unique, permanent ID of this plot in the world save.")]
        private string plotId;
        [SerializeField] private string displayName = "건설 부지";
        [SerializeField, Min(1)] private int width = 1;
        [SerializeField, Min(1)] private int depth = 1;
        [SerializeField] private PlotGround ground;
        [SerializeField, Tooltip("The sign is positioned at the front (+Z) edge when the village starts.")]
        private VillagePlotSign sign;
        [SerializeField] private Transform frontMarker;
        [SerializeField] private GameObject cornerMarker;

        private GameObject contents;
        private VillageInteriorMap interior;
        internal PlotFacilityDefinition InstalledFacility { get; private set; }
        internal string Id => plotId;
        public string DisplayName => displayName;
        internal PlotFootprint Footprint => new PlotFootprint(width, depth, ground);
        internal bool IsConfigured => !string.IsNullOrWhiteSpace(plotId) && sign != null && Footprint.IsValid;

        internal void Bind(VillagePrototype village)
        {
            transform.position = village.SurfacePosition(transform.position.x, transform.position.z);
            float halfDepth = depth * CellSide * .5f;
            sign.transform.position = Surface(village, 0f, halfDepth + .45f);
            sign.Bind(this);
            if (frontMarker != null)
                frontMarker.position = Surface(village, 0f, halfDepth - .42f);
            if (cornerMarker == null)
                return;

            float halfWidth = width * CellSide * .5f;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Instantiate(cornerMarker, Surface(village, x * halfWidth, z * halfDepth), transform.rotation, transform);
        }

        internal bool Render(VillagePrototype village, PlotFacilityDefinition facility, out bool navigationChanged)
        {
            navigationChanged = false;
            if (InstalledFacility?.Id == facility?.Id)
                return false;
            if (contents != null)
            {
                if (!village.RemovePlotInterior(interior))
                    return false;
                interior = null;
                navigationChanged = contents.GetComponentInChildren<VillageNavigationBlocker>() != null;
                foreach (var crop in contents.GetComponentsInChildren<HarvestableCrop>(true))
                    village.RemovePlotCrop(crop);
                contents.SetActive(false);
                Destroy(contents);
            }
            InstalledFacility = facility;
            if (facility == null)
                return true;

            contents = new GameObject(facility.Title);
            contents.transform.SetParent(transform, false);
            string resourceId = $"plot/{plotId}/{facility.Id}";
            if (facility.FillsPlot)
                PlantField(village, facility, resourceId);
            else
            {
                Instantiate(facility.Prefab, contents.transform, false);
                interior = village.AddPlotInterior(this, contents.GetComponentInChildren<VillageBuildingDoor>(true));
                var crops = contents.GetComponentsInChildren<HarvestableCrop>(true);
                for (int i = 0; i < crops.Length; i++)
                    village.RegisterPlotCrop(crops[i], $"{resourceId}/{i}");
            }

            foreach (var npc in contents.GetComponentsInChildren<CharacterActor>(true))
            {
                Vector3 position = npc.transform.position;
                npc.transform.position = village.SurfacePosition(position.x, position.z);
                VillagePrototype.BindCharacter(npc, idleOnly: true);
            }
            navigationChanged |= contents.GetComponentInChildren<VillageNavigationBlocker>() != null;
            return true;
        }

        private void PlantField(VillagePrototype village, PlotFacilityDefinition facility, string resourceId)
        {
            float side = CellSide;
            Vector2Int density = facility.PlantsPerCell;
            for (int row = 0; row < depth; row++)
                for (int column = 0; column < width; column++)
                {
                    float x = (column - width * .5f) * side;
                    float z = (row - depth * .5f) * side;
                    Instantiate(facility.Prefab, Surface(village, x + side * .5f, z + side * .5f), transform.rotation, contents.transform);
                    for (int plantZ = 0; plantZ < density.y; plantZ++)
                        for (int plantX = 0; plantX < density.x; plantX++)
                        {
                            var position = Surface(village, x + (plantX + .5f) * side / density.x, z + (plantZ + .5f) * side / density.y);
                            var crop = Instantiate(facility.FieldCrop, position, transform.rotation, contents.transform);
                            village.RegisterPlotCrop(crop, $"{resourceId}/{column}/{row}/{plantX}/{plantZ}");
                        }
                }
        }

        private Vector3 Surface(VillagePrototype village, float x, float z)
        {
            Vector3 position = transform.TransformPoint(new Vector3(x, 0f, z));
            return village.SurfacePosition(position.x, position.z) + Vector3.up * .025f;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                DrawGrid(transform, Footprint, ground == PlotGround.Fishing ? new Color(.25f, .65f, 1f) : Color.cyan);
        }

        internal static void DrawGrid(Transform origin, PlotFootprint footprint, Color outline)
        {
            if (!footprint.IsValid)
                return;
            int width = footprint.Width;
            int depth = footprint.Depth;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;
            Gizmos.matrix = origin.localToWorldMatrix;
            float halfWidth = width * CellSide * .5f;
            float halfDepth = depth * CellSide * .5f;
            const float top = .09f;
            Gizmos.color = new Color(outline.r, outline.g, outline.b, .12f);
            Gizmos.DrawCube(Vector3.up * top, new Vector3(halfWidth * 2f, .01f, halfDepth * 2f));
            Gizmos.color = outline;
            for (int column = 0; column <= width; column++)
            {
                float x = column * CellSide - halfWidth;
                Gizmos.DrawLine(new Vector3(x, top, -halfDepth), new Vector3(x, top, halfDepth));
            }
            for (int row = 0; row <= depth; row++)
            {
                float z = row * CellSide - halfDepth;
                Gizmos.DrawLine(new Vector3(-halfWidth, top, z), new Vector3(halfWidth, top, z));
            }

            Gizmos.color = Color.yellow;
            Vector3 front = new Vector3(0f, top, halfDepth + .82f);
            Gizmos.DrawLine(Vector3.up * top, front);
            Gizmos.DrawLine(front, front + new Vector3(-.32f, 0f, -.36f));
            Gizmos.DrawLine(front, front + new Vector3(.32f, 0f, -.36f));
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }
    }
}
