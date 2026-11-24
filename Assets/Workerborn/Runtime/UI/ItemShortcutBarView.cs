using UnityEngine;

namespace Workerborn
{
    public sealed class ItemShortcutBarView : MonoBehaviour
    {
        [SerializeField] private ItemShortcutSlotView[] slots;
        internal RectTransform Rect => (RectTransform)transform;

        internal void Bind(VillageHud hud)
        {
            for (int index = 0; index < slots.Length; index++)
                slots[index].Bind(hud, index);
        }

        internal void Refresh(VillageState state, VillageSettings settings)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                string id = state.ItemShortcutAt(index);
                if (id == null)
                    slots[index].Clear();
                else
                    slots[index].Show(settings.FindItem(id), id, state.Inventory.Count(id), state.IsEquipped(ItemCatalog.Get(id)));
            }
        }
    }
}
