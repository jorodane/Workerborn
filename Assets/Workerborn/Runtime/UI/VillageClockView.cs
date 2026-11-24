using TMPro;
using UnityEngine;

namespace Workerborn
{
    public sealed class VillageClockView : MonoBehaviour
    {
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text weatherText;
        private static readonly string[] Weekdays = { "월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일" };
        private VillagePrototype game;
        private int shownDay, shownMinute = -1;

        internal static string TimeLabel(int minute) => $"{minute / 60:00}:{minute % 60:00}";
        internal static string DateLabel(int day) => $"{day}일 · {Weekdays[VillageCalendar.WeekdayIndex(day)]}";

        internal void Bind(VillagePrototype owner)
        {
            game = owner;
            LateUpdate();
        }

        private void OnEnable() => LateUpdate();

        private void LateUpdate()
        {
            if (game == null)
                return;
            var state = game.State;
            int minute = Mathf.FloorToInt(state.Minute / 10f) * 10;
            if (shownMinute != minute)
            {
                shownMinute = minute;
                timeText.text = TimeLabel(minute);
            }
            if (shownDay != state.Day)
            {
                shownDay = state.Day;
                dateText.text = DateLabel(shownDay);
                weatherText.text = game.WeatherName;
            }
        }
    }
}
