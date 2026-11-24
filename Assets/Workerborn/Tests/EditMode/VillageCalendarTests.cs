using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class VillageCalendarTests
    {
        [Test]
        public void OvernightScheduleUsesTheWeekdayItStartedOn()
        {
            Assert.That(VillageCalendar.Weekday(1), Is.EqualTo(VillageWeekdays.Monday));
            Assert.That(VillageCalendar.Weekday(6), Is.EqualTo(VillageWeekdays.Saturday));
            Assert.That(VillageCalendar.Weekday(8), Is.EqualTo(VillageWeekdays.Monday));
            Assert.That(VillageCalendar.TryGetScheduleDay(6, 420, 1080, 480, out int entered), Is.True);
            Assert.That(VillageCalendar.Weekday(entered), Is.EqualTo(VillageWeekdays.Friday));
            Assert.That(VillageCalendar.TryGetScheduleDay(6, 480, 1080, 480, out _), Is.False);
            Assert.That(VillageCalendar.TryGetScheduleDay(6, 1080, 1080, 480, out entered), Is.True);
            Assert.That(entered, Is.EqualTo(6));
        }

        [Test]
        public void EventsUseTheirDatesAndCanRepeat()
        {
            Assert.That(VillageCalendar.IsEventDay(3, 4, 2, 7), Is.False);
            Assert.That(VillageCalendar.IsEventDay(4, 4, 2, 7), Is.True);
            Assert.That(VillageCalendar.IsEventDay(5, 4, 2, 7), Is.True);
            Assert.That(VillageCalendar.IsEventDay(6, 4, 2, 7), Is.False);
            Assert.That(VillageCalendar.IsEventDay(11, 4, 2, 7), Is.True);
            Assert.That(VillageCalendar.IsEventDay(11, 4, 2, 0), Is.False);
        }
    }
}
