using System;

namespace Workerborn
{
    public enum JobStatus { None, Active, Completed, Failed }

    [Serializable]
    public sealed class GroundDropSave
    {
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
        public bool bagExpanded;
        public int inventoryColumns = 8;
        public int inventoryRows = 4;
        public InventoryEntrySave[] inventory = Array.Empty<InventoryEntrySave>();
        public int[] cropRemaining = Array.Empty<int>();
        public GroundDropSave[] drops = Array.Empty<GroundDropSave>();
        public float playerX;
        public float playerZ = -4.4f;
    }

    // Unity-independent rules, shared by the playable village and its NUnit tests.
    public sealed class VillageState
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
        public int Gold { get; private set; }
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
        public bool BagExpanded { get; private set; }
        public bool LastRestWasCollapse { get; private set; }
        public GridInventory Inventory { get; private set; } = new GridInventory();
        public int Version { get; private set; }
        public int NoticeSerial { get; private set; }
        public string LastMessage { get; private set; } = "마을의 농부 미라에게 말을 걸어 보세요.";
        public int NextLevelExperience => 20 + Level * 10;
        public int NextSkillCost => SkillRank + 1;
        public bool HasHoe => Inventory.Count(ItemIds.Hoe) > 0;
        public int HarvestEnergyCost => Math.Max(1, 5 - SkillRank / 3 - (HasHoe ? 1 : 0));
        public float WorkRate => 1f + SkillRank * 0.25f + (HasHoe ? 0.25f : 0f);
        public int CareerTier => Career >= 8 ? 3 : Career >= 3 ? 2 : 1;
        public int OfferedJobRequirement => (CareerTier == 1 ? 6 : CareerTier == 2 ? 10 : 16) + (FarmLevel - 1) * 4;
        public int OfferedJobReward => (CareerTier == 1 ? 45 : CareerTier == 2 ? 80 : 140) + (FarmLevel - 1) * 30;
        public int OfferedJobExperience => (CareerTier == 1 ? 35 : CareerTier == 2 ? 60 : 100) + (FarmLevel - 1) * 20;
        public bool CanRebirth => IntroClaimed && Level >= 3;
        public bool JobWindowOpen => Minute >= JobStart && Minute < JobEnd;

        private bool Result(bool success, string message)
        {
            LastMessage = message;
            NoticeSerial++;
            Version++;
            return success;
        }

        public bool AdvanceTime(float seconds, bool paused = false)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (paused) return false;
            Minute += seconds * MinutesPerSecond;
            if (Job == JobStatus.Active && Minute >= JobEnd)
            {
                Job = JobStatus.Failed;
                Result(false, "18:00이 지나 아르바이트가 마감됐어요. 내일 다시 도전해 보세요.");
            }
            if (Minute < 24 * 60 && Energy > 0) return false;
            NextDay(45, true);
            Result(true, "무리하다 쓰러졌어요. 숙소에서 에너지 45로 다음 날을 맞이했어요.");
            return true;
        }

        public void MeetFarmer()
        {
            if (IntroAccepted) return;
            IntroAccepted = true;
            Result(true, "퀘스트 수락: 마을의 새 일꾼 — 미라의 아르바이트를 한 번 완료하세요.");
        }

        public bool TryAcceptJob()
        {
            MeetFarmer();
            if (!JobWindowOpen) return Result(false, "아르바이트 접수·납품 시간은 08:00~18:00예요.");
            if (LastJobDay == Day) return Result(false, "오늘의 아르바이트는 이미 받았어요. 내일 다시 찾아와 주세요.");
            LastJobDay = Day;
            Job = JobStatus.Active;
            JobRequirement = OfferedJobRequirement;
            JobReward = OfferedJobReward;
            JobExperience = OfferedJobExperience;
            return Result(true, $"아르바이트 수락! 18:00 전까지 감자 {JobRequirement}개를 미라에게 납품하세요.");
        }

        public bool TryDeliverJob()
        {
            if (Job != JobStatus.Active || LastJobDay != Day) return Result(false, "진행 중인 아르바이트가 없어요.");
            if (!JobWindowOpen)
            {
                if (Minute >= JobEnd) Job = JobStatus.Failed;
                return Result(false, "아르바이트 납품 시간이 아니에요.");
            }
            if (!Inventory.TryRemove(ItemIds.Potato, JobRequirement))
                return Result(false, $"감자가 {JobRequirement}개 필요해요. 현재 {Inventory.Count(ItemIds.Potato)}개를 가지고 있어요.");
            Job = JobStatus.Completed;
            Career++;
            Gold += JobReward;
            AddExperience(JobExperience);
            return Result(true, $"아르바이트 완료! {JobReward}G · 경험치 {JobExperience} · 농장 경력 +1");
        }

        public bool TryClaimIntroQuest()
        {
            if (!IntroAccepted || Career == 0) return Result(false, "미라의 아르바이트를 먼저 완료해 주세요.");
            if (IntroClaimed) return Result(false, "이 퀘스트의 보상은 이미 받았어요.");
            IntroClaimed = true;
            Gold += 30;
            AP += 2;
            AddExperience(15);
            return Result(true, "퀘스트 완료: 마을의 새 일꾼! 30G · AP 2 · 경험치 15. 농장 확장 기부가 열렸어요.");
        }

        public bool TryHarvest(int baseYield, bool lucky, out int drops)
        {
            drops = 0;
            if (baseYield < 1 || baseYield > 100) return Result(false, "수확량 설정을 확인해 주세요.");
            if (Energy < HarvestEnergyCost) return Result(false, "에너지가 부족해요. 빵을 먹거나 숙소에서 쉬어 주세요.");
            Energy -= HarvestEnergyCost;
            LifetimeHarvests++;
            drops = (baseYield + SkillRank / 3) * (lucky ? 2 : 1);
            AddExperience(8);
            return Result(true, lucky ? $"럭키 보너스! 감자 {drops}개가 쏟아졌어요." : $"수확 완료! 바닥에 떨어진 감자 {drops}개를 주워 주세요.");
        }

        private void AddExperience(int amount)
        {
            if (Level >= 200) return;
            Experience += amount;
            while (Level < 200 && Experience >= NextLevelExperience)
            {
                Experience -= NextLevelExperience;
                Level++;
                TotalLevel++;
                AP += 2;
            }
            if (Level == 200) Experience = 0;
        }

        public bool TryUpgradeSkill()
        {
            if (SkillRank >= 8) return Result(false, "프로토타입의 최고 수확 랭크에 도달했어요.");
            if (AP < NextSkillCost) return Result(false, $"승급하려면 AP {NextSkillCost}가 필요해요.");
            AP -= NextSkillCost;
            SkillRank++;
            return Result(true, "수확 스킬 승급! 더 빠르게 일할 수 있어요. 이 능력은 환생 후에도 남아요.");
        }

        public bool TryBuy(string itemId)
        {
            if (itemId != ItemIds.Bread && itemId != ItemIds.Hoe && itemId != ItemIds.Bag) return Result(false, "판매하지 않는 물건이에요.");
            var spec = ItemCatalog.Get(itemId);
            if (itemId == ItemIds.Hoe && HasHoe) return Result(false, "이미 튼튼한 괭이를 가지고 있어요.");
            if (itemId == ItemIds.Bag && BagExpanded) return Result(false, "이미 여행 가방을 사용 중이에요.");
            if (Gold < spec.Price) return Result(false, "골드가 부족해요.");
            if (!Inventory.TryAdd(itemId, 1)) return Result(false, "가방에 빈 공간이 없어요. 물건을 정리해 주세요.");
            Gold -= spec.Price;
            if (itemId == ItemIds.Bag) { BagExpanded = true; Inventory.ExpandRows(6); }
            return Result(true, $"{spec.Name} 구입! {spec.Price}G를 지불했어요.");
        }

        public bool TryEatBread()
        {
            if (Energy == MaxEnergy) return Result(false, "에너지가 이미 가득해요.");
            if (!Inventory.TryRemove(ItemIds.Bread, 1)) return Result(false, "가방에 빵이 없어요.");
            Energy = Math.Min(MaxEnergy, Energy + 25);
            return Result(true, "빵을 먹고 에너지를 25 회복했어요.");
        }

        public bool TrySellPotatoes(int amount)
        {
            if (amount < 1 || amount > 10000 || !Inventory.TryRemove(ItemIds.Potato, amount)) return Result(false, "판매할 감자가 부족해요.");
            Gold += amount * ItemCatalog.Get(ItemIds.Potato).Price;
            return Result(true, $"감자 {amount}개 판매! {amount * 3}G를 받았어요.");
        }

        public bool TryDonateGold()
        {
            if (!IntroClaimed || FarmLevel > 1 || FarmUpgradePending) return Result(false, "지금은 농장 확장을 위한 기부를 받지 않아요.");
            int amount = Math.Min(25, RequiredDonationGold - DonatedGold);
            if (amount <= 0) return Result(false, "필요한 자금은 모두 모였어요.");
            if (Gold < amount) return Result(false, $"기부하려면 {amount}G가 필요해요.");
            Gold -= amount; DonatedGold += amount;
            CheckDonation();
            return Result(true, FarmUpgradePending ? "농장 확장 준비 완료! 다음 환생 때 넓어진 밭을 만날 수 있어요." : $"마을에 {amount}G를 기부했어요. ({DonatedGold}/{RequiredDonationGold}G)");
        }

        public bool TryDonatePotatoes()
        {
            if (!IntroClaimed || FarmLevel > 1 || FarmUpgradePending) return Result(false, "지금은 농장 확장을 위한 기부를 받지 않아요.");
            int amount = Math.Min(5, RequiredDonationPotatoes - DonatedPotatoes);
            if (amount <= 0) return Result(false, "필요한 감자는 모두 모였어요.");
            if (!Inventory.TryRemove(ItemIds.Potato, amount)) return Result(false, $"기부하려면 감자 {amount}개가 필요해요.");
            DonatedPotatoes += amount;
            CheckDonation();
            return Result(true, FarmUpgradePending ? "농장 확장 준비 완료! 다음 환생 때 넓어진 밭을 만날 수 있어요." : $"마을에 감자 {amount}개를 기부했어요. ({DonatedPotatoes}/{RequiredDonationPotatoes}개)");
        }

        private void CheckDonation()
        {
            FarmUpgradePending = DonatedGold >= RequiredDonationGold && DonatedPotatoes >= RequiredDonationPotatoes;
        }

        public bool TrySleep(bool paidRoom)
        {
            if (paidRoom && Gold < 10) return Result(false, "편안한 객실은 10G예요. 무료 잠자리도 이용할 수 있어요.");
            if (paidRoom) Gold -= 10;
            NextDay(paidRoom ? MaxEnergy : 70, false);
            return Result(true, paidRoom ? "편안히 쉬었어요. 다음 날 06:00, 에너지를 모두 회복했어요." : "소박한 잠자리에서 쉬었어요. 다음 날 06:00, 에너지 70으로 시작해요.");
        }

        private void NextDay(int energy, bool collapsed)
        {
            Day++; LifeDay++; Minute = 6 * 60; Energy = energy;
            if (Job == JobStatus.Active) Job = JobStatus.Failed;
            LastRestWasCollapse = collapsed;
            Version++;
        }

        public bool TryWaitForWork()
        {
            if (Minute >= JobStart) return Result(false, "이미 아침 08:00이 지났어요.");
            Minute = JobStart;
            return Result(true, "아침 08:00까지 기다렸어요. 농장 아르바이트가 시작됐어요.");
        }

        public bool TryRebirth()
        {
            if (!CanRebirth) return Result(false, "'마을의 새 일꾼' 퀘스트 보상을 받고 레벨 3에 도달하면 환생할 수 있어요.");
            if (FarmUpgradePending) { FarmLevel = 2; FarmUpgradePending = false; }
            Life++; Day++; LifeDay = 1; Minute = JobStart;
            Level = 1; Experience = 0; Energy = MaxEnergy; Gold = 0;
            Inventory = new GridInventory(); BagExpanded = false;
            Job = JobStatus.None; LastJobDay = -1;
            JobRequirement = JobReward = JobExperience = 0;
            LastRestWasCollapse = false;
            return Result(true, $"{Life}번째 생이 시작됐어요. 스킬·AP·경력과 마을의 변화는 이어집니다.");
        }

        public VillageSave Export()
        {
            return new VillageSave
            {
                day = Day, lifeDay = LifeDay, life = Life, minute = Minute, energy = Energy, gold = Gold,
                level = Level, experience = Experience, totalLevel = TotalLevel, ap = AP, skillRank = SkillRank,
                career = Career, lifetimeHarvests = LifetimeHarvests, introAccepted = IntroAccepted, introClaimed = IntroClaimed,
                donatedGold = DonatedGold, donatedPotatoes = DonatedPotatoes, farmUpgradePending = FarmUpgradePending,
                farmLevel = FarmLevel, lastJobDay = LastJobDay, jobStatus = (int)Job, jobRequirement = JobRequirement,
                jobReward = JobReward, jobExperience = JobExperience, bagExpanded = BagExpanded,
                inventoryColumns = Inventory.Columns, inventoryRows = Inventory.Rows, inventory = Inventory.Export()
            };
        }

        public static VillageState Restore(VillageSave save)
        {
            if (save == null || save.schema != 1 || save.day < 1 || save.lifeDay < 1 || save.life < 1
                || save.day > 1000000 || save.life > 1000000 || save.lifeDay > save.day
                || float.IsNaN(save.minute) || float.IsInfinity(save.minute) || save.minute < 0 || save.minute >= 1440
                || save.energy < 0 || save.energy > MaxEnergy || save.gold < 0 || save.gold > 100000000
                || save.level < 1 || save.level > 200 || save.experience < 0 || save.experience >= 20 + save.level * 10
                || save.totalLevel < save.level || save.totalLevel > 100000000 || save.ap < 0 || save.ap > 1000000
                || save.skillRank < 0 || save.skillRank > 8 || save.career < 0 || save.career > 1000000
                || save.lifetimeHarvests < 0 || save.donatedGold < 0 || save.donatedGold > RequiredDonationGold
                || save.donatedPotatoes < 0 || save.donatedPotatoes > RequiredDonationPotatoes
                || save.farmLevel < 1 || save.farmLevel > 2 || save.jobStatus < 0 || save.jobStatus > 3
                || save.lastJobDay > save.day || save.lastJobDay < -1 || save.inventoryColumns != 8
                || save.inventoryRows != (save.bagExpanded ? 6 : 4)) throw new ArgumentException("Invalid village save.");
            int tier = save.career >= 8 ? 3 : save.career >= 3 ? 2 : 1;
            if ((JobStatus)save.jobStatus == JobStatus.Active
                && (save.lastJobDay != save.day || save.jobRequirement != (tier == 1 ? 6 : tier == 2 ? 10 : 16) + (save.farmLevel - 1) * 4
                    || save.jobReward != (tier == 1 ? 45 : tier == 2 ? 80 : 140) + (save.farmLevel - 1) * 30
                    || save.jobExperience != (tier == 1 ? 35 : tier == 2 ? 60 : 100) + (save.farmLevel - 1) * 20))
                throw new ArgumentException("Invalid active job.");
            var inventory = GridInventory.Restore(save.inventoryColumns, save.inventoryRows, save.inventory);
            if (save.bagExpanded != (inventory.Count(ItemIds.Bag) == 1) || inventory.Count(ItemIds.Hoe) > 1)
                throw new ArgumentException("Invalid equipment.");
            var state = new VillageState
            {
                Day = save.day, LifeDay = save.lifeDay, Life = save.life, Minute = save.minute, Energy = save.energy,
                Gold = save.gold, Level = save.level, Experience = save.experience, TotalLevel = save.totalLevel,
                AP = save.ap, SkillRank = save.skillRank, Career = save.career, LifetimeHarvests = save.lifetimeHarvests,
                IntroAccepted = save.introAccepted, IntroClaimed = save.introClaimed, DonatedGold = save.donatedGold,
                DonatedPotatoes = save.donatedPotatoes, FarmUpgradePending = save.farmUpgradePending,
                FarmLevel = save.farmLevel, LastJobDay = save.lastJobDay, Job = (JobStatus)save.jobStatus,
                JobRequirement = save.jobRequirement, JobReward = save.jobReward, JobExperience = save.jobExperience,
                BagExpanded = save.bagExpanded, Inventory = inventory
            };
            if (state.Job == JobStatus.Active && state.Minute >= JobEnd) state.Job = JobStatus.Failed;
            return state;
        }
    }
}
