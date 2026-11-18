using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class QuantityWindowView : VillageWindowView
    {
        [SerializeField]
        private UiItemView item;
        [SerializeField]
        private TMP_Text title;
        [SerializeField]
        private TMP_Text quantityText;
        [SerializeField]
        private TMP_Text total;
        [SerializeField]
        private TMP_Text confirmText;
        [SerializeField]
        private Button plus;
        [SerializeField]
        private Button minus;
        [SerializeField]
        private Button confirm;
        private string id;
        private bool selling;
        private int quantity = 1;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            plus.onClick.AddListener(() =>
            {
                quantity = Mathf.Min(Maximum, quantity + 1);
                Refresh();
            });
            minus.onClick.AddListener(() =>
            {
                quantity = Mathf.Max(1, quantity - 1);
                Refresh();
            });
            confirm.onClick.AddListener(() =>
            {
                bool success = Game.Perform(() => selling ? Game.State.TrySellItems(id, quantity) : Game.State.TryBuy(id, quantity));
                if (success)
                    gameObject.SetActive(false);
            });
        }

        private int Maximum => selling ? Game.State.Inventory.Count(id) : id == ItemIds.Bag || id == ItemIds.Hoe ? 1 : Mathf.Min(999, Game.State.Gold / Mathf.Max(1, ItemCatalog.Get(id).Price));

        public void Open(string itemId, bool sell)
        {
            id = itemId;
            selling = sell;
            quantity = 1;
            Refresh();
        }

        public override void Refresh()
        {
            if (string.IsNullOrEmpty(id))
                return;
            var spec = ItemCatalog.Get(id);
            quantity = Mathf.Clamp(quantity, 1, Mathf.Max(1, Maximum));
            item.Display(Game.Settings.FindItem(id), id, quantity);
            item.Passive();
            title.text = spec.Name;
            quantityText.text = quantity.ToString();
            total.text = $"가격  {spec.Price * quantity:N0} Gold";
            confirmText.text = selling ? "판매" : "구입";
            confirm.interactable = Maximum > 0;
            plus.interactable = quantity < Maximum;
            minus.interactable = quantity > 1;
        }
    }
}
