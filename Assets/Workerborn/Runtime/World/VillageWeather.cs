using System;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
using UnityEngine;

namespace Workerborn
{
    public sealed class VillageWeather : MonoBehaviour
    {
        public const int SkyLayer = 12;

        [Serializable]
        private struct WeatherChoice
        {
            public WeatherProfile profile;
            [Min(0)] public int weight;
        }

        [SerializeField] private CozyWeather sphere;
        [SerializeField] private int seed = 125;
        [SerializeField] private WeatherChoice[] forecast;

        private int appliedDay;

        public WeatherProfile ProfileForDay(int day)
        {
            if (forecast == null)
                return null;

            double total = 0;
            foreach (var choice in forecast)
                if (choice.profile != null && choice.weight > 0)
                    total += choice.weight;

            if (total <= 0)
                return null;

            double roll = DayRoll(day) * total;
            foreach (var choice in forecast)
                if (choice.profile != null && choice.weight > 0)
                {
                    roll -= choice.weight;
                    if (roll < 0)
                        return choice.profile;
                }

            return null;
        }

        private double DayRoll(int day)
        {
            // Mix the saved date and the authored seed without touching the game's random state.
            unchecked
            {
                uint hash = (uint)seed + (uint)day * 0x9e3779b9u;
                hash = (hash ^ (hash >> 16)) * 0x85ebca6bu;
                hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
                return (hash ^ (hash >> 16)) / 4294967296d;
            }
        }

        public void SetDate(int day, float minute)
        {
            if (sphere == null || !sphere.isActiveAndEnabled || sphere.timeModule == null)
                return;

            var time = sphere.timeModule;
            var clock = time.currentTime ??= new MeridiemTime();
            int seconds = Mathf.FloorToInt(Mathf.Repeat(minute, 1440) * 60f);
            clock.hours = seconds / 3600;
            clock.minutes = seconds / 60 % 60;
            clock.seconds = seconds % 60;
            clock.milliseconds = 0;

            if (appliedDay == day)
                return;

            appliedDay = day;
            int yearLength = Mathf.Max(1, time.DaysPerYear);
            time.currentDay = (day - 1) % yearLength;
            time.currentYear = (day - 1) / yearLength;

            var ecosystem = sphere.weatherModule?.ecosystem;
            var selected = ProfileForDay(day);
            if (ecosystem != null && selected != null && ecosystem.currentWeather != selected)
                ecosystem.SetWeather(selected, 0f);
        }
    }
}
