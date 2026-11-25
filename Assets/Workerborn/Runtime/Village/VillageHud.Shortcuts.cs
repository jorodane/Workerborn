using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private Vector2 chatAtStatus;

        private static readonly string[] ShortcutActions =
        {
            "Gameplay/Shortcut1", "Gameplay/Shortcut2", "Gameplay/Shortcut3", "Gameplay/Shortcut4", "Gameplay/Shortcut5",
            "Gameplay/Shortcut6", "Gameplay/Shortcut7", "Gameplay/Shortcut8", "Gameplay/Shortcut9", "Gameplay/Shortcut0"
        };

        private bool ShortcutsAvailable => itemShortcuts != null && itemShortcuts.isActiveAndEnabled &&
            !game.State.AwaitingIncarnation && !game.IsWorldTransitioning && !game.AwaitingWorldCommand &&
            !WorldActionsLocked && !IsChatTyping;

        private void UpdateItemShortcutPresentation(bool duringDialogue)
        {
            if (itemShortcuts == null)
                return;

            bool show = !game.State.AwaitingIncarnation;
            itemShortcuts.gameObject.SetActive(show);
            var position = chatAtStatus;
            if (show && !duringDialogue)
                position.y += itemShortcuts.Rect.anchoredPosition.y + itemShortcuts.Rect.rect.height + 4f;
            chatLayer.anchoredPosition = position;
        }

        private void ReadItemShortcuts()
        {
            if (!ShortcutsAvailable || IsHoldingItem)
                return;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<TMP_InputField>() is { isFocused: true })
                return;

            for (int index = 0; index < ShortcutActions.Length; index++)
                if (WorkerbornInput.Pressed(ShortcutActions[index]))
                    UseItemShortcut(index);
        }

        internal void ClickItemShortcut(int index, PointerEventData.InputButton button)
        {
            if (button == PointerEventData.InputButton.Right)
            {
                if (suppressRightContext)
                {
                    suppressRightContext = false;
                    return;
                }
                if (ShortcutsAvailable && !IsHoldingItem && game.State.ItemShortcutAt(index) != null)
                {
                    HideItemTooltip();
                    game.Perform(() => game.State.TryClearItemShortcut(index));
                }
            }
            else if (button == PointerEventData.InputButton.Left && ShortcutsAvailable)
            {
                if (IsHoldingItem)
                    AssignHeldItemShortcut(index);
                else
                    UseItemShortcut(index);
            }
        }

        private void AssignHeldItemShortcut(int index)
        {
            if (ShortcutsAvailable && draggedEntry != 0 && game.State.PendingPurchase == null &&
                game.Perform(() => game.State.TryAssignItemShortcut(index, draggedEntry)))
                CancelItemDrag();
        }

        private void UseItemShortcut(int index)
        {
            string id = game.State.ItemShortcutAt(index);
            if (id == null)
                return;
            var spec = ItemCatalog.Get(id);
            if (game.State.IsEquipped(spec))
                return;
            var entry = game.State.FindShortcutEntry(index);
            if (entry != null)
                UseInventoryItem(entry);
            else
                ShowToast($"소지품에 {spec.Name} 아이템이 없어요.");
        }

        internal void ShowShortcutTooltip(int index, Vector2 screen)
        {
            var state = game.State;
            string id = state.ItemShortcutAt(index);
            if (id == null)
                return;
            var spec = ItemCatalog.Get(id);
            var condition = state.IsEquipped(spec) ? state.EquippedCondition(spec.EquipmentSlot) :
                state.FindShortcutEntry(index)?.Condition ?? default;
            ShowItemTooltip(id, screen, condition: condition);
        }
    }
}
