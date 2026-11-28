using System;
using System.Collections.Generic;

namespace Workerborn
{
    public sealed class JobTier
    {
        public int RequiredCompletions;
        public int Quantity;
        public int Gold;
        public int Experience;
        public string RewardItemId;
        public int RewardItemCount;
        public JobTier(int required, int quantity, int gold, int experience, string rewardItemId = null, int rewardItemCount = 0)
        {
            RequiredCompletions = required; Quantity = quantity; Gold = gold; Experience = experience;
            RewardItemId = rewardItemId; RewardItemCount = rewardItemCount;
        }
    }

    public sealed class FarmFoundationRules
    {
        internal int Importance = 120;
        public string Title;
        public string Description;
        public string TimberItemId;
        public int TimberCount;
        public string DocumentItemId;
        public int RewardGold;
        public int RewardAP;
        public int RewardExperience;
    }

    public sealed class VillageRules
    {
        internal readonly Dictionary<string, PlotFootprint> BuildPlots = new Dictionary<string, PlotFootprint>();
        internal PlotFacilityRule[] PlotFacilities = Array.Empty<PlotFacilityRule>();

        internal PlotFacilityRule FindFacility(string id)
        {
            foreach (var facility in PlotFacilities)
                if (facility != null && facility.Id == id)
                    return facility;
            return null;
        }

        private readonly SkillProgression[] skills = new SkillProgression[SkillProgression.All.Count];
        public SkillProgression Skill(SkillId id) => skills[(int)id] ?? SkillProgression.Default(id);
        public void SetSkill(SkillProgression skill) => skills[(int)skill.Id] = skill;

        public float MinutesPerSecond = 3f;
        public int WakeMinute = 360;
        public int CollapseMinute = 1440;
        public int RoomPrice = 10;
        public int FreeRestEnergy = 70;
        public int CollapseEnergy = 45;
        public float StaminaRecoveryPerSecond = 2f;
        public float SeatedRecoveryMultiplier = 2.5f;
        public NutritionRules Nutrition = new NutritionRules();
        public int RebirthDays = 7;
        public int LegendFoundingTimber = 100;
        public int LegendTrialTimber = 25;
        public int LegendMaturationDays = 7;
        internal int LegendFoundingImportance = 20;
        internal int LegendTrialImportance = 30;
        public string LegendRewardItem = ItemIds.LegendAxe;
        public int HealthPerLevel = 2;
        public int StaminaPerLevel = 1;
        public int ExpansionRewardGold;
        public int ExpansionRewardAP;
        public int ExpansionRewardExperience;
        public int APPerLevel = 2;
        public int HarvestExperience = 8;
        public int HarvestEnergy = 5;
        public string JobId = "potato_delivery";
        public string JobTitle = "감자 납품";
        internal int JobQuestImportance = 60;
        public string JobDescription = "";
        public string JobItemId = ItemIds.Potato;
        public string JobDocumentItemId;
        public int JobStart = 480;
        public int JobEnd = 1080;
        public JobTier[] JobTiers = { new JobTier(0, 6, 45, 35), new JobTier(3, 10, 80, 60), new JobTier(8, 16, 140, 100) };
        public int WorldJobQuantity = 4;
        public int WorldJobGold = 30;
        public int WorldJobExperience = 20;
        public string IntroTitle = "마을의 새 일꾼";
        internal int IntroQuestImportance = 110;
        public string IntroDescription = "농부의 아르바이트를 마치고 보고한다.";
        public int IntroRequiredJobs = 1;
        public int IntroGold = 30;
        public int IntroAP = 2;
        public int IntroExperience = 15;
        public string ExpansionTitle = "농장에 남긴 발자국";
        internal int ExpansionQuestImportance = 100;
        public string ExpansionDescription = "마을의 농장 확장에 필요한 자원을 보탠다.";
        public int DonationGold = 100;
        public int DonationItems = 20;
        public string DonationItemId = ItemIds.Potato;
        public int DonationGoldStep = 25;
        public int DonationItemStep = 5;
        public int ExpandedStage = 2;
        public FarmFoundationRules Foundation;
    }
}
