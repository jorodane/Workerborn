using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Quests/Woodcutting Legend", fileName = "WoodcuttingLegend")]
    public sealed class WoodcuttingLegendDefinition : ScriptableObject
    {
        [SerializeField, Min(1)] private int foundingTimber = 100;
        [SerializeField, Min(1)] private int maturationDays = 7;
        [SerializeField, Min(1)] private int trialTimber = 25;
        [SerializeField] private ItemDefinition reward;

        internal void Configure(VillageRules rules)
        {
            rules.LegendFoundingTimber = Mathf.Max(1, foundingTimber);
            rules.LegendMaturationDays = Mathf.Max(1, maturationDays);
            rules.LegendTrialTimber = Mathf.Max(1, trialTimber);
            rules.LegendRewardItem = reward != null ? reward.Id : ItemIds.LegendAxe;
        }
    }
}
