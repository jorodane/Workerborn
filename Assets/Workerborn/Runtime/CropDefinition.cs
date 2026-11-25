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
        [SerializeField] private SkillId skill;
        [SerializeField] private string interactionVerb = "수확하기";
        [SerializeField] private ItemDefinition requiredTool;
        [SerializeField] private Sprite skillIcon;
        [SerializeField] private WorkAnimationDefinition workAnimation;
        [SerializeField, Min(0), Tooltip("0 uses the village's default stamina cost.")] private int staminaCost;
        [SerializeField, Range(0, 1)] private float successChance = 1f;
        [Header("Loot Scatter")]
        [SerializeField] private bool scatterTowardWorker;
        [SerializeField, Range(0, 360)] private float scatterAngle = 360f;
        [SerializeField] private Vector2 scatterRadius = new Vector2(.4f, 1.25f);
        [SerializeField, Range(1, GroundDropSave.MaximumGatheredDrops)] private int maxVisibleDrops = 24;
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
        public SkillId Skill => skill;
        public ItemDefinition RequiredTool => requiredTool;
        public Sprite SkillIcon => skillIcon;
        public WorkAnimationDefinition WorkAnimation => workAnimation;
        public int StaminaCost => staminaCost;
        public int MaxVisibleDrops => Mathf.Clamp(maxVisibleDrops, 1, GroundDropSave.MaximumGatheredDrops);

        public Vector3 SampleDropOffset(Vector3 towardWorker, int index, int count, float angleRoll, float radiusRoll)
        {
            towardWorker.y = 0;
            Vector3 forward = scatterTowardWorker && towardWorker.sqrMagnitude > .001f ? towardWorker.normalized : Vector3.forward;
            float angle = ((index + angleRoll) / count - .5f) * scatterAngle;
            float radius = Mathf.Lerp(Mathf.Max(0, scatterRadius.x), Mathf.Max(scatterRadius.x, scatterRadius.y), radiusRoll);
            return Quaternion.AngleAxis(angle, Vector3.up) * forward * radius;
        }

        public WorkOutcome RollOutcome(VillageState worker, float successRoll, float bonusRoll)
        {
            if (successRoll >= successChance && successChance < 1f)
                return WorkOutcome.Failure;
            return worker.RollGatheringBonus(bonusRoll);
        }
    }
}
