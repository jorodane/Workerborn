using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class QuestDetailWindowView : VillageWindowView
    {
        [SerializeField]
        private TMP_Text description;
        [SerializeField]
        private TMP_Text rewards;
        [SerializeField]
        private TMP_Text objectives;
        private VillageQuestPage page;
        public void Select(VillageQuestPage selected)
        {
            page = selected;
            Refresh();
        }

        public override void Refresh()
        {
            var s = Game.State;
            var r = s.Rules;
            if (page == VillageQuestPage.FarmJob)
            {
                description.text = r.JobTitle + "\n\n" + r.JobDescription;
                rewards.text = $"보수\n{s.JobReward} Gold\n경험치 {s.JobExperience}";
                if (s.JobRewardItemCount > 0)
                    rewards.text += $"\n{ItemCatalog.Get(s.JobRewardItemId).Name} ×{s.JobRewardItemCount}";
                string status = s.Job == JobStatus.Completed ? "납품 완료" : s.Job == JobStatus.Failed ? "기간 만료" : "미라와 대화해 납품하세요.";
                int progress = s.Job == JobStatus.Completed ? s.JobRequirement : s.Inventory.Count(r.JobItemId);
                objectives.text = $"{ItemCatalog.Get(r.JobItemId).Name}  {progress}/{s.JobRequirement}\n\n{s.LastJobDay}일  {r.JobStart / 60:00}:{r.JobStart % 60:00} ~ {r.JobEnd / 60:00}:{r.JobEnd % 60:00}\n\n{status}";
                return;
            }
            if (r.Foundation != null && (page == VillageQuestPage.FarmFoundation || page == VillageQuestPage.TimberContract))
            {
                var quest = r.Foundation;
                bool delivered = s.FoundationStage >= FarmFoundationStage.SelectingSite;
                if (page == VillageQuestPage.TimberContract)
                {
                    description.text = (Game.Settings.FindItem(quest.DocumentItemId)?.DisplayName ?? quest.Title) + "\n\n농장 건설에 필요한 목재를 미라에게 납품하세요.";
                    rewards.text = $"보상\nAP {quest.RewardAP}\n경험치 {quest.RewardExperience}";
                    int progress = delivered ? quest.TimberCount : s.Inventory.Count(quest.TimberItemId);
                    objectives.text = $"{ItemCatalog.Get(quest.TimberItemId).Name}  {progress}/{quest.TimberCount}\n\n" + (delivered ? "납품 완료" : "달걀을 팔아 도끼를 구하고 주무기에 장착하세요.");
                }
                else
                {
                    description.text = quest.Title + "\n\n" + quest.Description;
                    rewards.text = "다음 생에 농장 완공";
                    objectives.text = s.FoundationStage switch
                    {
                        FarmFoundationStage.WaitingForContract => "미라와 대화해 목재 계약서를 받으세요.",
                        FarmFoundationStage.CollectingTimber => "계약서에 적힌 목재를 모아 미라에게 납품하세요.",
                        FarmFoundationStage.SelectingSite => "농장 후보지의 팻말을 클릭해 부지를 정하세요.",
                        FarmFoundationStage.ReadyToRebirth => Game.FarmSiteName + "\n\n기억의 우물에서 다음 삶을 준비하세요.",
                        _ => "농장이 완공되었습니다."
                    };
                }
                return;
            }
            bool expansion = page == VillageQuestPage.Expansion;
            description.text = expansion ? r.ExpansionTitle + "\n\n" + r.ExpansionDescription : r.IntroTitle + "\n\n" + r.IntroDescription;
            rewards.text = expansion ? "다음 생에 농장 확장" : $"보상\n{r.IntroGold} Gold\nAP {r.IntroAP}\n경험치 {r.IntroExperience}";
            objectives.text = expansion ? $"기부금  {s.DonatedGold}/{r.DonationGold}G\n\n{ItemCatalog.Get(r.DonationItemId).Name}  {s.DonatedPotatoes}/{r.DonationItems}\n\n" + (s.FarmUpgradePending ? "[완료] 다음 환생에 반영" : "[진행 중]") : $"농장 아르바이트 완료  {Mathf.Min(s.Career, r.IntroRequiredJobs)}/{r.IntroRequiredJobs}\n\n" + (s.IntroClaimed ? "[완료] 보상 수령" : s.Career >= r.IntroRequiredJobs ? "미라와 대화해 보고" : "[진행 중]");
        }
    }
}
