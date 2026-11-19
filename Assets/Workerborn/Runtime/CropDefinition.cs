using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Crop", fileName = "Crop")]
    public sealed class CropDefinition : ScriptableObject
    {
        [SerializeField]
        private string itemId = ItemIds.Potato;
        [SerializeField]
        private string displayName = "감자";
        [SerializeField, Min(1)]
        private int harvestsPerPlant = 3;
        [SerializeField, Min(0.1f)]
        private float harvestSeconds = 2.4f;
        [SerializeField, Range(1, 12)]
        private int itemsPerHarvest = 2;
        [Header("Work")]
        [SerializeField] private string interactionVerb = "수확하기";
        [SerializeField] private ItemDefinition requiredTool;
        [SerializeField] private Sprite skillIcon;
        [SerializeField, Min(0), Tooltip("0 uses the village's default stamina cost.")] private int staminaCost;
        [SerializeField, Range(0, 1)] private float successChance = 1f;
        [SerializeField, Range(0, 1)] private float luckyChance = .1f;
        [SerializeField, Range(0, 1)] private float greatLuckyChance = .01f;
        [SerializeField, Range(0, 8)] private int greatLuckyRank = 6;
        [Header("Legacy harvest demo model references")]
        [SerializeField]
        private GameObject plantPrefab;
        [SerializeField]
        private GameObject dropPrefab;
        [Header("Village ground-item prefab")]
        [SerializeField]
        private GroundPickup pickupPrefab;
        public GroundPickup PickupPrefab => pickupPrefab;
        public string DisplayName => displayName;
        public string ItemId => itemId;
        public int HarvestsPerPlant => Mathf.Max(1, harvestsPerPlant);
        public float HarvestSeconds => Mathf.Max(0.1f, harvestSeconds);
        public int ItemsPerHarvest => Mathf.Clamp(itemsPerHarvest, 1, 12);
        public GameObject PlantPrefab => plantPrefab;
        public GameObject DropPrefab => dropPrefab;
        public string InteractionVerb => interactionVerb;
        public ItemDefinition RequiredTool => requiredTool;
        public Sprite SkillIcon => skillIcon;
        public int StaminaCost => staminaCost;

        public WorkOutcome RollOutcome(int rank, float successRoll, float bonusRoll)
        {
            if (successRoll >= successChance && successChance < 1f)
                return WorkOutcome.Failure;
            float great = rank >= greatLuckyRank ? greatLuckyChance : 0;
            if (bonusRoll < great)
                return WorkOutcome.GreatLucky;
            return bonusRoll < great + luckyChance ? WorkOutcome.Lucky : WorkOutcome.Success;
        }
    }
}
