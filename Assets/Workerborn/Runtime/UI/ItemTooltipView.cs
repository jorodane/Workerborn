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
        public void Show(ItemSpec item, int quantity = 0)
        {
            title.text = item.Name;
            description.text = quantity < 1 ? item.Description
                : $"{item.Description}\n\n수량 {quantity:N0}   ·   개당 {item.Price:N0} Gold";
        }
    }
}
