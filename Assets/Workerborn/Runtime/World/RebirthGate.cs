using UnityEngine;

namespace Workerborn
{
    public sealed class RebirthGate : WorldInteractable
    {
        [SerializeField] private string gateName = "마을로 내려가는 문";
        public override string DisplayName => gateName;
        public override string InteractionVerb => "마을로 내려가기";
        public override bool ShowNameByDefault => true;

        protected override void Interact(HarvestWorker worker) => worker.CommandApproach(this);
        protected override void OnArrived(VillagePrototype village, HarvestWorker worker) => village.LeaveRebirthZone();
    }
}
