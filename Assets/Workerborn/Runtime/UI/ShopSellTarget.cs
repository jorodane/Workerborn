using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed class ShopSellTarget : MonoBehaviour, IPointerClickHandler
    {
        public VillageHud Hud { get; set; }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && Hud != null)
                Hud.SellHeldItem();
        }
    }
}
