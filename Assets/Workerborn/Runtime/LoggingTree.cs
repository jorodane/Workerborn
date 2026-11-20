using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class LoggingTree : HarvestableCrop
    {
        [SerializeField] private ItemDefinition branch;
        [SerializeField, Range(1, 8)] private int branchesPerStrike = 1;
        public ItemDefinition Branch => branch;
        internal override bool CanReceiveStrike => CanInteract && branch != null && branch.GroundPrefab != null;

        public override string InteractionVerbFor(HarvestWorker worker) =>
            StrikesByDefault(worker) ? "때리기" : InteractionVerb;

        protected override void Interact(HarvestWorker worker)
        {
            if (StrikesByDefault(worker))
                worker.CommandAttack(this);
            else
                base.Interact(worker);
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            if (CanReceiveStrike)
                actions.Add(new WorldContextAction("때리기", () =>
                {
                    if (BeginInteraction(village.Worker))
                        village.Worker.CommandAttack(this);
                }));
            actions.Add(new WorldContextAction(InteractionVerb, () =>
            {
                if (BeginInteraction(village.Worker))
                    village.Worker.CommandHarvest(this);
            }));
        }

        internal override void ReceiveStrike(HarvestWorker worker, int damage)
        {
            if (CanReceiveStrike && worker.Village != null &&
                worker.Village.DropStrikeLoot(transform, branch, Mathf.Clamp(branchesPerStrike, 1, 8)))
                NotifyInteractionCompleted();
        }

        private bool StrikesByDefault(HarvestWorker worker)
        {
            var tool = Definition?.RequiredTool;
            return CanReceiveStrike && tool != null && worker.Village != null && !worker.Village.State.HasTool(tool.Id);
        }
    }
}
