using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ItemTooltipView : VillageWindowView
    {
        [SerializeField]
        private TMP_Text title;
        [SerializeField]
        private TMP_Text description;
        public void Show(ItemSpec item)
        {
            title.text = item.Name;
            description.text = item.Description;
        }
    }
}
