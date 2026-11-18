using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SkillsWindowView : VillageWindowView
    {
        [SerializeField]
        private UiListRow harvest;
        [SerializeField]
        private Button advance;
        [SerializeField]
        private Button use;
        [SerializeField]
        private TMP_Text advanceText;
        [SerializeField]
        private TMP_Text ap;
        [SerializeField]
        private TMP_Text details;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            use.onClick.AddListener(game.HarvestNearest);
            advance.onClick.AddListener(() => game.Perform(game.State.TryUpgradeSkill));
        }

        public override void Refresh()
        {
            var state = Game.State;
            string rank = new[]
            {
                "F",
                "E",
                "D",
                "C",
                "B",
                "A",
                "9",
                "8",
                "7"
            }[state.SkillRank];
            harvest.Bind("수확", $"생활  ·  랭크 {rank}", Game.Settings.FindItem(ItemIds.Hoe)?.Icon, state.SkillRank / 8f, null);
            ap.text = $"어빌리티 포인트  {state.AP}";
            advanceText.text = $"승급  AP {state.NextSkillCost}";
            advance.interactable = state.SkillRank < 8 && state.AP >= state.NextSkillCost;
            details.text = $"작업 속도 ×{state.WorkRate:0.00}\n스태미나 소모 {state.HarvestEnergyCost}\n럭키 보너스 {10 + state.SkillRank * 2}%";
        }
    }
}
