using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Timber Work", fileName = "TimberAssets")]
    public sealed class TimberAssets : ScriptableObject
    {
        [SerializeField] private TimberPiece stump;
        [SerializeField] private TimberPiece lower;
        [SerializeField] private TimberPiece upper;
        [SerializeField] private TimberPiece log;
        [SerializeField] private CropDefinition felling;
        [SerializeField] private AnimationClip split;
        [SerializeField] private AnimationClip carry;
        [SerializeField] private AnimationClip toss;
        [SerializeField] private AnimationClip strike;
        [SerializeField] private Material cutSurface;

        internal CropDefinition Felling => felling;
        internal AnimationClip Split => split;
        internal AnimationClip Carry => carry;
        internal AnimationClip Toss => toss;
        internal AnimationClip Strike => strike;
        internal Material CutSurface => cutSurface;

        internal TimberPiece Prefab(TimberPart part) => part switch
        {
            TimberPart.Stump => stump,
            TimberPart.Lower or TimberPart.Trunk => lower,
            TimberPart.Upper => upper,
            _ => log
        };
    }
}
