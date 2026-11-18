using UnityEngine;

namespace Workerborn
{
    public class MonsterInteractable : WorldInteractable
    {
        [SerializeField]
        private string displayName = "몬스터";
        [SerializeField, Min(.5f)]
        private float reach = 1.8f;
        public override string DisplayName => displayName;
        public override bool CanTarget => true;
        public override float InteractionDistance => reach;
        public override string InteractionVerb => "다가가기";

        protected override void Interact(HarvestWorker worker) => worker.CommandApproach(this);
    // Override OnArrived for combat; the shared completion event is also exposed in the prefab.
    }
}
