using UnityEngine;

namespace Workerborn
{
    [System.Serializable]
    public sealed class JobTierDefinition
    {
        [Min(0)] public int requiredCompletions;
        [Min(1)] public int quantity = 6;
        [Min(0)] public int gold = 45;
        [Min(0)] public int experience = 35;
    }

    [CreateAssetMenu(menuName = "Workerborn/Content/Part-time Job", fileName = "Job")]
    public sealed class PartTimeJobDefinition : ScriptableObject
    {
        [SerializeField] private string id = "potato_delivery";
        [SerializeField] private string title = "감자 납품";
        [SerializeField, TextArea(3, 8)] private string description;
        [SerializeField] private ItemDefinition item;
        [SerializeField, Range(0, 23)] private int startHour = 8;
        [SerializeField, Range(1, 24)] private int endHour = 18;
        [SerializeField] private JobTierDefinition[] tiers = { new JobTierDefinition() };
        [Header("Additional demand per world stage")]
        [SerializeField, Min(0)] private int extraQuantity = 4;
        [SerializeField, Min(0)] private int extraGold = 30;
        [SerializeField, Min(0)] private int extraExperience = 20;

        public string Id => id;
        public string Title => title;
        public string Description => description;
        public ItemDefinition Item => item;
        public int StartMinute => startHour * 60;
        public int EndMinute => endHour * 60;
        public JobTierDefinition[] Tiers => tiers;
        public int ExtraQuantity => extraQuantity;
        public int ExtraGold => extraGold;
        public int ExtraExperience => extraExperience;
    }
}
