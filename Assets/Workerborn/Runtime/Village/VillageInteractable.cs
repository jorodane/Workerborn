using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public enum VillageRole
    {
        Farmer,
        Merchant,
        Innkeeper,
        Shrine,
        NoticeBoard,
        Guide
    }

    public class VillageInteractable : WorldInteractable
    {
        [SerializeField]
        private NpcDefinition definition;
        private VillageRole fallbackRole;
        private string fallbackName;
        private string fallbackSubtitle;
        public NpcDefinition Definition => definition;
        public VillageRole Role => definition != null ? definition.Role : fallbackRole;
        public override string DisplayName => definition != null ? definition.DisplayName : fallbackName;
        public string Subtitle => definition != null ? definition.Occupation : fallbackSubtitle;
        public override float InteractionDistance => definition != null ? definition.InteractionDistance : 1.8f;
        public override bool CanTarget => Role != VillageRole.Shrine && Role != VillageRole.NoticeBoard;
        public override bool ShowNameByDefault => true;
        internal NpcRoutine Routine { get; set; }
        internal NpcActivity CurrentActivity => Routine != null && Routine.isActiveAndEnabled ? Routine.Activity : NpcActivity.Idle;
        public override Vector3 NamePosition => Routine?.IsSeated == true && TryGetComponent<CharacterActor>(out var actor)
            ? actor.NamePosition : base.NamePosition;
        public override string InteractionVerb => Role == VillageRole.NoticeBoard ? "게시판 보기" : Role == VillageRole.Shrine ? "기도하기" : "대화하기";
        internal override InteractionCursor CursorFor(HarvestWorker worker) => Role switch
        {
            VillageRole.NoticeBoard => InteractionCursor.Inspect,
            VillageRole.Shrine => InteractionCursor.Pray,
            _ => InteractionCursor.Talk
        };

        protected override void Interact(HarvestWorker worker) => worker.CommandInteract(this);
        protected override void OnArrived(VillagePrototype village, HarvestWorker worker) => village?.BeginConversation(worker, this);

        public bool IsTopicAvailable(VillagePrototype village, DialogueAction action) => village.FindDialogueTopic(this, action) != null;

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            base.AddContextActions(village, actions);
            if (Role == VillageRole.NoticeBoard)
                return;
            foreach (var entry in village.DialogueTopicsFor(this))
            {
                var topic = entry.Topic;
                if (topic.action == DialogueAction.Greeting || string.IsNullOrWhiteSpace(topic.keyword))
                    continue;
                actions.Add(new WorldContextAction(topic.keyword, () =>
                {
                    if (village.IsDialogueTopicAvailable(this, topic) && BeginInteraction(village.Worker))
                        village.Worker.CommandApproach(this, () => village.OpenConversationTopic(this, topic));
                }));
            }
        }

        public void Initialize(VillageRole role, string displayName, string subtitle)
        {
            fallbackRole = role;
            fallbackName = displayName;
            fallbackSubtitle = subtitle;
        }
    }
}
