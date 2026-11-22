using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private RectTransform inventoryGrid;
        private RectTransform dragGhost;
        private int selectedEntry;
        private int draggedEntry;
        private bool releaseHeldOnMouseUp;
        private float CellX => views.TryGetValue("inventory", out var view) ? ((InventoryWindowView)view).CellSize.x : 32f;
        private float CellY => views.TryGetValue("inventory", out var view) ? ((InventoryWindowView)view).CellSize.y : 32f;

        private void DrawMap(RectTransform root, bool large)
        {
            var map = root.GetComponent<VillageMapView>();
            if (map != null)
                map.Bind(game);
        }

        public void RefreshWindows()
        {
            foreach (var view in views.Values)
                if (view != null && view.gameObject.activeSelf)
                    view.Refresh();
        }

        private void RefreshInventoryItems()
        {
            if (views.TryGetValue("inventory", out var view) && view is InventoryWindowView inventory && view.gameObject.activeSelf)
                RenderInventoryItems(inventory);
        }

        public void RenderInventoryItems(InventoryWindowView view)
        {
            inventoryGrid = view.ItemLayer;
            if (draggedEntry != 0 && game.State.Inventory.Find(draggedEntry) == null)
            {
                draggedEntry = 0;
                if (dragGhost != null)
                    Destroy(dragGhost.gameObject);
                dragGhost = null;
            }

            InventoryWindowView.Clear(inventoryGrid);
            foreach (var entry in game.State.Inventory.Entries)
            {
                if (entry.Id == draggedEntry)
                    continue;
                var item = Instantiate(library.Item, inventoryGrid);
                item.Rect.sizeDelta = new Vector2(entry.Spec.Width * CellX - 2, entry.Spec.Height * CellY - 2);
                item.Rect.anchoredPosition = new Vector2(entry.X * CellX, -entry.Y * CellY);
                item.Display(game.Settings.FindItem(entry.ItemId), entry.ItemId, entry.Quantity);
                item.Inventory(this, entry.Id);
            }
        }

        public void ClickInventoryItem(int id, Vector2 screen)
        {
            if (HasQuantityPrompt)
            {
                if (IsShopOpen && views["quantity"] is QuantityWindowView prompt && prompt.IsSelling &&
                    game.State.MaximumSellQuantity(id) > 0)
                    OpenStackQuantity(game.State.Inventory.Find(id), QuantityAction.Sell);
                return;
            }
            HideItemTooltip();
            CloseItemContext();
            if (IsHoldingItem)
            {
                EndItemDrag(screen);
                return;
            }

            selectedEntry = id;
            var item = game.State.Inventory.Find(id);
            if (item != null && game.State.TryGetDocumentPage(item.ItemId, out var page))
            {
                OpenQuestDetail(page);
                return;
            }
            if (WorkerbornInput.Held("Gameplay/Target"))
            {
                UseSelectedInventoryItem();
                return;
            }

            BeginItemDrag(id, screen);
        }

        public void SelectInventoryItem(int id) => selectedEntry = id;
        public void UseSelectedInventoryItem()
        {
            var item = game.State.Inventory.Find(selectedEntry);
            if (item == null)
                return;
            if (game.State.TryGetDocumentPage(item.ItemId, out var page))
                OpenQuestDetail(page);
            else if (WorldActionsLocked)
                return;
            else if (item.ItemId == ItemIds.Bread)
                game.Perform(game.State.TryEatBread);
            else if (item.Spec.EquipmentSlot != EquipmentSlot.None)
                game.Perform(() => game.State.TryEquip(item.Id));
            CloseItemContext();
        }

        public void ClickEquipment(EquipmentSlot slot, Vector2 screen)
        {
            if (WorldActionsLocked)
                return;
            if (IsHoldingItem)
            {
                var item = game.State.Inventory.Find(draggedEntry);
                if (item == null || item.Spec.EquipmentSlot != slot)
                {
                    ShowToast("해당 위치에 장착할 수 없습니다.");
                    return;
                }

                if (game.Perform(() => game.State.TryEquip(item.Id)))
                    CancelItemDrag();
            }
            else if (game.State.Equipment.TryGetValue(slot, out string id) && game.Perform(() => game.State.TryUnequip(slot)))
            {
                InventoryEntry newest = null;
                foreach (var item in game.State.Inventory.Entries)
                    if (item.ItemId == id && (newest == null || item.Id > newest.Id))
                        newest = item;
                if (newest != null)
                    BeginItemDrag(newest.Id, screen);
            }
        }

        public void OpenItemContext(int id, Vector2 screen)
        {
            if (HasQuantityPrompt)
                return;
            if (suppressRightContext)
            {
                suppressRightContext = false;
                return;
            }

            if (IsHoldingItem)
            {
                CancelItemDrag();
                return;
            }

            selectedEntry = id;
            HideItemTooltip();
            var item = game.State.Inventory.Find(id);
            if (item == null)
                return;
            var view = OpenWindow("itemcontext") as ItemContextView;
            view?.Show(item);
            if (view != null)
                ShowAt(view, screen);
        }

        private void CloseItemContext()
        {
            if (views.TryGetValue("itemcontext", out var view))
                view.gameObject.SetActive(false);
        }

        public void ShowItemTooltip(string id, Vector2 screen)
        {
            if (IsHoldingItem || string.IsNullOrEmpty(id))
                return;
            var view = OpenWindow("tooltip") as ItemTooltipView;
            view?.Show(ItemCatalog.Get(id));
            if (view != null)
                ShowAt(view, screen + new Vector2(16, -12));
        }

        internal void ShowEquipmentTooltip(EquipmentSlot slot, Vector2 screen)
        {
            if (game.State.Equipment.TryGetValue(slot, out string item))
                ShowItemTooltip(item, screen);
        }

        internal void InspectGroundItem(string id, int quantity)
        {
            HideItemTooltip();
            (OpenWindow("itemdetail") as ItemTooltipView)?.Show(ItemCatalog.Get(id), quantity);
        }

        public void HideItemTooltip()
        {
            if (views.TryGetValue("tooltip", out var view))
                view.gameObject.SetActive(false);
        }

        public void SplitSelectedInventoryItem()
        {
            var item = game.State.Inventory.Find(selectedEntry);
            if (item != null && game.State.TryGetDocumentPage(item.ItemId, out _))
            {
                CloseItemContext();
                BeginItemDrag(item.Id, WorkerbornInput.Point);
                return;
            }
            CloseItemContext();
            if (item == null || item.Quantity < 2)
                return;
            if (game.State.Inventory.MaximumSplitQuantity(item.Id) == 0)
            {
                ShowToast("나눌 공간이 부족합니다.");
                return;
            }
            OpenStackQuantity(item, QuantityAction.Split);
        }

        public void BeginItemDrag(int id, Vector2 screen, bool releaseOnMouseUp = false)
        {
            CancelItemDrag();
            var item = game.State.Inventory.Find(id);
            if (item == null || inventoryGrid == null)
                return;
            CloseQuantity();
            releaseHeldOnMouseUp = releaseOnMouseUp;
            draggedEntry = id;
            selectedEntry = id;
            RebuildHeldVisual();
            RefreshInventoryItems();
            MoveItemDrag(screen);
        }

        private void RebuildHeldVisual()
        {
            if (dragGhost != null)
                Destroy(dragGhost.gameObject);
            var item = game.State.Inventory.Find(draggedEntry);
            if (item == null)
            {
                draggedEntry = 0;
                dragGhost = null;
                return;
            }

            var visual = Instantiate(library.Item, transform);
            dragGhost = visual.Rect;
            dragGhost.anchorMin = dragGhost.anchorMax = new Vector2(.5f, .5f);
            dragGhost.pivot = new Vector2(0, 1);
            dragGhost.sizeDelta = new Vector2(item.Spec.Width * CellX, item.Spec.Height * CellY);
            visual.Display(game.Settings.FindItem(item.ItemId), item.ItemId, item.Quantity);
            visual.ShowHeldIcon();
        }

        public void MoveItemDrag(Vector2 screen)
        {
            if (dragGhost == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local);
            dragGhost.anchoredPosition = local - CanvasRect.rect.center;
            if (views.TryGetValue("inventory", out var window) && window is InventoryWindowView view)
            {
                var entry = game.State.Inventory.Find(draggedEntry);
                if (entry != null && TryInventoryCell(screen, view, out var cell) && game.State.Inventory.CanPlaceHeld(draggedEntry, cell.x, cell.y))
                    view.ShowPlacement(cell.x, cell.y, entry.Spec.Width, entry.Spec.Height);
                else
                    view.ShowPlacement(0, 0, 0, 0);
            }
        }

        public void EndItemDrag(Vector2 screen)
        {
            if (!IsHoldingItem || !views.TryGetValue("inventory", out var window) || !(window is InventoryWindowView view)
                || !TryInventoryCell(screen, view, out var cell))
                return;
            bool success = game.State.Inventory.TryPlaceHeld(draggedEntry, cell.x, cell.y, out int next);
            if (!success)
                return;
            draggedEntry = next;
            if (next == 0)
                CancelItemDrag();
            else
                RebuildHeldVisual();
            RefreshInventoryItems();
            MoveItemDrag(screen);
            game.SaveGame(false);
        }

        private bool TryInventoryCell(Vector2 screen, InventoryWindowView view, out Vector2Int cell)
        {
            cell = default;
            if (!InventoryOpen || view.ItemViewport == null || !view.ItemViewport.gameObject.activeInHierarchy
                || !RectTransformUtility.RectangleContainsScreenPoint(view.ItemViewport, screen)
                || !RectTransformUtility.RectangleContainsScreenPoint(inventoryGrid, screen))
                return false;
            GameObject hit = game.Targeting?.UiAt(screen);
            if (hit != null && hit.GetComponentInParent<InventoryWindowView>() != view)
                return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out var local))
                return false;
            var gridBounds = inventoryGrid.rect;
            cell = new Vector2Int(Mathf.FloorToInt((local.x - gridBounds.xMin) / CellX),
                Mathf.FloorToInt((gridBounds.yMax - local.y) / CellY));
            return true;
        }

        public void DropHeldOnGround(Vector2 screen)
        {
            if (!IsHoldingItem || releaseHeldOnMouseUp || game.Targeting == null)
                return;
            if (!game.Targeting.TryDropPoint(screen, out var ground))
            {
                ShowToast("아이템을 놓을 바닥을 선택해 주세요.");
                return;
            }
            if (game.Worker.CommandDrop(draggedEntry, ground))
                CancelItemDrag();
        }

        private void ReleaseDraggedItem()
        {
            if (!releaseHeldOnMouseUp || !WorkerbornInput.Released("Gameplay/Interact"))
                return;
            releaseHeldOnMouseUp = false;
            if (!IsHoldingItem || game.Targeting == null)
                return;
            Vector2 screen = WorkerbornInput.Point;
            GameObject hit = game.Targeting.UiAt(screen);
            if (hit == null || hit.GetComponentInParent<WorldNameplateView>() != null)
                DropHeldOnGround(screen);
            else if (hit.GetComponentInParent<ShopSellTarget>() != null)
                SellHeldItem();
            else if (hit.GetComponentInParent<EquipmentSlotPointer>() is EquipmentSlotPointer equipment)
                ClickEquipment(equipment.Slot, screen);
            else
                EndItemDrag(screen);
        }

        public void CancelItemDrag()
        {
            bool wasHolding = draggedEntry != 0;
            if (dragGhost != null)
            {
                dragGhost.gameObject.SetActive(false);
                Destroy(dragGhost.gameObject);
            }

            dragGhost = null;
            draggedEntry = 0;
            releaseHeldOnMouseUp = false;
            if (views.TryGetValue("inventory", out var window) && window is InventoryWindowView view)
                view.ShowPlacement(0, 0, 0, 0);
            HideItemTooltip();
            CloseItemContext();
            if (wasHolding && InventoryOpen)
                RefreshInventoryItems();
        }
    }
}
