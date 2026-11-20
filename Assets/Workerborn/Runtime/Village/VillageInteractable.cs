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
        public override string InteractionVerb => Role == VillageRole.NoticeBoard ? "게시판 보기" : Role == VillageRole.Shrine ? "기도하기" : "대화하기";

        protected override void Interact(HarvestWorker worker) => worker.CommandInteract(this);
        protected override void OnArrived(VillagePrototype village, HarvestWorker worker) => village?.OpenConversation(this);

        public bool IsTopicAvailable(VillagePrototype village, DialogueAction action)
        {
            if (Role != VillageRole.Farmer)
                return true;
            return village.State.FarmAvailable ? action != DialogueAction.Foundation : action == DialogueAction.Foundation;
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            base.AddContextActions(village, actions);
            if (Role == VillageRole.NoticeBoard || Definition?.Dialogue?.Topics == null)
                return;
            foreach (var topic in Definition.Dialogue.Topics)
            {
                var action = topic.action;
                if (action == DialogueAction.Greeting || string.IsNullOrWhiteSpace(topic.keyword) || !IsTopicAvailable(village, action))
                    continue;
                actions.Add(new WorldContextAction(topic.keyword, () =>
                {
                    if (BeginInteraction(village.Worker))
                        village.Worker.CommandApproach(this, () => village.OpenConversation(this, action));
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
