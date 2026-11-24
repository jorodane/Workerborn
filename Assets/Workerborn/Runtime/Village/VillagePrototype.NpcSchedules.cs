using System.Collections.Generic;
using UnityEngine;

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

        internal Transform FindNpcSchedulePoint(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (var point in FindObjectsByType<NpcSchedulePoint>(FindObjectsSortMode.None))
                    if (point.isActiveAndEnabled && point.gameObject.scene == gameObject.scene && point.Id == id)
                        return point.transform;
            return null;
        }
    }
}
