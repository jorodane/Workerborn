using UnityEngine;

namespace Workerborn
{
    public enum VillageRole { Farmer, Merchant, Innkeeper, Shrine, NoticeBoard }

    public sealed class VillageInteractable : MonoBehaviour
    {
        public VillageRole Role { get; private set; }
        public string DisplayName { get; private set; }
        public string Subtitle { get; private set; }
        public Vector3 LabelPosition => transform.position + Vector3.up * (Role == VillageRole.Shrine ? 2.5f : 2.05f);

        public void Initialize(VillageRole role, string displayName, string subtitle)
        {
            Role = role; DisplayName = displayName; Subtitle = subtitle;
        }
    }
}
