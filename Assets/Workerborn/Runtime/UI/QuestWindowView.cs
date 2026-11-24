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

        public override void Refresh()
        {
            var s = Game.State;
            var r = s.Rules;
            if (!s.FarmAvailable)
            {
                firstWork.gameObject.SetActive(true);
                firstWork.Bind(r.Foundation.Title, s.CanAdvanceEra ? "시대 전환 가능" : "진행 중", null,
                    (int)s.FoundationStage / 3f, () => Hud.OpenQuestDetail(VillageQuestPage.FarmFoundation));
                expansion.gameObject.SetActive(s.TimberContractActive);
                if (s.TimberContractActive)
                {
                    var document = Game.Settings.FindItem(r.Foundation.DocumentItemId);
                    string status = s.CanConfirmTimberDelivery ? "요구량 충족" : $"{s.TimberDelivered:N0}/{s.TimberRequired:N0}";
                    expansion.Bind(document?.DisplayName ?? r.Foundation.Title, status, document?.Icon,
                        Progress(s.TimberDelivered, s.TimberRequired), () => Hud.OpenQuestDetail(VillageQuestPage.TimberContract));
                }
            }
            else
            {
                firstWork.gameObject.SetActive(s.IntroAccepted && !s.IntroClaimed);
                if (firstWork.gameObject.activeSelf)
                    firstWork.Bind(r.IntroTitle, s.CanClaimIntroQuest ? "보고 가능" : "진행 중", null,
                        (float)s.Career / r.IntroRequiredJobs, () => Hud.OpenQuestDetail(false));
                expansion.gameObject.SetActive(s.ExpansionQuestActive);
                if (s.ExpansionQuestActive)
                    expansion.Bind(r.ExpansionTitle, s.CanConfirmExpansion ? "요구량 충족" : "진행 중", null,
                        (Progress(s.DonatedGold, s.ExpansionGoldRequired) + Progress(s.DonatedPotatoes, s.ExpansionItemsRequired)) * .5f,
                        () => Hud.OpenQuestDetail(true));
            }
            var player = Game.LegendPlayerId;
            legendFounding.gameObject.SetActive(!s.HasFoundedLegend(player));
            if (legendFounding.gameObject.activeSelf)
                legendFounding.Bind("전설의 나무꾼 이야기", $"{s.LifetimeTimber:N0}/{r.LegendFoundingTimber:N0}", null,
                    (float)s.LifetimeTimber / r.LegendFoundingTimber, () => Hud.OpenQuestDetail(VillageQuestPage.LegendFounding));
            legendTrial.gameObject.SetActive(s.Legend.founders.Length > 0 && !s.HasProvenLegend(player));
            if (legendTrial.gameObject.activeSelf)
            {
                bool guidance = s.HasLegendGuidance(player);
                legendTrial.Bind(guidance ? "나무꾼에게 돌아온 기회" : "전설의 나무꾼 시련",
                    $"남은 인정 인원 {s.LegendPlacesAvailable}명", Game.Settings.FindItem(r.LegendRewardItem)?.Icon,
                    (float)s.Inventory.Count(ItemIds.Timber) / r.LegendTrialTimber,
                    () => Hud.OpenQuestDetail(guidance ? VillageQuestPage.LegendGuidance : VillageQuestPage.LegendTrial));
            }
            job.gameObject.SetActive(s.Job == JobStatus.Active);
            if (s.Job == JobStatus.Active)
            {
                int amount = s.Inventory.Count(r.JobItemId);
                job.Bind(r.JobTitle, $"{amount}/{s.JobRequirement}  ·  {r.JobEnd / 60:00}:{r.JobEnd % 60:00}",
                    Game.Settings.FindItem(r.JobDocumentItemId)?.Icon, (float)amount / Mathf.Max(1, s.JobRequirement),
                    () => Hud.OpenQuestDetail(VillageQuestPage.FarmJob));
            }
        }

        private static float Progress(int amount, int required) => required <= 0 ? 1f : Mathf.Clamp01((float)amount / required);
    }
}
