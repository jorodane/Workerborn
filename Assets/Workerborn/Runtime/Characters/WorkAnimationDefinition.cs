using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Characters/Work Animation", fileName = "WorkAnimation")]
    public sealed class WorkAnimationDefinition : ScriptableObject
    {
        [SerializeField] private AnimationClip begin;
        [SerializeField] private AnimationClip loop;
        [SerializeField] private AnimationClip finish;
        [SerializeField, Range(0f, 1f), Tooltip("Normalized contact time in each loop. Zero disables impacts.")]
        private float loopImpact;

        public AnimationClip Begin => begin;
        public AnimationClip Loop => loop;
        public AnimationClip Finish => finish;
        public float LoopImpact => loopImpact;
    }
}
