using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ItemContextView : VillageWindowView
    {
        [SerializeField]
        private Button use;
        [SerializeField]
        private TMP_Text useLabel;
        [SerializeField]
        private Button split;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            use.onClick.AddListener(hud.UseSelectedInventoryItem);
            split.onClick.AddListener(hud.SplitSelectedInventoryItem);
        }

        public void Show(InventoryEntry item)
        {
            useLabel.text = item.Spec.EquipmentSlot != EquipmentSlot.None ? "장착" : "사용";
            use.interactable = item.ItemId == ItemIds.Bread || item.Spec.EquipmentSlot != EquipmentSlot.None;
            split.interactable = item.Quantity > 1;
        }
    }
}
