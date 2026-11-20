using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class CharacterWindowView : VillageWindowView
    {
        [SerializeField]
        private TMP_Text identity;
        [SerializeField]
        private TMP_Text level;
        [SerializeField]
        private TMP_Text stamina;
        [SerializeField]
        private TMP_Text ap;
        [SerializeField]
        private Image staminaBar;
        [SerializeField]
        private Image experienceBar;
        [SerializeField]
        private TMP_Text statistics;
        public override void Refresh()
        {
            var s = Game.State;
            identity.text = $"일꾼\n{s.Life}번째 생  ·  {s.LifeDay}일째";
            level.text = $"레벨 {s.Level}  {s.Experience}/{s.NextLevelExperience}  ·  누적 {s.TotalLevel}";
            stamina.text = $"스태미나  {s.Energy}/{s.StaminaLimit}";
            ap.text = $"어빌리티 포인트  {s.AP}";
            staminaBar.fillAmount = (float)s.Energy / s.StaminaLimit;
            experienceBar.fillAmount = (float)s.Experience / s.NextLevelExperience;
            string farm = s.FarmAvailable ? $"{s.FarmLevel}단계" : "건설 예정";
            statistics.text = $"채집 경력      {s.LifetimeHarvests}회\n아르바이트      {s.Career}회\n채집량          ×{s.YieldMultiplier}\n소지금          {s.Gold:N0} Gold\n마을 농장      {farm}";
        }
    }
}
