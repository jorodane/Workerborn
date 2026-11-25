using UnityEngine;

namespace Workerborn
{
    public sealed class VillageFarmSite : WorldInteractable
    {
        [SerializeField] private string siteId;
        [SerializeField] private string siteName;
        [SerializeField] private Transform farmOrigin;
        public string SiteId => siteId;
        public Transform FarmOrigin => farmOrigin != null ? farmOrigin : transform;
        public override string DisplayName => siteName;
        public override string InteractionVerb => siteName + " 지정하기";
        internal override InteractionCursor CursorFor(HarvestWorker worker) => InteractionCursor.Manage;

        protected override void Interact(HarvestWorker worker) => worker.CommandApproach(this);
        protected override void OnArrived(VillagePrototype village, HarvestWorker worker)
        {
            if (worker.IsLocallyControlled)
                village.ChooseFarmSite(this);
        }
    }
}
