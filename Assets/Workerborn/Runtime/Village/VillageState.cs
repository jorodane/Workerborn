using System;
using System.Collections.Generic;

namespace Workerborn
{
    public enum JobStatus
    {
        None,
        Active,
        Completed,
        Failed
    }

    public enum WorkOutcome { Failure, Success, Lucky, GreatLucky }

    [Serializable]
    public sealed class CropStateSave
    {
        public string id;
        public int remaining;
    }

    [Serializable]
    public sealed class GroundDropSave
    {
        public string id;
        public int quantity = 1;
        public string itemId;
        public float x;
        public float z;
    }

    [Serializable]
    public sealed class VillageSave
    {
        public int schema = 1;
        public int day = 1;
        public int lifeDay = 1;
        public int life = 1;
        public float minute = 480f;
        public int energy = 100;
        public int gold;
        public int level = 1;
        public int experience;
        public int totalLevel = 1;
        public int ap;
        public int skillRank;
        public int career;
        public int lifetimeHarvests;
        public bool introAccepted;
        public bool introClaimed;
        public int donatedGold;
        public int donatedPotatoes;
        public bool farmUpgradePending;
        public int farmLevel = 1;
        public int lastJobDay = -1;
        public int jobStatus;
        public int jobRequirement;
        public int jobReward;
        public int jobExperience;
        public string jobRewardItemId;
        public int jobRewardItemCount;
        public int foundationStage;
        public string farmSiteId;
        public int lastGuideLife;
        public bool bagExpanded;
        public int inventoryColumns = 8;
        public int inventoryRows = 4;
        public InventoryEntrySave[] inventory = Array.Empty<InventoryEntrySave>();
        public int[] cropRemaining = Array.Empty<int>();
        public CropStateSave[] cropStates = Array.Empty<CropStateSave>();
        public string[] collectedPickupIds = Array.Empty<string>();
        public GroundDropSave[] drops = Array.Empty<GroundDropSave>();
        public float playerX;
        public float playerZ = -4.4f;
        public EquipmentEntrySave[] equipment = Array.Empty<EquipmentEntrySave>();
    }

    // Unity-independent rules, shared by the playable village and its NUnit tests.
    public sealed partial class VillageState
    {
        public const int MaxEnergy = 100;
        public const int JobStart = 8 * 60;
        public const int JobEnd = 18 * 60;
        public const int RequiredDonationGold = 100;
        public const int RequiredDonationPotatoes = 20;
        public const float MinutesPerSecond = 3f;
        public int Day { get; private set; } = 1;
        public int LifeDay { get; private set; } = 1;
        public int Life { get; private set; } = 1;
        public float Minute { get; private set; } = 480f;
        public int Energy { get; private set; } = MaxEnergy;
        public int Gold => Inventory.Count(ItemIds.Gold);
        public int Level { get; private set; } = 1;
        public int Experience { get; private set; }
        public int TotalLevel { get; private set; } = 1;
        public int AP { get; private set; }
        public int SkillRank { get; private set; }
        public int Career { get; private set; }
        public int LifetimeHarvests { get; private set; }
        public bool IntroAccepted { get; private set; }
        public bool IntroClaimed { get; private set; }
        public int DonatedGold { get; private set; }
        public int DonatedPotatoes { get; private set; }
        public bool FarmUpgradePending { get; private set; }
        public int FarmLevel { get; private set; } = 1;
        public int LastJobDay { get; private set; } = -1;
        public JobStatus Job { get; private set; }
        public int JobRequirement { get; private set; }
        public int JobReward { get; private set; }
        public int JobExperience { get; private set; }
        public string JobRewardItemId { get; private set; }
        public int JobRewardItemCount { get; private set; }
        public bool BagExpanded { get; private set; }
        public bool LastRestWasCollapse { get; private set; }
        public GridInventory Inventory { get; private set; } = new GridInventory();

        private readonly Dictionary<EquipmentSlot, string> equipment = new Dictionary<EquipmentSlot, string>();
        public IReadOnlyDictionary<EquipmentSlot, string> Equipment => equipment;
        public VillageRules Rules { get; }
        public int Version { get; private set; }
        public int NoticeSerial { get; private set; }
        public string LastMessage { get; private set; } = "";
        public int NextLevelExperience => 20 + Level * 10;
        public int NextSkillCost => SkillRank + 1;
        public bool HasHoe => equipment.TryGetValue(EquipmentSlot.MainHand, out string item) && item == ItemIds.Hoe;
        public int HarvestEnergyCost => WorkEnergyCost(ItemIds.Potato, Rules.HarvestEnergy);
        public int YieldMultiplier => 1 + SkillRank * (SkillRank + 1) / 2;
        public bool HasTool(string itemId) => string.IsNullOrEmpty(itemId) ||
            equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) && equipped == itemId;
        public int WorkEnergyCost(string itemId, int baseCost) =>
            Math.Max(1, baseCost - SkillRank / 3 - (itemId == ItemIds.Potato && HasHoe ? 1 : 0));

        public int CareerTier
        {
            get
            {
                int tier = 0;
                for (int i = 0; i < Rules.JobTiers.Length; i++)
                    if (Career >= Rules.JobTiers[i].RequiredCompletions)
                        tier = i;
                return tier + 1;
            }
        }

        public JobTier OfferedJobTier => Rules.JobTiers[CareerTier - 1];
        public int OfferedJobRequirement => OfferedJobTier.Quantity + (FarmLevel - 1) * Rules.WorldJobQuantity;
        public int OfferedJobReward => OfferedJobTier.Gold + (FarmLevel - 1) * Rules.WorldJobGold;
        public int OfferedJobExperience => OfferedJobTier.Experience + (FarmLevel - 1) * Rules.WorldJobExperience;
        public bool CanRebirth => FarmAvailable ? IntroClaimed && Level >= Rules.RebirthLevel : FoundationStage == FarmFoundationStage.ReadyToRebirth;
        public bool JobWindowOpen => Minute >= Rules.JobStart && Minute < Rules.JobEnd;

        public VillageState(VillageRules rules = null)
        {
            Rules = rules ?? new VillageRules();
            Minute = Rules.JobStart;
            if (Rules.Foundation == null)
                FoundationStage = FarmFoundationStage.Built;
        }

        public bool TryEquip(int entryId)
        {
            var item = Inventory.Find(entryId);
            if (item == null || item.Spec.EquipmentSlot == EquipmentSlot.None)
                return Result(false, "장착할 수 없는 아이템입니다.");
            var copy = Inventory.Copy();
            if (!copy.TryRemoveAt(entryId, 1))
                return false;
            var slot = item.Spec.EquipmentSlot;
            if (equipment.TryGetValue(slot, out string previous) && !copy.TryAdd(previous, 1))
                return Result(false, "기존 장비를 넣을 공간이 없습니다.");
            Inventory.ReplaceWith(copy);
            equipment[slot] = item.ItemId;
            return Result(true, item.Spec.Name + " 장착");
        }

        public bool TryUnequip(EquipmentSlot slot)
        {
            if (!equipment.TryGetValue(slot, out string item))
                return false;
            if (!Inventory.TryAdd(item, 1))
                return Result(false, "장비를 넣을 공간이 없습니다.");
            equipment.Remove(slot);
            return Result(true, ItemCatalog.Get(item).Name + " 해제");
        }

        private bool Result(bool success, string message)
        {
            LastMessage = message;
            NoticeSerial++;
            Version++;
            return success;
        }

        public bool AdvanceTime(float seconds, bool paused = false)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (paused)
                return false;
            Minute += seconds * Rules.MinutesPerSecond;
            if (Job == JobStatus.Active && Minute >= Rules.JobEnd)
            {
                Job = JobStatus.Failed;
                Result(false, "아르바이트 시간이 종료되었습니다.");
            }

            if (Minute < Rules.CollapseMinute && Energy > 0)
                return false;
            NextDay(Rules.CollapseEnergy, true);
            Result(true, "의식을 잃고 숙소에서 깨어났습니다.");
            return true;
        }

        public void MeetFarmer()
        {
            if (!FarmAvailable || IntroAccepted)
                return;
            IntroAccepted = true;
            Result(true, "퀘스트 수락: " + Rules.IntroTitle);
        }

        public bool TryAcceptJob()
        {
            if (!FarmAvailable)
                return Result(false, "농장이 완공되면 아르바이트를 받을 수 있어요.");
            if (!JobWindowOpen)
                return Result(false, $"접수·납품 시간은 {Rules.JobStart / 60:00}:{Rules.JobStart % 60:00}~{Rules.JobEnd / 60:00}:{Rules.JobEnd % 60:00}입니다.");
            if (LastJobDay == Day)
                return Result(false, "오늘의 아르바이트는 이미 받았어요. 내일 다시 찾아와 주세요.");
            string document = Rules.JobDocumentItemId;
            if (!string.IsNullOrEmpty(document) && Inventory.Count(document) == 0 && !Inventory.TryAdd(document, 1))
                return Result(false, "계약서를 넣을 소지품 공간이 부족합니다.");
            MeetFarmer();
            LastJobDay = Day;
            Job = JobStatus.Active;
            JobRequirement = OfferedJobRequirement;
            JobReward = OfferedJobReward;
            JobExperience = OfferedJobExperience;
            JobRewardItemId = OfferedJobTier.RewardItemId;
            JobRewardItemCount = OfferedJobTier.RewardItemCount;
            return Result(true, $"아르바이트 수락: {Rules.JobTitle} ({JobRequirement}개)");
        }

        public bool TryDeliverJob()
        {
            if (Job != JobStatus.Active || LastJobDay != Day)
                return Result(false, "진행 중인 아르바이트가 없어요.");
            if (!JobWindowOpen)
            {
                if (Minute >= Rules.JobEnd)
                    Job = JobStatus.Failed;
                return Result(false, "아르바이트 납품 시간이 아니에요.");
            }

            var updated = Inventory.Copy();
            if (!updated.TryRemove(Rules.JobItemId, JobRequirement)
                || JobReward > 0 && !updated.TryAdd(ItemIds.Gold, JobReward)
                || JobRewardItemCount > 0 && !updated.TryAdd(JobRewardItemId, JobRewardItemCount))
                return Result(false, "납품 물품이나 보상을 받을 공간이 부족합니다.");
            Inventory.ReplaceWith(updated);
            Job = JobStatus.Completed;
            Career++;
            AddExperience(JobExperience);
            return Result(true, $"아르바이트 완료! {JobReward}G · 경험치 {JobExperience} · 농장 경력 +1");
        }

        public bool TryClaimIntroQuest()
        {
            if (!IntroAccepted || Career < Rules.IntroRequiredJobs)
                return Result(false, "아직 퀘스트 목표를 완료하지 못했습니다.");
            if (IntroClaimed)
                return Result(false, "이 퀘스트의 보상은 이미 받았어요.");
            if (Rules.IntroGold > 0 && !Inventory.TryAdd(ItemIds.Gold, Rules.IntroGold))
                return Result(false, "보상을 받을 소지품 공간이 부족합니다.");
            IntroClaimed = true;
            AP += Rules.IntroAP;
            AddExperience(Rules.IntroExperience);
            return Result(true, "퀘스트 완료: " + Rules.IntroTitle);
        }

        public bool TryHarvest(int baseYield, bool lucky, out int drops)
            => TryGather(ItemIds.Potato, baseYield, Rules.HarvestEnergy, lucky ? WorkOutcome.Lucky : WorkOutcome.Success, out drops);

        public bool TryGather(string itemId, int baseYield, int baseEnergy, WorkOutcome outcome, out int drops)
        {
            drops = 0;
            if (baseYield < 1 || baseYield > 100 || !ItemCatalog.Contains(itemId))
                return Result(false, "채집 결과 설정을 확인해 주세요.");
            int cost = WorkEnergyCost(itemId, baseEnergy);
            if (Energy < cost)
                return Result(false, "스태미나가 부족해요. 빵을 먹거나 숙소에서 쉬어 주세요.");
            Energy -= cost;
            if (outcome == WorkOutcome.Failure)
                return Result(false, "채집에 실패했어요.");
            LifetimeHarvests++;
            int bonus = outcome == WorkOutcome.GreatLucky ? 50 : outcome == WorkOutcome.Lucky ? 2 : 1;
            drops = baseYield * YieldMultiplier * bonus;
            AddExperience(Rules.HarvestExperience);
            string result = outcome == WorkOutcome.GreatLucky ? "대박 보너스!" : outcome == WorkOutcome.Lucky ? "럭키 보너스!" : "채집 완료!";
            return Result(true, $"{result} {ItemCatalog.Get(itemId).Name} {drops:N0}개");
        }

        private void AddExperience(int amount)
        {
            if (Level >= 200)
                return;
            Experience += amount;
            while (Level < 200 && Experience >= NextLevelExperience)
            {
                Experience -= NextLevelExperience;
                Level++;
                TotalLevel++;
                AP += Rules.APPerLevel;
            }

            if (Level == 200)
                Experience = 0;
        }

        public bool TryUpgradeSkill()
        {
            if (SkillRank >= 8)
                return Result(false, "최고 채집 랭크에 도달했어요.");
            if (AP < NextSkillCost)
                return Result(false, $"승급하려면 AP {NextSkillCost}가 필요해요.");
            AP -= NextSkillCost;
            SkillRank++;
            return Result(true, $"채집 스킬 승급! 한 번에 얻는 양이 기본의 {YieldMultiplier}배가 됐어요.");
        }

        public bool TryBuy(string itemId) => TryBuy(itemId, 1);
        public bool TryBuy(string itemId, int quantity)
        {
            if (quantity < 1 || quantity > 999 || quantity > 1 && (itemId == ItemIds.Hoe || itemId == ItemIds.Axe || itemId == ItemIds.Bag))
                return Result(false, "구입할 수 없는 수량입니다.");
            if (!ItemCatalog.Contains(itemId) || itemId == ItemIds.Gold || TryGetDocumentPage(itemId, out _))
                return Result(false, "판매하지 않는 물건입니다.");
            var spec = ItemCatalog.Get(itemId);
            if (itemId == ItemIds.Hoe && (HasHoe || Inventory.Count(ItemIds.Hoe) > 0))
                return Result(false, "이미 같은 도구를 가지고 있습니다.");
            if (itemId == ItemIds.Bag && BagExpanded)
                return Result(false, "이미 여행 가방을 사용 중이에요.");
            if ((long)Gold < (long)spec.Price * quantity)
                return Result(false, "골드가 부족해요.");
            if (!Inventory.TryExchange(ItemIds.Gold, checked(spec.Price * quantity), itemId, quantity))
                return Result(false, "골드나 소지품 공간이 부족합니다.");
            if (itemId == ItemIds.Bag)
            {
                BagExpanded = true;
                Inventory.ExpandRows(Math.Max(Inventory.Rows, 6));
            }

            return Result(true, $"{spec.Name} {quantity}개 구입! {spec.Price * quantity}G를 지불했어요.");
        }

        public bool TrySellItems(string itemId, int quantity)
        {
            if (quantity < 1 || quantity > 999 || !ItemCatalog.Contains(itemId) || itemId == ItemIds.Gold || itemId == ItemIds.Bag || TryGetDocumentPage(itemId, out _))
                return Result(false, "판매할 수 없는 물건입니다.");
            long total = (long)ItemCatalog.Get(itemId).Price * quantity;
            if (total > int.MaxValue || !Inventory.TryExchange(itemId, quantity, ItemIds.Gold, (int)total))
                return Result(false, "판매 수량이나 소지품 공간이 부족합니다.");
            return Result(true, $"{ItemCatalog.Get(itemId).Name} {quantity}개 판매 · {total} Gold");
        }

        public bool TryEatBread()
        {
            if (Energy == MaxEnergy)
                return Result(false, "에너지가 이미 가득해요.");
            if (!Inventory.TryRemove(ItemIds.Bread, 1))
                return Result(false, "가방에 빵이 없어요.");
            Energy = Math.Min(MaxEnergy, Energy + 25);
            return Result(true, "빵을 먹고 에너지를 25 회복했어요.");
        }

        public bool TrySellPotatoes(int amount)
        {
            int reward = amount >= 1 && amount <= 10000 ? amount * ItemCatalog.Get(ItemIds.Potato).Price : 0;
            if (reward <= 0 || !Inventory.TryExchange(ItemIds.Potato, amount, ItemIds.Gold, reward))
                return Result(false, "물품이나 소지품 공간이 부족합니다.");
            return Result(true, $"감자 {amount}개 판매 · {reward}G");
        }

        public bool TryDonateGold()
        {
            if (!IntroClaimed || FarmLevel > 1 || FarmUpgradePending)
                return Result(false, "지금은 농장 확장을 위한 기부를 받지 않아요.");
            int amount = Math.Min(Rules.DonationGoldStep, Rules.DonationGold - DonatedGold);
            if (amount <= 0)
                return Result(false, "필요한 자금은 모두 모였어요.");
            if (Gold < amount)
                return Result(false, $"기부하려면 {amount}G가 필요해요.");
            Inventory.TryRemove(ItemIds.Gold, amount);
            DonatedGold += amount;
            CheckDonation();
            return Result(true, FarmUpgradePending ? "농장 확장 준비 완료! 다음 환생 때 넓어진 밭을 만날 수 있어요." : $"마을에 {amount}G를 기부했어요. ({DonatedGold}/{Rules.DonationGold}G)");
        }

        public bool TryDonatePotatoes()
        {
            if (!IntroClaimed || FarmLevel > 1 || FarmUpgradePending)
                return Result(false, "지금은 농장 확장을 위한 기부를 받지 않아요.");
            int amount = Math.Min(Rules.DonationItemStep, Rules.DonationItems - DonatedPotatoes);
            if (amount <= 0)
                return Result(false, "필요한 물품은 모두 모였습니다.");
            if (!Inventory.TryRemove(Rules.DonationItemId, amount))
                return Result(false, "기부 물품이 부족합니다.");
            DonatedPotatoes += amount;
            CheckDonation();
            return Result(true, FarmUpgradePending ? "농장 확장 준비 완료! 다음 환생 때 넓어진 밭을 만날 수 있어요." : $"마을에 {ItemCatalog.Get(Rules.DonationItemId).Name} {amount}개를 기부했어요. ({DonatedPotatoes}/{Rules.DonationItems}개)");
        }

        private void CheckDonation()
        {
            FarmUpgradePending = DonatedGold >= Rules.DonationGold && DonatedPotatoes >= Rules.DonationItems;
        }

        public bool TrySleep(bool paidRoom)
        {
            if (paidRoom && Gold < Rules.RoomPrice)
                return Result(false, "숙박비가 부족합니다.");
            if (paidRoom && Rules.RoomPrice > 0)
                Inventory.TryRemove(ItemIds.Gold, Rules.RoomPrice);
            NextDay(paidRoom ? MaxEnergy : Rules.FreeRestEnergy, false);
            return Result(true, $"다음 날 {Rules.WakeMinute / 60:00}:{Rules.WakeMinute % 60:00}. 에너지 {Energy} 회복.");
        }

        private void NextDay(int energy, bool collapsed)
        {
            Day++;
            LifeDay++;
            Minute = Rules.WakeMinute;
            Energy = energy;
            if (Job == JobStatus.Active)
                Job = JobStatus.Failed;
            LastRestWasCollapse = collapsed;
            Version++;
        }

        public bool TryWaitForWork()
        {
            if (Minute >= Rules.JobStart)
                return Result(false, "이미 접수 시간이 지났습니다.");
            Minute = Rules.JobStart;
            return Result(true, $"{Rules.JobStart / 60:00}:{Rules.JobStart % 60:00}까지 기다렸습니다.");
        }

        public bool TryRebirth()
        {
            if (!CanRebirth)
                return Result(false, FarmAvailable ? $"'{Rules.IntroTitle}' 보상을 받고 레벨 {Rules.RebirthLevel}에 도달하면 환생할 수 있어요." : "목재를 납품하고 농장 부지를 지정해 주세요.");
            if (!FarmAvailable)
                FoundationStage = FarmFoundationStage.Built;
            if (FarmUpgradePending)
            {
                FarmLevel = Rules.ExpandedStage;
                FarmUpgradePending = false;
            }

            Life++;
            Day++;
            LifeDay = 1;
            Minute = Rules.JobStart;
            Level = 1;
            Experience = 0;
            Energy = MaxEnergy;
            Inventory = new GridInventory();
            BagExpanded = false;
            equipment.Clear();
            Job = JobStatus.None;
            LastJobDay = -1;
            JobRequirement = JobReward = JobExperience = 0;
            JobRewardItemId = null;
            JobRewardItemCount = 0;
            LastRestWasCollapse = false;
            return Result(true, $"{Life}번째 생이 시작됐어요. 스킬·AP·경력과 마을의 변화는 이어집니다.");
        }

        public VillageSave Export()
        {
            var worn = new List<EquipmentEntrySave>();
            foreach (var entry in equipment)
                worn.Add(new EquipmentEntrySave { slot = entry.Key, itemId = entry.Value });
            return new VillageSave
            {
                schema = 3,
                equipment = worn.ToArray(),
                day = Day,
                lifeDay = LifeDay,
                life = Life,
                minute = Minute,
                energy = Energy,
                gold = 0,
                level = Level,
                experience = Experience,
                totalLevel = TotalLevel,
                ap = AP,
                skillRank = SkillRank,
                career = Career,
                lifetimeHarvests = LifetimeHarvests,
                introAccepted = IntroAccepted,
                introClaimed = IntroClaimed,
                donatedGold = DonatedGold,
                donatedPotatoes = DonatedPotatoes,
                farmUpgradePending = FarmUpgradePending,
                farmLevel = FarmLevel,
                lastJobDay = LastJobDay,
                jobStatus = (int)Job,
                jobRequirement = JobRequirement,
                jobReward = JobReward,
                jobExperience = JobExperience,
                jobRewardItemId = JobRewardItemId,
                jobRewardItemCount = JobRewardItemCount,
                foundationStage = (int)FoundationStage,
                farmSiteId = FarmSiteId,
                lastGuideLife = LastGuideLife,
                bagExpanded = BagExpanded,
                inventoryColumns = Inventory.Columns,
                inventoryRows = Inventory.Rows,
                inventory = Inventory.Export()
            };
        }

        public static VillageState Restore(VillageSave save, VillageRules rules = null)
        {
            rules = rules ?? new VillageRules();
            if (save == null || (save.schema < 1 || save.schema > 3) || save.day < 1 || save.lifeDay < 1 || save.life < 1 || save.day > 1000000 || save.life > 1000000 || save.lifeDay > save.day || float.IsNaN(save.minute) || float.IsInfinity(save.minute) || save.minute < 0 || save.minute >= 1440 || save.energy < 0 || save.energy > MaxEnergy || save.gold < 0 || save.gold > 100000000 || save.level < 1 || save.level > 200 || save.experience < 0 || save.experience >= 20 + save.level * 10 || save.totalLevel < save.level || save.totalLevel > 100000000 || save.ap < 0 || save.ap > 1000000 || save.skillRank < 0 || save.skillRank > 8 || save.career < 0 || save.career > 1000000 || save.lifetimeHarvests < 0 || save.donatedGold < 0 || save.donatedGold > Math.Max(RequiredDonationGold, rules.DonationGold) || save.donatedPotatoes < 0 || save.donatedPotatoes > Math.Max(RequiredDonationPotatoes, rules.DonationItems) || save.farmLevel < 1 || save.farmLevel > Math.Max(2, rules.ExpandedStage) || save.jobStatus < 0 || save.jobStatus > 3 || save.lastJobDay > save.day || save.lastJobDay < -1 || save.inventoryColumns != 8 || save.inventoryRows < 4 || save.inventoryRows > 16)
                throw new ArgumentException("Invalid village save.");
            if ((JobStatus)save.jobStatus == JobStatus.Active && (save.lastJobDay != save.day || save.jobRequirement < 1 || save.jobRequirement > 10000 || save.jobReward < 0 || save.jobReward > 10000 || save.jobExperience < 0 || save.jobExperience > 100000))
                throw new ArgumentException("Invalid active job.");
            var foundationStage = RestoreFoundationStage(save, rules);
            if (save.jobRewardItemCount < 0 || save.jobRewardItemCount > 10000 || (save.jobRewardItemCount > 0 && !ItemCatalog.Contains(save.jobRewardItemId)))
                throw new ArgumentException("Invalid job item reward.");
            var inventory = GridInventory.Restore(save.inventoryColumns, save.inventoryRows, save.inventory);
            if (save.schema == 1 && save.gold > 0)
            {
                int remaining = save.gold;
                while (remaining > 0)
                {
                    int amount = Math.Min(remaining, 10000);
                    while (!inventory.CanAdd(ItemIds.Gold, amount) && inventory.Rows < 16)
                        inventory.ExpandRows(inventory.Rows + 1);
                    if (!inventory.TryAdd(ItemIds.Gold, amount))
                        throw new ArgumentException("Legacy gold exceeds inventory capacity.");
                    remaining -= amount;
                }
            }

            if (save.bagExpanded != (inventory.Count(ItemIds.Bag) == 1) || inventory.Count(ItemIds.Hoe) > 1)
                throw new ArgumentException("Invalid equipment.");
            var state = new VillageState(rules)
            {
                Day = save.day,
                LifeDay = save.lifeDay,
                Life = save.life,
                Minute = save.minute,
                Energy = save.energy,
                Level = save.level,
                Experience = save.experience,
                TotalLevel = save.totalLevel,
                AP = save.ap,
                SkillRank = save.skillRank,
                Career = save.career,
                LifetimeHarvests = save.lifetimeHarvests,
                IntroAccepted = save.introAccepted,
                IntroClaimed = save.introClaimed,
                DonatedGold = save.donatedGold,
                DonatedPotatoes = save.donatedPotatoes,
                FarmUpgradePending = save.farmUpgradePending,
                FarmLevel = save.farmLevel,
                LastJobDay = save.lastJobDay,
                Job = (JobStatus)save.jobStatus,
                JobRequirement = save.jobRequirement,
                JobReward = save.jobReward,
                JobExperience = save.jobExperience,
                JobRewardItemId = save.jobRewardItemId,
                JobRewardItemCount = save.jobRewardItemCount,
                FoundationStage = foundationStage,
                FarmSiteId = save.farmSiteId,
                LastGuideLife = save.schema < 3 ? save.life : save.lastGuideLife,
                BagExpanded = save.bagExpanded,
                Inventory = inventory
            };
            if (save.schema >= 2 && save.equipment != null)
                foreach (var entry in save.equipment)
                {
                    if (entry == null || !ItemCatalog.Contains(entry.itemId) || entry.slot == EquipmentSlot.None || ItemCatalog.Get(entry.itemId).EquipmentSlot != entry.slot || state.equipment.ContainsKey(entry.slot))
                        throw new ArgumentException("Invalid equipped item.");
                    state.equipment.Add(entry.slot, entry.itemId);
                }

            if (state.Job == JobStatus.Active && state.Minute >= rules.JobEnd)
                state.Job = JobStatus.Failed;
            if (save.schema < 3 && state.Job != JobStatus.None && !string.IsNullOrEmpty(rules.JobDocumentItemId)
                && state.Inventory.Count(rules.JobDocumentItemId) == 0)
                state.Inventory.TryAdd(rules.JobDocumentItemId, 1);
            return state;
        }
    }
}
