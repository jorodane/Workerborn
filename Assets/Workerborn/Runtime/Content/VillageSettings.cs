using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Village Settings", fileName = "VillageSettings")]
    public sealed class VillageSettings : ScriptableObject
    {
        [Header("Content")]
        [SerializeField] private ItemDefinition[] items;
        [SerializeField] private PartTimeJobDefinition defaultJob;
        [SerializeField] private QuestDefinition introQuest;
        [SerializeField] private QuestDefinition expansionQuest;
        [SerializeField] private CharacterDefinition player;
        [SerializeField] private Font interfaceFont;
        [Header("Time and rest")]
        [SerializeField, Min(0.1f)] private float minutesPerSecond = 3f;
        [SerializeField, Range(0, 23)] private int wakeHour = 6;
        [SerializeField, Range(1, 24)] private int collapseHour = 24;
        [SerializeField, Min(0)] private int roomPrice = 10;
        [SerializeField, Range(1, 100)] private int freeRestEnergy = 70;
        [SerializeField, Range(1, 100)] private int collapseEnergy = 45;
        [Header("Progression")]
        [SerializeField, Min(1)] private int rebirthLevel = 3;
        [SerializeField, Min(1)] private int apPerLevel = 2;
        [SerializeField, Min(1)] private int harvestExperience = 8;
        [SerializeField, Min(1)] private int harvestEnergy = 5;

        public CharacterDefinition Player => player;
        public Font InterfaceFont => interfaceFont;
        public PartTimeJobDefinition DefaultJob => defaultJob;
        public QuestDefinition IntroQuest => introQuest;
        public QuestDefinition ExpansionQuest => expansionQuest;
        public ItemDefinition FindItem(string id)
        {
            if (items != null) foreach (var item in items) if (item != null && item.Id == id) return item;
            return null;
        }

        public VillageRules CreateRules(NpcDefinition employer = null)
        {
            var jobDefinition = employer != null && employer.Job != null ? employer.Job : defaultJob;
            var introduction = introQuest;
            var expansion = expansionQuest;
            if (employer != null && employer.Quests != null)
                foreach (var quest in employer.Quests)
                {
                    if (quest == null) continue;
                    if (quest.Kind == QuestKind.FirstJob) introduction = quest;
                    if (quest.Kind == QuestKind.WorldContribution) expansion = quest;
                }
            if (items != null)
            {
                var specs = new System.Collections.Generic.List<ItemSpec>();
                foreach (var item in items) if (item != null) specs.Add(item.ToSpec());
                ItemCatalog.Configure(specs);
            }
            var rules = new VillageRules
            {
                MinutesPerSecond = minutesPerSecond, WakeMinute = wakeHour * 60, CollapseMinute = collapseHour * 60,
                RoomPrice = roomPrice, FreeRestEnergy = freeRestEnergy, CollapseEnergy = collapseEnergy,
                RebirthLevel = rebirthLevel, APPerLevel = apPerLevel, HarvestExperience = harvestExperience, HarvestEnergy = harvestEnergy
            };
            if (jobDefinition != null)
            {
                rules.JobTitle = jobDefinition.Title; rules.JobId = jobDefinition.Id;
                rules.JobItemId = jobDefinition.Item != null ? jobDefinition.Item.Id : ItemIds.Potato;
                rules.JobStart = jobDefinition.StartMinute; rules.JobEnd = jobDefinition.EndMinute;
                var tiers = jobDefinition.Tiers;
                if (tiers != null && tiers.Length > 0)
                {
                    rules.JobTiers = new JobTier[tiers.Length];
                    for (int i = 0; i < tiers.Length; i++) rules.JobTiers[i] = new JobTier(tiers[i].requiredCompletions, tiers[i].quantity, tiers[i].gold, tiers[i].experience);
                }
                rules.WorldJobQuantity = jobDefinition.ExtraQuantity; rules.WorldJobGold = jobDefinition.ExtraGold; rules.WorldJobExperience = jobDefinition.ExtraExperience;
            }
            if (introduction != null)
            {
                rules.IntroTitle = introduction.Title; rules.IntroDescription = introduction.Description;
                rules.IntroRequiredJobs = introduction.RequiredCompletions;
                rules.IntroGold = introduction.RewardGold; rules.IntroAP = introduction.RewardAP; rules.IntroExperience = introduction.RewardExperience;
            }
            if (expansion != null)
            {
                rules.ExpansionTitle = expansion.Title; rules.ExpansionDescription = expansion.Description;
                rules.DonationGold = expansion.RequiredGold; rules.DonationItems = expansion.RequiredItems;
                rules.DonationItemId = expansion.ContributionItem != null ? expansion.ContributionItem.Id : ItemIds.Potato;
                rules.DonationGoldStep = expansion.GoldPerContribution; rules.DonationItemStep = expansion.ItemsPerContribution;
                rules.ExpandedStage = expansion.ResultingWorldStage;
            }
            return rules;
        }
    }
}
