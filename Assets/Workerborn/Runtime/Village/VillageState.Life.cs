using System;

namespace Workerborn
{
    public enum LifeAttribute { Strength, Dexterity, Knowledge }

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
        public PlotPlacement[] plotPlacements = Array.Empty<PlotPlacement>();

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
        private const int LoadoutSaveSchema = 17;
        public const int CurrentSaveSchema = LoadoutSaveSchema;
        public const int LevelLimit = 200;
        private double lifeStartedAt;
        public bool AwaitingIncarnation { get; private set; }
        public CharacterLook? LifeLook { get; private set; }

        public int Era { get; private set; } = 1;
        public int CharacterLimit { get; private set; }
        public int RequirementCharacters { get; private set; } = 1;
        public double WorldMinute => (Day - 1) * 1440d + Minute;
        public double LifeMinutes => AwaitingIncarnation ? 0 : Math.Max(0, WorldMinute - lifeStartedAt);
        public double RebirthMinutesLeft => Math.Max(0, Rules.RebirthDays * 1440d - LifeMinutes);
        public bool CanRebirth => !AwaitingIncarnation && RebirthMinutesLeft <= 0;
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
            if (AwaitingIncarnation)
                return Result(false, "운명의 인도자에게서 환생을 마쳐 주세요.");
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
            if (AwaitingIncarnation)
                return;
            Life++;
            AwaitingIncarnation = true;
            lifeStartedAt = WorldMinute;
            LifeDay = 1;
            Level = 1;
            Experience = 0;
            AttributePoints = Strength = Dexterity = Knowledge = 0;
            RefreshSkillStats();
            Health = HealthLimit;
            RestoreStamina(StaminaLimit);
            Inventory = new GridInventory(InventoryColumns, BaseInventoryRows);
            CursorItem = null;
            cursorItemWasSplit = false;
            pendingHarvests.Clear();
            nextHarvestReadyAt = long.MaxValue;
            equipment.Clear();
            equipmentConditions.Clear();
            otherHandEquipment.Clear();
            HandSet = 0;
            Job = JobStatus.None;
            LastJobDay = -1;
            JobRequirement = JobReward = JobExperience = 0;
            JobRewardItemId = null;
            JobRewardItemCount = 0;
            LastRestWasCollapse = false;
            Result(true, "운명의 인도자와 다음 생을 준비하세요.");
        }

        internal static VillageState BeforeFirstLife(VillageRules rules) => new VillageState(rules) { AwaitingIncarnation = true };

        internal static VillageState RestorePlayer(VillageSave save, VillageRules rules, out bool learnedSoulGrowth)
        {
            var state = Restore(save, rules);
            learnedSoulGrowth = !state.AwaitingIncarnation && state.LearnSkill(SkillId.SoulGrowth);
            return state;
        }

        internal bool CanIncarnate(int selection) => CanIncarnate(selection, out _);

        private bool CanIncarnate(int selection, out CharacterLook look)
        {
            look = default;
            return AwaitingIncarnation && !HasVillageProgress && CharacterLook.TryFromSelection(selection, out look);
        }

        internal bool TryIncarnate(int selection)
        {
            if (!CanIncarnate(selection, out var look))
                return Result(false, "운명의 인도자에게서 다시 환생을 준비해 주세요.");
            LifeLook = look;
            AwaitingIncarnation = false;
            lifeStartedAt = WorldMinute;
            LifeDay = 1;
            Health = HealthLimit;
            RestoreStamina(StaminaLimit);
            LastGuideLife = Life;
            bool learnedSoulGrowth = LearnSkill(SkillId.SoulGrowth);
            return Result(true, $"{Life}번째 생이 시작됐어요." + (learnedSoulGrowth ? " 영혼의 성장을 배웠어요." : ""));
        }

        private bool HasVillageProgress => Level != 1 || Experience != 0 || LifeDay != 1 || AttributePoints != 0 ||
            Strength != 0 || Dexterity != 0 || Knowledge != 0 || Job != JobStatus.None ||
            Inventory.Entries.Count != 0 || equipment.Count != 0 || otherHandEquipment.Count != 0 || CursorItem != null || pendingHarvests.Count != 0;

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
            legend = Legend,
            plotPlacements = plotPlacements
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
            LifeDay = AwaitingIncarnation ? 1 : Math.Max(1, Day - (int)(lifeStartedAt / 1440d));
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
            if (ApplyPlotPlacements(world.plotPlacements) || changed)
                Version++;
        }

        internal void BeginInWorld(VillageWorldRecord world)
        {
            lifeStartedAt = world.Clock;
            ApplyWorld(world);
        }

        internal bool NeedsRest => !AwaitingIncarnation && Health <= 0;

        internal bool RecoverInSharedWorld()
        {
            if (!NeedsRest)
                return false;
            Health = HealthLimit;
            RestoreStamina(Rules.CollapseEnergy);
            return Result(true, "숙소에서 깨어났습니다.");
        }
    }
}
