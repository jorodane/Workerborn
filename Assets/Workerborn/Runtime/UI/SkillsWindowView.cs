using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SkillsWindowView : VillageWindowView
    {
        [SerializeField] private RectTransform skillRows;
        [SerializeField] private Button advance;
        [SerializeField] private Button use;
        [SerializeField] private TMP_Text advanceText;
        [SerializeField] private TMP_Text ap;
        [SerializeField] private TMP_Text details;

        private readonly Dictionary<SkillId, UiListRow> rows = new Dictionary<SkillId, UiListRow>();
        private SkillId selected;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            foreach (var skill in SkillProgression.All)
                rows.Add(skill, Instantiate(hud.Library.ListRow, skillRows));
            use.onClick.AddListener(() => game.UseGatheringSkill(selected));
            advance.onClick.AddListener(() => game.UpgradeSkill(selected));
        }

        public override void Refresh()
        {
            var state = Game.State;
            foreach (var pair in rows)
            {
                var skill = pair.Key;
                int rank = state.RankOf(skill);
                var icon = Game.Settings.FindSkill(skill)?.Icon;
                pair.Value.Bind(state.Rules.Skill(skill).Name, state.IsSkillLearned(skill) ? SkillProgression.RankLabel(rank) : "미습득", icon,
                    rank / (float)SkillProgression.MaxRank, () =>
                    {
                        selected = skill;
                        Refresh();
                    });
                pair.Value.HighlightTitle(skill == selected);
            }

            ap.text = $"AP  {state.AP}";
            bool learned = state.IsSkillLearned(selected);
            bool maxed = state.RankOf(selected) == SkillProgression.MaxRank;
            int cost = state.NextSkillCostFor(selected);
            advanceText.text = !learned ? "미습득" : maxed ? "최고 랭크" : $"승급  AP {cost}";
            advance.interactable = learned && !maxed && state.AP >= cost;
            use.gameObject.SetActive(SkillProgression.IsGathering(selected));
            use.interactable = learned && !state.AwaitingIncarnation;
            details.text = learned ? Describe(state, selected) : selected switch
            {
                SkillId.Harvesting => "첫 일자리 퀘스트를 완료하면 배울 수 있어요.",
                SkillId.Woodcutting => "목재 납품을 마치거나 잡화점의 벌목 입문서를 읽으면 배울 수 있어요.",
                _ => $"잡화점의 {state.Rules.Skill(selected).Name} 입문서를 읽으면 배울 수 있어요."
            };
        }

        private static string Describe(VillageState state, SkillId skill)
        {
            if (skill == SkillId.HarvestAcquisition)
            {
                int delay = state.HarvestAcquisitionDelayMilliseconds;
                return delay == 0
                    ? "수확 즉시 수확물이 소지품에 들어옵니다."
                    : $"수확 후 {delay / 1000f:0.##}초 뒤에 수확물이 소지품에 들어옵니다.";
            }

            var lines = new List<string>();
            if (SkillProgression.IsGathering(skill))
            {
                lines.Add($"획득량 ×{state.YieldMultiplierFor(skill):N0}    스태미나 절약 {state.SkillBonus(skill, SkillEffect.StaminaSaving):+0;-0;0}");
                int lucky = state.SkillBonus(skill, SkillEffect.LuckyChance);
                int great = state.SkillBonus(skill, SkillEffect.GreatLuckyChance);
                if (lucky != 0 || great != 0)
                    lines.Add($"럭키 {lucky:+0;-0;0}%p    대박 {great:+0;-0;0}%p");
            }
            else if (skill == SkillId.Carving)
                lines.Add($"갈무리 획득량 ×{state.YieldMultiplierFor(skill):N0}");

            int damage = state.SkillBonus(skill, SkillEffect.UnarmedDamage);
            int stamina = state.SkillBonus(skill, SkillEffect.MaxStamina);
            if (skill == SkillId.UnarmedCombat || damage != 0)
                lines.Add($"맨손 피해 {damage:+0;-0;0}");
            if (stamina != 0)
                lines.Add($"최대 스태미나 {stamina:+0;-0;0}");
            return string.Join("\n", lines);
        }
    }
}
