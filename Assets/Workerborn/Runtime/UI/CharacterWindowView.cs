using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class CharacterWindowView : VillageWindowView
    {
        [SerializeField]
        private Text identity;
        [SerializeField]
        private Text level;
        [SerializeField]
        private Text stamina;
        [SerializeField]
        private Text ap;
        [SerializeField]
        private Image staminaBar;
        [SerializeField]
        private Image experienceBar;
        [SerializeField]
        private Text statistics;
        public override void Refresh()
        {
            var s = Game.State;
            identity.text = $"일꾼\n{s.Life}번째 생  ·  {s.LifeDay}일째";
            level.text = $"레벨 {s.Level}  {s.Experience}/{s.NextLevelExperience}  ·  누적 {s.TotalLevel}";
            stamina.text = $"스태미나  {s.Energy}/{VillageState.MaxEnergy}";
            ap.text = $"어빌리티 포인트  {s.AP}";
            staminaBar.fillAmount = (float)s.Energy / VillageState.MaxEnergy;
            experienceBar.fillAmount = (float)s.Experience / s.NextLevelExperience;
            statistics.text = $"수확 경력      {s.LifetimeHarvests}회\n아르바이트      {s.Career}회\n작업 속도      ×{s.WorkRate:0.00}\n소지금          {s.Gold:N0} Gold\n마을 농장      {s.FarmLevel}단계";
        }
    }
}
