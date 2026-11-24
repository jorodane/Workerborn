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
    public sealed class AnimalStateSave
    {
        public string id;
        public int health;
        public bool lootTaken;
    }

    [Serializable]
    public struct InteriorPositionSave
    {
        public string id;
        public float localX;
        public float localZ;
    }

    [Serializable]
    public sealed class GroundDropSave
    {
        public string id;
        public int quantity = 1;
        public string itemId;
        public float x;
        public float z;
        public InteriorPositionSave interior;
    }

    [Serializable]
    public sealed class PendingPurchaseSave
    {
        public string itemId;
        public int quantity;
    }

    [Serializable]
    public sealed class VillageSave
    {
        public int schema = 1;
        public int day = 1;
        public int lifeDay = 1;
        public int life = 1;
        public int era = 1;
        public double lifeStartedAt;
        public int health = 100;
        public int attributePoints;
        public int strength;
        public int dexterity;
        public int knowledge;
        public int timberDelivered;
        public int characterLimit;
        public int requirementCharacters = 1;
        public int lifetimeTimber;
        public WoodcuttingLegend legend;
        public float minute = 480f;
        public int energy = 100;
        public int gold;
        public int level = 1;
        public long experience;
        public long totalLevel = 1;
        public int ap;
        public int skillRank;
        public SkillRankSave[] skillRanks = Array.Empty<SkillRankSave>();
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
        public PlotPlacement[] plotPlacements = Array.Empty<PlotPlacement>();
        public int lastGuideLife;
        public bool bagExpanded;
        public int inventoryColumns = VillageState.LegacyInventoryColumns;
        public int inventoryRows = VillageState.BaseInventoryRows;
        public InventoryEntrySave[] inventory = Array.Empty<InventoryEntrySave>();
        public PendingPurchaseSave pendingPurchase;
        public int[] cropRemaining = Array.Empty<int>();
        public CropStateSave[] cropStates = Array.Empty<CropStateSave>();
        public AnimalStateSave[] animalStates = Array.Empty<AnimalStateSave>();
        public string[] collectedPickupIds = Array.Empty<string>();
        public GroundDropSave[] drops = Array.Empty<GroundDropSave>();
        public float playerX;
        public float playerZ = -4.4f;
        public InteriorPositionSave interior;
        public bool awaitingIncarnation;
        public bool hasLifeLook;
        public CharacterLook lifeLook;
        public EquipmentEntrySave[] equipment = Array.Empty<EquipmentEntrySave>();
    }

    // Unity-independent rules, shared by the playable village and its NUnit tests.
    public sealed partial class VillageState
    {
        internal const int InventoryColumns = 6;
        internal const int LegacyInventoryColumns = 8;
        internal const int BaseInventoryRows = 4;
        private const int BagInventoryRows = 6;
        private const int LegacyMaxInventoryRows = 16;
        private const int SixColumnInventorySchema = 7;
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
        public long Experience { get; private set; }
        public long TotalLevel { get; private set; } = 1;
        public int AP { get; private set; }
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
        public bool BagExpanded => equipment.ContainsKey(EquipmentSlot.Bag);
        public bool LastRestWasCollapse { get; private set; }
        public GridInventory Inventory { get; private set; } = new GridInventory(InventoryColumns, BaseInventoryRows);
        internal InventoryEntry PendingPurchase { get; private set; }

        private readonly Dictionary<EquipmentSlot, string> equipment = new Dictionary<EquipmentSlot, string>();
        public IReadOnlyDictionary<EquipmentSlot, string> Equipment => equipment;
        public VillageRules Rules { get; }
        public int Version { get; private set; }
        public int NoticeSerial { get; private set; }
        public string LastMessage { get; private set; } = "";
        public int NextLevelExperience => 20 + Level * 10;
        public bool HasHoe => equipment.TryGetValue(EquipmentSlot.MainHand, out string item) && item == ItemIds.Hoe;
        public int HarvestEnergyCost => WorkEnergyCost(ItemIds.Potato, Rules.HarvestEnergy);
        public bool HasTool(string itemId) => string.IsNullOrEmpty(itemId) ||
            equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) &&
                (equipped == itemId || itemId == ItemIds.Axe && equipped == ItemIds.LegendAxe);

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
        public bool JobWindowOpen => Minute >= Rules.JobStart && Minute < Rules.JobEnd;

        public VillageState(VillageRules rules = null)
        {
            Rules = rules ?? new VillageRules();
            RefreshSkillStats();
            Energy = StaminaLimit;
            Health = HealthLimit;
            Minute = Rules.JobStart;
            lifeStartedAt = WorldMinute;
            if (Rules.Foundation == null)
                FoundationStage = FarmFoundationStage.Built;
        }

        public bool TryEquip(int entryId)
        {
            var item = CarriedItem(entryId);
            if (item == null || item.Spec.EquipmentSlot == EquipmentSlot.None)
                return Result(false, "장착할 수 없는 아이템입니다.");
            var copy = Inventory.Copy();
            if (entryId != 0 && !copy.TryRemoveAt(entryId, 1))
                return false;
            var slot = item.Spec.EquipmentSlot;
            if (equipment.TryGetValue(slot, out string previous) && !copy.TryAdd(previous, 1))
                return Result(false, "기존 장비를 넣을 공간이 없습니다.");
            if (slot == EquipmentSlot.Bag && copy.Rows < BagInventoryRows)
                copy.ExpandRows(BagInventoryRows);
            Inventory.ReplaceWith(copy);
            equipment[slot] = item.ItemId;
            if (entryId == 0)
                RemovePurchaseQuantity(1);
            return Result(true, item.Spec.Name + " 장착");
        }

        public bool TryUnequip(EquipmentSlot slot)
        {
            if (!equipment.TryGetValue(slot, out string item))
                return false;
            var copy = Inventory.Copy();
            if ((slot == EquipmentSlot.Bag && !copy.TryShrinkRows(BaseInventoryRows)) || !copy.TryAdd(item, 1))
                return Result(false, "장비를 해제할 소지품 공간이 부족합니다.");
            Inventory.ReplaceWith(copy);
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

        internal bool WillAdvanceDay(float seconds, bool paused)
        {
            if (paused)
                return false;
            float nextMinute = Minute + seconds * Rules.MinutesPerSecond;
            return AwaitingIncarnation ? nextMinute >= 1440f : nextMinute >= Rules.CollapseMinute || NeedsRest;
        }

        public bool AdvanceTime(float seconds, bool paused = false)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (paused)
                return false;
            Minute += seconds * Rules.MinutesPerSecond;
            if (AwaitingIncarnation)
            {
                int elapsedDays = (int)(Minute / 1440f);
                Day += elapsedDays;
                Minute %= 1440f;
                if (elapsedDays > 0)
                    Version++;
                return false;
            }
            if (Job == JobStatus.Active && Minute >= Rules.JobEnd)
            {
                ExpireJob();
                Result(false, "아르바이트 시간이 종료되었습니다.");
            }

            if (Minute < Rules.CollapseMinute && !NeedsRest)
                return false;
            NextDay(Rules.CollapseEnergy, true);
            Result(true, "의식을 잃고 숙소에서 깨어났습니다.");
            return true;
        }

        public int TakeHit(int damage)
        {
            int applied = Math.Min(Health, Math.Max(0, damage));
            if (applied > 0)
            {
                Health -= applied;
                Version++;
            }
            return applied;
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
                    ExpireJob();
                return Result(false, "아르바이트 납품 시간이 아니에요.");
            }

            var updated = Inventory.Copy();
            if (!updated.TryRemove(Rules.JobItemId, JobRequirement))
                return Result(false, "납품할 물품이 부족합니다.");
            RemoveDocument(updated, Rules.JobDocumentItemId);
            if (JobReward > 0 && !updated.TryAdd(ItemIds.Gold, JobReward)
                || JobRewardItemCount > 0 && !updated.TryAdd(JobRewardItemId, JobRewardItemCount))
                return Result(false, "보상을 받을 소지품 공간이 부족합니다.");
            Inventory.ReplaceWith(updated);
            Job = JobStatus.Completed;
            Career++;
            AddExperience(JobExperience);
            return Result(true, $"아르바이트 완료! {JobReward}G · 경험치 {JobExperience} · 농장 경력 +1");
        }

        private void ExpireJob()
        {
            Job = JobStatus.Failed;
            RemoveDocument(Inventory, Rules.JobDocumentItemId);
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

        public bool TryGather(string itemId, int baseYield, int baseEnergy, WorkOutcome outcome, out int drops) =>
            TryGather(SkillProgression.ForItem(itemId), itemId, baseYield, baseEnergy, outcome, out drops);

        public bool TryGather(SkillId skill, string itemId, int baseYield, int baseEnergy, WorkOutcome outcome, out int drops)
        {
            drops = 0;
            if (baseYield < 1 || baseYield > 100 || !ItemCatalog.Contains(itemId) || !SkillProgression.IsDefined(skill))
                return Result(false, "채집 결과 설정을 확인해 주세요.");
            int cost = WorkEnergyCost(skill, itemId, baseEnergy);
            if (Energy < cost)
                return Result(false, "스태미나가 부족해요. 빵을 먹거나 숙소에서 쉬어 주세요.");
            Energy -= cost;
            if (outcome == WorkOutcome.Failure)
                return Result(false, "채집에 실패했어요.");
            LifetimeHarvests++;
            int bonus = outcome == WorkOutcome.GreatLucky ? 50 : outcome == WorkOutcome.Lucky ? 2 : 1;
            drops = ScaledYield(skill, baseYield, bonus);
            if (itemId == ItemIds.Timber)
                LifetimeTimber = (int)Math.Min(int.MaxValue, (long)LifetimeTimber + drops);
            AddExperience(Rules.HarvestExperience + Knowledge / 5);
            string result = outcome == WorkOutcome.GreatLucky ? "대박 보너스!" : outcome == WorkOutcome.Lucky ? "럭키 보너스!" : "채집 완료!";
            return Result(true, $"{result} {ItemCatalog.Get(itemId).Name} {drops:N0}개");
        }

        private void AddExperience(int amount)
        {
            int previousLevel = Level, previousStamina = StaminaLimit, previousHealth = HealthLimit;
            Experience += Math.Min(long.MaxValue - Experience, Math.Max(0, amount));
            while (Level < LevelLimit && Experience >= NextLevelExperience)
            {
                Experience -= NextLevelExperience;
                Level++;
                if (TotalLevel < long.MaxValue)
                    TotalLevel++;
                if (Rules.APPerLevel > 0)
                    AP += Math.Min(int.MaxValue - AP, Rules.APPerLevel);
                AttributePoints++;
            }

            if (previousLevel != Level)
            {
                RefreshSkillStats();
                Energy = Math.Min(StaminaLimit, Energy + StaminaLimit - previousStamina);
                Health = Math.Min(HealthLimit, Health + HealthLimit - previousHealth);
            }
        }

        public bool TryBuy(string itemId) => TryBuy(itemId, 1);
        public int MaximumBuyQuantity(string itemId)
        {
            if (!IsPurchasable(itemId) || AlreadyOwnsUniquePurchase(itemId))
                return 0;
            var spec = ItemCatalog.Get(itemId);
            int limit = PurchaseLimit(spec);
            return spec.Price > 0 ? Math.Min(limit, Gold / spec.Price) : limit;
        }

        private bool IsPurchasable(string itemId) => ItemCatalog.Contains(itemId) && itemId != ItemIds.Gold &&
            itemId != ItemIds.LegendAxe && !TryGetDocumentPage(itemId, out _);

        private bool AlreadyOwnsUniquePurchase(string itemId) => itemId switch
        {
            ItemIds.Hoe => HasHoe || Inventory.Count(ItemIds.Hoe) > 0 || PendingPurchase?.ItemId == itemId,
            ItemIds.Bag => BagExpanded || Inventory.Count(ItemIds.Bag) > 0 || PendingPurchase?.ItemId == itemId,
            _ => false
        };

        private static int PurchaseLimit(ItemSpec spec) => spec.EquipmentSlot == EquipmentSlot.None && spec.MaxStack > 1 ? 999 : 1;

        public bool TryBuy(string itemId, int quantity) => TryBuy(itemId, quantity, placeOnCursor: false);

        internal bool TryBuyForPlacement(string itemId, int quantity) => TryBuy(itemId, quantity, placeOnCursor: true);

        private bool TryBuy(string itemId, int quantity, bool placeOnCursor)
        {
            if (placeOnCursor && (PendingPurchase != null || AwaitingIncarnation))
                return Result(false, "손에 든 물건을 먼저 놓아 주세요.");
            if (quantity < 1 || quantity > 999)
                return Result(false, "구입할 수 없는 수량입니다.");
            if (!IsPurchasable(itemId))
                return Result(false, "판매하지 않는 물건입니다.");
            var spec = ItemCatalog.Get(itemId);
            if (quantity > PurchaseLimit(spec))
                return Result(false, "이 물건은 한 개씩 구입할 수 있어요.");
            if (itemId == ItemIds.Hoe && AlreadyOwnsUniquePurchase(itemId))
                return Result(false, "이미 같은 도구를 가지고 있습니다.");
            if (itemId == ItemIds.Bag && AlreadyOwnsUniquePurchase(itemId))
                return Result(false, "이미 여행 가방을 가지고 있어요.");
            if ((long)Gold < (long)spec.Price * quantity)
                return Result(false, "골드가 부족해요.");
            int cost = spec.Price * quantity;
            if (placeOnCursor)
            {
                if (cost > 0 && !Inventory.TryRemove(ItemIds.Gold, cost))
                    return Result(false, "골드가 부족해요.");
                PendingPurchase = new InventoryEntry { ItemId = itemId, Quantity = quantity };
                return Result(true, $"{spec.Name} {quantity}개 구입! {cost}G를 지불했어요.");
            }
            if (itemId == ItemIds.Bag)
            {
                if (spec.Price > 0 && !Inventory.TryRemove(ItemIds.Gold, spec.Price))
                    return Result(false, "골드가 부족해요.");
                equipment.Add(EquipmentSlot.Bag, itemId);
                if (Inventory.Rows < BagInventoryRows)
                    Inventory.ExpandRows(BagInventoryRows);
                return Result(true, $"{spec.Name} 구입 및 장착. {spec.Price}G를 지불했어요.");
            }
            if (!Inventory.TryExchange(ItemIds.Gold, cost, itemId, quantity))
                return Result(false, "골드나 소지품 공간이 부족합니다.");
            return Result(true, $"{spec.Name} {quantity}개 구입! {cost}G를 지불했어요.");
        }

        // A fresh purchase has no source cell. Zero is reserved for this carried stack.
        internal InventoryEntry CarriedItem(int entryId) => entryId == 0 ? PendingPurchase : Inventory.Find(entryId);

        internal bool TryPlacePurchase(int x, int y)
        {
            if (PendingPurchase == null)
                return false;
            int placed = Inventory.PlacePurchase(PendingPurchase, x, y);
            return placed > 0 && RemovePurchaseQuantity(placed);
        }

        internal bool TryStowPurchase() => PendingPurchase != null &&
            Inventory.TryAdd(PendingPurchase.ItemId, PendingPurchase.Quantity) && RemovePurchaseQuantity(PendingPurchase.Quantity);

        internal bool TryRemoveCarriedItem(int entryId, int quantity) => entryId == 0
            ? RemovePurchaseQuantity(quantity) : Inventory.TryRemoveAt(entryId, quantity);

        private bool RemovePurchaseQuantity(int amount)
        {
            if (PendingPurchase == null || amount < 1 || amount > PendingPurchase.Quantity)
                return false;
            PendingPurchase.Quantity -= amount;
            if (PendingPurchase.Quantity == 0)
                PendingPurchase = null;
            Version++;
            return true;
        }

        public bool TrySellItems(string itemId, int quantity)
        {
            if (quantity < 1 || quantity > 999 || !CanSell(itemId))
                return Result(false, "판매할 수 없는 물건입니다.");
            long total = (long)ItemCatalog.Get(itemId).Price * quantity;
            if (total > int.MaxValue || !Inventory.TryExchange(itemId, quantity, ItemIds.Gold, (int)total))
                return Result(false, "판매 수량이나 소지품 공간이 부족합니다.");
            return Result(true, $"{ItemCatalog.Get(itemId).Name} {quantity}개 판매 · {total} Gold");
        }

        internal bool CanSell(string itemId) => ItemCatalog.Contains(itemId) && itemId != ItemIds.Gold &&
            itemId != ItemIds.Bag && !TryGetDocumentPage(itemId, out _);

        public int MaximumSellQuantity(int entryId)
        {
            var entry = CarriedItem(entryId);
            if (entry == null || !CanSell(entry.ItemId))
                return 0;
            return entry.Spec.Price > 0 ? Math.Min(entry.Quantity, int.MaxValue / entry.Spec.Price) : entry.Quantity;
        }

        public bool TrySellStack(int entryId, int quantity)
        {
            if (quantity < 1 || quantity > MaximumSellQuantity(entryId))
                return Result(false, "판매할 수 없는 수량입니다.");
            var entry = CarriedItem(entryId);
            var spec = entry.Spec;
            int total = spec.Price * quantity;
            bool received = entryId == 0
                ? total == 0 || Inventory.TryAdd(ItemIds.Gold, total)
                : Inventory.TryExchangeAt(entryId, quantity, ItemIds.Gold, total);
            if (!received)
                return Result(false, "골드를 받을 소지품 공간이 부족합니다.");
            if (entryId == 0)
                RemovePurchaseQuantity(quantity);
            return Result(true, $"{spec.Name} {quantity}개 판매 · {total:N0} Gold");
        }

        public bool TrySplitStack(int entryId, int quantity) =>
            Inventory.TrySplit(entryId, quantity)
                ? Result(true, $"{quantity:N0}개를 나눴어요.")
                : Result(false, "수량이나 나눌 공간을 확인해 주세요.");

        public bool TryEatBread()
        {
            if (Energy >= StaminaLimit)
                return Result(false, "에너지가 이미 가득해요.");
            if (!Inventory.TryRemove(ItemIds.Bread, 1))
                return Result(false, "가방에 빵이 없어요.");
            Energy = Math.Min(StaminaLimit, Energy + 25);
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
            if (!ExpansionQuestActive)
                return Result(false, "지금은 농장 확장을 위한 기부를 받지 않아요.");
            if (CanConfirmExpansion)
                return ConfirmExpansion();
            int amount = Math.Min(Rules.DonationGoldStep, ExpansionGoldRequired - DonatedGold);
            if (amount <= 0)
                return Result(false, "필요한 자금은 모두 모였어요.");
            if (Gold < amount)
                return Result(false, $"기부하려면 {amount}G가 필요해요.");
            int reward = DonatedGold + amount >= ExpansionGoldRequired && DonatedPotatoes >= ExpansionItemsRequired ? Rules.ExpansionRewardGold : 0;
            if (!Inventory.TryExchange(ItemIds.Gold, amount, ItemIds.Gold, reward))
                return Result(false, "기부 보상을 받을 소지품 공간이 부족합니다.");
            DonatedGold += amount;
            CheckDonation();
            return Result(true, FarmUpgradePending ? "농장 확장 준비 완료! 시대 전환을 진행할 수 있어요." : $"마을에 {amount}G를 기부했어요. ({DonatedGold}/{ExpansionGoldRequired}G)");
        }

        public bool TryDonatePotatoes()
        {
            if (!ExpansionQuestActive)
                return Result(false, "지금은 농장 확장을 위한 기부를 받지 않아요.");
            if (CanConfirmExpansion)
                return ConfirmExpansion();
            int amount = Math.Min(Rules.DonationItemStep, ExpansionItemsRequired - DonatedPotatoes);
            if (amount <= 0)
                return Result(false, "필요한 물품은 모두 모였습니다.");
            if (Inventory.Count(Rules.DonationItemId) < amount)
                return Result(false, "기부 물품이 부족합니다.");
            int reward = DonatedGold >= ExpansionGoldRequired && DonatedPotatoes + amount >= ExpansionItemsRequired ? Rules.ExpansionRewardGold : 0;
            if (!Inventory.TryExchange(Rules.DonationItemId, amount, ItemIds.Gold, reward))
                return Result(false, "기부 보상을 받을 소지품 공간이 부족합니다.");
            DonatedPotatoes += amount;
            CheckDonation();
            return Result(true, FarmUpgradePending ? "농장 확장 준비 완료! 시대 전환을 진행할 수 있어요." : $"마을에 {ItemCatalog.Get(Rules.DonationItemId).Name} {amount}개를 기부했어요. ({DonatedPotatoes}/{ExpansionItemsRequired}개)");
        }

        private bool ConfirmExpansion()
        {
            if (Rules.ExpansionRewardGold > 0 && !Inventory.TryAdd(ItemIds.Gold, Rules.ExpansionRewardGold))
                return Result(false, "기부 보상을 받을 소지품 공간이 부족합니다.");
            CheckDonation();
            return Result(true, "농장 확장 준비 완료! 시대 전환을 진행할 수 있어요.");
        }

        private void CheckDonation()
        {
            FarmUpgradePending = DonatedGold >= ExpansionGoldRequired && DonatedPotatoes >= ExpansionItemsRequired;
            if (FarmUpgradePending)
            {
                AP += Rules.ExpansionRewardAP;
                AddExperience(Rules.ExpansionRewardExperience);
            }
        }

        public bool TrySleep(bool paidRoom) => TrySleep(paidRoom, false);

        internal bool TrySleep(bool paidRoom, bool sharedClock)
        {
            if (AwaitingIncarnation)
                return false;
            if (paidRoom && Gold < Rules.RoomPrice)
                return Result(false, "숙박비가 부족합니다.");
            if (paidRoom && Rules.RoomPrice > 0)
                Inventory.TryRemove(ItemIds.Gold, Rules.RoomPrice);
            int restoredEnergy = paidRoom ? StaminaLimit : Rules.FreeRestEnergy;
            if (sharedClock)
            {
                Energy = Math.Min(StaminaLimit, restoredEnergy);
                Health = HealthLimit;
                return Result(true, $"숙소에서 쉬었습니다. 에너지 {Energy} 회복.");
            }
            NextDay(restoredEnergy, false);
            return Result(true, $"다음 날 {Rules.WakeMinute / 60:00}:{Rules.WakeMinute % 60:00}. 에너지 {Energy} 회복.");
        }

        private void NextDay(int energy, bool collapsed)
        {
            Day++;
            LifeDay++;
            Minute = Rules.WakeMinute;
            Energy = Math.Min(energy, StaminaLimit);
            Health = HealthLimit;
            if (Job == JobStatus.Active)
                ExpireJob();
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

        public VillageSave Export()
        {
            var worn = new List<EquipmentEntrySave>();
            foreach (var entry in equipment)
                worn.Add(new EquipmentEntrySave { slot = entry.Key, itemId = entry.Value });
            return new VillageSave
            {
                schema = CurrentSaveSchema,
                awaitingIncarnation = AwaitingIncarnation,
                hasLifeLook = LifeLook.HasValue,
                lifeLook = LifeLook.GetValueOrDefault(),
                equipment = worn.ToArray(),
                day = Day,
                lifeDay = LifeDay,
                life = Life,
                era = Era,
                lifeStartedAt = lifeStartedAt,
                health = Health,
                attributePoints = AttributePoints,
                strength = Strength,
                dexterity = Dexterity,
                knowledge = Knowledge,
                timberDelivered = TimberDelivered,
                characterLimit = CharacterLimit,
                requirementCharacters = RequirementCharacters,
                lifetimeTimber = LifetimeTimber,
                legend = Legend,
                minute = Minute,
                energy = Energy,
                gold = 0,
                level = Level,
                experience = Experience,
                totalLevel = TotalLevel,
                ap = AP,
                skillRanks = ExportSkills(),
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
                plotPlacements = plotPlacements,
                lastGuideLife = LastGuideLife,
                bagExpanded = BagExpanded,
                inventoryColumns = Inventory.Columns,
                inventoryRows = Inventory.Rows,
                inventory = Inventory.Export(),
                pendingPurchase = PendingPurchase == null ? null :
                    new PendingPurchaseSave { itemId = PendingPurchase.ItemId, quantity = PendingPurchase.Quantity }
            };
        }

        public static VillageState Restore(VillageSave save, VillageRules rules = null)
        {
            rules = rules ?? new VillageRules();
            // An earlier era may have collected more than the current era requires.
            int highestScale = save != null && save.characterLimit > 0 ? WorldCapacity.MaxCharacters : 1;
            if (save == null || save.schema < 1 || save.schema > CurrentSaveSchema ||
                save.day < 1 || save.lifeDay < 1 || save.life < 1 || save.day > 1000000 || save.life > 1000000 || save.lifeDay > save.day ||
                float.IsNaN(save.minute) || float.IsInfinity(save.minute) || save.minute < 0 || save.minute >= 1440 ||
                save.energy < 0 || save.energy > (save.schema < 5 ? MaxEnergy : 1000000) || save.gold < 0 || save.gold > 100000000 ||
                save.level < 1 || save.level > LevelLimit || save.experience < 0 ||
                (save.schema < 6 || save.level < LevelLimit) && save.experience >= 20 + save.level * 10 ||
                save.totalLevel < save.level || save.ap < 0 ||
                (save.schema < 5 && (save.skillRank < 0 || save.skillRank > 8)) ||
                save.career < 0 || save.career > 1000000 || save.lifetimeHarvests < 0 ||
                save.characterLimit < 0 || save.characterLimit > WorldCapacity.MaxCharacters ||
                save.requirementCharacters < 0 || save.requirementCharacters > Math.Max(1, save.characterLimit) ||
                save.donatedGold < 0 || save.donatedGold > WorldCapacity.ScaleRequirement(Math.Max(RequiredDonationGold, rules.DonationGold), highestScale) ||
                save.donatedPotatoes < 0 || save.donatedPotatoes > WorldCapacity.ScaleRequirement(Math.Max(RequiredDonationPotatoes, rules.DonationItems), highestScale) ||
                save.farmLevel < 1 || save.farmLevel > Math.Max(2, rules.ExpandedStage) ||
                save.jobStatus < 0 || save.jobStatus > 3 || save.lastJobDay > save.day || save.lastJobDay < -1 ||
                save.inventoryColumns != (save.schema < SixColumnInventorySchema ? LegacyInventoryColumns : InventoryColumns) ||
                save.inventoryRows < BaseInventoryRows ||
                save.inventoryRows > (save.schema < SixColumnInventorySchema ? LegacyMaxInventoryRows : GridInventory.MaxRows))
                throw new ArgumentException("Invalid village save.");
            if ((JobStatus)save.jobStatus == JobStatus.Active && (save.lastJobDay != save.day || save.jobRequirement < 1 || save.jobRequirement > 10000 || save.jobReward < 0 || save.jobReward > 10000 || save.jobExperience < 0 || save.jobExperience > 100000))
                throw new ArgumentException("Invalid active job.");
            var foundationStage = RestoreFoundationStage(save, rules);
            double worldMinute = (save.day - 1) * 1440d + save.minute;
            if (save.schema >= 6 && (double.IsNaN(save.lifeStartedAt) || save.lifeStartedAt < 0 || save.lifeStartedAt > worldMinute ||
                save.era < 1 || save.era > 1000000 || save.health < 0 || save.health > 1000000 || save.attributePoints < 0 ||
                save.strength < 0 || save.dexterity < 0 || save.knowledge < 0 ||
                (long)save.attributePoints + save.strength + save.dexterity + save.knowledge > save.level - 1 ||
                save.lifetimeTimber < 0 || save.timberDelivered < 0 ||
                save.timberDelivered > WorldCapacity.ScaleRequirement(rules.Foundation?.TimberCount ?? 0, highestScale)))
                throw new ArgumentException("Invalid life or era progress.");
            if (save.jobRewardItemCount < 0 || save.jobRewardItemCount > 10000 || (save.jobRewardItemCount > 0 && !ItemCatalog.Contains(save.jobRewardItemId)))
                throw new ArgumentException("Invalid job item reward.");
            var inventory = GridInventory.Restore(save.inventoryColumns, save.inventoryRows, save.inventory);
            if (save.schema == 1 && save.gold > 0)
            {
                int remaining = save.gold;
                while (remaining > 0)
                {
                    int amount = Math.Min(remaining, 10000);
                    while (!inventory.CanAdd(ItemIds.Gold, amount) && inventory.Rows < LegacyMaxInventoryRows)
                        inventory.ExpandRows(inventory.Rows + 1);
                    if (!inventory.TryAdd(ItemIds.Gold, amount))
                        throw new ArgumentException("Legacy gold exceeds inventory capacity.");
                    remaining -= amount;
                }
            }

            if (inventory.Count(ItemIds.Hoe) > 1)
                throw new ArgumentException("Invalid equipment.");
            var state = new VillageState(rules)
            {
                AwaitingIncarnation = save.schema >= 8 && save.awaitingIncarnation,
                LifeLook = save.schema >= 8 && save.hasLifeLook ? save.lifeLook.Validated() : (CharacterLook?)null,
                Day = save.day,
                LifeDay = save.lifeDay,
                Life = save.life,
                Era = save.schema >= 6 ? save.era : (foundationStage == FarmFoundationStage.Built && rules.Foundation != null ? save.farmLevel + 1 : save.farmLevel),
                lifeStartedAt = save.schema >= 6 ? save.lifeStartedAt : worldMinute - (save.lifeDay - 1) * 1440d,
                AttributePoints = save.schema >= 6 ? save.attributePoints : save.level - 1,
                Strength = save.schema >= 6 ? save.strength : 0,
                Dexterity = save.schema >= 6 ? save.dexterity : 0,
                Knowledge = save.schema >= 6 ? save.knowledge : 0,
                TimberDelivered = save.schema >= 6 ? save.timberDelivered :
                    (int)foundationStage >= (int)FarmFoundationStage.SelectingSite ? rules.Foundation?.TimberCount ?? 0 : 0,
                CharacterLimit = save.characterLimit,
                RequirementCharacters = Math.Max(1, save.requirementCharacters),
                Minute = save.minute,
                Energy = save.energy,
                Level = save.level,
                Experience = save.experience,
                TotalLevel = save.totalLevel,
                AP = save.ap,
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
                LifetimeTimber = save.lifetimeTimber,
                Legend = RestoreLegend(save.legend),
                FarmSiteId = save.farmSiteId,
                plotPlacements = RestorePlotPlacements(save.plotPlacements),
                LastGuideLife = save.schema < 3 ? save.life : save.lastGuideLife,
                Inventory = inventory
            };
            state.RestoreSkills(save);
            state.RefreshSkillStats();
            state.Health = save.schema >= 6 ? Math.Min(save.health, state.HealthLimit) : state.HealthLimit;
            state.Energy = Math.Min(state.Energy, state.StaminaLimit);
            if (save.schema >= 2 && save.equipment != null)
                foreach (var entry in save.equipment)
                {
                    if (entry == null || !ItemCatalog.Contains(entry.itemId) || entry.slot == EquipmentSlot.None || ItemCatalog.Get(entry.itemId).EquipmentSlot != entry.slot || state.equipment.ContainsKey(entry.slot))
                        throw new ArgumentException("Invalid equipped item.");
                    state.equipment.Add(entry.slot, entry.itemId);
                }

            if (save.schema < 4 && save.bagExpanded)
            {
                if (state.BagExpanded || inventory.Count(ItemIds.Bag) != 1 || !inventory.TryRemove(ItemIds.Bag, 1))
                    throw new ArgumentException("Invalid legacy bag.");
                state.equipment.Add(EquipmentSlot.Bag, ItemIds.Bag);
            }
            if (save.bagExpanded != state.BagExpanded || state.BagExpanded && inventory.Rows < BagInventoryRows)
                throw new ArgumentException("Invalid bag equipment.");

            if (save.pendingPurchase is PendingPurchaseSave purchase &&
                (purchase.quantity != 0 || !string.IsNullOrEmpty(purchase.itemId)))
            {
                if (!state.IsPurchasable(purchase.itemId) || purchase.quantity < 1 ||
                    purchase.quantity > PurchaseLimit(ItemCatalog.Get(purchase.itemId)))
                    throw new ArgumentException("Invalid pending purchase.");
                state.PendingPurchase = new InventoryEntry { ItemId = purchase.itemId, Quantity = purchase.quantity };
            }

            if (state.AwaitingIncarnation && (state.HasVillageProgress || save.gold != 0 ||
                (save.drops?.Length ?? 0) > 0 || !string.IsNullOrEmpty(save.interior.id)))
                throw new ArgumentException("An incarnation waiting on the clouds cannot carry possessions or progress from the village.");

            if (state.Job == JobStatus.Active && state.Minute >= rules.JobEnd)
                state.ExpireJob();
            if (state.Job != JobStatus.Active)
                RemoveDocument(inventory, rules.JobDocumentItemId);
            else if (save.schema < 3 && !string.IsNullOrEmpty(rules.JobDocumentItemId)
                && state.Inventory.Count(rules.JobDocumentItemId) == 0)
                state.Inventory.TryAdd(rules.JobDocumentItemId, 1);
            if (state.FoundationStage >= FarmFoundationStage.SelectingSite)
                RemoveDocument(inventory, rules.Foundation?.DocumentItemId);
            if (inventory.Columns != InventoryColumns)
                state.Inventory = inventory.ReflowColumns(InventoryColumns);
            return state;
        }
    }
}
