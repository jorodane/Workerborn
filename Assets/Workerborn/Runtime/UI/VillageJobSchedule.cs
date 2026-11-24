using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    internal sealed class VillageJobSchedule
    {
        private const string ReminderKey = "Workerborn.JobReminder.";
        private const float ArrivalRange = 2f;

        internal sealed class Entry
        {
            internal readonly string Id, Title;
            internal readonly int Start, End;
            internal readonly VillageInteractable Workplace;
            internal bool Reminder;
            internal int LastRemindedDay;

            internal Entry(string id, string title, int start, int end, VillageInteractable workplace)
            {
                Id = id;
                Title = title;
                Start = start;
                End = end;
                Workplace = workplace;
                Reminder = PlayerPrefs.GetInt(ReminderKey + id) == 1;
            }
        }

        private readonly VillagePrototype game;
        private readonly List<Entry> jobs;
        private Entry pinned;
        internal IReadOnlyList<Entry> Jobs => jobs;
        internal int Revision { get; private set; }
        internal Transform PinnedWorkplace => pinned?.Workplace != null ? pinned.Workplace.transform : null;

        internal VillageJobSchedule(VillagePrototype owner)
        {
            game = owner;
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
                    entries[job.Id] = new Entry(job.Id, job.Title, job.StartMinute, job.EndMinute, npc);
            }

            var rules = game.State.Rules;
            if (!string.IsNullOrWhiteSpace(rules.JobId) && rules.JobStart < rules.JobEnd)
            {
                entries.TryGetValue(rules.JobId, out var listed);
                entries[rules.JobId] = new Entry(rules.JobId, rules.JobTitle, rules.JobStart, rules.JobEnd,
                    game.FindInteraction(VillageRole.Farmer) ?? listed?.Workplace);
            }
            jobs = new List<Entry>(entries.Values);
            jobs.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Id, b.Id));
        }

        internal bool IsPinned(Entry job) => job != null && pinned == job;

        internal void Pin(Entry job)
        {
            if (job?.Workplace == null || pinned == job)
                return;
            pinned = job;
            Revision++;
        }

        internal void ToggleReminder(Entry job)
        {
            if (job == null)
                return;
            job.Reminder = !job.Reminder;
            PlayerPrefs.SetInt(ReminderKey + job.Id, job.Reminder ? 1 : 0);
            PlayerPrefs.Save();
            Revision++;
        }

        internal void Update()
        {
            var workplace = PinnedWorkplace;
            if (pinned != null && (workplace == null ||
                game.SharesMap(workplace.position) && game.Worker.InReach(workplace.position, ArrivalRange)))
            {
                pinned = null;
                Revision++;
            }

            var state = game.State;
            if (state.AwaitingIncarnation || game.IsWorldTransitioning)
                return;
            List<string> opened = null;
            foreach (var job in jobs)
            {
                if (!job.Reminder || job.LastRemindedDay == state.Day || state.Minute < job.Start || state.Minute >= job.End)
                    continue;
                if (job.Id == state.Rules.JobId && (!state.FarmAvailable ||
                    state.LastJobDay == state.Day && state.Job == JobStatus.Completed))
                    continue;
                job.LastRemindedDay = state.Day;
                (opened ??= new List<string>()).Add(job.Title);
            }
            if (opened != null)
                game.Hud.ShowToast($"{string.Join(", ", opened)} 아르바이트가 시작됐어요.");
        }
    }
}
