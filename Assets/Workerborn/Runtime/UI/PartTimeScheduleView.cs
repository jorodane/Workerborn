using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class PartTimeScheduleView : MonoBehaviour
    {
        [SerializeField] private RectTransform rows;
        [SerializeField] private PartTimeScheduleRowView rowPrefab;
        [SerializeField] private VillageMapView map;
        [SerializeField, Min(0), Tooltip("Upcoming rows on the HUD. Zero shows all jobs for the selected day.")]
        private int upcomingRows;
        [SerializeField] private Button previousDay;
        [SerializeField] private Button nextDay;
        [SerializeField] private TMP_Text dateLabel;
        [SerializeField] private TMP_Text emptyLabel;
        private const int DaysAhead = 7;
        private readonly List<PartTimeScheduleRowView> views = new List<PartTimeScheduleRowView>();
        private readonly List<Entry> jobs = new List<Entry>();
        private VillagePrototype game;
        private int dayOffset;
        private (int Day, int Minute, bool Farm, JobStatus Job, int LastJobDay, int Offset) shown;

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
            if (previousDay != null)
                previousDay.onClick.AddListener(() => TurnPage(-1));
            if (nextDay != null)
                nextDay.onClick.AddListener(() => TurnPage(1));
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

        private void OnEnable()
        {
            dayOffset = 0;
            LateUpdate();
        }

        private void TurnPage(int step)
        {
            dayOffset = Mathf.Clamp(dayOffset + step, 0, DaysAhead);
            var scroll = rows.GetComponentInParent<ScrollRect>();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 1f;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (game == null)
                return;
            var state = game.State;
            var current = (state.Day, Mathf.FloorToInt(state.Minute), state.FarmAvailable, state.Job, state.LastJobDay, dayOffset);
            if (shown == current)
                return;
            shown = current;

            int count = 0;
            bool today = dayOffset == 0;
            if (dateLabel != null)
                dateLabel.text = VillageClockView.DateLabel(state.Day + dayOffset);
            if (previousDay != null)
                previousDay.interactable = !today;
            if (nextDay != null)
                nextDay.interactable = dayOffset < DaysAhead;

            foreach (var job in jobs)
            {
                if (upcomingRows > 0 && shown.Minute >= job.End)
                    continue;
                ShowRow(count++, job, today);
                if (count == upcomingRows)
                    break;
            }
            for (int index = count; index < views.Count; index++)
                views[index].gameObject.SetActive(false);
            if (upcomingRows > 0)
                rows.gameObject.SetActive(count > 0);
            if (emptyLabel != null)
            {
                emptyLabel.text = jobs.Count == 0 ? "등록된 일정 없음" : "오늘 일정 종료";
                emptyLabel.gameObject.SetActive(count == 0);
            }
        }

        private void ShowRow(int index, Entry job, bool today)
        {
            if (index == views.Count)
            {
                var row = Instantiate(rowPrefab, rows);
                row.Bind(map);
                views.Add(row);
            }
            string hours = $"{VillageClockView.TimeLabel(job.Start)}–{VillageClockView.TimeLabel(job.End)}";
            bool current = today && shown.Minute >= job.Start && shown.Minute < job.End;
            var view = views[index];
            view.Show(job.Title, hours, Status(job, today), current, job.Workplace != null ? job.Workplace.transform : null);
            view.gameObject.SetActive(true);
        }

        private string Status(Entry job, bool today)
        {
            bool contract = string.Equals(job.Id, game.State.Rules.JobId, StringComparison.Ordinal);
            if (today && contract && shown.LastJobDay == shown.Day && shown.Job == JobStatus.Completed)
                return "완료";
            if (today && shown.Minute >= job.End)
                return "마감";
            if (contract && !shown.Farm)
                return "농장 완공 후";
            if (!today || shown.Minute < job.Start)
                return "예정";
            if (!contract)
                return "시간대";
            if (shown.LastJobDay != shown.Day)
                return "접수 중";
            return shown.Job == JobStatus.Active ? "진행 중" : "마감";
        }
    }
}
