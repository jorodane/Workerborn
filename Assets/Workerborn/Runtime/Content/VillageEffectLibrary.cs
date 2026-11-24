using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public enum VillageEffectKind
    {
        [InspectorName("월드 클릭")] WorldClick = 0,
        [InspectorName("아이템 착지")] DropLanding = 1,
        [InspectorName("획득 도착")] PickupArrival = 2,
        [InspectorName("레벨업")] LevelUp = 3,
        [InspectorName("드롭 트레일")] DropTrail = 4,
        [InspectorName("획득 트레일")] PickupTrail = 5,
        [InspectorName("시체 소멸")] CorpseDespawn = 6,
        [InspectorName("전투 타격")] CombatHit = 7
    }

    [Serializable]
    public struct VillageEffectEntry
    {
        public VillageEffectKind effect;
        public GameObject prefab;
        [Min(0)] public int prewarm;
        [Min(.05f)] public float lifetime;
    }

    [CreateAssetMenu(menuName = "Workerborn/Content/Village Effects", fileName = "VillageEffects")]
    public sealed class VillageEffectLibrary : ScriptableObject
    {
        [SerializeField] private VillageEffectEntry[] effects;
        public IReadOnlyList<VillageEffectEntry> Effects => effects ?? Array.Empty<VillageEffectEntry>();

        public static bool UsesTrail(VillageEffectKind effect) =>
            effect == VillageEffectKind.DropTrail || effect == VillageEffectKind.PickupTrail;
    }
}
