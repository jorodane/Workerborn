using UnityEngine;

namespace Workerborn
{
    public enum VillageRole
    {
        Farmer,
        Merchant,
        Innkeeper,
        Shrine,
        NoticeBoard
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
        public override bool CanTarget => Role == VillageRole.Farmer || Role == VillageRole.Merchant || Role == VillageRole.Innkeeper;
        public override string InteractionVerb => Role == VillageRole.NoticeBoard ? "게시판 보기" : Role == VillageRole.Shrine ? "기도하기" : "대화하기";

        protected override void Interact(HarvestWorker worker) => worker.CommandInteract(this);
        protected override void OnArrived(VillagePrototype village, HarvestWorker worker) => village?.OpenConversation(this);
        public Vector3 LabelPosition => NamePosition;

        public void Initialize(VillageRole role, string displayName, string subtitle)
        {
            fallbackRole = role;
            fallbackName = displayName;
            fallbackSubtitle = subtitle;
        }
    }
}
