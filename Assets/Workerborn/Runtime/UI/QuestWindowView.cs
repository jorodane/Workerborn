using UnityEngine;

namespace Workerborn
{
    public sealed class QuestWindowView : VillageWindowView
    {
        [SerializeField]
        private UiListRow firstWork;
        [SerializeField]
        private UiListRow expansion;
        [SerializeField]
        private UiListRow job;
        [SerializeField]
        private UiListRow legendFounding;
        [SerializeField]
        private UiListRow legendTrial;
        [SerializeField] private UiActionView firstWorkDisplay;
        [SerializeField] private UiActionView expansionDisplay;
        [SerializeField] private UiActionView jobDisplay;
        [SerializeField] private UiActionView legendFoundingDisplay;
        [SerializeField] private UiActionView legendTrialDisplay;

        public override void Refresh()
        {
            var s = Game.State;
            var r = s.Rules;
            if (!s.FarmAvailable)
            {
                ShowRow(firstWork, true);
                BindRow(firstWork, firstWorkDisplay, VillageQuestPage.FarmFoundation, r.Foundation.Title,
                    s.CanAdvanceEra ? "시대 전환 가능" : "진행 중", null, (int)s.FoundationStage / 3f);
                if (ShowRow(expansion, s.TimberContractActive))
                {
                    var document = Game.Settings.FindItem(r.Foundation.DocumentItemId);
                    string status = s.CanConfirmTimberDelivery ? "요구량 충족" : $"{s.TimberDelivered:N0}/{s.TimberRequired:N0}";
                    BindRow(expansion, expansionDisplay, VillageQuestPage.TimberContract,
                        document?.DisplayName ?? r.Foundation.Title, status, document?.Icon, Progress(s.TimberDelivered, s.TimberRequired));
                }
            }
            else
            {
                if (ShowRow(firstWork, IsActive(VillageQuestPage.FirstWork)))
                    BindRow(firstWork, firstWorkDisplay, VillageQuestPage.FirstWork, r.IntroTitle,
                        s.CanClaimIntroQuest ? "보고 가능" : "진행 중", null, Progress(s.Career, r.IntroRequiredJobs));
                if (ShowRow(expansion, IsActive(VillageQuestPage.Expansion)))
                    BindRow(expansion, expansionDisplay, VillageQuestPage.Expansion, r.ExpansionTitle,
                        s.FarmUpgradePending ? "시대 전환 가능" : s.CanConfirmExpansion ? "요구량 충족" : "진행 중", null,
                        (Progress(s.DonatedGold, s.ExpansionGoldRequired) + Progress(s.DonatedPotatoes, s.ExpansionItemsRequired)) * .5f);
            }
            var player = Game.LegendPlayerId;
            if (ShowRow(legendFounding, IsActive(VillageQuestPage.LegendFounding)))
                BindRow(legendFounding, legendFoundingDisplay, VillageQuestPage.LegendFounding, "전설의 나무꾼 이야기",
                    $"{s.LifetimeTimber:N0}/{r.LegendFoundingTimber:N0}", null, Progress(s.LifetimeTimber, r.LegendFoundingTimber));
            if (ShowRow(legendTrial, IsActive(VillageQuestPage.LegendTrial)))
            {
                bool guidance = s.HasLegendGuidance(player);
                BindRow(legendTrial, legendTrialDisplay, guidance ? VillageQuestPage.LegendGuidance : VillageQuestPage.LegendTrial,
                    guidance ? "나무꾼에게 돌아온 기회" : "전설의 나무꾼 시련",
                    $"남은 인정 인원 {s.LegendPlacesAvailable}명", Game.Settings.FindItem(r.LegendRewardItem)?.Icon,
                    Progress(s.Inventory.Count(ItemIds.Timber), r.LegendTrialTimber));
            }
            if (ShowRow(job, IsActive(VillageQuestPage.FarmJob)))
            {
                int amount = s.Inventory.Count(r.JobItemId);
                BindRow(job, jobDisplay, VillageQuestPage.FarmJob, r.JobTitle, $"{amount}/{s.JobRequirement}  ·  {r.JobEnd / 60:00}:{r.JobEnd % 60:00}",
                    Game.Settings.FindItem(r.JobDocumentItemId)?.Icon, Progress(amount, s.JobRequirement));
            }
        }

        private bool IsActive(VillageQuestPage page) =>
            QuestProgress.StatusFor(Game.State, page, Game.LegendPlayerId) == QuestProgressStatus.Active;

        private static bool ShowRow(UiListRow row, bool visible)
        {
            row.transform.parent.gameObject.SetActive(visible);
            return visible;
        }

        private void BindRow(UiListRow row, UiActionView display, VillageQuestPage page, string title, string details, Sprite icon, float progress)
        {
            row.Bind(title, details, icon, progress, () => Hud.OpenQuestDetail(page));
            Hud.BindQuestProgressToggle(display, page);
        }

        private static float Progress(int amount, int required) => required <= 0 ? 1f : Mathf.Clamp01((float)amount / required);
    }
}
