using UnityEngine;

namespace Workerborn
{
    public sealed class VillagePlotSign : WorldInteractable
    {
        private VillagePlot plot;
        public override string DisplayName => plot != null ? plot.DisplayName : name;
        public override bool ShowNameByDefault => true;
        public override bool CanInteract => plot != null && base.CanInteract;
        public override string InteractionVerb => "부지 관리";
        internal override InteractionCursor CursorFor(HarvestWorker worker) => InteractionCursor.Manage;

        internal void Bind(VillagePlot owner) => plot = owner;
        protected override void Interact(HarvestWorker worker) => worker.CommandApproach(this);
        protected override void OnArrived(VillagePrototype village, HarvestWorker worker)
        {
            if (worker.IsLocallyControlled)
                village.Hud.OpenPlot(plot);
        }
    }
}
