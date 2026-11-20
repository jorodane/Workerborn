using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Animal Combat", fileName = "AnimalCombat")]
    public sealed class AnimalCombatProfile : ScriptableObject
    {
        [SerializeField, Min(1)] private int health = 30;
        [SerializeField, Min(1)] private int counterDamage = 7;
        [SerializeField, Min(.1f)] private float counterDelay = 1.15f;
        [SerializeField] private ItemDefinition loot;
        [SerializeField, Range(1, 8)] private int lootCount = 2;

        public int Health => Mathf.Max(1, health);
        public int CounterDamage => Mathf.Max(1, counterDamage);
        public float CounterDelay => Mathf.Max(.1f, counterDelay);
        public ItemDefinition Loot => loot;
        public int LootCount => Mathf.Clamp(lootCount, 1, 8);
    }
}
