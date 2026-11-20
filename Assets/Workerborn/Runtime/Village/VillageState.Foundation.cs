using System;

namespace Workerborn
{
    public enum FarmFoundationStage
    {
        WaitingForContract,
        CollectingTimber,
        SelectingSite,
        ReadyToRebirth,
        Built
    }

    public enum VillageQuestPage
    {
        FirstWork,
        Expansion,
        FarmFoundation,
        TimberContract,
        FarmJob,
        LegendFounding,
        LegendTrial,
        LegendGuidance
    }

    public sealed partial class VillageState
    {
        public FarmFoundationStage FoundationStage { get; private set; }
        public int TimberDelivered { get; private set; }
        public string FarmSiteId { get; private set; }
        public int LastGuideLife { get; private set; }
        public bool FarmAvailable => Rules.Foundation == null || FoundationStage == FarmFoundationStage.Built;
        public bool NeedsGuide => Rules.Foundation != null && LastGuideLife < Life;
        public bool CanSelectFarmSite => FoundationStage == FarmFoundationStage.SelectingSite || FoundationStage == FarmFoundationStage.ReadyToRebirth;

        public void FinishGuideVisit()
        {
            LastGuideLife = Life;
            Version++;
        }

        public bool TryGetDocumentPage(string itemId, out VillageQuestPage page)
        {
            if (!string.IsNullOrEmpty(itemId))
            {
                if (itemId == Rules.Foundation?.DocumentItemId)
                {
                    page = VillageQuestPage.TimberContract;
                    return true;
                }
                if (itemId == Rules.JobDocumentItemId)
                {
                    page = VillageQuestPage.FarmJob;
                    return true;
                }
            }
            page = default;
            return false;
        }

        public bool TryAcceptFoundation()
        {
            var quest = Rules.Foundation;
            if (quest == null || FoundationStage > FarmFoundationStage.CollectingTimber || Inventory.Count(quest.DocumentItemId) > 0)
                return Result(false, "목재 계약서를 이미 받았어요.");
            if (!Inventory.TryAdd(quest.DocumentItemId, 1))
                return Result(false, "계약서를 넣을 소지품 공간이 부족합니다.");

            FoundationStage = FarmFoundationStage.CollectingTimber;
            return Result(true, "목재 계약서를 받았어요. 소지품에서 클릭해 펼쳐 보세요.");
        }

        public bool TryDeliverTimber()
        {
            var quest = Rules.Foundation;
            if (quest == null || FoundationStage != FarmFoundationStage.CollectingTimber)
                return Result(false, "진행 중인 목재 납품이 없어요.");
            int amount = Math.Min(quest.TimberCount - TimberDelivered, Inventory.Count(quest.TimberItemId));
            if (amount < 1)
                return Result(false, "납품할 목재가 없어요.");
            int reward = TimberDelivered + amount == quest.TimberCount ? quest.RewardGold : 0;
            if (!Inventory.TryExchange(quest.TimberItemId, amount, ItemIds.Gold, reward))
                return Result(false, "목재를 납품하고 보상을 받을 소지품 공간이 부족합니다.");

            TimberDelivered += amount;
            if (TimberDelivered < quest.TimberCount)
                return Result(true, $"목재 {amount}개를 납품했어요. ({TimberDelivered}/{quest.TimberCount})");

            FoundationStage = FarmFoundationStage.SelectingSite;
            AP += quest.RewardAP;
            AddExperience(quest.RewardExperience);
            return Result(true, "납품을 마쳤어요. 농장이 들어설 부지를 지정해 주세요.");
        }

        public bool TryChooseFarmSite(string siteId, string siteName)
        {
            if (!CanSelectFarmSite || string.IsNullOrWhiteSpace(siteId))
                return Result(false, "목재 납품을 끝낸 뒤 부지를 정할 수 있어요.");
            FarmSiteId = siteId;
            FoundationStage = FarmFoundationStage.ReadyToRebirth;
            return Result(true, $"{siteName}에 농장을 짓기로 했어요. 기억의 우물에서 시대 전환을 진행하세요.");
        }

        private static FarmFoundationStage RestoreFoundationStage(VillageSave save, VillageRules rules)
        {
            // A save from the original village already has a working farm.
            if (rules.Foundation == null || save.schema < 3)
                return FarmFoundationStage.Built;
            if (!Enum.IsDefined(typeof(FarmFoundationStage), save.foundationStage)
                || save.lastGuideLife < 0 || save.lastGuideLife > save.life
                || save.foundationStage == (int)FarmFoundationStage.ReadyToRebirth && string.IsNullOrWhiteSpace(save.farmSiteId))
                throw new ArgumentException("Invalid farm foundation progress.");
            return (FarmFoundationStage)save.foundationStage;
        }
    }
}
