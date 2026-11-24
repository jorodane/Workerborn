using System.Collections.Generic;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        private readonly List<NpcRoutine> npcRoutines = new List<NpcRoutine>();
        internal bool CanRunNpcSchedules => initialized && !IsWorldTransitioning;

        internal void BindNpcCharacter(CharacterActor actor)
        {
            var npc = actor.GetComponent<VillageInteractable>();
            bool scheduled = npc != null && npc.Definition != null && npc.Definition.DailySchedule.Length > 0;
            BindCharacter(actor, idleOnly: !scheduled);
            if (!scheduled)
                return;

            var routine = actor.GetComponent<NpcRoutine>();
            if (routine != null)
                return;
            routine = actor.gameObject.AddComponent<NpcRoutine>();
            routine.Bind(this, npc, actor.GetComponent<WorkerAnimation>());
            npc.Routine = routine;
            npcRoutines.RemoveAll(entry => entry == null);
            npcRoutines.Add(routine);
        }
    }
}
