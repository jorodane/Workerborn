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
        private VillagePrototype game;
        private VillageJobSchedule schedule;
        private int dayOffset;
        private (int Day, int Minute, bool Farm, JobStatus Job, int LastJobDay, int Offset, int Revision) shown;

        internal void Bind(VillagePrototype owner)
        {
            if (game != null)
                return;
            game = owner;
            schedule = owner.Hud.Jobs;
            if (previousDay != null)
                previousDay.onClick.AddListener(() => TurnPage(-1));
            if (nextDay != null)
                nextDay.onClick.AddListener(() => TurnPage(1));
            LateUpdate();
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
            var current = (state.Day, Mathf.FloorToInt(state.Minute), state.FarmAvailable, state.Job, state.LastJobDay, dayOffset, schedule.Revision);
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

            foreach (var job in schedule.Jobs)
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
                emptyLabel.text = schedule.Jobs.Count == 0 ? "등록된 일정 없음" : "오늘 일정 종료";
                emptyLabel.gameObject.SetActive(count == 0);
            }
        }

        private void ShowRow(int index, VillageJobSchedule.Entry job, bool today)
        {
            if (index == views.Count)
            {
                var row = Instantiate(rowPrefab, rows);
                row.Bind(map, schedule);
                views.Add(row);
            }
            string hours = $"{VillageClockView.TimeLabel(job.Start)}–{VillageClockView.TimeLabel(job.End)}";
            bool current = today && shown.Minute >= job.Start && shown.Minute < job.End;
            var view = views[index];
            view.Show(job, hours, Status(job, today), current);
            view.gameObject.SetActive(true);
        }

        private string Status(VillageJobSchedule.Entry job, bool today)
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
