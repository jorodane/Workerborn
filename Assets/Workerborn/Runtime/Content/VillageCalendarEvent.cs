using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/Content/Calendar Event", fileName = "CalendarEvent")]
    public sealed class VillageCalendarEvent : ScriptableObject
    {
        [SerializeField, Min(1)] private int firstDay = 1;
        [SerializeField, Min(1)] private int durationDays = 1;
        [SerializeField, Min(0), Tooltip("0 runs the event once. Otherwise it recurs after this many world days.")]
        private int repeatEveryDays;

        internal bool OccursOn(int day) => VillageCalendar.IsEventDay(day, firstDay, durationDays, repeatEveryDays);
    }
}
