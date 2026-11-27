using System;
using UnityEngine;

namespace Workerborn
{
    public enum NpcActivity { Idle, Walking, Working, Harvesting, Resting, Event, Eating, Shopping }

    [Serializable]
    public sealed class NpcScheduleEntry
    {
        [SerializeField] private NpcActivity activity;
        [SerializeField, Tooltip("Facility type to find in the current village layout. Leave empty for idle time at the spawn point.")]
        private VillageFacilityType facility;
        [SerializeField] private VillageWeekdays weekdays = VillageWeekdays.All;
        [SerializeField, Range(0, 1439)] private int startMinute = 480;
        [SerializeField, Range(0, 1440)] private int endMinute = 1080;
        [SerializeField, Tooltip("Event entries take priority over the weekly routine outside the job shift.")]
        private VillageCalendarEvent calendarEvent;
        [SerializeField, Tooltip("Follow the NPC's part-time job hours and reserve this time for the workplace.")]
        private bool useJobHours;
        [SerializeField, Min(0), Tooltip("How many in-game minutes before the job opens the NPC heads to work.")]
        private int leadMinutes = 45;

        internal NpcActivity Activity => activity;
        internal VillageFacilityType Facility => facility;

        internal static NpcScheduleEntry Resolve(NpcScheduleEntry[] entries, VillageState state, PartTimeJobDefinition job)
        {
            NpcScheduleEntry weekly = null, special = null;
            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;
                if (entry.useJobHours)
                {
                    if (entry.MatchesJob(state, job))
                        return entry;
                    continue;
                }
                if (!VillageCalendar.TryGetScheduleDay(state.Day, state.Minute, entry.startMinute, entry.endMinute, out int day) ||
                    (entry.weekdays & VillageCalendar.Weekday(day)) == 0)
                    continue;
                if (entry.calendarEvent == null)
                    weekly ??= entry;
                else if (entry.calendarEvent.OccursOn(day))
                    special ??= entry;
            }
            return special ?? weekly;
        }

        private bool MatchesJob(VillageState state, PartTimeJobDefinition job)
        {
            if (job == null)
                return false;
            bool villageJob = job.Id == state.Rules.JobId;
            if (villageJob && !state.FarmAvailable)
                return false;
            int start = villageJob ? state.Rules.JobStart : job.StartMinute;
            int end = villageJob ? state.Rules.JobEnd : job.EndMinute;
            return start < end && state.Minute >= Math.Max(0, start - Math.Max(0, leadMinutes)) && state.Minute < end;
        }
    }
}
