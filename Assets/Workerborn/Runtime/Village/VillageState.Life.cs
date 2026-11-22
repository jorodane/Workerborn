using System;

namespace Workerborn
{
    public enum LifeAttribute { Strength, Dexterity, Knowledge }

    internal static class WorldCapacity
    {
        public const int MaxCharacters = 256;
        public const int MaxConnections = 12;
        public const int DefaultNewWorld = 4;

        public static bool IsValid(int limit) => limit >= 1 && limit <= MaxCharacters;

        public static int ScaleRequirement(int amount, int characters) =>
            (int)Math.Min(int.MaxValue, (long)Math.Max(0, amount) * characters);
    }

    [Serializable]
    public sealed class VillageWorldRecord
    {
        public int era = 1;
        public int day = 1;
        public float minute;
        public int farmLevel = 1;
        public int foundationStage;
        public int timberDelivered;
        public string farmSiteId;
        public int donatedGold;
        public int donatedItems;
        public bool expansionReady;
        public int characterLimit;
        public int requirementCharacters = 1;
        public WoodcuttingLegend legend = new WoodcuttingLegend();

        public double Clock => (day - 1) * 1440d + minute;

        internal void AddMinutes(double amount)
        {
            double clock = Clock + amount;
            day = (int)(clock / 1440d) + 1;
            minute = (float)(clock - (day - 1) * 1440d);
            if (minute >= 1440f)
            {
                day++;
                minute = 0;
            }
        }
    }

    public sealed partial class VillageState
    {
        public const int CurrentSaveSchema = 6;
        public const int LevelLimit = 200;
        private double lifeStartedAt;

        public int Era { get; private set; } = 1;
        public int CharacterLimit { get; private set; }
        public int RequirementCharacters { get; private set; } = 1;
        public double WorldMinute => (Day - 1) * 1440d + Minute;
        public double LifeMinutes => Math.Max(0, WorldMinute - lifeStartedAt);
        public double RebirthMinutesLeft => Math.Max(0, Rules.RebirthDays * 1440d - LifeMinutes);
        public bool CanRebirth => RebirthMinutesLeft <= 0;
        public bool CanAdvanceEra => FoundationStage == FarmFoundationStage.ReadyToRebirth || FarmUpgradePending;
        public int Health { get; private set; }
        public int HealthLimit => 100 + (Level - 1) * Rules.HealthPerLevel;
        public int AttributePoints { get; private set; }
        public int Strength { get; private set; }
        public int Dexterity { get; private set; }
        public int Knowledge { get; private set; }

        public bool TrySpendAttribute(LifeAttribute attribute)
        {
            if (AttributePoints < 1)
                return Result(false, "배분할 능력치 포인트가 없어요.");
            switch (attribute)
            {
                case LifeAttribute.Strength: Strength++; break;
                case LifeAttribute.Dexterity: Dexterity++; break;
                case LifeAttribute.Knowledge: Knowledge++; break;
                default: return false;
            }
            AttributePoints--;
            return Result(true, "능력치 포인트를 배분했어요.");
        }

        public bool TryRebirth()
        {
            if (!CanRebirth)
                return Result(false, $"일반 환생까지 인게임 {Math.Ceiling(RebirthMinutesLeft / 60d):N0}시간 남았어요.");
            RebirthPersonalLife();
            return true;
        }

        public bool TryAdvanceEra(bool reincarnate = true)
        {
            if (!CanAdvanceEra)
                return Result(false, "시대 퀘스트를 먼저 완료해 주세요.");
            if (FoundationStage == FarmFoundationStage.ReadyToRebirth)
                FoundationStage = FarmFoundationStage.Built;
            else
            {
                FarmLevel = Rules.ExpandedStage;
                FarmUpgradePending = false;
            }
            Era++;
            RequirementCharacters = Math.Max(1, CharacterLimit);
            if (reincarnate)
                RebirthPersonalLife();
            return Result(true, $"{Era}시대가 시작됐어요.");
        }

        internal void RebirthPersonalLife()
        {
            Life++;
            lifeStartedAt = WorldMinute;
            LifeDay = 1;
            Level = 1;
            Experience = 0;
            AttributePoints = Strength = Dexterity = Knowledge = 0;
            RefreshSkillStats();
            Health = HealthLimit;
            Energy = StaminaLimit;
            Inventory = new GridInventory();
            equipment.Clear();
            Job = JobStatus.None;
            LastJobDay = -1;
            JobRequirement = JobReward = JobExperience = 0;
            JobRewardItemId = null;
            JobRewardItemCount = 0;
            LastRestWasCollapse = false;
            Result(true, $"{Life}번째 생이 시작됐어요.");
        }

        public VillageWorldRecord ExportWorld() => new VillageWorldRecord
        {
            era = Era,
            day = Day,
            minute = Minute,
            farmLevel = FarmLevel,
            foundationStage = (int)FoundationStage,
            timberDelivered = TimberDelivered,
            farmSiteId = FarmSiteId,
            donatedGold = DonatedGold,
            donatedItems = DonatedPotatoes,
            expansionReady = FarmUpgradePending,
            characterLimit = CharacterLimit,
            requirementCharacters = RequirementCharacters,
            legend = Legend
        };

        internal void ApplyWorld(VillageWorldRecord world)
        {
            int previousDay = Day;
            int requirementCharacters = Math.Max(1, world.requirementCharacters);
            bool changed = Era != world.era || Day != world.day || FarmLevel != world.farmLevel ||
                (int)FoundationStage != world.foundationStage || FarmSiteId != world.farmSiteId ||
                TimberDelivered != world.timberDelivered || DonatedGold != world.donatedGold ||
                DonatedPotatoes != world.donatedItems || FarmUpgradePending != world.expansionReady ||
                CharacterLimit != world.characterLimit || RequirementCharacters != requirementCharacters ||
                Legend.founders.Length != world.legend.founders.Length || Legend.recognized.Length != world.legend.recognized.Length;
            Era = world.era;
            Day = world.day;
            Minute = world.minute;
            LifeDay = Math.Max(1, Day - (int)(lifeStartedAt / 1440d));
            FarmLevel = world.farmLevel;
            FoundationStage = (FarmFoundationStage)world.foundationStage;
            TimberDelivered = world.timberDelivered;
            FarmSiteId = world.farmSiteId;
            DonatedGold = world.donatedGold;
            DonatedPotatoes = world.donatedItems;
            FarmUpgradePending = world.expansionReady;
            CharacterLimit = world.characterLimit;
            RequirementCharacters = requirementCharacters;
            Legend = world.legend;
            if (FoundationStage >= FarmFoundationStage.SelectingSite && changed)
                RemoveDocument(Inventory, Rules.Foundation?.DocumentItemId);
            if (Job == JobStatus.Active && (Day != previousDay || Minute >= Rules.JobEnd))
            {
                ExpireJob();
                changed = true;
            }
            if (changed)
                Version++;
        }

        internal void BeginInWorld(VillageWorldRecord world)
        {
            lifeStartedAt = world.Clock;
            ApplyWorld(world);
        }

        internal bool RecoverInSharedWorld()
        {
            if (Health > 0 && Energy > 0)
                return false;
            Health = HealthLimit;
            Energy = Math.Min(StaminaLimit, Rules.CollapseEnergy);
            return Result(true, "숙소에서 깨어났습니다.");
        }
    }
}
