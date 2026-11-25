using TMPro;
using UnityEngine;

namespace Workerborn
{
    public sealed class ItemTooltipView : VillageWindowView
    {
        [SerializeField]
        private TMP_Text title;
        [SerializeField]
        private TMP_Text description;
        private float defaultHeight;
        private float defaultTextHeight;
        private float frameHeight;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            defaultHeight = Rect.rect.height;
            defaultTextHeight = description.rectTransform.rect.height;
            frameHeight = defaultHeight - defaultTextHeight;
        }

        public void Show(ItemSpec item, int quantity = 0, string trade = null, EquipmentCondition condition = default)
        {
            title.text = item.Name;
            string info = item.Description;
            if (item.HealthRestore > 0)
                info += $"\n생명력 회복량  {item.HealthRestore:N0}";
            if (item.MaxDurability > 0)
            {
                condition = condition.ForItem(item);
                info += $"\n내구도  {condition.durability:N0}/{item.MaxDurability:N0}   ·   오염도  {condition.contamination}%";
            }
            description.text = quantity < 1 ? info : $"{info}\n\n수량 {quantity:N0}   ·   개당 {item.Price:N0} Gold";
            if (!string.IsNullOrEmpty(trade))
                description.text += "\n\n" + trade;
            float height = description.GetPreferredValues(description.text, description.rectTransform.rect.width, Mathf.Infinity).y;
            description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(defaultTextHeight, height));
            SetExpandedHeight(Mathf.Max(defaultHeight, height + frameHeight));
        }
    }
}
