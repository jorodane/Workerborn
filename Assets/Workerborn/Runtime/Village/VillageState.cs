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
        public string lootOwnerCharacterId;
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
        internal const long ReservationMilliseconds = 60_000;
        internal const long AutoPickupPeriodMilliseconds = 1_000;
        internal const float PickupDistance = 2f;
        internal const int MaximumGatheredDrops = 48;
        internal static long UtcNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public string id;
        public int quantity = 1;
        public string itemId;
        public EquipmentCondition condition;
        // Older saves used this field to mark the NPC's uncollected harvests.
        public string harvestedBy;
        public string ownerId;
        public long reservedUntilUtc;
        public bool combined;
        public float x;
        public float z;
        public InteriorPositionSave interior;
        public bool hasDropOrigin;
        public float originX, originY, originZ;
        public bool harvestYield, crownYield;
        public int flightMilliseconds;

        internal bool CanCollect(string playerId, long now) =>
            string.IsNullOrEmpty(ownerId) || now >= reservedUntilUtc || ownerId == playerId;

        internal bool CanAutoCollect(string playerId, long now) => harvestYield &&
            now >= reservedUntilUtc - ReservationMilliseconds + flightMilliseconds &&
            IsVisibleTo(playerId) && CanCollect(playerId, now);

        internal bool CanAcquireFromCrown(string playerId, long now) => crownYield && ownerId == playerId && now < reservedUntilUtc;

        internal bool IsVisibleTo(string playerId) => interior.id?.EndsWith("/guest", StringComparison.Ordinal) != true || ownerId == playerId;

        internal string RoomId => string.IsNullOrEmpty(interior.id) ? null : interior.id;
        internal float MapX => RoomId == null ? x : interior.localX;
        internal float MapZ => RoomId == null ? z : interior.localZ;
        internal GroundDropSave Copy() => (GroundDropSave)MemberwiseClone();

        internal void ReserveFor(string playerId, long now)
        {
            ownerId = playerId;
            reservedUntilUtc = now + ReservationMilliseconds;
        }
    }

    [Serializable]
    public sealed class CursorItemSave
    {
        public string itemId;
        public int quantity;
        public EquipmentCondition condition;
        public bool splitFromInventory;
    }

    [Serializable]
    public class CharacterSave
    {
        public string characterId;
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
        public double staminaFraction;
        public double staminaRecoveryDelay;
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
        public string[] itemShortcuts = Array.Empty<string>();
        public int[] skillShortcuts = Array.Empty<int>();
        public CursorItemSave pendingPurchase; // Retains the original save field for items without a source cell.
        public HarvestAcquisitionSave[] pendingHarvests = Array.Empty<HarvestAcquisitionSave>();
        public float playerX;
        public float playerZ = -4.4f;
        public float yaw;
        public bool sitting;
        public string harvestTargetId;
        public float harvestProgress;
        public bool casting;
        public float castX;
        public float castY;
        public float castZ;
        public InteriorPositionSave interior;
        public bool awaitingIncarnation;
        public bool hasLifeLook;
        public CharacterLook lifeLook;
        public EquipmentEntrySave[] equipment = Array.Empty<EquipmentEntrySave>();
        public EquipmentEntrySave[] otherEquipment = Array.Empty<EquipmentEntrySave>();
        public int equipmentSet, blockedEquipment, otherBlockedEquipment;
        // The previous schema stored only the other set's hand slots.
        public EquipmentEntrySave[] otherHandEquipment = Array.Empty<EquipmentEntrySave>();
        public int handSet;

        internal void CopyPoseAndAction(CharacterSave source, bool teleport = false)
        {
            characterId = source.characterId;
            playerX = source.playerX;
            playerZ = source.playerZ;
            if (teleport)
                return;
            interior = source.interior;
            yaw = source.yaw;
            sitting = source.sitting;
            harvestTargetId = source.harvestTargetId;
            harvestProgress = source.harvestProgress;
            casting = source.casting;
            castX = source.castX;
            castY = source.castY;
            castZ = source.castZ;
        }
    }

    [Serializable]
    public sealed class VillageSave : CharacterSave
    {
        public WoodlandSave woodland;
        public MountSave[] mounts = Array.Empty<MountSave>();
        public int[] cropRemaining = Array.Empty<int>();
        public CropStateSave[] cropStates = Array.Empty<CropStateSave>();
        public AnimalStateSave[] animalStates = Array.Empty<AnimalStateSave>();
        public string[] collectedPickupIds = Array.Empty<string>();
        public GroundDropSave[] drops = Array.Empty<GroundDropSave>();
        public CharacterSave[] characters = Array.Empty<CharacterSave>();
    }

    // Unity-independent rules, shared by the playable village and its NUnit tests.
    public sealed partial class VillageState
    {
        internal const int ItemShortcutCount = 10;
        private const int ShortcutCount = ItemShortcutCount * 2;
        private readonly string[] itemShortcuts = new string[ShortcutCount];
        private readonly int[] skillShortcuts = new int[ShortcutCount];
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
        public int Gold => Inventory.Count(ItemIds.Gold);
        public int Level { get; private set; } = 1;
        public long Experience { get; private set; }
        public long TotalLevel { get; private set; } = 1;
        public int AP { get; private set; }
        public int Career { get; private set; }
        public int LifetimeHarvests { get; private set; }
        public bool IntroAccepted { get; private set; }
        public bool IntroClaimed { get; private set; }
        public bool CanClaimIntroQuest => IntroAccepted && !IntroClaimed && Career >= Rules.IntroRequiredJobs;
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
        internal InventoryEntry CursorItem { get; private set; }
        private bool cursorItemWasSplit;

        private readonly Dictionary<EquipmentSlot, string> equipment = new Dictionary<EquipmentSlot, string>();
        private readonly EquipmentLoadout[] equipmentSets = { new EquipmentLoadout(), new EquipmentLoadout() };
        internal int EquipmentSet { get; private set; }
        private EquipmentLoadout ActiveEquipment => equipmentSets[EquipmentSet];
        private EquipmentLoadout OtherEquipment => equipmentSets[1 - EquipmentSet];
        public IReadOnlyDictionary<EquipmentSlot, string> Equipment => equipment;
        public EquipmentCondition EquippedCondition(EquipmentSlot slot)
        {
            if (ActiveEquipment.Items.TryGetValue(slot, out var assigned))
                return assigned.Condition;
            return !ActiveEquipment.IsBlocked(slot) && OtherEquipment.Items.TryGetValue(slot, out var borrowed) ? borrowed.Condition : default;
        }
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
            RestoreStamina(StaminaLimit);
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
            if (ActiveEquipment.Items.TryGetValue(slot, out var previous) && !copy.TryAdd(previous.ItemId, 1, previous.Condition))
                return Result(false, "기존 장비를 넣을 공간이 없습니다.");
            if (slot == EquipmentSlot.Bag)
                SetBagCapacity(copy, true);
            Inventory.ReplaceWith(copy);
            SetEquippedItem(slot, item.Spec, item.Condition);
            if (entryId == 0)
                RemoveCursorQuantity(1);
            return Result(true, item.Spec.Name + " 장착");
        }

        public bool TryUnequip(EquipmentSlot slot)
        {
            if (!ActiveEquipment.Items.TryGetValue(slot, out var item))
                return IsEquipmentFallback(slot) && TryToggleEquipmentFallback(slot);
            var copy = Inventory.Copy();
            if (slot == EquipmentSlot.Bag && !OtherEquipment.Items.ContainsKey(slot) && !SetBagCapacity(copy, false) ||
                !copy.TryAdd(item.ItemId, 1, item.Condition))
                return Result(false, "장비를 해제할 소지품 공간이 부족합니다.");
            Inventory.ReplaceWith(copy);
            ActiveEquipment.Items.Remove(slot);
            RefreshEffectiveEquipment();
            return Result(true, ItemCatalog.Get(item.ItemId).Name + " 해제");
        }

        private void SetEquippedItem(EquipmentSlot slot, ItemSpec item, EquipmentCondition condition = default)
        {
            ActiveEquipment.Items[slot] = (item.Id, condition.ForItem(item));
            ActiveEquipment.Blocked &= ~(1 << (int)slot);
            RefreshEffectiveEquipment();
        }

        internal bool TrySelectEquipmentSet(int set)
        {
            if (AwaitingIncarnation || CursorItem != null || (uint)set >= equipmentSets.Length || EquipmentSet == set)
                return false;
            bool willWearBag = OtherEquipment.Items.ContainsKey(EquipmentSlot.Bag) ||
                !OtherEquipment.IsBlocked(EquipmentSlot.Bag) && ActiveEquipment.Items.ContainsKey(EquipmentSlot.Bag);
            if (willWearBag != BagExpanded && !SetBagCapacity(Inventory, willWearBag))
                return Result(false, "가방을 벗으려면 소지품 공간이 더 필요해요.");
            EquipmentSet = set;
            RefreshEffectiveEquipment();
            return Result(true, $"장비 세트 {set + 1}번");
        }

        internal string AssignedEquipmentAt(EquipmentSlot slot) => ActiveEquipment.Items.TryGetValue(slot, out var item) ? item.ItemId : null;
        internal bool IsEquipmentFallback(EquipmentSlot slot) => equipment.ContainsKey(slot) && !ActiveEquipment.Items.ContainsKey(slot);
        internal bool IsEquipmentBlocked(EquipmentSlot slot) => ActiveEquipment.IsBlocked(slot);

        internal bool TryToggleEquipmentFallback(EquipmentSlot slot)
        {
            if (AwaitingIncarnation || CursorItem != null || !ValidEquipmentSlot(slot) || ActiveEquipment.Items.ContainsKey(slot))
                return false;
            bool willBorrow = ActiveEquipment.IsBlocked(slot);
            if (slot == EquipmentSlot.Bag && OtherEquipment.Items.ContainsKey(slot) && !SetBagCapacity(Inventory, willBorrow))
                return Result(false, "가방을 벗으려면 소지품 공간이 더 필요해요.");
            ActiveEquipment.Blocked ^= 1 << (int)slot;
            RefreshEffectiveEquipment();
            return Result(true, null);
        }

        private void RefreshEffectiveEquipment()
        {
            equipment.Clear();
            foreach (var item in ActiveEquipment.Items)
                equipment.Add(item.Key, item.Value.ItemId);
            foreach (var item in OtherEquipment.Items)
                if (!ActiveEquipment.IsBlocked(item.Key))
                    equipment.TryAdd(item.Key, item.Value.ItemId);
        }

        private static bool SetBagCapacity(GridInventory inventory, bool wearingBag)
        {
            if (!wearingBag)
                return inventory.Rows <= BaseInventoryRows || inventory.TryShrinkRows(BaseInventoryRows);
            if (inventory.Rows < BagInventoryRows)
                inventory.ExpandRows(BagInventoryRows);
            return true;
        }

        private bool OwnsEquippedItem(string id, EquipmentSlot slot) => AssignedEquipmentAt(slot) == id ||
            OtherEquipment.Items.TryGetValue(slot, out var other) && other.ItemId == id;

        private const int EquipmentSlotMask = (1 << ((int)EquipmentSlot.Bag + 1)) - 2;
        private static bool ValidEquipmentSlot(EquipmentSlot slot) => slot is > EquipmentSlot.None and <= EquipmentSlot.Bag;

        private sealed class EquipmentLoadout
        {
            internal readonly Dictionary<EquipmentSlot, (string ItemId, EquipmentCondition Condition)> Items =
                new Dictionary<EquipmentSlot, (string, EquipmentCondition)>();
            internal int Blocked;
            internal bool IsBlocked(EquipmentSlot slot) => ValidEquipmentSlot(slot) && (Blocked & (1 << (int)slot)) != 0;

            internal EquipmentEntrySave[] ExportItems()
            {
                var saved = new EquipmentEntrySave[Items.Count];
                int i = 0;
                foreach (var entry in Items)
                    saved[i++] = new EquipmentEntrySave { slot = entry.Key, itemId = entry.Value.ItemId, condition = entry.Value.Condition };
                return saved;
            }

            internal void RestoreItems(EquipmentEntrySave[] saved, int blocked, bool handsOnly = false)
            {
                if ((blocked & ~EquipmentSlotMask) != 0)
                    throw new ArgumentException("Invalid blocked equipment slots.");
                Blocked = blocked;
                foreach (var entry in saved ?? Array.Empty<EquipmentEntrySave>())
                {
                    if (entry == null || !ValidEquipmentSlot(entry.slot) || IsBlocked(entry.slot) ||
                        handsOnly && entry.slot is not (EquipmentSlot.MainHand or EquipmentSlot.OffHand) ||
                        !ItemCatalog.Contains(entry.itemId))
                        throw new ArgumentException("Invalid equipped item.");
                    var item = ItemCatalog.Get(entry.itemId);
                    if (item.EquipmentSlot != entry.slot || !Items.TryAdd(entry.slot, (entry.itemId, entry.condition.ForItem(item))))
                        throw new ArgumentException("Invalid equipped item.");
                }
            }
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
            int earned = AddExperience(JobExperience);
            string experience = earned > 0 ? $" · 경험치 {earned:N0}" : "";
            return Result(true, $"아르바이트 완료! {JobReward}G{experience} · 농장 경력 +1");
        }

        private void ExpireJob()
        {
            Job = JobStatus.Failed;
            RemoveDocument(Inventory, Rules.JobDocumentItemId);
        }

        public bool TryClaimIntroQuest()
        {
            if (IntroClaimed)
                return Result(false, "이 퀘스트의 보상은 이미 받았어요.");
            if (!CanClaimIntroQuest)
                return Result(false, "아직 퀘스트 목표를 완료하지 못했습니다.");
            if (Rules.IntroGold > 0 && !Inventory.TryAdd(ItemIds.Gold, Rules.IntroGold))
                return Result(false, "보상을 받을 소지품 공간이 부족합니다.");
            IntroClaimed = true;
            AP += Rules.IntroAP;
            AddExperience(Rules.IntroExperience);
            bool learned = LearnSkill(SkillId.Harvesting);
            return Result(true, "퀘스트 완료: " + Rules.IntroTitle + (learned ? " · 수확 스킬을 배웠어요." : ""));
        }

        public bool TryHarvest(int baseYield, bool lucky, out int drops)
            => TryGather(ItemIds.Potato, baseYield, Rules.HarvestEnergy, lucky ? WorkOutcome.Lucky : WorkOutcome.Success, out drops);

        public bool TryGather(string itemId, int baseYield, int baseEnergy, WorkOutcome outcome, out int drops) =>
            TryGather(SkillProgression.ForItem(itemId), itemId, baseYield, baseEnergy, outcome, out drops);

        public bool TryGather(SkillId skill, string itemId, int baseYield, int baseEnergy, WorkOutcome outcome, out int drops) =>
            TryGather(skill, itemId, baseYield, baseEnergy, outcome, null, out drops, out _);

        internal bool TryGather(SkillId skill, string itemId, int baseYield, int baseEnergy, WorkOutcome outcome,
            Func<int, bool> receive, out int drops, out int experience) =>
            ResolveGather(skill, itemId, baseYield, WorkEnergyCost(skill, itemId, baseEnergy), outcome, receive, out drops, out experience);

        internal bool CompleteGather(SkillId skill, string itemId, int baseYield, WorkOutcome outcome,
            Func<int, bool> receive, out int experience) =>
            ResolveGather(skill, itemId, baseYield, 0, outcome, receive, out _, out experience);

        private bool ResolveGather(SkillId skill, string itemId, int baseYield, int cost, WorkOutcome outcome,
            Func<int, bool> receive, out int drops, out int experience)
        {
            drops = 0;
            experience = 0;
            if (baseYield < 1 || baseYield > 100 || !ItemCatalog.Contains(itemId) || !SkillProgression.IsDefined(skill))
                return Result(false, "채집 결과 설정을 확인해 주세요.");
            if (Stamina < cost)
                return Result(false, "스태미나가 부족해요.");
            if (outcome == WorkOutcome.Failure)
            {
                SpendWorkStamina(cost);
                return Result(false, "채집에 실패했어요.");
            }
            if ((outcome == WorkOutcome.Lucky && LuckyChancePercent == 0) ||
                (outcome == WorkOutcome.GreatLucky && GreatLuckyChancePercent == 0))
                outcome = WorkOutcome.Success;
            int bonus = outcome == WorkOutcome.GreatLucky ? 50 : outcome == WorkOutcome.Lucky ? 2 : 1;
            int quantity = ScaledYield(skill, baseYield, bonus);
            if (receive != null && !receive(quantity))
                return false;
            drops = quantity;
            SpendWorkStamina(cost);
            LifetimeHarvests++;
            if (itemId == ItemIds.Timber)
                LifetimeTimber = (int)Math.Min(int.MaxValue, (long)LifetimeTimber + drops);
            experience = AddExperience((int)Math.Min(int.MaxValue, (long)Rules.HarvestExperience + Knowledge / 5));
            string result = outcome == WorkOutcome.GreatLucky ? "대박 보너스!" : outcome == WorkOutcome.Lucky ? "럭키 보너스!" : "채집 완료!";
            return Result(true, $"{result} {ItemCatalog.Get(itemId).Name} {drops:N0}개");
        }

        private int AddExperience(int amount)
        {
            int earned = (int)Math.Min(long.MaxValue - Experience, ScaledExperience(amount));
            if (earned <= 0 || AwaitingIncarnation)
                return 0;
            int previousLevel = Level, previousStamina = StaminaLimit, previousHealth = HealthLimit;
            Experience += earned;
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
                SetStamina(Stamina + StaminaLimit - previousStamina);
                Health = Math.Min(HealthLimit, Health + HealthLimit - previousHealth);
            }
            return earned;
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
            ItemIds.Hoe => OwnsEquippedItem(itemId, EquipmentSlot.MainHand) || Inventory.Count(ItemIds.Hoe) > 0 || CursorItem?.ItemId == itemId,
            ItemIds.Bag => OwnsEquippedItem(itemId, EquipmentSlot.Bag) || Inventory.Count(ItemIds.Bag) > 0 || CursorItem?.ItemId == itemId,
            _ => false
        };

        private static int PurchaseLimit(ItemSpec spec) => spec.EquipmentSlot == EquipmentSlot.None && spec.MaxStack > 1 ? 999 : 1;

        public bool TryBuy(string itemId, int quantity) => TryBuy(itemId, quantity, placeOnCursor: false);

        internal bool TryBuyForPlacement(string itemId, int quantity) => TryBuy(itemId, quantity, placeOnCursor: true);

        private bool TryBuy(string itemId, int quantity, bool placeOnCursor)
        {
            if (placeOnCursor && (CursorItem != null || AwaitingIncarnation))
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
                CursorItem = new InventoryEntry { ItemId = itemId, Quantity = quantity, Condition = default(EquipmentCondition).ForItem(spec) };
                cursorItemWasSplit = false;
                return Result(true, $"{spec.Name} {quantity}개 구입! {cost}G를 지불했어요.");
            }
            if (itemId == ItemIds.Bag)
            {
                if (spec.Price > 0 && !Inventory.TryRemove(ItemIds.Gold, spec.Price))
                    return Result(false, "골드가 부족해요.");
                SetEquippedItem(EquipmentSlot.Bag, spec);
                SetBagCapacity(Inventory, true);
                return Result(true, $"{spec.Name} 구입 및 장착. {spec.Price}G를 지불했어요.");
            }
            if (!Inventory.TryExchange(ItemIds.Gold, cost, itemId, quantity))
                return Result(false, "골드나 소지품 공간이 부족합니다.");
            return Result(true, $"{spec.Name} {quantity}개 구입! {cost}G를 지불했어요.");
        }

        // A purchased or split stack on the cursor has no source cell and uses entry ID zero.
        internal InventoryEntry CarriedItem(int entryId) => entryId == 0 ? CursorItem : Inventory.Find(entryId);

        internal bool TryPlaceCursorItem(int x, int y)
        {
            if (CursorItem == null)
                return false;
            int placed = Inventory.PlaceCursorItem(CursorItem, x, y);
            return placed > 0 && RemoveCursorQuantity(placed);
        }

        internal bool TryStowCursorItem() => CursorItem != null &&
            Inventory.TryAdd(CursorItem.ItemId, CursorItem.Quantity, CursorItem.Condition) && RemoveCursorQuantity(CursorItem.Quantity);

        internal bool TryRemoveCarriedItem(int entryId, int quantity) => entryId == 0
            ? RemoveCursorQuantity(quantity) : Inventory.TryRemoveAt(entryId, quantity);

        private bool RemoveCursorQuantity(int amount)
        {
            if (CursorItem == null || amount < 1 || amount > CursorItem.Quantity)
                return false;
            CursorItem.Quantity -= amount;
            if (CursorItem.Quantity == 0)
            {
                CursorItem = null;
                cursorItemWasSplit = false;
            }
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
                RemoveCursorQuantity(quantity);
            return Result(true, $"{spec.Name} {quantity}개 판매 · {total:N0} Gold");
        }

        internal int MaximumCursorSplitQuantity(int entryId) => !AwaitingIncarnation && CursorItem == null
            ? Math.Max(0, (Inventory.Find(entryId)?.Quantity ?? 0) - 1) : 0;

        public bool TrySplitStack(int entryId, int quantity)
        {
            if (quantity < 1 || quantity > MaximumCursorSplitQuantity(entryId))
                return Result(false, "손에 든 물건과 나눌 수량을 확인해 주세요.");
            var item = Inventory.Find(entryId);
            if (!Inventory.TryRemoveAt(entryId, quantity))
                return false;
            CursorItem = new InventoryEntry { ItemId = item.ItemId, Quantity = quantity, Condition = item.Condition };
            cursorItemWasSplit = true;
            return Result(true, $"{quantity:N0}개를 나눴어요.");
        }

        public bool TryEatBread()
        {
            if (Stamina >= StaminaLimit)
                return Result(false, "에너지가 이미 가득해요.");
            if (!Inventory.TryRemove(ItemIds.Bread, 1))
                return Result(false, "가방에 빵이 없어요.");
            SetStamina(Stamina + 25);
            return Result(true, "빵을 먹고 에너지를 25 회복했어요.");
        }

        internal string ItemShortcutAt(int index) => (uint)index < ShortcutCount ? itemShortcuts[index] : null;

        internal SkillId? SkillShortcutAt(int index) => (uint)index < ShortcutCount && skillShortcuts[index] > 0
            ? (SkillId)(skillShortcuts[index] - 1) : null;

        internal bool IsEquipped(ItemSpec item) => equipment.TryGetValue(item.EquipmentSlot, out string id) && id == item.Id;

        internal InventoryEntry FindShortcutEntry(int index)
        {
            string itemId = ItemShortcutAt(index);
            if (itemId != null)
                foreach (var entry in Inventory.Entries)
                    if (entry.ItemId == itemId)
                        return entry;
            return null;
        }

        internal bool TryAssignItemShortcut(int index, int entryId)
        {
            if (AwaitingIncarnation || (uint)index >= ShortcutCount)
                return false;
            var entry = Inventory.Find(entryId);
            if (entry == null || !CanAssignItemShortcut(entry.Spec))
                return Result(false, "장비나 사용할 수 있는 소모품을 등록해 주세요.");
            itemShortcuts[index] = entry.ItemId;
            skillShortcuts[index] = 0;
            return Result(true, $"{ShortcutKey(index)} 단축키에 {entry.Spec.Name} 등록");
        }

        internal bool TryAssignSkillShortcut(int index, SkillId skill)
        {
            if (AwaitingIncarnation || (uint)index >= ShortcutCount || !IsSkillLearned(skill) || !SkillProgression.CanActivate(skill))
                return false;
            skillShortcuts[index] = (int)skill + 1;
            itemShortcuts[index] = null;
            return Result(true, $"{ShortcutKey(index)} 단축키에 {Rules.Skill(skill).Name} 등록");
        }

        internal bool TryClearItemShortcut(int index)
        {
            if (AwaitingIncarnation || ItemShortcutAt(index) == null && !SkillShortcutAt(index).HasValue)
                return false;
            itemShortcuts[index] = null;
            skillShortcuts[index] = 0;
            return Result(true, $"{ShortcutKey(index)} 단축키 등록을 해제했어요.");
        }

        internal static string ShortcutKey(int index) => (index < ItemShortcutCount ? "" : "Shift+") + ((index + 1) % ItemShortcutCount);

        private static bool CanAssignItemShortcut(ItemSpec item) => item.EquipmentSlot != EquipmentSlot.None ||
            item.HealthRestore > 0 || item.Id == ItemIds.Bread || item.TaughtSkill.HasValue;

        public bool TryDrinkPotion(int entryId)
        {
            if (AwaitingIncarnation || NeedsRest)
                return Result(false, "지금은 생명력 포션을 사용할 수 없어요.");
            var item = Inventory.Find(entryId);
            if (item == null || item.Spec.HealthRestore <= 0)
                return Result(false, "사용할 생명력 포션이 없어요.");
            int restored = Math.Min(HealthLimit - Health, item.Spec.HealthRestore);
            if (restored <= 0)
                return Result(false, "생명력이 이미 가득해요.");
            if (!Inventory.TryRemoveAt(entryId, 1))
                return Result(false, "사용할 생명력 포션이 없어요.");
            Health += restored;
            return Result(true, $"생명력을 {restored:N0} 회복했어요.");
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
                RestoreStamina(restoredEnergy);
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
            RestoreStamina(energy);
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

        public VillageSave Export() => Export<VillageSave>();

        internal T Export<T>() where T : CharacterSave, new()
        {
            return new T
            {
                schema = CurrentSaveSchema,
                awaitingIncarnation = AwaitingIncarnation,
                hasLifeLook = LifeLook.HasValue,
                lifeLook = LifeLook.GetValueOrDefault(),
                equipment = ActiveEquipment.ExportItems(),
                otherEquipment = OtherEquipment.ExportItems(),
                equipmentSet = EquipmentSet,
                blockedEquipment = ActiveEquipment.Blocked,
                otherBlockedEquipment = OtherEquipment.Blocked,
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
                energy = (int)Math.Floor(Stamina),
                staminaFraction = Stamina - Math.Floor(Stamina),
                staminaRecoveryDelay = staminaRecoveryDelay,
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
                itemShortcuts = (string[])itemShortcuts.Clone(),
                skillShortcuts = (int[])skillShortcuts.Clone(),
                pendingHarvests = pendingHarvests.ToArray(),
                pendingPurchase = CursorItem == null ? null :
                    new CursorItemSave
                    {
                        itemId = CursorItem.ItemId, quantity = CursorItem.Quantity, condition = CursorItem.Condition,
                        splitFromInventory = cursorItemWasSplit
                    }
            };
        }

        public static VillageState Restore(CharacterSave save, VillageRules rules = null)
        {
            rules = rules ?? new VillageRules();
            // An earlier era may have collected more than the current era requires.
            int highestScale = save != null && save.characterLimit > 0 ? WorldCapacity.MaxCharacters : 1;
            if (save == null || save.schema < 1 || save.schema > CurrentSaveSchema ||
                save.day < 1 || save.lifeDay < 1 || save.life < 1 || save.day > 1000000 || save.life > 1000000 || save.lifeDay > save.day ||
                float.IsNaN(save.minute) || float.IsInfinity(save.minute) || save.minute < 0 || save.minute >= 1440 ||
                save.energy < 0 || save.energy > (save.schema < 5 ? MaxEnergy : 1000000) || save.gold < 0 || save.gold > 100000000 ||
                save.schema >= StaminaSaveSchema &&
                (!double.IsFinite(save.staminaFraction) || save.staminaFraction < 0d || save.staminaFraction >= 1d ||
                    !double.IsFinite(save.staminaRecoveryDelay) || save.staminaRecoveryDelay < 0d || save.staminaRecoveryDelay > RecoveryDelaySeconds) ||
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
                Stamina = save.energy + (save.schema >= StaminaSaveSchema ? save.staminaFraction : 0d),
                staminaRecoveryDelay = save.schema >= StaminaSaveSchema ? save.staminaRecoveryDelay : 0d,
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
            state.RestoreHarvestAcquisitions(save);
            if (save.itemShortcuts != null)
                for (int index = 0; index < Math.Min(save.itemShortcuts.Length, ShortcutCount); index++)
                {
                    string id = save.itemShortcuts[index];
                    if (ItemCatalog.Contains(id) && CanAssignItemShortcut(ItemCatalog.Get(id)))
                        state.itemShortcuts[index] = id;
                }
            if (save.schema >= LoadoutSaveSchema && save.skillShortcuts != null)
                for (int index = 0; index < Math.Min(save.skillShortcuts.Length, ShortcutCount); index++)
                {
                    int id = save.skillShortcuts[index];
                    if (id > 0 && SkillProgression.CanActivate((SkillId)(id - 1)) && state.IsSkillLearned((SkillId)(id - 1)))
                    {
                        state.skillShortcuts[index] = id;
                        state.itemShortcuts[index] = null;
                    }
                }
            state.RefreshSkillStats();
            state.Health = save.schema >= 6 ? Math.Min(save.health, state.HealthLimit) : state.HealthLimit;
            state.Stamina = Math.Min(state.Stamina, state.StaminaLimit);
            bool fullSets = save.schema >= EquipmentSetsSaveSchema;
            int set = fullSets ? save.equipmentSet : save.schema >= LoadoutSaveSchema ? save.handSet : 0;
            if ((uint)set >= state.equipmentSets.Length)
                throw new ArgumentException("Invalid equipment set.");
            state.EquipmentSet = set;
            if (save.schema >= 2)
                state.ActiveEquipment.RestoreItems(save.equipment, fullSets ? save.blockedEquipment : 0);
            if (fullSets)
                state.OtherEquipment.RestoreItems(save.otherEquipment, save.otherBlockedEquipment);
            else if (save.schema >= LoadoutSaveSchema)
                state.OtherEquipment.RestoreItems(save.otherHandEquipment, 0, handsOnly: true);
            state.RefreshEffectiveEquipment();

            if (save.schema < 4 && save.bagExpanded)
            {
                if (state.BagExpanded || inventory.Count(ItemIds.Bag) != 1 || !inventory.TryRemove(ItemIds.Bag, 1))
                    throw new ArgumentException("Invalid legacy bag.");
                state.SetEquippedItem(EquipmentSlot.Bag, ItemCatalog.Get(ItemIds.Bag));
            }
            if (save.bagExpanded != state.BagExpanded || state.BagExpanded && inventory.Rows < BagInventoryRows)
                throw new ArgumentException("Invalid bag equipment.");

            if (save.pendingPurchase is CursorItemSave carried &&
                (carried.quantity != 0 || !string.IsNullOrEmpty(carried.itemId)))
            {
                if (!ItemCatalog.Contains(carried.itemId) || carried.quantity < 1)
                    throw new ArgumentException("Invalid item on the cursor.");
                var spec = ItemCatalog.Get(carried.itemId);
                bool legal = carried.splitFromInventory ? carried.quantity < spec.MaxStack :
                    state.IsPurchasable(carried.itemId) && carried.quantity <= PurchaseLimit(spec);
                if (!legal)
                    throw new ArgumentException("Invalid cursor item quantity.");
                state.CursorItem = new InventoryEntry
                {
                    ItemId = carried.itemId, Quantity = carried.quantity, Condition = carried.condition.ForItem(spec)
                };
                state.cursorItemWasSplit = carried.splitFromInventory;
            }

            if (state.AwaitingIncarnation && (state.HasVillageProgress || save.gold != 0 ||
                save is VillageSave { drops: { Length: > 0 } } || !string.IsNullOrEmpty(save.interior.id)))
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
