using System;
using UnityEngine;

namespace Workerborn
{
    public enum DialogueAction { Greeting, Job, IntroQuest, Contribution, Shop, Rest, Rebirth, Foundation, GuideAfter, Legend, PlainText, GuideSoulGrowth }

    public enum DialogueRequirementKind
    {
        QuestStage, FarmAvailable, JobWindowOpen, MinimumEra, MinimumLife, ItemCount, NpcActivity, Weekday, CalendarEvent
    }

    [Flags]
    public enum DialogueQuestStage
    {
        None = 0,
        Unavailable = 1,
        Available = 2,
        InProgress = 4,
        ReadyToReport = 8,
        Completed = 16
    }

    [Serializable]
    public sealed class DialogueRequirement
    {
        [SerializeField] private DialogueRequirementKind kind;
        [SerializeField] private bool invert;
        [SerializeField, Tooltip("Stages of the quest that supplies this topic.")]
        private DialogueQuestStage questStages = DialogueQuestStage.InProgress;
        [SerializeField, Tooltip("Item to check for Item Count.")]
        private ItemDefinition item;
        [SerializeField, Min(1), Tooltip("Minimum era, life, or item quantity.")]
        private int minimum = 1;
        [SerializeField] private NpcActivity activity;
        [SerializeField] private VillageWeekdays weekdays = VillageWeekdays.All;
        [SerializeField] private VillageCalendarEvent calendarEvent;

        internal bool IsMet(VillageState state, DialogueQuestStage questStage, NpcActivity currentActivity)
        {
            bool? matches = kind switch
            {
                DialogueRequirementKind.QuestStage when questStage != DialogueQuestStage.None => (questStages & questStage) != 0,
                DialogueRequirementKind.FarmAvailable => state.FarmAvailable,
                DialogueRequirementKind.JobWindowOpen => state.JobWindowOpen,
                DialogueRequirementKind.MinimumEra => state.Era >= minimum,
                DialogueRequirementKind.MinimumLife => state.Life >= minimum,
                DialogueRequirementKind.ItemCount when item != null => state.Inventory.Count(item.Id) >= minimum,
                DialogueRequirementKind.NpcActivity => activity == currentActivity,
                DialogueRequirementKind.Weekday => (weekdays & VillageCalendar.Weekday(state.Day)) != 0,
                DialogueRequirementKind.CalendarEvent when calendarEvent != null => calendarEvent.OccursOn(state.Day),
                _ => null
            };
            return matches.HasValue && matches.Value != invert;
        }
    }

    [Serializable]
    public sealed class DialogueTopicDefinition
    {
        public string keyword;
        public DialogueAction action;
        [TextArea(3, 10)] public string text;
        [SerializeField, Tooltip("All requirements must be met before showing this topic.")]
        private DialogueRequirement[] requirements;

        internal bool IsAvailable(VillageState state, NpcActivity activity, DialogueQuestStage questStage = DialogueQuestStage.None)
        {
            if (requirements != null)
                foreach (var requirement in requirements)
                    if (requirement == null || !requirement.IsMet(state, questStage, activity))
                        return false;
            return true;
        }
    }

    [Serializable]
    public sealed class DialogueOffer
    {
        [SerializeField] private NpcDefinition recipient;
        [SerializeField, Tooltip("Use the topic as the opening line when its requirements are met.")]
        private bool openOnTalk;
        [SerializeField] private DialogueTopicDefinition topic;

        internal NpcDefinition Recipient => recipient;
        internal bool OpenOnTalk => openOnTalk;
        internal DialogueTopicDefinition Topic => topic;
    }

    [CreateAssetMenu(menuName = "Workerborn/Content/Dialogue", fileName = "Dialogue")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        [SerializeField] private DialogueTopicDefinition[] topics;
        public DialogueTopicDefinition[] Topics => topics ?? Array.Empty<DialogueTopicDefinition>();
        public string TextFor(DialogueAction action)
        {
            foreach (var topic in Topics)
                if (topic != null && topic.action == action)
                    return topic.text;
            return string.Empty;
        }
    }
}
