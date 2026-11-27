using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/NPC", fileName = "NPC")]
    public sealed class NpcDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string occupation;
        [SerializeField] private VillageRole role;
        [SerializeField] private CharacterDefinition appearance;
        [SerializeField] private DialogueDefinition dialogue;
        [SerializeField] private PartTimeJobDefinition job;
        [SerializeField] private QuestDefinition[] quests;
        [SerializeField] private ItemDefinition[] shopItems;
        [SerializeField, Min(0.5f)] private float interactionDistance = 1.8f;
        [SerializeField] private NpcScheduleEntry[] dailySchedule;
        [SerializeField] private ItemDefinition startingFood;
        [SerializeField, Min(0)] private int startingFoodCount;
        [SerializeField, Min(0)] private int shoppingGold;

        public string Id => id;
        public string DisplayName => displayName;
        public string Occupation => occupation;
        public VillageRole Role => role;
        public CharacterDefinition Appearance => appearance;
        public DialogueDefinition Dialogue => dialogue;
        public PartTimeJobDefinition Job => job;
        public QuestDefinition[] Quests => quests;
        public ItemDefinition[] ShopItems => shopItems;
        public float InteractionDistance => interactionDistance;
        internal NpcScheduleEntry[] DailySchedule => dailySchedule ?? System.Array.Empty<NpcScheduleEntry>();
        internal ItemDefinition StartingFood => startingFood;
        internal int StartingFoodCount => startingFoodCount;
        internal int ShoppingGold => shoppingGold;
    }
}
