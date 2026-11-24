using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class PartTimeScheduleRowView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text time;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Color idle = new Color(.10f, .17f, .22f, 1f);
        [SerializeField] private Color active = new Color(.17f, .29f, .36f, 1f);
        [SerializeField] private Color hovered = new Color(.24f, .37f, .44f, 1f);
        [SerializeField] private Button reminder;
        [SerializeField] private Image reminderIcon;
        [SerializeField] private Sprite reminderOn;
        [SerializeField] private Sprite reminderOff;
        [SerializeField] private Button tracking;
        [SerializeField] private Image trackingIcon;
        private VillageMapView map;
        private VillageJobSchedule schedule;
        private VillageJobSchedule.Entry job;
        private bool isCurrent, pointerOver;
        private Transform Workplace => job?.Workplace != null ? job.Workplace.transform : null;

        internal void Bind(VillageMapView view, VillageJobSchedule jobs)
        {
            map = view;
            schedule = jobs;
            reminder.onClick.AddListener(() =>
            {
                schedule.ToggleReminder(job);
                UpdateReminderIcon();
            });
            tracking.onClick.AddListener(ToggleTracking);
        }

        internal void Show(VillageJobSchedule.Entry entry, string hours, string state, bool current)
        {
            title.text = entry.Title;
            time.text = hours;
            status.text = state;
            isCurrent = current;
            if (job != entry)
            {
                job = entry;
                if (pointerOver && map != null)
                    map.ShowJobLocation(this, Workplace);
            }
            UpdateReminderIcon();
            UpdateTrackingIcon();
            UpdateColor();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerOver = true;
            if (map != null)
                map.ShowJobLocation(this, Workplace);
            UpdateColor();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            ToggleTracking();
        }

        public void OnPointerExit(PointerEventData eventData) => ClearHover();
        private void OnDisable() => ClearHover();

        private void ClearHover()
        {
            if (!pointerOver)
                return;
            pointerOver = false;
            if (map != null)
                map.ClearJobLocation(this);
            UpdateColor();
        }

        private void UpdateReminderIcon()
        {
            bool enabled = job?.Reminder == true;
            reminderIcon.sprite = enabled ? reminderOn : reminderOff;
            reminderIcon.color = enabled ? new Color(.64f, .87f, 1f) : new Color(.63f, .68f, .72f);
        }

        private void ToggleTracking()
        {
            schedule.TogglePin(job);
            if (!schedule.IsPinned(job) && map != null)
                map.ClearJobLocation(this);
            UpdateTrackingIcon();
            UpdateColor();
        }

        private void UpdateTrackingIcon()
        {
            tracking.interactable = Workplace != null;
            trackingIcon.color = schedule.IsPinned(job) ? new Color(1f, .77f, .38f) : new Color(.63f, .68f, .72f);
        }

        private void UpdateColor() =>
            background.color = pointerOver || schedule?.IsPinned(job) == true ? hovered : isCurrent ? active : idle;
    }
}
