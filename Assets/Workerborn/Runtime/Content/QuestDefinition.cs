using UnityEngine;

namespace Workerborn
{
    public enum QuestKind { FirstJob, WorldContribution, FarmFoundation }

    [CreateAssetMenu(menuName = "Workerborn/Content/Quest", fileName = "Quest")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string title;
        [SerializeField] private QuestKind kind;
        [SerializeField, TextArea(3, 10)] private string description;
        [Header("Completion and rewards")]
        [SerializeField, Min(1)] private int requiredCompletions = 1;
        [SerializeField, Min(0)] private int rewardGold = 30;
        [SerializeField, Min(0)] private int rewardAP = 2;
        [SerializeField, Min(0)] private int rewardExperience = 15;
        [Header("World contribution")]
        [SerializeField] private ItemDefinition contributionItem;
        [SerializeField, Min(0)] private int requiredGold = 100;
        [SerializeField, Min(1)] private int requiredItems = 20;
        [SerializeField, Min(1)] private int goldPerContribution = 25;
        [SerializeField, Min(1)] private int itemsPerContribution = 5;
        [SerializeField, Min(1)] private int resultingWorldStage = 2;
        [Header("Document")]
        [SerializeField] private ItemDefinition contractItem;

        public string Id => id;
        public string Title => title;
        public QuestKind Kind => kind;
        public string Description => description;
        public int RequiredCompletions => requiredCompletions;
        public int RewardGold => rewardGold;
        public int RewardAP => rewardAP;
        public int RewardExperience => rewardExperience;
        public ItemDefinition ContributionItem => contributionItem;
        public int RequiredGold => requiredGold;
        public int RequiredItems => requiredItems;
        public int GoldPerContribution => goldPerContribution;
        public int ItemsPerContribution => itemsPerContribution;
        public int ResultingWorldStage => resultingWorldStage;
        public ItemDefinition ContractItem => contractItem;
    }
}
