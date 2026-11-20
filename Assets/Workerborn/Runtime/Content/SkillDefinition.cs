using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Skill", fileName = "Skill")]
    public sealed class SkillDefinition : ScriptableObject
    {
        [SerializeField] private SkillId skill;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField, Min(0)] private int firstUpgradeCost = 1;
        [SerializeField, Min(0)] private int costIncreasePerRank = 1;
        [SerializeField] private SkillRankEffect[] rankEffects;

        public SkillId Id => skill;
        public Sprite Icon => icon;

        public SkillProgression ToProgression() => new SkillProgression(skill,
            string.IsNullOrWhiteSpace(displayName) ? SkillProgression.Default(skill).Name : displayName,
            rankEffects, firstUpgradeCost, costIncreasePerRank);
    }
}
