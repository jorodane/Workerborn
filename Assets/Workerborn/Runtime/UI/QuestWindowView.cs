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
            firstWork.Bind(r.IntroTitle, s.IntroClaimed ? "완료" : s.Career >= r.IntroRequiredJobs ? "보고 가능" : "진행 중", null, (float)s.Career / r.IntroRequiredJobs, () => Hud.OpenQuestDetail(false));
            expansion.Bind(r.ExpansionTitle, s.FarmUpgradePending ? "완료 · 다음 생에 반영" : s.FarmLevel >= r.ExpandedStage ? "완료" : "진행 중", null, ((float)s.DonatedGold / Mathf.Max(1, r.DonationGold) + (float)s.DonatedPotatoes / Mathf.Max(1, r.DonationItems)) * .5f, () => Hud.OpenQuestDetail(true));
            job.gameObject.SetActive(s.Job == JobStatus.Active);
            if (s.Job == JobStatus.Active)
                job.Bind(r.JobTitle, $"{Game.State.Inventory.Count(r.JobItemId)}/{s.JobRequirement}  ·  {r.JobEnd / 60:00}:{r.JobEnd % 60:00}", null, (float)s.Inventory.Count(r.JobItemId) / s.JobRequirement, null);
        }
    }
}
