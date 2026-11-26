using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class CharacterWindowView : VillageWindowView
    {
        [SerializeField] private TMP_Text identity;
        [SerializeField] private TMP_Text level;
        [SerializeField] private TMP_Text health;
        [SerializeField] private TMP_Text stamina;
        [SerializeField] private TMP_Text ap;
        [SerializeField] private TMP_Text statistics;
        [SerializeField] private TMP_Text attributes;
        [SerializeField] private Image staminaBar;
        [SerializeField] private Image experienceBar;
        [SerializeField] private HealthBarView healthBar;
        [SerializeField] private Button strength;
        [SerializeField] private Button dexterity;
        [SerializeField] private Button knowledge;
        [SerializeField] private Button grit;
        [SerializeField] private Button digestion;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            strength.onClick.AddListener(() => game.Perform(() => game.State.TrySpendAttribute(LifeAttribute.Strength)));
            dexterity.onClick.AddListener(() => game.Perform(() => game.State.TrySpendAttribute(LifeAttribute.Dexterity)));
            knowledge.onClick.AddListener(() => game.Perform(() => game.State.TrySpendAttribute(LifeAttribute.Knowledge)));
            grit.onClick.AddListener(() => game.Perform(() => game.State.TrySpendAttribute(LifeAttribute.Grit)));
            digestion.onClick.AddListener(() => game.Perform(() => game.State.TrySpendAttribute(LifeAttribute.Digestion)));
        }

        public override void Refresh()
        {
            var s = Game.State;
            identity.text = $"{Game.LegendPlayerName}\n{s.Era}시대  ·  {s.Life}번째 생  ·  {s.LifeDay}일째";
            bool atLimit = s.Level == VillageState.LevelLimit;
            string experience = atLimit ? $"경험치 {s.Experience:N0}" : $"{s.Experience:N0}/{s.NextLevelExperience:N0}";
            level.text = $"레벨 {s.Level}   {experience}   ·   누적 {s.TotalLevel:N0}";
            health.text = $"생명력  {s.Health}/{s.HealthLimit}";
            RefreshStamina(s);
            ap.text = $"어빌리티 포인트  {s.AP}";
            healthBar.SetValue(s.Health, s.HealthLimit);
            experienceBar.fillAmount = atLimit ? 1f : (float)s.Experience / s.NextLevelExperience;
            attributes.text = $"배분 가능 {s.AttributePoints}   ·   힘 {s.Strength}   솜씨 {s.Dexterity}   지능 {s.Knowledge}\n근성 {s.Grit}   소화 {s.Digestion}";
            strength.interactable = dexterity.interactable = knowledge.interactable =
                grit.interactable = digestion.interactable = s.AttributePoints > 0;
            string farm = s.FarmAvailable ? $"{s.FarmLevel}단계" : "건설 예정";
            statistics.text = $"채집 경력   {s.LifetimeHarvests:N0}회     아르바이트   {s.Career:N0}회\n소지금   {s.Gold:N0} Gold     마을 농장   {farm}";
        }

        private void LateUpdate()
        {
            if (IsOpen && Game != null && Game.State is { } state)
                RefreshStamina(state);
        }

        private void RefreshStamina(VillageState state)
        {
            stamina.SetText("스태미나  {0}/{1}   최대 {2}   만복도 {3}/{4}",
                state.Energy, state.StaminaLimit, state.WellFedStaminaLimit, state.Fullness, state.FullnessLimit);
            staminaBar.fillAmount = state.StaminaFill;
        }

        private void OnDisable() => healthBar.ClearValue();
    }
}
