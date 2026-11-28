using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private static readonly string[] ShortcutActions =
        {
            "Gameplay/Shortcut1", "Gameplay/Shortcut2", "Gameplay/Shortcut3", "Gameplay/Shortcut4", "Gameplay/Shortcut5",
            "Gameplay/Shortcut6", "Gameplay/Shortcut7", "Gameplay/Shortcut8", "Gameplay/Shortcut9", "Gameplay/Shortcut0"
        };
        private int shownShortcutOffset = -1;
        private ItemShortcutBarView shiftShortcuts;
        private RectTransform skillShortcutGhost;
        private Vector2 shortcutHome;
        private float shortcutScale = 1f;
        private const float DockMargin = 14f;
        private const float RowGap = 6f;
        private const float BottomMenuClearance = 32f;

        private static int ShortcutOffset => WorkerbornInput.Held("Gameplay/ShortcutBank") ? VillageState.ItemShortcutCount : 0;
        private bool ShowBothShortcutRows => IsHoldingItem || skillShortcutGhost != null;
        private static bool IsTextInputFocused => EventSystem.current?.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() is { isFocused: true };

        private bool ShortcutsAvailable => itemShortcuts != null && itemShortcuts.isActiveAndEnabled &&
            !game.State.AwaitingIncarnation && !game.IsWorldTransitioning && !game.AwaitingWorldCommand &&
            !HasQuantityPrompt && !IsChatTyping;

        private void UpdateItemShortcutPresentation()
        {
            if (itemShortcuts != null)
                itemShortcuts.gameObject.SetActive(!game.State.AwaitingIncarnation);
            RefreshItemShortcutBar();
        }

        private void BindShortcuts()
        {
            if (itemShortcuts == null)
                return;
            itemShortcuts.Bind(this);
            var primary = itemShortcuts.Rect;
            shiftShortcuts = Instantiate(itemShortcuts, primary.parent);
            shiftShortcuts.name = "Shift shortcuts";
            shiftShortcuts.Rect.anchoredPosition = primary.anchoredPosition - Vector2.up * (primary.rect.height + RowGap);
            shiftShortcuts.Bind(this);
            shiftShortcuts.gameObject.SetActive(false);
        }

        private Rect BoundsOnCanvas(RectTransform content)
        {
            Vector2 min = CanvasRect.InverseTransformPoint(content.TransformPoint(content.rect.min));
            Vector2 max = CanvasRect.InverseTransformPoint(content.TransformPoint(content.rect.max));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void LayoutTopDock()
        {
            if (itemShortcuts == null)
                return;

            Rect canvas = CanvasRect.rect;
            Rect status = BoundsOnCanvas(characterStatus.Rect);
            Rect time = BoundsOnCanvas((RectTransform)clock.transform);
            float width = itemShortcuts.Rect.rect.width;
            float left = status.xMax + RowGap;
            float right = time.xMin - RowGap;
            float space = right - left;
            float top = -DockMargin;
            if (space < width * .6f)
            {
                left = canvas.xMin + DockMargin;
                right = canvas.xMax - DockMargin;
                space = right - left;
                float bottom = minimap != null ? BoundsOnCanvas((RectTransform)minimap.transform).yMin : time.yMin;
                top = Mathf.Min(status.yMin, bottom) - canvas.yMax - RowGap;
            }
            shortcutScale = Mathf.Clamp(space / width, .1f, 1f);
            float halfWidth = width * shortcutScale * .5f;
            shortcutHome = new Vector2(Mathf.Clamp(canvas.center.x, left + halfWidth, right - halfWidth) - canvas.center.x, top);
            itemShortcuts.Rect.localScale = shiftShortcuts.Rect.localScale = Vector3.one * shortcutScale;
            if (views.TryGetValue("inventory", out var panel) && panel is InventoryWindowView inventory && inventory.IsOpen)
                PlaceInventory(inventory);
            UpdateTopShortcutPosition();
        }

        private void PlaceInventory(InventoryWindowView inventory)
        {
            if (!inventory.HasSavedPosition)
                inventory.Rect.anchoredPosition = shortcutHome;
            float clearance = itemShortcuts == null ? 0f : 2f * itemShortcuts.Rect.rect.height * shortcutScale + 3f * RowGap;
            float height = CanvasRect.rect.height + shortcutHome.y - clearance - BottomMenuClearance;
            inventory.Rect.localScale = Vector3.one * Mathf.Clamp(height / inventory.MaximumHeight, .1f, shortcutScale);
        }

        internal void UpdateTopShortcutPosition()
        {
            if (itemShortcuts == null || shiftShortcuts == null)
                return;

            var bar = itemShortcuts.Rect;
            Rect canvas = CanvasRect.rect;
            float rowHeight = bar.rect.height * shortcutScale;
            float visibleRows = shiftShortcuts.gameObject.activeSelf ? rowHeight * 2f + RowGap : rowHeight;
            float center = canvas.center.x + shortcutHome.x;
            float y = shortcutHome.y;
            if (views.TryGetValue("inventory", out var panel) && panel is InventoryWindowView inventory && inventory.gameObject.activeInHierarchy)
            {
                Rect visiblePanel = BoundsOnCanvas(inventory.Presentation);
                float halfWidth = bar.rect.width * shortcutScale * .5f;
                float below = visiblePanel.yMin - canvas.yMax - RowGap;
                if (visiblePanel.xMin < center + halfWidth && visiblePanel.xMax > center - halfWidth &&
                    visiblePanel.yMax > canvas.yMax + y - visibleRows && below > -canvas.height + visibleRows + BottomMenuClearance)
                    y = Mathf.Min(y, below);
            }
            bar.anchoredPosition = new Vector2(shortcutHome.x, y);
            shiftShortcuts.Rect.anchoredPosition = bar.anchoredPosition - Vector2.up * (rowHeight + RowGap);
            if (areaAnnouncement != null)
            {
                var announcement = (RectTransform)areaAnnouncement.transform;
                float minimum = -canvas.height + announcement.rect.height + BottomMenuClearance;
                announcement.anchoredPosition = Vector2.up * Mathf.Max(minimum, y - visibleRows - DockMargin);
            }
        }

        private void RefreshItemShortcutBar()
        {
            bool both = itemShortcuts != null && itemShortcuts.gameObject.activeSelf && ShowBothShortcutRows;
            shownShortcutOffset = both ? 0 : ShortcutOffset;
            itemShortcuts?.Refresh(game.State, game.Settings, shownShortcutOffset);
            if (shiftShortcuts != null)
            {
                if (both)
                    shiftShortcuts.Refresh(game.State, game.Settings, VillageState.ItemShortcutCount);
                if (shiftShortcuts.gameObject.activeSelf != both)
                    shiftShortcuts.gameObject.SetActive(both);
            }
            if (views.TryGetValue("inventory", out var panel) && panel is InventoryWindowView inventory)
                inventory.RefreshShortcuts();
        }

        private void UpdateShortcutRows()
        {
            bool both = itemShortcuts != null && itemShortcuts.gameObject.activeSelf && ShowBothShortcutRows;
            if (shownShortcutOffset != (both ? 0 : ShortcutOffset) || shiftShortcuts != null && shiftShortcuts.gameObject.activeSelf != both)
                RefreshItemShortcutBar();
        }

        private void ReadItemShortcuts()
        {
            if (!ShortcutsAvailable || IsShopOpen || IsHoldingItem || skillShortcutGhost != null || IsTextInputFocused)
                return;

            for (int index = 0; index < ShortcutActions.Length; index++)
                if (WorkerbornInput.Pressed(ShortcutActions[index]))
                    UseShortcut(index + ShortcutOffset);
        }

        internal void ClickItemShortcut(int binding, PointerEventData.InputButton button)
        {
            if (!ShortcutsAvailable || skillShortcutGhost != null)
                return;
            if (button == PointerEventData.InputButton.Right)
            {
                if (suppressRightContext)
                {
                    suppressRightContext = false;
                    return;
                }
                if (!IsHoldingItem && (game.State.ItemShortcutAt(binding) != null || game.State.SkillShortcutAt(binding).HasValue))
                {
                    HideItemTooltip();
                    game.Perform(() => game.State.TryClearItemShortcut(binding));
                }
            }
            else if (button == PointerEventData.InputButton.Left)
            {
                if (IsHoldingItem)
                    AssignHeldItemShortcut(binding);
                else if (!IsShopOpen)
                    UseShortcut(binding);
            }
        }

        private void AssignHeldItemShortcut(int binding)
        {
            if (ShortcutsAvailable && IsHoldingItem &&
                game.Perform(() => game.State.TryAssignItemShortcut(binding, draggedEntry)))
                CancelItemDrag();
        }

        private void UseShortcut(int index)
        {
            if (game.State.SkillShortcutAt(index) is SkillId skill)
            {
                game.UseSkill(skill);
                return;
            }
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
            if (IsHoldingItem || skillShortcutGhost != null)
                return;
            var state = game.State;
            if (state.SkillShortcutAt(index) is SkillId skill)
            {
                var view = OpenWindow("tooltip") as ItemTooltipView;
                if (view != null)
                {
                    view.Show(state.Rules.Skill(skill).Name, SkillProgression.RankLabel(state.RankOf(skill)));
                    ShowAt(view, screen + new Vector2(16, -12));
                }
                return;
            }
            string id = state.ItemShortcutAt(index);
            if (id == null)
                return;
            var spec = ItemCatalog.Get(id);
            var condition = state.IsEquipped(spec) ? state.EquippedCondition(spec.EquipmentSlot) :
                state.FindShortcutEntry(index)?.Condition ?? default;
            ShowItemTooltip(id, screen, condition: condition);
        }

        internal bool BeginSkillShortcutDrag(SkillId skill, Vector2 screen)
        {
            if (!ShortcutsAvailable || IsHoldingItem || skillShortcutGhost != null || !game.State.IsSkillLearned(skill))
                return false;
            HideItemTooltip();
            var icon = Instantiate(library.Item, transform);
            icon.ShowIconOnly();
            icon.DisplayIcon(game.Settings.FindSkill(skill)?.Icon, game.State.Rules.Skill(skill).Name);
            skillShortcutGhost = icon.Rect;
            skillShortcutGhost.anchorMin = skillShortcutGhost.anchorMax = new Vector2(.5f, .5f);
            skillShortcutGhost.pivot = new Vector2(.5f, .5f);
            skillShortcutGhost.sizeDelta = new Vector2(42f, 42f);
            MoveSkillShortcutDrag(screen);
            UpdateShortcutRows();
            return true;
        }

        internal void MoveSkillShortcutDrag(Vector2 screen)
        {
            if (skillShortcutGhost != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screen, null, out var local))
                skillShortcutGhost.anchoredPosition = local - CanvasRect.rect.center;
        }

        internal void EndSkillShortcutDrag(SkillId skill, Vector2 screen)
        {
            if (skillShortcutGhost == null)
                return;
            var slot = game.Targeting?.UiAt(screen)?.GetComponentInParent<ItemShortcutSlotView>();
            CancelSkillShortcutDrag();
            if (ShortcutsAvailable && slot != null)
                game.Perform(() => game.State.TryAssignSkillShortcut(slot.Binding, skill));
        }

        internal void CancelSkillShortcutDrag()
        {
            if (skillShortcutGhost != null)
                Destroy(skillShortcutGhost.gameObject);
            skillShortcutGhost = null;
        }

        internal void SelectEquipmentSet(int set)
        {
            if (game.State.AwaitingIncarnation || game.IsWorldTransitioning || game.AwaitingWorldCommand ||
                HasQuantityPrompt || WorldActionsLocked && !IsShopOpen || IsChatTyping || IsTextInputFocused ||
                IsHoldingItem || skillShortcutGhost != null || game.Worker.CarriedLogs > 0 || game.State.EquipmentSet == set)
                return;
            game.Perform(() =>
            {
                if (!game.State.TrySelectEquipmentSet(set))
                    return false;
                var worker = game.Worker;
                if (worker.Action is WorkerAction.Harvesting or WorkerAction.Attacking || worker.IsApproachingInteraction)
                    worker.Cancel();
                return true;
            });
        }
    }
}
