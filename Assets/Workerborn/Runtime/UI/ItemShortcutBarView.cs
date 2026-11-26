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

        internal void Refresh(VillageState state, VillageSettings settings, int offset)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                int binding = index + offset;
                string id = state.ItemShortcutAt(binding);
                if (state.SkillShortcutAt(binding) is SkillId skill)
                    slots[index].ShowSkill(settings.FindSkill(skill)?.Icon, state.Rules.Skill(skill).Name);
                else if (id != null)
                    slots[index].ShowItem(settings.FindItem(id), id, state.Inventory.Count(id), state.IsEquipped(ItemCatalog.Get(id)));
                else
                    slots[index].Clear();
                slots[index].ShowKey(binding);
            }
        }
    }
}
