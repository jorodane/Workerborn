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
        [SerializeField] private Button advanceGrade;
        [SerializeField] private Button use;
        [SerializeField] private TMP_Text advanceText;
        [SerializeField] private TMP_Text advanceGradeText;
        [SerializeField] private TMP_Text ap;
        [SerializeField] private TMP_Text details;

        private readonly Dictionary<SkillId, UiListRow> rows = new Dictionary<SkillId, UiListRow>();
        private readonly Dictionary<SkillId, SkillShortcutDrag> shortcuts = new Dictionary<SkillId, SkillShortcutDrag>();
        private SkillId selected;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            foreach (var skill in SkillProgression.All)
            {
                var row = Instantiate(hud.Library.ListRow, skillRows);
                rows.Add(skill, row);
                if (SkillProgression.CanActivate(skill))
                    shortcuts.Add(skill, row.AddSkillShortcut(hud, skill));
            }
            use.onClick.AddListener(() => game.UseSkill(selected));
            advance.onClick.AddListener(() => game.UpgradeSkill(selected));
            advanceGrade.onClick.AddListener(() => game.UpgradeSkillToNextGrade(selected));
        }

        public override void Refresh()
        {
            var state = Game.State;
            foreach (var pair in rows)
            {
                var skill = pair.Key;
                int rank = state.RankOf(skill);
                bool available = state.IsSkillLearned(skill);
                if (shortcuts.TryGetValue(skill, out var shortcut))
                    shortcut.enabled = available && !state.AwaitingIncarnation;
                var icon = Game.Settings.FindSkill(skill)?.Icon;
                pair.Value.Bind(state.Rules.Skill(skill).Name, available ? SkillProgression.RankLabel(rank) : "미습득", icon,
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
            int gradeRank = state.NextSkillGradeRankFor(selected);
            int gradeCost = state.NextSkillGradeCostFor(selected);
            advanceGradeText.text = !learned ? "다음 급수" : gradeRank == 0
                ? "최고 급수" : $"{SkillProgression.GradeAtRank(gradeRank)}급까지  AP {gradeCost}";
            advanceGrade.interactable = gradeRank > 0 && state.AP >= gradeCost;
            bool active = SkillProgression.CanActivate(selected);
            use.gameObject.SetActive(active);
            use.interactable = learned && !state.AwaitingIncarnation && (selected != SkillId.Throwing || Game.Worker.CarriedLogs > 0);
            details.text = learned ? Describe(state, selected) : selected switch
            {
                SkillId.Harvesting => "첫 일자리 퀘스트를 완료하면 배울 수 있어요.",
                SkillId.Woodcutting => "목재 납품을 마치거나 잡화점의 나무 약탈 입문서를 읽으면 배울 수 있어요.",
                SkillId.SoulGrowth => "운명의 인도자와 환생을 마치면 배울 수 있어요.",
                _ => $"잡화점의 {state.Rules.Skill(selected).Name} 입문서를 읽으면 배울 수 있어요."
            };
            if (learned && active && !state.AwaitingIncarnation)
                details.text += "\n스킬 아이콘을 단축칸에 끌어 등록할 수 있어요.";
        }

        private static string Describe(VillageState state, SkillId skill)
        {
            if (skill == SkillId.HarvestAcquisition)
            {
                return state.HasInstantHarvestAcquisition
                    ? "수확물을 즉시 얻고, 주변의 떨어진 수확물도 즉시 주웁니다."
                    : $"2m 안에 떨어진 수확물을 초당 최대 {state.HarvestItemsPerSecond:N0}개 줍습니다.";
            }
            if (skill == SkillId.LuckyBonus)
                return $"채집 성공 시 럭키 {state.LuckyChancePercent}% · 획득량 ×2\n대박 {state.GreatLuckyChancePercent}% · 획득량 ×50";
            if (skill == SkillId.SoulGrowth)
                return $"경험치 획득량 {state.ExperienceGainPercent}%";
            if (skill == SkillId.CriticalHit)
                return $"치명타 확률 {state.CriticalChance:P1} · 피해 ×{state.CriticalDamagePercent / 100f:0.##}";
            if (skill == SkillId.Woodcutting)
                return $"생산물 슬롯 +{state.TreeExtraSlots}\n작업 성공률 +{state.TreeChanceBonus / 100f:0.#}%p · 생산량 +{state.TreeYieldBonus}%";
            if (skill == SkillId.Lifting)
                return $"통나무 최대 {state.LiftCapacity}개 · 이동 속도 {state.LiftSpeedPercent}%\n사용 후 통나무를 클릭하세요. Esc로 앞에 내려놓습니다.";
            if (skill == SkillId.Throwing)
                return $"던지기 범위 {state.ThrowRange:0.#}m\n통나무를 든 채 사용하고 바닥을 클릭하세요.";
            if (skill == SkillId.RestMaster)
                return $"앉아서 초당 {state.StaminaRecoveryPerSecond(sitting: true):0.##} SP 회복\n앉아 있을 때 회복 효율 +{state.SeatedRecoveryBonus}%";

            var lines = new List<string>();
            if (SkillProgression.IsGathering(skill))
                lines.Add($"획득량 ×{state.YieldMultiplierFor(skill):N0}    스태미나 절약 {state.SkillBonus(skill, SkillEffect.StaminaSaving):+0;-0;0}");
            else if (skill == SkillId.Carving)
                lines.Add($"갈무리 획득량 ×{state.YieldMultiplierFor(skill):N0}");

            int damage = state.SkillBonus(skill, SkillEffect.UnarmedDamage);
            int stamina = state.SkillBonus(skill, SkillEffect.MaxStamina);
            int savings = state.SkillBonus(skill, SkillEffect.StaminaSaving);
            if (skill == SkillId.UnarmedCombat || damage != 0)
                lines.Add($"맨손 피해 {damage:+0;-0;0}");
            if (skill == SkillId.UnarmedCombat && savings > 0)
                lines.Add($"공격 스태미나 절약 {savings}");
            if (stamina != 0)
                lines.Add($"최대 스태미나 {stamina:+0;-0;0}");
            return string.Join("\n", lines);
        }
    }
}
