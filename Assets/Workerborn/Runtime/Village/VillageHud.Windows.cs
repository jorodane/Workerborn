using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private RectTransform inventoryGrid;
        private RectTransform dragGhost;
        private int selectedEntry;
        private int draggedEntry;
        private Vector2 dragGrabOffset;
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

        private void RenderInventory()
        {
            if (views.TryGetValue("inventory", out var view) && view.gameObject.activeSelf)
                view.Refresh();
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
            else if (item.ItemId == ItemIds.Bread)
                game.Perform(game.State.TryEatBread);
            else if (item.Spec.EquipmentSlot != EquipmentSlot.None)
                game.Perform(() => game.State.TryEquip(item.Id));
            CloseItemContext();
        }

        public void ClickEquipment(EquipmentSlot slot, Vector2 screen)
        {
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

        public void ShowItemTooltip(int id, Vector2 screen)
        {
            if (IsHoldingItem)
                return;
            var item = game.State.Inventory.Find(id);
            if (item == null)
                return;
            var view = OpenWindow("tooltip") as ItemTooltipView;
            view?.Show(item.Spec);
            if (view != null)
                ShowAt(view, screen + new Vector2(16, -12));
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
            bool success = game.State.Inventory.TrySplit(selectedEntry);
            CloseItemContext();
            if (success)
            {
                RenderInventory();
                game.SaveGame(false);
            }
            else
                ShowToast("나눌 공간이 부족합니다.");
        }

        public void BeginItemDrag(int id, Vector2 screen)
        {
            CancelItemDrag();
            var item = game.State.Inventory.Find(id);
            if (item == null)
                return;
            draggedEntry = id;
            selectedEntry = id;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out Vector2 local);
            dragGrabOffset = local - new Vector2(item.X * CellX, -item.Y * CellY);
            if (dragGrabOffset.x < 0f || dragGrabOffset.x > item.Spec.Width * CellX || dragGrabOffset.y > 0f || dragGrabOffset.y < -item.Spec.Height * CellY)
                dragGrabOffset = new Vector2(CellX * 0.5f, -CellY * 0.5f);
            RebuildHeldVisual();
            RenderInventory();
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
            visual.Passive();
        }

        public void MoveItemDrag(Vector2 screen)
        {
            if (dragGhost == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local);
            dragGhost.anchoredPosition = local - dragGrabOffset;
        }

        public void EndItemDrag(Vector2 screen)
        {
            if (!IsHoldingItem || inventoryGrid == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out Vector2 local);
            local -= dragGrabOffset;
            bool success = game.State.Inventory.TryPlaceHeld(draggedEntry, Mathf.FloorToInt(local.x / CellX), Mathf.FloorToInt(-local.y / CellY), out int next);
            if (!success)
                return;
            draggedEntry = next;
            if (next == 0)
                CancelItemDrag();
            else
                RebuildHeldVisual();
            RenderInventory();
            MoveItemDrag(screen);
            game.SaveGame(false);
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
            HideItemTooltip();
            CloseItemContext();
            if (wasHolding && InventoryOpen)
                RenderInventory();
        }
    }
}
