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
        private TMP_Text splitLabel;
        private string defaultSplitLabel;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            splitLabel = split.GetComponentInChildren<TMP_Text>(true);
            if (splitLabel != null)
                defaultSplitLabel = splitLabel.text;
            use.onClick.AddListener(hud.UseSelectedInventoryItem);
            split.onClick.AddListener(hud.SplitSelectedInventoryItem);
        }

        public void Show(InventoryEntry item)
        {
            bool document = Game.State.TryGetDocumentPage(item.ItemId, out _);
            useLabel.text = document ? "펼치기" : item.Spec.EquipmentSlot != EquipmentSlot.None ? "장착" : "사용";
            use.interactable = document || item.ItemId == ItemIds.Bread || item.Spec.HealthRestore > 0 ||
                item.Spec.EquipmentSlot != EquipmentSlot.None;
            if (splitLabel != null)
                splitLabel.text = document ? "옮기기" : defaultSplitLabel;
            split.interactable = document || item.Quantity > 1;
        }
    }
}
