using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class PartTimeScheduleView : MonoBehaviour
    {
        [SerializeField] private RectTransform rows;
        [SerializeField] private PartTimeScheduleRowView rowPrefab;
        [SerializeField] private VillageMapView map;
        [SerializeField, Min(0), Tooltip("Upcoming rows on the HUD. Zero shows today and tomorrow in full.")]
        private int upcomingRows;
        private readonly List<PartTimeScheduleRowView> views = new List<PartTimeScheduleRowView>();
        private readonly List<Entry> jobs = new List<Entry>();
        private VillagePrototype game;
        private (int Day, int Minute, bool Farm, JobStatus Job, int LastJobDay) shown;

        private sealed class Entry
        {
            public string Id, Title;
            public int Start, End;
            public VillageInteractable Workplace;
        }

        internal void Bind(VillagePrototype owner)
        {
            if (game != null)
                return;
            game = owner;
            CollectJobs();
            LateUpdate();
        }

        private void CollectJobs()
        {
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var npc in game.Interactables)
            {
                if (npc == null)
                    continue;
                var job = npc.Definition?.Job;
                if (job == null || string.IsNullOrWhiteSpace(job.Id) || job.StartMinute >= job.EndMinute)
                    continue;
                if (!entries.TryGetValue(job.Id, out var existing) ||
                    (existing.Workplace.Role == VillageRole.NoticeBoard && npc.Role != VillageRole.NoticeBoard))
                {
                    entries[job.Id] = new Entry
                    {
                        Id = job.Id, Title = job.Title, Start = job.StartMinute, End = job.EndMinute, Workplace = npc
                    };
                }
            }

            var rules = game.State.Rules;
            if (!string.IsNullOrWhiteSpace(rules.JobId) && rules.JobStart < rules.JobEnd)
            {
                entries.TryGetValue(rules.JobId, out var listed);
                entries[rules.JobId] = new Entry
                {
                    Id = rules.JobId, Title = rules.JobTitle, Start = rules.JobStart, End = rules.JobEnd,
                    Workplace = game.FindInteraction(VillageRole.Farmer) ?? listed?.Workplace
                };
            }
            jobs.AddRange(entries.Values);
            jobs.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Id, b.Id));
        }

        private void OnEnable() => LateUpdate();

        private void LateUpdate()
        {
            if (game == null)
                return;
            var state = game.State;
            var current = (state.Day, Mathf.FloorToInt(state.Minute), state.FarmAvailable, state.Job, state.LastJobDay);
            if (shown == current)
                return;
            shown = current;

            int count = 0;
            if (upcomingRows == 0)
            {
                foreach (var job in jobs)
                    ShowRow(count++, job, tomorrow: false);
                foreach (var job in jobs)
                    ShowRow(count++, job, tomorrow: true);
            }
            else
            {
                int first = FirstCurrentOrUpcoming();
                for (int index = Mathf.Max(0, first); index < jobs.Count && count < upcomingRows; index++)
                {
                    var job = jobs[index];
                    if (first < 0 || shown.Minute < job.End)
                        ShowRow(count++, job, tomorrow: first < 0);
                }
            }
            for (int index = count; index < views.Count; index++)
                views[index].gameObject.SetActive(false);
        }

        private int FirstCurrentOrUpcoming()
        {
            for (int index = 0; index < jobs.Count; index++)
                if (shown.Minute < jobs[index].End)
                    return index;
            return -1;
        }

        private void ShowRow(int index, Entry job, bool tomorrow)
        {
            if (index == views.Count)
            {
                var row = Instantiate(rowPrefab, rows);
                row.Bind(map);
                views.Add(row);
            }
            string date = tomorrow ? "내일 " : upcomingRows == 0 ? "오늘 " : "";
            string hours = $"{date}{VillageClockView.TimeLabel(job.Start)}–{VillageClockView.TimeLabel(job.End)}";
            bool current = !tomorrow && shown.Minute >= job.Start && shown.Minute < job.End;
            var view = views[index];
            view.Show(job.Title, hours, Status(job, tomorrow), current, job.Workplace != null ? job.Workplace.transform : null);
            view.gameObject.SetActive(true);
        }

        private string Status(Entry job, bool tomorrow)
        {
            bool contract = string.Equals(job.Id, game.State.Rules.JobId, StringComparison.Ordinal);
            if (!tomorrow && contract && shown.LastJobDay == shown.Day && shown.Job == JobStatus.Completed)
                return "완료";
            if (!tomorrow && shown.Minute >= job.End)
                return "마감";
            if (contract && !shown.Farm)
                return "농장 완공 후";
            if (tomorrow || shown.Minute < job.Start)
                return "예정";
            if (!contract)
                return "시간대";
            if (shown.LastJobDay != shown.Day)
                return "접수 중";
            return shown.Job == JobStatus.Active ? "진행 중" : "마감";
        }
    }
}
