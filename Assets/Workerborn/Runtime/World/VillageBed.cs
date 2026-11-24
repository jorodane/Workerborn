using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class VillageBed : WorldInteractable
    {
        [SerializeField] private Transform approach;
        [SerializeField] private Transform pose;

        internal Transform Approach => approach;
        internal Transform Pose => pose;
        public override bool CanInteract => base.CanInteract && approach != null && pose != null;
        public override string DisplayName => "내 침대";
        public override string InteractionVerb => "잠자기";

        protected override void Interact(HarvestWorker worker) =>
            worker.CommandApproachAt(this, approach.position);

        protected override void OnArrived(VillagePrototype village, HarvestWorker worker) =>
            village.SleepInBed(worker, this, paidRoom: false);

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            base.AddContextActions(village, actions);
            actions.Add(new WorldContextAction($"푹 쉬기 ({village.State.Rules.RoomPrice} Gold)", () =>
            {
                var worker = village.Worker;
                if (BeginInteraction(worker))
                    worker.CommandApproachAt(this, approach.position, () => village.SleepInBed(worker, this, paidRoom: true));
            }));
        }
    }
}
