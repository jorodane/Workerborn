using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class AnimalNpc : HarvestableCrop
    {
        [SerializeField] private string inspectMessage = "암탉이 모이를 쪼고 있어요.";
        public override bool CanTarget => true;
        public override bool ShowNameByDefault => true;
        public override bool CanInteract => isActiveAndEnabled;
        public override string InteractionVerb => IsDepleted ? "살펴보기" : base.InteractionVerb;

        protected override void Interact(HarvestWorker worker)
        {
            if (IsDepleted)
                Inspect(worker);
            else
                base.Interact(worker);
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            base.AddContextActions(village, actions);
            if (!IsDepleted)
                actions.Add(new WorldContextAction("살펴보기", () => Inspect(village.Worker)));
        }

        private void Inspect(HarvestWorker worker)
        {
            if (worker.Village != null)
                worker.CommandApproach(this, () => worker.Village.Hud.ShowToast(inspectMessage));
        }
    }
}
