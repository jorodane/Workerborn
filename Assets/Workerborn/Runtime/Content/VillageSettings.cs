using TMPro;
using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Village Settings", fileName = "VillageSettings")]
    public sealed class VillageSettings : ScriptableObject
    {
        [Header("Content")]
        [SerializeField]
        private ItemDefinition[] items;
        [SerializeField]
        private SkillDefinition[] skills;
        [SerializeField]
        private PlotFacilityDefinition[] plotFacilities;
        [SerializeField]
        private PartTimeJobDefinition defaultJob;
        [SerializeField]
        private QuestDefinition introQuest;
        [SerializeField]
        private QuestDefinition expansionQuest;
        [SerializeField]
        private QuestDefinition farmFoundation;
        [SerializeField, Tooltip("Situation-dependent dialogue supplied outside a quest.")]
        private DialogueOffer[] dialogueOffers;
        [SerializeField]
        private CharacterDefinition player;
        [SerializeField]
        private TMP_FontAsset interfaceFont;
        [SerializeField] private VillageCursorSet cursors;
        [Header("Time and rest")]
        [SerializeField, Min(0.1f)]
        private float minutesPerSecond = 3f;
        [SerializeField, Range(0, 23)]
        private int wakeHour = 6;
        [SerializeField, Range(1, 24)]
        private int collapseHour = 24;
        [SerializeField, Min(0)]
        private int roomPrice = 10;
        [SerializeField, Range(1, 100)]
        private int freeRestEnergy = 70;
        [SerializeField, Range(1, 100)]
        private int collapseEnergy = 45;
        [Header("Progression")]
        [SerializeField, Min(1)]
        private int rebirthDays = 7;
        [SerializeField, Min(0)]
        private int healthPerLevel = 2;
        [SerializeField, Min(0)]
        private int staminaPerLevel = 1;
        [SerializeField, Min(1)]
        private int apPerLevel = 2;
        [SerializeField, Min(1)]
        private int harvestExperience = 8;
        [SerializeField, Min(1)]
        private int harvestEnergy = 5;
        public CharacterDefinition Player => player;
        public TMP_FontAsset InterfaceFont => interfaceFont;
        internal VillageCursorSet Cursors => cursors;
        public PartTimeJobDefinition DefaultJob => defaultJob;
        public QuestDefinition IntroQuest => introQuest;
        public QuestDefinition ExpansionQuest => expansionQuest;
        internal DialogueOffer[] DialogueOffers => dialogueOffers ?? System.Array.Empty<DialogueOffer>();
        internal System.Collections.Generic.IReadOnlyList<ItemDefinition> Items => items ?? System.Array.Empty<ItemDefinition>();
        internal System.Collections.Generic.IReadOnlyList<PlotFacilityDefinition> PlotFacilities => plotFacilities ?? System.Array.Empty<PlotFacilityDefinition>();

        internal PlotFacilityDefinition FindPlotFacility(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (var facility in PlotFacilities)
                    if (facility != null && facility.Id == id && facility.IsConfigured)
                        return facility;
            return null;
        }

        public SkillDefinition FindSkill(SkillId id)
        {
            if (skills != null)
                foreach (var skill in skills)
                    if (skill != null && skill.Id == id)
                        return skill;
            return null;
        }

        public ItemDefinition FindItem(string id)
        {
            if (items != null)
                foreach (var item in items)
                    if (item != null && item.Id == id)
                        return item;
            return null;
        }

        [SerializeField] private RemoteVillagePlayer remotePlayerPrefab;
        [SerializeField] private WoodcuttingLegendDefinition woodcuttingLegend;
        public RemoteVillagePlayer RemotePlayerPrefab => remotePlayerPrefab;

        internal (QuestDefinition Introduction, QuestDefinition Expansion, QuestDefinition Foundation) GetQuests(NpcDefinition employer)
        {
            var introduction = introQuest;
            var expansion = expansionQuest;
            if (employer != null && employer.Quests != null)
                foreach (var quest in employer.Quests)
                {
                    if (quest == null)
                        continue;
                    if (quest.Kind == QuestKind.FirstJob)
                        introduction = quest;
                    if (quest.Kind == QuestKind.WorldContribution)
                        expansion = quest;
                }
            return (introduction, expansion, farmFoundation);
        }

        public VillageRules CreateRules(NpcDefinition employer = null)
        {
            var jobDefinition = employer != null && employer.Job != null ? employer.Job : defaultJob;
            var (introduction, expansion, foundation) = GetQuests(employer);

            if (items != null)
            {
                var specs = new System.Collections.Generic.List<ItemSpec>();
                foreach (var item in items)
                    if (item != null)
                        specs.Add(item.ToSpec());
                ItemCatalog.Configure(specs);
            }

            var rules = new VillageRules
            {
                MinutesPerSecond = minutesPerSecond,
                WakeMinute = wakeHour * 60,
                CollapseMinute = collapseHour * 60,
                RoomPrice = roomPrice,
                FreeRestEnergy = freeRestEnergy,
                CollapseEnergy = collapseEnergy,
                RebirthDays = Mathf.Max(1, rebirthDays),
                HealthPerLevel = Mathf.Clamp(healthPerLevel, 0, 1000),
                StaminaPerLevel = Mathf.Clamp(staminaPerLevel, 0, 1000),
                APPerLevel = apPerLevel,
                HarvestExperience = harvestExperience,
                HarvestEnergy = harvestEnergy
            };
            rules.PlotFacilities = new PlotFacilityRule[PlotFacilities.Count];
            var facilityIds = new System.Collections.Generic.HashSet<string>();
            for (int index = 0; index < PlotFacilities.Count; index++)
            {
                var facility = PlotFacilities[index];
                if (facility == null)
                    continue;
                if (!facility.IsConfigured || !facilityIds.Add(facility.Id))
                {
                    Debug.LogError("Assign a valid plot facility with a unique ID.", facility);
                    continue;
                }
                rules.PlotFacilities[index] = new PlotFacilityRule(facility.Id, facility.Title, facility.Footprint, facility.FillsPlot);
            }
            woodcuttingLegend?.Configure(rules);
            foreach (var id in SkillProgression.All)
            {
                var skill = FindSkill(id);
                if (skill != null)
                    rules.SetSkill(skill.ToProgression());
            }
            if (jobDefinition != null)
            {
                rules.JobTitle = jobDefinition.Title;
                rules.JobId = jobDefinition.Id;
                rules.JobDescription = jobDefinition.Description;
                rules.JobItemId = jobDefinition.Item != null ? jobDefinition.Item.Id : ItemIds.Potato;
                rules.JobDocumentItemId = jobDefinition.ContractItem?.Id;
                rules.JobStart = jobDefinition.StartMinute;
                rules.JobEnd = jobDefinition.EndMinute;
                var tiers = jobDefinition.Tiers;
                if (tiers != null && tiers.Length > 0)
                {
                    rules.JobTiers = new JobTier[tiers.Length];
                    for (int i = 0; i < tiers.Length; i++)
                        rules.JobTiers[i] = new JobTier(tiers[i].requiredCompletions, tiers[i].quantity, tiers[i].gold, tiers[i].experience,
                            tiers[i].rewardItem?.Id, tiers[i].rewardItem != null ? tiers[i].rewardItemCount : 0);
                }

                rules.WorldJobQuantity = jobDefinition.ExtraQuantity;
                rules.WorldJobGold = jobDefinition.ExtraGold;
                rules.WorldJobExperience = jobDefinition.ExtraExperience;
            }

            if (introduction != null)
            {
                rules.IntroTitle = introduction.Title;
                rules.IntroDescription = introduction.Description;
                rules.IntroRequiredJobs = introduction.RequiredCompletions;
                rules.IntroGold = introduction.RewardGold;
                rules.IntroAP = introduction.RewardAP;
                rules.IntroExperience = introduction.RewardExperience;
            }

            if (expansion != null)
            {
                rules.ExpansionTitle = expansion.Title;
                rules.ExpansionDescription = expansion.Description;
                rules.DonationGold = expansion.RequiredGold;
                rules.DonationItems = expansion.RequiredItems;
                rules.DonationItemId = expansion.ContributionItem != null ? expansion.ContributionItem.Id : ItemIds.Potato;
                rules.DonationGoldStep = expansion.GoldPerContribution;
                rules.DonationItemStep = expansion.ItemsPerContribution;
                rules.ExpandedStage = expansion.ResultingWorldStage;
                rules.ExpansionRewardGold = expansion.RewardGold;
                rules.ExpansionRewardAP = expansion.RewardAP;
                rules.ExpansionRewardExperience = expansion.RewardExperience;
            }

            if (foundation != null && foundation.ContractItem != null && foundation.ContributionItem != null)
                rules.Foundation = new FarmFoundationRules
                {
                    Title = foundation.Title,
                    Description = foundation.Description,
                    DocumentItemId = foundation.ContractItem.Id,
                    TimberItemId = foundation.ContributionItem.Id,
                    TimberCount = foundation.RequiredItems,
                    RewardGold = foundation.RewardGold,
                    RewardAP = foundation.RewardAP,
                    RewardExperience = foundation.RewardExperience
                };

            return rules;
        }
    }
}
