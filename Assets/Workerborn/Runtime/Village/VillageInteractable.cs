using UnityEngine;

namespace Workerborn
{
    public enum VillageRole { Farmer, Merchant, Innkeeper, Shrine, NoticeBoard }

    public sealed class VillageInteractable : MonoBehaviour
    {
        [SerializeField] private NpcDefinition definition;
        private VillageRole fallbackRole;
        private string fallbackName;
        private string fallbackSubtitle;
        public NpcDefinition Definition => definition;
        public VillageRole Role => definition != null ? definition.Role : fallbackRole;
        public string DisplayName => definition != null ? definition.DisplayName : fallbackName;
        public string Subtitle => definition != null ? definition.Occupation : fallbackSubtitle;
        public float InteractionDistance => definition != null ? definition.InteractionDistance : 1.8f;
        public Vector3 LabelPosition => transform.position + Vector3.up * (Role == VillageRole.Shrine ? 2.5f : 2.05f);

        public void Initialize(VillageRole role, string displayName, string subtitle)
        {
            fallbackRole = role; fallbackName = displayName; fallbackSubtitle = subtitle;
        }
    }
}
