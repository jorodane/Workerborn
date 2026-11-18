using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ItemTooltipView : VillageWindowView
    {
        [SerializeField]
        private Text title;
        [SerializeField]
        private Text description;
        public void Show(ItemSpec item)
        {
            title.text = item.Name;
            description.text = item.Description;
        }
    }
}
