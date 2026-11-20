using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Characters/Work Animation", fileName = "WorkAnimation")]
    public sealed class WorkAnimationDefinition : ScriptableObject
    {
        [SerializeField] private AnimationClip begin;
        [SerializeField] private AnimationClip loop;
        [SerializeField] private AnimationClip finish;

        public AnimationClip Begin => begin;
        public AnimationClip Loop => loop;
        public AnimationClip Finish => finish;
    }
}
