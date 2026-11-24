using System;

namespace Workerborn
{
    [Flags]
    public enum VillageWeekdays
    {
        Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64,
        Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekend = Saturday | Sunday,
        All = Weekdays | Weekend
    }

    internal static class VillageCalendar
    {
        internal static int WeekdayIndex(int day) => (Math.Max(1, day) - 1) % 7;
        internal static VillageWeekdays Weekday(int day) => (VillageWeekdays)(1 << WeekdayIndex(day));

        internal static bool TryGetScheduleDay(int day, float minute, int start, int end, out int scheduleDay)
        {
            scheduleDay = day;
            if (day < 1 || !(minute >= 0f && minute < 1440f) || start < 0 || start >= 1440 || end < 0 || end > 1440 || start == end)
                return false;
            if (start < end)
                return minute >= start && minute < end;
            if (minute >= start)
                return true;
            if (minute >= end || day <= 1)
                return false;
            scheduleDay = day - 1;
            return true;
        }

        internal static bool IsEventDay(int day, int firstDay, int duration, int repeatEveryDays)
        {
            long elapsed = (long)day - firstDay;
            return firstDay > 0 && elapsed >= 0 && (repeatEveryDays > 0 ? elapsed % repeatEveryDays : elapsed) < duration;
        }
    }
}
