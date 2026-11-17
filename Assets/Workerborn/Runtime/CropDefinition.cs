using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Crop", fileName = "Crop")]
    public sealed class CropDefinition : ScriptableObject
    {
        [SerializeField] private string itemId = ItemIds.Potato;
        [SerializeField] private string displayName = "감자";
        [SerializeField, Min(1)] private int harvestsPerPlant = 3;
        [SerializeField, Min(0.1f)] private float harvestSeconds = 2.4f;
        [SerializeField, Range(1, 12)] private int itemsPerHarvest = 2;
        [SerializeField] private GameObject plantPrefab;
        [SerializeField] private GameObject dropPrefab;

        public string DisplayName => displayName;
        public string ItemId => itemId;
        public int HarvestsPerPlant => Mathf.Max(1, harvestsPerPlant);
        public float HarvestSeconds => Mathf.Max(0.1f, harvestSeconds);
        public int ItemsPerHarvest => Mathf.Clamp(itemsPerHarvest, 1, 12);
        public GameObject PlantPrefab => plantPrefab;
        public GameObject DropPrefab => dropPrefab;
    }
}
