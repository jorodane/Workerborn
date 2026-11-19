using UnityEngine;
using UnityEngine.UI;

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
        public override void Refresh()
        {
            var s = Game.State;
            var r = s.Rules;
            firstWork.gameObject.SetActive(!s.FarmAvailable || s.IntroAccepted);
            if (!s.FarmAvailable)
            {
                firstWork.Bind(r.Foundation.Title, s.CanRebirth ? "환생 준비" : "진행 중", null,
                    (int)s.FoundationStage / 3f, () => Hud.OpenQuestDetail(VillageQuestPage.FarmFoundation));
                bool hasDocument = s.FoundationStage >= FarmFoundationStage.CollectingTimber;
                expansion.gameObject.SetActive(hasDocument);
                if (hasDocument)
                {
                    bool delivered = s.CanSelectFarmSite;
                    int amount = s.Inventory.Count(r.Foundation.TimberItemId);
                    var document = Game.Settings.FindItem(r.Foundation.DocumentItemId);
                    expansion.Bind(document?.DisplayName ?? r.Foundation.Title, delivered ? "납품 완료" : $"{amount}/{r.Foundation.TimberCount}", document?.Icon,
                        delivered ? 1 : (float)amount / r.Foundation.TimberCount, () => Hud.OpenQuestDetail(VillageQuestPage.TimberContract));
                }
            }
            else
            {
                firstWork.Bind(r.IntroTitle, s.IntroClaimed ? "완료" : s.Career >= r.IntroRequiredJobs ? "보고 가능" : "진행 중", null, (float)s.Career / r.IntroRequiredJobs, () => Hud.OpenQuestDetail(false));
                expansion.gameObject.SetActive(s.IntroClaimed);
                expansion.Bind(r.ExpansionTitle, s.FarmUpgradePending ? "완료 · 다음 생에 반영" : s.FarmLevel >= r.ExpandedStage ? "완료" : "진행 중", null, ((float)s.DonatedGold / Mathf.Max(1, r.DonationGold) + (float)s.DonatedPotatoes / Mathf.Max(1, r.DonationItems)) * .5f, () => Hud.OpenQuestDetail(true));
            }
            job.gameObject.SetActive(s.Job != JobStatus.None);
            if (s.Job != JobStatus.None)
            {
                int amount = s.Inventory.Count(r.JobItemId);
                string status = s.Job == JobStatus.Completed ? "완료" : s.Job == JobStatus.Failed ? "기간 만료" : $"{amount}/{s.JobRequirement}  ·  {r.JobEnd / 60:00}:{r.JobEnd % 60:00}";
                job.Bind(r.JobTitle, status, Game.Settings.FindItem(r.JobDocumentItemId)?.Icon,
                    s.Job == JobStatus.Completed ? 1 : (float)amount / Mathf.Max(1, s.JobRequirement), () => Hud.OpenQuestDetail(VillageQuestPage.FarmJob));
            }
        }
    }
}
