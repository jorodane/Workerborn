using System;
using UnityEngine;

namespace Workerborn
{
    internal enum ConversationGesture { None, Wave, Nod, Talk }

    [Serializable]
    internal struct NpcConversationLine
    {
        [SerializeField] private bool partnerSpeaks;
        [SerializeField, TextArea(1, 3)] private string text;
        [SerializeField] private ConversationGesture gesture;

        internal bool PartnerSpeaks => partnerSpeaks;
        internal string Text => text;
        internal ConversationGesture Gesture => gesture;
    }

    [Serializable]
    internal sealed class NpcConversation
    {
        [SerializeField, Tooltip("Passing greetings leave both characters on their current routes.")]
        private bool passing;
        [SerializeField] private NpcDefinition speaker;
        [SerializeField] private NpcDefinition partner;
        [SerializeField] private DialogueRequirement[] speakerRequirements;
        [SerializeField] private DialogueRequirement[] partnerRequirements;
        [SerializeField, Range(0, 1439)] private int startMinute;
        [SerializeField, Range(0, 1440)] private int endMinute = 1440;
        [SerializeField, Tooltip("Higher priority conversations take precedence when multiple entries match.")]
        private int priority;
        [SerializeField] private NpcConversationLine[] lines;

        internal int Priority => priority;
        internal NpcConversationLine[] Lines => lines ?? Array.Empty<NpcConversationLine>();

        internal bool Matches(bool onTheWay, NpcDefinition first, VillageState firstState, NpcActivity firstActivity,
            NpcDefinition second, VillageState secondState, NpcActivity secondActivity) =>
            passing == onTheWay && (speaker == null || speaker == first) && (partner == null || partner == second) &&
            Lines.Length >= 2 && VillageCalendar.TryGetScheduleDay(firstState.Day, firstState.Minute, startMinute, endMinute, out _) &&
            DialogueRequirement.AllMet(speakerRequirements, firstState, firstActivity) &&
            DialogueRequirement.AllMet(partnerRequirements, secondState, secondActivity);
    }
}
