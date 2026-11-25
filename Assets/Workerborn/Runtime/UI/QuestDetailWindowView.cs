using TMPro;
using UnityEngine;

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
        [SerializeField] private UiActionView tracking;
        private VillageQuestPage page;
        public void Select(VillageQuestPage selected)
        {
            page = selected;
            Refresh();
        }

        public override void Refresh()
        {
            RefreshTracking();
            var s = Game.State;
            var r = s.Rules;
            if (page == VillageQuestPage.LegendFounding)
            {
                description.text = "전설의 나무꾼 이야기\n\n여러 생애에 걸쳐 목재를 벌목하고 기억의 우물에 업적을 남기세요.";
                objectives.text = $"누적 벌목  {s.LifetimeTimber:N0}/{r.LegendFoundingTimber:N0}\n" +
                    (s.HasFoundedLegend(Game.LegendPlayerId) ? "전설 기록 완료" : "기억의 우물에서 전설을 기록하세요.");
                rewards.text = $"인게임 {r.LegendMaturationDays}일이 지나거나 시대가 바뀌면 시련을 열어요.";
                return;
            }
            if (page == VillageQuestPage.LegendTrial || page == VillageQuestPage.LegendGuidance)
            {
                description.text = page == VillageQuestPage.LegendGuidance
                    ? "나무꾼에게 돌아온 기회\n\n이 생에서 남긴 이야기가 전설이 됐어요. 기억의 우물에서 지금 시련에 도전할 수 있어요."
                    : "전설의 나무꾼 시련\n\n목재를 모아 기억의 우물에서 실력을 증명하세요.";
                objectives.text = $"소지 목재  {s.Inventory.Count(ItemIds.Timber):N0}/{r.LegendTrialTimber:N0}\n남은 인정 인원  {s.LegendPlacesAvailable}명";
                rewards.text = "보상\n" + ItemCatalog.Get(r.LegendRewardItem).Name;
                return;
            }
            if (page == VillageQuestPage.FarmJob)
            {
                description.text = r.JobTitle + "\n\n" + r.JobDescription;
                rewards.text = $"보수\n{s.JobReward} Gold\n경험치 {s.ScaledExperience(s.JobExperience)}";
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
                    rewards.text = $"보상\n{quest.RewardGold} Gold\nAP {quest.RewardAP}\n경험치 {s.ScaledExperience(quest.RewardExperience)}";
                    objectives.text = $"누적 납품  {s.TimberDelivered:N0}/{s.TimberRequired:N0}\n\n" +
                        (delivered ? "납품 완료" : s.CanConfirmTimberDelivery ? "요구량을 채웠어요. 미라에게 납품 완료를 확인하세요." : "달걀을 팔아 도끼를 구하고 주무기에 장착하세요.");
                }
                else
                {
                    description.text = quest.Title + "\n\n" + quest.Description;
                    rewards.text = "시대 전환 후 농장 완공";
                    objectives.text = s.FoundationStage switch
                    {
                        FarmFoundationStage.WaitingForContract => "미라와 대화해 목재 계약서를 받으세요.",
                        FarmFoundationStage.CollectingTimber => s.CanConfirmTimberDelivery ? "미라에게 납품 완료를 확인하세요." : "계약서에 적힌 목재를 모아 미라에게 납품하세요.",
                        FarmFoundationStage.SelectingSite => "농장 후보지의 팻말을 클릭해 부지를 정하세요.",
                        FarmFoundationStage.ReadyToRebirth => Game.FarmSiteName + "\n\n기억의 우물에서 시대를 전환하세요.",
                        _ => "농장이 완공되었습니다."
                    };
                }
                return;
            }
            bool expansion = page == VillageQuestPage.Expansion;
            description.text = expansion ? r.ExpansionTitle + "\n\n" + r.ExpansionDescription : r.IntroTitle + "\n\n" + r.IntroDescription;
            rewards.text = expansion
                ? $"시대 전환 후 농장 확장\n{r.ExpansionRewardGold} Gold\nAP {r.ExpansionRewardAP}\n경험치 {s.ScaledExperience(r.ExpansionRewardExperience)}"
                : $"보상\n{r.IntroGold} Gold\nAP {r.IntroAP}\n경험치 {s.ScaledExperience(r.IntroExperience)}";
            objectives.text = expansion
                ? $"기부금  {s.DonatedGold:N0}/{s.ExpansionGoldRequired:N0}G\n\n{ItemCatalog.Get(r.DonationItemId).Name}  {s.DonatedPotatoes:N0}/{s.ExpansionItemsRequired:N0}\n\n" +
                    (s.FarmUpgradePending ? "[완료] 시대 전환 가능" : s.CanConfirmExpansion ? "요구량을 채웠어요. 확장 준비를 확인하세요." : "[진행 중]")
                : $"농장 아르바이트 완료  {Mathf.Min(s.Career, r.IntroRequiredJobs)}/{r.IntroRequiredJobs}\n\n" +
                    (s.IntroClaimed ? "[완료] 보상 수령" : s.CanClaimIntroQuest ? "미라와 대화해 보고" : "[진행 중]");
        }

        internal void RefreshTracking()
        {
            bool selected = Hud.IsTrackingQuest(page);
            tracking.Bind(selected ? "추적 해제" : "추적하기", () => Hud.ToggleQuestTracking(page),
                selected || Game.CanTrackQuest(page));
        }
    }
}
