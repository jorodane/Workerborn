using TMPro;
using UnityEngine;

namespace Workerborn
{
    [CreateAssetMenu(menuName = "Workerborn/UI/Prefab Library", fileName = "VillageUI")]
    public sealed class VillageUiLibrary : ScriptableObject
    {
        [SerializeField]
        private VillageWindowView[] windows;
        [SerializeField]
        private UiItemView item;
        [SerializeField]
        private RectTransform inventoryCell;
        [SerializeField]
        private UiActionView actionButton;
        [SerializeField]
        private UiListRow listRow;
        [SerializeField]
        private PickupNameView pickupName;
        [SerializeField]
        private SkillBalloonView skillBalloon;
        [SerializeField]
        private WorldTooltipView worldTooltip;
        [SerializeField]
        private WorldNameplateView npcName;
        [SerializeField]
        private TMP_Text floatingText;
        [SerializeField] private PlayerOverheadView playerOverhead;
        [SerializeField] private VillageChatView chat;
        public SkillBalloonView SkillBalloon => skillBalloon;
        public WorldTooltipView WorldTooltip => worldTooltip;
        public WorldNameplateView NpcName => npcName;
        public TMP_Text FloatingText => floatingText;
        public UiItemView Item => item;
        public RectTransform InventoryCell => inventoryCell;
        public UiActionView ActionButton => actionButton;
        public UiListRow ListRow => listRow;
        public PickupNameView PickupName => pickupName;
        public PlayerOverheadView PlayerOverhead => playerOverhead;
        public VillageChatView Chat => chat;

        public VillageWindowView Find(string id)
        {
            foreach (var window in windows)
                if (window != null && window.Id == id)
                    return window;
            return null;
        }
    }
}
