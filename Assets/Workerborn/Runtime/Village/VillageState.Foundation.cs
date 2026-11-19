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
        FarmJob
    }

    public sealed partial class VillageState
    {
        public FarmFoundationStage FoundationStage { get; private set; }
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
            if (quest == null || FoundationStage != FarmFoundationStage.WaitingForContract)
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
            if (!Inventory.TryRemove(quest.TimberItemId, quest.TimberCount))
                return Result(false, $"목재가 {quest.TimberCount}개 필요해요.");

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
            return Result(true, $"{siteName}에 농장을 짓기로 했어요. 기억의 우물에서 다음 삶을 준비하세요.");
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
