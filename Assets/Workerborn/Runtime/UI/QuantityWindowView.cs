using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    internal enum QuantityAction { Buy, Sell, Split }

    public sealed class QuantityWindowView : VillageWindowView
    {
        [SerializeField] private UiItemView item;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_InputField quantityInput;
        [SerializeField] private Slider quantitySlider;
        [SerializeField] private TMP_Text total;
        [SerializeField] private TMP_Text confirmText;
        [SerializeField] private Button minimum;
        [SerializeField] private Button minusTen;
        [SerializeField] private Button minusOne;
        [SerializeField] private Button half;
        [SerializeField] private Button plusOne;
        [SerializeField] private Button plusTen;
        [SerializeField] private Button maximum;
        [SerializeField] private Button confirm;
        private string id;
        private QuantityAction action;
        private int entryId;
        private int quantity;
        private bool updating;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            item.Passive();
            quantitySlider.wholeNumbers = true;
            quantitySlider.onValueChanged.AddListener(value =>
            {
                if (!updating)
                    SetQuantity((int)value);
            });
            quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            quantityInput.characterLimit = 10;
            quantityInput.onValueChanged.AddListener(text => ApplyInput(text, false));
            quantityInput.onEndEdit.AddListener(text => ApplyInput(text, true));
            minimum.onClick.AddListener(() => SetQuantity(1));
            minusTen.onClick.AddListener(() => SetQuantity(quantity - 10));
            minusOne.onClick.AddListener(() => SetQuantity(quantity - 1));
            half.onClick.AddListener(() => SetQuantity((action == QuantityAction.Split ? Stack?.Quantity ?? 0 : Maximum) / 2));
            plusOne.onClick.AddListener(() => SetQuantity((long)quantity + 1));
            plusTen.onClick.AddListener(() => SetQuantity((long)quantity + 10));
            maximum.onClick.AddListener(() => SetQuantity(Maximum));
            confirm.onClick.AddListener(Confirm);
        }

        private InventoryEntry Stack
        {
            get
            {
                var entry = Game.State.Inventory.Find(entryId);
                return entry != null && entry.ItemId == id ? entry : null;
            }
        }

        private int Maximum => action switch
        {
            QuantityAction.Buy => Game.State.MaximumBuyQuantity(id),
            QuantityAction.Sell when Stack != null => Game.State.MaximumSellQuantity(entryId),
            QuantityAction.Split when Stack != null => Game.State.Inventory.MaximumSplitQuantity(entryId),
            _ => 0
        };

        internal void Open(string itemId, QuantityAction operation, int stackId = 0)
        {
            id = itemId;
            action = operation;
            entryId = stackId;
            SetQuantity(1);
        }

        public override void Refresh()
        {
            if (!string.IsNullOrEmpty(id))
                SetQuantity(quantity, !quantityInput.isFocused);
        }

        private void ApplyInput(string text, bool finish)
        {
            if (updating || string.IsNullOrEmpty(id))
                return;
            if (long.TryParse(text, out long requested))
                SetQuantity(requested, finish);
            else if (finish)
                SetQuantity(quantity);
        }

        private void SetQuantity(long requested, bool updateInput = true)
        {
            int limit = Maximum;
            quantity = limit == 0 ? 0 : (int)Math.Max(1, Math.Min(limit, requested));
            updating = true;
            quantitySlider.minValue = 1;
            quantitySlider.maxValue = Mathf.Max(1, limit);
            quantitySlider.value = Mathf.Max(1, quantity);
            if (updateInput)
                quantityInput.SetTextWithoutNotify(quantity.ToString());
            updating = false;

            var spec = ItemCatalog.Get(id);
            item.Display(Game.Settings.FindItem(id), id, quantity);
            title.text = spec.Name;
            total.text = action == QuantityAction.Split
                ? $"원래 묶음에 {((Stack?.Quantity ?? 0) - quantity):N0}개 남음"
                : $"{(action == QuantityAction.Sell ? "받는 금액" : "가격")}  {(long)spec.Price * quantity:N0} Gold";
            confirmText.text = action switch
            {
                QuantityAction.Sell => "판매",
                QuantityAction.Split => "나누기",
                _ => "구입"
            };
            quantitySlider.interactable = limit > 1;
            quantityInput.interactable = limit > 1;
            confirm.interactable = limit > 0;
            minimum.interactable = minusOne.interactable = minusTen.interactable = quantity > 1;
            plusOne.interactable = plusTen.interactable = maximum.interactable = quantity < limit;
            half.interactable = limit > 1;
        }

        private void Confirm()
        {
            ApplyInput(quantityInput.text, true);
            if (quantity < 1)
                return;
            bool success = Game.Perform(() => action switch
            {
                QuantityAction.Sell => Stack != null && Game.State.TrySellStack(entryId, quantity),
                QuantityAction.Split => Stack != null && Game.State.TrySplitStack(entryId, quantity),
                _ => Game.State.TryBuy(id, quantity)
            });
            if (success)
                gameObject.SetActive(false);
        }
    }
}
