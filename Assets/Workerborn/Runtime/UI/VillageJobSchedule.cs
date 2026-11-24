using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    internal sealed class VillageJobSchedule
    {
        private const string ReminderKey = "Workerborn.JobReminder.";

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
        internal VillageInteractable PinnedProvider => pinned != null && IsOpenNow(pinned) ? pinned.Workplace : null;

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

        internal void TogglePin(Entry job)
        {
            if (job?.Workplace == null)
                return;
            pinned = pinned == job ? null : job;
            if (pinned != null)
                game.Hud.ClearQuestTracking();
            Revision++;
        }

        internal void ClearPin()
        {
            if (pinned == null)
                return;
            pinned = null;
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
            if (pinned != null && pinned.Workplace == null)
                ClearPin();

            var state = game.State;
            if (state.AwaitingIncarnation || game.IsWorldTransitioning)
                return;
            List<string> opened = null;
            foreach (var job in jobs)
            {
                if (!job.Reminder || job.LastRemindedDay == state.Day || !IsOpenNow(job))
                    continue;
                job.LastRemindedDay = state.Day;
                (opened ??= new List<string>()).Add(job.Title);
            }
            if (opened != null)
                game.Hud.ShowToast($"{string.Join(", ", opened)} 아르바이트가 시작됐어요.");
        }

        private bool IsOpenNow(Entry job)
        {
            var state = game.State;
            return state.Minute >= job.Start && state.Minute < job.End &&
                (job.Id != state.Rules.JobId || state.FarmAvailable &&
                (state.LastJobDay != state.Day || state.Job is not (JobStatus.Completed or JobStatus.Failed)));
        }
    }
}
