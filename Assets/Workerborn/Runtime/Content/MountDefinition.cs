using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Mount", fileName = "Mount")]
    public sealed class MountDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "탑승물";
        [SerializeField, Min(.1f)] private float walkSpeed = 2.4f;
        [SerializeField, Min(.1f)] private float runSpeed = 4.8f;
        [SerializeField, Min(.1f)] private float boardingReach = 2f;
        [SerializeField, Min(.1f)] private float navigationRadius = .48f;
        [SerializeField, Min(.1f)] private float dismountDistance = .95f;

        [Header("Mounted Attack")]
        [SerializeField, Min(0), Tooltip("Zero disables mounted attacks.")]
        private int attackDamage;
        [SerializeField, Min(.1f)] private float attackReach = 2.2f;
        [SerializeField, Range(.05f, .95f)] private float attackImpact = .52f;
        [SerializeField] private AnimationClip attackAnimation;

        internal string DisplayName => displayName;
        internal float BoardingReach => Mathf.Max(.1f, boardingReach);
        internal float NavigationRadius => Mathf.Max(.1f, navigationRadius);
        internal float DismountDistance => Mathf.Max(.1f, dismountDistance);
        internal float Speed(bool walk) => Mathf.Max(.1f, walk ? walkSpeed : Mathf.Max(walkSpeed, runSpeed));
        internal int AttackDamage => Mathf.Max(0, attackDamage);
        internal float AttackReach => Mathf.Max(.1f, attackReach);
        internal float AttackImpact => Mathf.Clamp(attackImpact, .05f, .95f);
        internal float AttackSeconds => Mathf.Max(.2f, attackAnimation != null ? attackAnimation.length : .45f);
    }
}
