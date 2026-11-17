using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private RectTransform inventoryContent;
        private RectTransform inventoryGrid;
        private RectTransform dragGhost;
        private RectTransform itemTooltip;
        private RectTransform itemContext;
        private int selectedEntry;
        private int draggedEntry;
        private Vector2 dragGrabOffset;
        private const float Cell = 40f;

        public void RefreshWindows()
        {
            if (IsWindowOpen("inventory")) RenderInventory();
            if (IsWindowOpen("quests")) RenderQuests();
            if (IsWindowOpen("skills")) RenderSkills();
            if (IsWindowOpen("character")) RenderCharacter();
            if (dialogueTarget != null) RenderDialogue();
        }

        private bool IsWindowOpen(string id) => windows.TryGetValue(id, out var window) && window.gameObject.activeSelf;

        private RectTransform Content(string id)
        {
            var window = windows[id];
            for (int i = window.childCount - 1; i >= 1; i--)
            {
                window.GetChild(i).gameObject.SetActive(false);
                Destroy(window.GetChild(i).gameObject);
            }
            var content = new GameObject("Window content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(window, false); content.anchorMin = content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, 1);
            content.anchoredPosition = new Vector2(0, -32); content.sizeDelta = new Vector2(window.rect.width, window.rect.height - 32);
            return content;
        }

        private void BuildInventoryWindow()
        {
            Window("inventory", "소지품", new Vector2(604, 422), new Vector2(250, 0));
            RenderInventory();
        }

        private void RenderInventory()
        {
            if (draggedEntry != 0 && game.State.Inventory.Find(draggedEntry) == null) CancelItemDrag();
            var inventory = game.State.Inventory;
            var window = windows["inventory"];
            window.sizeDelta = new Vector2(604, Mathf.Max(422f, 130f + inventory.Rows * Cell));
            inventoryContent = Content("inventory");
            Label("장비", inventoryContent, new Vector2(12, -5), new Vector2(220, 24), 12, ink);
            Label("기본 소지품", inventoryContent, new Vector2(248, -5), new Vector2(342, 24), 12, ink);
            var equipment = Panel("Equipment", inventoryContent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, 310), new Vector2(12, -32), new Color(0.79f, 0.8f, 0.8f));
            if (game.PortraitTexture != null)
            {
                var portrait = new GameObject("Character", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
                var rect = portrait.rectTransform; rect.SetParent(equipment, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(60, -22); rect.sizeDelta = new Vector2(114, 228);
                portrait.texture = game.PortraitTexture; portrait.raycastTarget = false;
            }
            EquipmentSlotView(equipment, EquipmentSlot.Head, "머리", new Vector2(178, -10), new Vector2(34, 38));
            EquipmentSlotView(equipment, EquipmentSlot.Body, "몸", new Vector2(174, -62), new Vector2(38, 76));
            EquipmentSlotView(equipment, EquipmentSlot.Hands, "손", new Vector2(8, -40), new Vector2(34, 38));
            EquipmentSlotView(equipment, EquipmentSlot.MainHand, "오른손", new Vector2(8, -96), new Vector2(38, 108));
            EquipmentSlotView(equipment, EquipmentSlot.OffHand, "왼손", new Vector2(174, -160), new Vector2(38, 76));
            EquipmentSlotView(equipment, EquipmentSlot.Feet, "발", new Vector2(88, -260), new Vector2(50, 38));
            EquipmentSlotView(equipment, EquipmentSlot.Accessory, "장신구", new Vector2(8, -260), new Vector2(38, 38));
            inventoryGrid = Panel("Inventory grid", inventoryContent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(inventory.Columns * Cell, inventory.Rows * Cell),
                new Vector2(248, -32), new Color(0.38f, 0.42f, 0.44f), true);
            inventoryGrid.gameObject.AddComponent<InventoryGridPointer>().Hud = this;
            for (int y = 0; y < inventory.Rows; y++) for (int x = 0; x < inventory.Columns; x++)
                Panel("Slot", inventoryGrid, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Cell - 2, Cell - 2),
                    new Vector2(x * Cell + 1, -y * Cell - 1), new Color(0.83f, 0.84f, 0.83f));
            foreach (var entry in inventory.Entries)
            {
                if (entry.Id == draggedEntry) continue;
                var item = Panel(entry.Spec.Name, inventoryGrid, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(entry.Spec.Width * Cell - 3, entry.Spec.Height * Cell - 3), new Vector2(entry.X * Cell + 1.5f, -entry.Y * Cell - 1.5f), new Color(0.68f, 0.71f, 0.73f), true);
                ItemVisual(item, entry.ItemId, entry.Quantity);
                var pointer = item.gameObject.AddComponent<InventoryItemDrag>(); pointer.Hud = this; pointer.EntryId = entry.Id;
            }
            Label($"소지금  {game.State.Gold:N0} G", inventoryContent, new Vector2(248, -48 - inventory.Rows * Cell), new Vector2(320, 26), 12, ink).alignment = TextAnchor.MiddleRight;
        }

        private void ItemVisual(RectTransform rect, string id, int quantity)
        {
            var definition = game.Settings.FindItem(id);
            if (definition != null && definition.Icon != null)
            {
                var image = new GameObject("Item icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                image.rectTransform.SetParent(rect, false); image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
                image.rectTransform.offsetMin = new Vector2(2, 2); image.rectTransform.offsetMax = new Vector2(-2, -2);
                image.sprite = definition.Icon; image.preserveAspect = true; image.raycastTarget = false;
            }
            else Label(ShortItemName(id), rect, Vector2.zero, rect.sizeDelta, 11, ink).alignment = TextAnchor.MiddleCenter;
            if (quantity > 1)
            {
                var count = Label(quantity.ToString(), rect, new Vector2(2, -rect.sizeDelta.y + 18), new Vector2(rect.sizeDelta.x - 4, 18), 10, Color.white, true);
                count.alignment = TextAnchor.MiddleRight; count.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
            }
        }

        private void EquipmentSlotView(RectTransform parent, EquipmentSlot slot, string title, Vector2 position, Vector2 size)
        {
            var rect = Panel(title, parent, new Vector2(0, 1), new Vector2(0, 1), size, position, new Color(0.67f, 0.7f, 0.72f, 0.85f), true);
            if (game.State.Equipment.TryGetValue(slot, out string item)) ItemVisual(rect, item, 1);
            else Label(title, rect, Vector2.zero, size, 10, new Color(0.35f, 0.38f, 0.4f)).alignment = TextAnchor.MiddleCenter;
            var pointer = rect.gameObject.AddComponent<EquipmentSlotPointer>(); pointer.Hud = this; pointer.Slot = slot;
        }

        public void ClickInventoryItem(int id, Vector2 screen)
        {
            HideItemTooltip(); CloseItemContext();
            if (IsHoldingItem) { EndItemDrag(screen); return; }
            selectedEntry = id;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)) { UseSelectedInventoryItem(); return; }
            BeginItemDrag(id, screen);
        }

        public void SelectInventoryItem(int id) => selectedEntry = id;

        public void UseSelectedInventoryItem()
        {
            var item = game.State.Inventory.Find(selectedEntry);
            if (item == null) return;
            if (item.ItemId == ItemIds.Bread) game.Perform(game.State.TryEatBread);
            else if (item.Spec.EquipmentSlot != EquipmentSlot.None) game.Perform(() => game.State.TryEquip(item.Id));
            CloseItemContext();
        }

        public void ClickEquipment(EquipmentSlot slot, Vector2 screen)
        {
            if (IsHoldingItem)
            {
                var item = game.State.Inventory.Find(draggedEntry);
                if (item == null || item.Spec.EquipmentSlot != slot) { ShowToast("해당 위치에 장착할 수 없습니다."); return; }
                if (game.Perform(() => game.State.TryEquip(item.Id))) CancelItemDrag();
            }
            else if (game.State.Equipment.TryGetValue(slot, out string id) && game.Perform(() => game.State.TryUnequip(slot)))
            {
                InventoryEntry newest = null;
                foreach (var item in game.State.Inventory.Entries) if (item.ItemId == id && (newest == null || item.Id > newest.Id)) newest = item;
                if (newest != null) BeginItemDrag(newest.Id, screen);
            }
        }

        public void OpenItemContext(int id, Vector2 screen)
        {
            if (suppressRightContext) { suppressRightContext = false; return; }
            if (IsHoldingItem) { CancelItemDrag(); return; }
            selectedEntry = id; HideItemTooltip(); CloseItemContext();
            var item = game.State.Inventory.Find(id); if (item == null) return;
            itemContext = Panel("Item menu", transform, new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(128, 90), Vector2.zero, paper, true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local); itemContext.anchoredPosition = local;
            var use = Button(item.Spec.EquipmentSlot != EquipmentSlot.None ? "장착" : "사용", itemContext, new Vector2(4, -4), new Vector2(120, 24), UseSelectedInventoryItem);
            use.interactable = item.ItemId == ItemIds.Bread || item.Spec.EquipmentSlot != EquipmentSlot.None;
            var split = Button("나누기", itemContext, new Vector2(4, -32), new Vector2(120, 24), SplitSelectedInventoryItem); split.interactable = item.Quantity > 1;
            Button("닫기", itemContext, new Vector2(4, -60), new Vector2(120, 24), CloseItemContext);
        }

        private void CloseItemContext() { if (itemContext != null) { itemContext.gameObject.SetActive(false); Destroy(itemContext.gameObject); } itemContext = null; }

        public void ShowItemTooltip(int id, Vector2 screen)
        {
            if (IsHoldingItem) return;
            var item = game.State.Inventory.Find(id); if (item == null) return;
            HideItemTooltip();
            itemTooltip = Panel("Item information", transform, new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(270, 108), Vector2.zero, paper);
            Label(item.Spec.Name, itemTooltip, new Vector2(10, -4), new Vector2(250, 26), 14, ink, true);
            Label(item.Spec.Description, itemTooltip, new Vector2(10, -37), new Vector2(250, 65), 12, ink);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local);
            itemTooltip.anchoredPosition = local + new Vector2(16, -10);
        }
        public void HideItemTooltip() { if (itemTooltip != null) Destroy(itemTooltip.gameObject); itemTooltip = null; }

        private void SplitSelectedInventoryItem()
        {
            bool success = game.State.Inventory.TrySplit(selectedEntry);
            CloseItemContext();
            if (success) { RenderInventory(); game.SaveGame(false); } else ShowToast("나눌 공간이 부족합니다.");
        }

        public void BeginItemDrag(int id, Vector2 screen)
        {
            CancelItemDrag();
            var item = game.State.Inventory.Find(id); if (item == null) return;
            draggedEntry = id; selectedEntry = id;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out Vector2 local);
            dragGrabOffset = local - new Vector2(item.X * Cell, -item.Y * Cell);
            if (dragGrabOffset.x < 0f || dragGrabOffset.x > item.Spec.Width * Cell || dragGrabOffset.y > 0f || dragGrabOffset.y < -item.Spec.Height * Cell)
                dragGrabOffset = new Vector2(Cell * 0.5f, -Cell * 0.5f);
            RebuildHeldVisual(); RenderInventory(); MoveItemDrag(screen);
        }

        private void RebuildHeldVisual()
        {
            if (dragGhost != null) Destroy(dragGhost.gameObject);
            var item = game.State.Inventory.Find(draggedEntry);
            if (item == null) { draggedEntry = 0; dragGhost = null; return; }
            dragGhost = Panel("Held item", transform, new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(item.Spec.Width * Cell, item.Spec.Height * Cell), Vector2.zero, new Color(0.7f, 0.73f, 0.76f, 0.65f));
            ItemVisual(dragGhost, item.ItemId, item.Quantity);
        }

        public void MoveItemDrag(Vector2 screen)
        {
            if (dragGhost == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local);
            dragGhost.anchoredPosition = local - dragGrabOffset;
        }

        public void EndItemDrag(Vector2 screen)
        {
            if (!IsHoldingItem || inventoryGrid == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out Vector2 local); local -= dragGrabOffset;
            bool success = game.State.Inventory.TryPlaceHeld(draggedEntry, Mathf.FloorToInt(local.x / Cell), Mathf.FloorToInt(-local.y / Cell), out int next);
            if (!success) return;
            draggedEntry = next;
            if (next == 0) CancelItemDrag(); else RebuildHeldVisual();
            RenderInventory(); MoveItemDrag(screen); game.SaveGame(false);
        }

        public void CancelItemDrag()
        {
            bool wasHolding = draggedEntry != 0;
            if (dragGhost != null) { dragGhost.gameObject.SetActive(false); Destroy(dragGhost.gameObject); }
            dragGhost = null; draggedEntry = 0; HideItemTooltip(); CloseItemContext();
            if (wasHolding && InventoryOpen) RenderInventory();
        }

        private static string ShortItemName(string id) => ItemCatalog.Get(id).Name;

        private void BuildQuestWindow()
        {
            Window("quests", "퀘스트", new Vector2(510, 485), new Vector2(-240, 10));
            RenderQuests();
        }

        private void RenderQuests()
        {
            var content = Content("quests"); var state = game.State; var rules = state.Rules;
            Label(rules.IntroTitle, content, new Vector2(20, -12), new Vector2(470, 30), 18, ink, true);
            Label((state.IntroClaimed ? "완료" : state.Career >= rules.IntroRequiredJobs ? "보고 가능" : "진행 중") + "\n" + rules.IntroDescription,
                content, new Vector2(20, -52), new Vector2(470, 90), 13, ink);
            Label(rules.ExpansionTitle, content, new Vector2(20, -158), new Vector2(470, 30), 18, ink, true);
            Label(rules.ExpansionDescription + $"\n기부금 {state.DonatedGold}/{rules.DonationGold}G\n물품 {state.DonatedPotatoes}/{rules.DonationItems}개" +
                (state.FarmUpgradePending ? "\n완료 · 다음 환생 시 반영" : state.FarmLevel >= rules.ExpandedStage ? "\n완료" : ""),
                content, new Vector2(20, -198), new Vector2(470, 118), 13, ink);
            if (state.Job == JobStatus.Active)
            {
                Label("아르바이트 · " + rules.JobTitle, content, new Vector2(20, -338), new Vector2(470, 26), 16, ink, true);
                Label($"{ItemCatalog.Get(rules.JobItemId).Name} {state.Inventory.Count(rules.JobItemId)}/{state.JobRequirement}개 · {rules.JobEnd / 60:00}:{rules.JobEnd % 60:00}까지",
                    content, new Vector2(20, -376), new Vector2(470, 28), 13, ink);
            }
        }

        private void BuildSkillsWindow()
        {
            Window("skills", "스킬", new Vector2(416, 365), new Vector2(-290, -65));
            RenderSkills();
        }

        private void RenderSkills()
        {
            var content = Content("skills"); var state = game.State;
            Label("감자 수확  " + RankName(state.SkillRank), content, new Vector2(22, -20), new Vector2(372, 36), 25, ink, true);

            Label($"작업 속도  ×{state.WorkRate:0.00}\n수확 에너지  {state.HarvestEnergyCost}\n럭키 보너스 확률  {10 + state.SkillRank * 2}%\n기본 수확량  {2 + state.SkillRank / 3}개\n\n레벨업마다 AP {state.Rules.APPerLevel}를 얻습니다.\n생활 스킬과 남은 AP는 환생 후에도 유지됩니다.",
                content, new Vector2(22, -78), new Vector2(372, 166), 15, ink);
            var upgrade = Button($"승급  AP {state.NextSkillCost}  /  보유 {state.AP}", content, new Vector2(22, -272), new Vector2(372, 36),
                () => game.Perform(game.State.TryUpgradeSkill), true);
            upgrade.interactable = state.SkillRank < 8 && state.AP >= state.NextSkillCost;
        }

        private void BuildCharacterWindow()
        {
            Window("character", "캐릭터", new Vector2(450, 470), new Vector2(-30, 25));
            RenderCharacter();
        }

        private void RenderCharacter()
        {
            var content = Content("character"); var state = game.State;
            Label($"{state.Life}번째 생의 일꾼", content, new Vector2(24, -20), new Vector2(402, 35), 25, ink, true);
            Label($"현재 레벨 {state.Level} / 200    누적 레벨 {state.TotalLevel}\n남은 AP {state.AP}    수확 랭크 {RankName(state.SkillRank)}\n농장 아르바이트 경력 {state.Career}회  ·  의뢰 {state.CareerTier}등급\n지금까지 수확 {state.LifetimeHarvests}회", content,
                new Vector2(24, -70), new Vector2(402, 106), 16, ink);
            Label("이번 생의 재산", content, new Vector2(24, -193), new Vector2(402, 25), 18, ink, true);
            Label($"{state.Gold:N0}G  ·  감자 {state.Inventory.Count(ItemIds.Potato)}개\n도구: {(state.HasHoe ? "튼튼한 괭이" : "기본 작업") }  ·  가방: {(state.BagExpanded ? "여행 가방" : "기본 소지품")}",
                content, new Vector2(24, -231), new Vector2(402, 55), 15, ink);
            Label("환생하면 레벨·골드·소지품·장비가 초기화됩니다.\n스킬·AP·경력·누적 레벨·마을에 남긴 변화는 유지됩니다.", content,
                new Vector2(24, -304), new Vector2(402, 58), 14, ink);

        }

        private void BuildMapWindow()
        {
            var panel = Window("map", "솔바람 마을 지도", new Vector2(650, 504), Vector2.zero);

            var root = Panel("Large village map", panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(610, 348), new Vector2(20, -60), new Color(0.49f, 0.6f, 0.44f), true);
            DrawMap(root, true);
            Label("농: 미라의 농장    상: 노아의 잡화점    숙: 레나의 여관    샘: 환생", panel,
                new Vector2(20, -463), new Vector2(610, 26), 14, ink);
        }

        private void DrawMap(RectTransform map, bool large)
        {
            map.gameObject.AddComponent<VillageMapClick>().Game = game;
            var image = new GameObject("Scene map", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            image.rectTransform.SetParent(map, false); image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero; image.texture = game.MinimapTexture; image.raycastTarget = false;
            var markers = new System.Collections.Generic.Dictionary<VillageInteractable, RectTransform>();
            foreach (var target in game.Interactables)
            {
                string label = target.Role == VillageRole.Farmer ? "농" : target.Role == VillageRole.Merchant ? "상" : target.Role == VillageRole.Innkeeper ? "숙" : target.Role == VillageRole.Shrine ? "샘" : "!";
                var text = Label(label, map, Vector2.zero, new Vector2(large ? 32 : 20, large ? 28 : 20), large ? 16 : 11, Color.white, true);
                text.alignment = TextAnchor.MiddleCenter; text.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero; text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                markers.Add(target, text.rectTransform);
            }
            var player = Panel("Player position", map, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.one * (large ? 8 : 5), Vector2.zero, Color.white);
            maps.Add(new MapView { Root = map, Player = player, Markers = markers });
        }

        private Vector2 MapPosition(RectTransform map, Vector3 position)
        {
            Vector3 point = game.MinimapCamera != null ? game.MinimapCamera.WorldToViewportPoint(position) : Vector3.zero;
            return new Vector2(point.x * map.rect.width, point.y * map.rect.height);
        }

        private void UpdateMaps()
        {
            for (int i = maps.Count - 1; i >= 0; i--)
            {
                var view = maps[i];
                if (view.Root == null) { maps.RemoveAt(i); continue; }
                view.Player.anchoredPosition = MapPosition(view.Root, game.Worker.transform.position);
                foreach (var marker in view.Markers) if (marker.Key != null) marker.Value.anchoredPosition = MapPosition(view.Root, marker.Key.transform.position);
            }
        }
    }
}
