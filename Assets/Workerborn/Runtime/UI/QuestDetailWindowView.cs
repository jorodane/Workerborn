using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class QuestDetailWindowView : VillageWindowView
    {
        [SerializeField]
        private Text description;
        [SerializeField]
        private Text rewards;
        [SerializeField]
        private Text objectives;
        private bool expansion;
        public void Select(bool world)
        {
            expansion = world;
            Refresh();
        }

        public override void Refresh()
        {
            var s = Game.State;
            var r = s.Rules;
            description.text = expansion ? r.ExpansionTitle + "\n\n" + r.ExpansionDescription : r.IntroTitle + "\n\n" + r.IntroDescription;
            rewards.text = expansion ? "다음 생에 농장 확장" : $"보상\n{r.IntroGold} Gold\nAP {r.IntroAP}\n경험치 {r.IntroExperience}";
            objectives.text = expansion ? $"기부금  {s.DonatedGold}/{r.DonationGold}G\n\n{ItemCatalog.Get(r.DonationItemId).Name}  {s.DonatedPotatoes}/{r.DonationItems}\n\n" + (s.FarmUpgradePending ? "[완료] 다음 환생에 반영" : "[진행 중]") : $"농장 아르바이트 완료  {Mathf.Min(s.Career, r.IntroRequiredJobs)}/{r.IntroRequiredJobs}\n\n" + (s.IntroClaimed ? "[완료] 보상 수령" : s.Career >= r.IntroRequiredJobs ? "미라와 대화해 보고" : "[진행 중]");
        }
    }
}
