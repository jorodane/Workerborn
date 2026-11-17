using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private RectTransform inventoryContent;
        private RectTransform inventoryGrid;
        private RectTransform dragGhost;
        private Text selectedItemInfo;
        private int selectedEntry;
        private int draggedEntry;
        private Vector2 dragGrabOffset;
        private const float Cell = 48f;

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
            content.anchoredPosition = new Vector2(0, -42); content.sizeDelta = new Vector2(window.rect.width, window.rect.height - 42);
            return content;
        }

        private void BuildInventoryWindow()
        {
            Window("inventory", "소지품  [I]", new Vector2(430, 548), new Vector2(300, 15));
            RenderInventory();
        }

        private void RenderInventory()
        {
            CancelItemDrag();
            inventoryContent = Content("inventory");
            var inventory = game.State.Inventory;
            Label($"가방 {inventory.Columns}×{inventory.Rows}  ·  {game.State.Gold:N0} G", inventoryContent, new Vector2(20, -13), new Vector2(390, 25), 16, ink, true);
            Label("드래그: 정리·겹치기  /  빵 우클릭: 사용", inventoryContent, new Vector2(20, -43), new Vector2(390, 23), 12, ink);
            inventoryGrid = Panel("Inventory grid", inventoryContent, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(inventory.Columns * Cell, inventory.Rows * Cell), new Vector2(20, -77), new Color(0.34f, 0.39f, 0.34f));
            for (int y = 0; y < inventory.Rows; y++) for (int x = 0; x < inventory.Columns; x++)
                Panel("Empty slot", inventoryGrid, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Cell - 3, Cell - 3),
                    new Vector2(x * Cell + 1.5f, -y * Cell - 1.5f), new Color(0.79f, 0.8f, 0.7f));
            foreach (var entry in inventory.Entries)
            {
                Color color = ItemColor(entry.ItemId);
                var item = Panel(entry.Spec.Name, inventoryGrid, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(entry.Spec.Width * Cell - 4, entry.Spec.Height * Cell - 4), new Vector2(entry.X * Cell + 2, -entry.Y * Cell - 2), color, true);
                if (entry.Id == selectedEntry)
                {
                    var outline = item.gameObject.AddComponent<Outline>(); outline.effectColor = gold; outline.effectDistance = new Vector2(2, -2);
                }
                var text = Label(ShortItemName(entry.ItemId), item, Vector2.zero, item.sizeDelta, 13, ink, true); text.alignment = TextAnchor.MiddleCenter;
                if (entry.Spec.MaxStack > 1)
                {
                    var number = Label(entry.Quantity.ToString(), item, new Vector2(2, -item.sizeDelta.y + 18), new Vector2(item.sizeDelta.x - 4, 17), 12, ink, true);
                    number.alignment = TextAnchor.MiddleRight;
                }
                var drag = item.gameObject.AddComponent<InventoryItemDrag>(); drag.Hud = this; drag.EntryId = entry.Id;
            }
            selectedItemInfo = Label("물건을 선택하면 설명을 확인할 수 있어요.\n도구와 가방의 효과는 소지하는 동안 적용돼요.", inventoryContent,
                new Vector2(20, -382), new Vector2(390, 65), 13, ink);
            Button("사용", inventoryContent, new Vector2(20, -459), new Vector2(105, 34), UseSelectedInventoryItem);
            Button("반으로 나누기", inventoryContent, new Vector2(137, -459), new Vector2(136, 34), SplitSelectedInventoryItem);
            Button("닫기", inventoryContent, new Vector2(285, -459), new Vector2(119, 34), () => ToggleWindow("inventory"));
            RefreshSelectedItem();
        }

        public void SelectInventoryItem(int id)
        {
            selectedEntry = id;
            if (InventoryOpen) RenderInventory();
            else RefreshSelectedItem();
        }

        private void RefreshSelectedItem()
        {
            var item = game.State.Inventory.Find(selectedEntry);
            if (item == null || selectedItemInfo == null) return;
            selectedItemInfo.text = $"{item.Spec.Name}  ·  {item.Quantity}개  ·  {item.Spec.Width}×{item.Spec.Height}칸\n{item.Spec.Description}";
        }

        public void UseSelectedInventoryItem()
        {
            var item = game.State.Inventory.Find(selectedEntry);
            if (item == null) { ShowToast("가방에서 사용할 물건을 선택해 주세요."); return; }
            if (item.ItemId == ItemIds.Bread) game.Perform(game.State.TryEatBread);
            else ShowToast(item.ItemId == ItemIds.Potato ? "감자는 농부에게 납품하거나 잡화점에서 팔 수 있어요." : "소지 중인 도구와 가방의 효과는 자동으로 적용돼요.");
        }

        private void SplitSelectedInventoryItem()
        {
            bool success = game.State.Inventory.TrySplit(selectedEntry);
            ShowToast(success ? "물건을 반으로 나눴어요." : "2개 이상 중첩된 물건과 빈 칸이 필요해요.");
            if (success) { RenderInventory(); game.SaveGame(false); }
        }

        public void BeginItemDrag(int id, Vector2 screen)
        {
            CancelItemDrag();
            var item = game.State.Inventory.Find(id);
            if (item == null) return;
            draggedEntry = id; selectedEntry = id;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out Vector2 gridPoint);
            dragGrabOffset = gridPoint - new Vector2(item.X * Cell + 2f, -item.Y * Cell - 2f);
            Color color = ItemColor(item.ItemId); color.a = 0.75f;
            dragGhost = Panel("Dragged item", transform, new Vector2(0.5f, 0.5f), new Vector2(0, 1),
                new Vector2(item.Spec.Width * Cell - 4, item.Spec.Height * Cell - 4), Vector2.zero, color);
            var label = Label(ShortItemName(item.ItemId), dragGhost, Vector2.zero, dragGhost.sizeDelta, 14, ink, true); label.alignment = TextAnchor.MiddleCenter;
            MoveItemDrag(screen);
        }

        public void MoveItemDrag(Vector2 screen)
        {
            if (dragGhost == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local);
            dragGhost.anchoredPosition = local - dragGrabOffset;
        }

        public void EndItemDrag(Vector2 screen)
        {
            if (draggedEntry == 0 || inventoryGrid == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(inventoryGrid, screen, null, out Vector2 local);
            local -= dragGrabOffset;
            bool success = game.State.Inventory.TryMove(draggedEntry, Mathf.FloorToInt(local.x / Cell), Mathf.FloorToInt(-local.y / Cell));
            CancelItemDrag();
            if (success) game.SaveGame(false);
            else ShowToast("그 칸에는 물건을 놓을 수 없어요. 원래 자리에 두었어요.");
            RenderInventory();
        }

        public void CancelItemDrag()
        {
            if (dragGhost != null) { dragGhost.gameObject.SetActive(false); Destroy(dragGhost.gameObject); }
            dragGhost = null; draggedEntry = 0;
        }

        private Color ItemColor(string itemId)
        {
            switch (itemId)
            {
                case ItemIds.Potato: return new Color(0.83f, 0.69f, 0.48f);
                case ItemIds.Bread: return new Color(0.91f, 0.79f, 0.55f);
                case ItemIds.Hoe: return new Color(0.64f, 0.72f, 0.69f);
                default: return new Color(0.66f, 0.72f, 0.51f);
            }
        }

        private static string ShortItemName(string id) => id == ItemIds.Potato ? "감자" : id == ItemIds.Bread ? "빵" : id == ItemIds.Hoe ? "괭이" : "가방";

        private void BuildQuestWindow()
        {
            Window("quests", "퀘스트와 일거리  [Q]", new Vector2(510, 485), new Vector2(-240, 10));
            RenderQuests();
        }

        private void RenderQuests()
        {
            var content = Content("quests"); var state = game.State;
            Label("마을의 새 일꾼", content, new Vector2(22, -18), new Vector2(466, 30), 21, ink, true);
            Label(state.IntroClaimed ? "완료 · 미라는 당신의 첫 일거리를 기억합니다." : state.Career > 0
                ? "목표 달성! 농부 미라에게 돌아가 퀘스트 보상을 받으세요."
                : "농장주 미라와 대화하고 감자 납품 아르바이트를 한 번 완료하세요.\n보상: 30G · AP 2 · 경험치 15", content,
                new Vector2(22, -59), new Vector2(466, 69), 15, ink);
            Label("농장에 남긴 발자국", content, new Vector2(22, -144), new Vector2(466, 30), 21, ink, true);
            string story = state.FarmLevel > 1 ? "완료 · 당신이 도운 농장은 24개의 작물을 키웁니다.\n확장된 농장은 더 큰 납품 일거리를 제공합니다."
                : state.FarmUpgradePending ? "확장 준비를 마쳤습니다.\n다음 환생 후 밭이 12개에서 24개로 늘어납니다."
                : $"미라에게 자금과 감자를 기부해 농장 확장을 도와주세요.\n자금 {state.DonatedGold}/100G  ·  감자 {state.DonatedPotatoes}/20개\n기부한 자원과 세계의 변화는 다음 생애에도 남습니다.";
            Label(story, content, new Vector2(22, -185), new Vector2(466, 92), 15, ink);
            Label("오늘의 아르바이트", content, new Vector2(22, -293), new Vector2(466, 27), 18, ink, true);
            Label(state.Job == JobStatus.Active ? $"감자 {state.Inventory.Count(ItemIds.Potato)}/{state.JobRequirement}개 · 18:00까지 미라에게 납품\n보상 {state.JobReward}G · 경험치 {state.JobExperience} · 경력 +1"
                : $"농장 경력 {state.Career}회 · 현재 {state.CareerTier}등급 의뢰\n하루 한 번, 08:00~18:00에 접수·납품할 수 있어요.",
                content, new Vector2(22, -330), new Vector2(466, 56), 14, ink);
            Button("미라 찾아가기", content, new Vector2(22, -399), new Vector2(219, 32), () => game.WalkTo(VillageRole.Farmer), true);
            Button("기억의 샘 찾아가기", content, new Vector2(255, -399), new Vector2(232, 32), () => game.WalkTo(VillageRole.Shrine));
        }

        private void BuildSkillsWindow()
        {
            Window("skills", "생활 스킬  [K]", new Vector2(416, 365), new Vector2(-290, -65));
            RenderSkills();
        }

        private void RenderSkills()
        {
            var content = Content("skills"); var state = game.State;
            Label("감자 수확  " + RankName(state.SkillRank), content, new Vector2(22, -20), new Vector2(372, 36), 25, ink, true);
            Label("[F1] 가장 가까운 작물로 이동하여 수확", content, new Vector2(22, -64), new Vector2(372, 28), 14, ink);
            Label($"작업 속도  ×{state.WorkRate:0.00}\n수확 에너지  {state.HarvestEnergyCost}\n럭키 보너스 확률  {10 + state.SkillRank * 2}%\n기본 수확량  {2 + state.SkillRank / 3}개\n\n레벨업마다 AP 2를 얻습니다.\n생활 스킬과 남은 AP는 환생 후에도 유지됩니다.",
                content, new Vector2(22, -106), new Vector2(372, 154), 15, ink);
            var upgrade = Button($"승급  AP {state.NextSkillCost}  /  보유 {state.AP}", content, new Vector2(22, -272), new Vector2(372, 36),
                () => game.Perform(game.State.TryUpgradeSkill), true);
            upgrade.interactable = state.SkillRank < 8 && state.AP >= state.NextSkillCost;
        }

        private void BuildCharacterWindow()
        {
            Window("character", "캐릭터  [C]", new Vector2(450, 470), new Vector2(-30, 25));
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
            Button("기억의 샘으로 찾아가기", content, new Vector2(24, -377), new Vector2(256, 36), () => game.WalkTo(VillageRole.Shrine), true);
            Button("저장하기", content, new Vector2(292, -377), new Vector2(134, 36), () => game.SaveGame(true));
        }

        private void BuildMapWindow()
        {
            var panel = Window("map", "솔바람 마을 지도  [M]", new Vector2(650, 504), Vector2.zero);
            Label("지도에서 이동할 곳을 클릭하세요.  ·  휠: 확대·축소  ·  가운데 버튼 드래그: 시점 회전", panel,
                new Vector2(20, -56), new Vector2(610, 30), 13, ink);
            var root = Panel("Large village map", panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(610, 348), new Vector2(20, -97), new Color(0.49f, 0.6f, 0.44f), true);
            DrawMap(root, true);
            Label("농: 미라의 농장    상: 노아의 잡화점    숙: 레나의 여관    샘: 환생", panel,
                new Vector2(20, -463), new Vector2(610, 26), 14, ink);
        }

        private void DrawMap(RectTransform map, bool large)
        {
            map.gameObject.AddComponent<VillageMapClick>().Game = game;
            MapArea(map, -13f, 7.3f, 12f, 18f, new Color(0.52f, 0.39f, 0.27f));
            MapArea(map, 0f, 1f, 11f, 10f, new Color(0.78f, 0.75f, 0.59f));
            MapArea(map, -10f, 1f, 19f, 2.8f, new Color(0.78f, 0.75f, 0.59f));
            MapArea(map, 8f, -0.5f, 15f, 2.5f, new Color(0.78f, 0.75f, 0.59f));
            MapArea(map, -1f, 11f, 2.6f, 18f, new Color(0.78f, 0.75f, 0.59f));
            MapArea(map, 7f, 10f, 13f, 2.4f, new Color(0.78f, 0.75f, 0.59f));
            MapArea(map, 10f, 5.8f, 6f, 5f, new Color(0.38f, 0.35f, 0.3f));
            MapArea(map, 3f, 12.7f, 5f, 4f, new Color(0.38f, 0.35f, 0.3f));
            MapArea(map, -22f, 5.7f, 5f, 4.5f, new Color(0.38f, 0.35f, 0.3f));
            foreach (var target in game.Interactables)
            {
                string name = target.Role == VillageRole.Farmer ? "농" : target.Role == VillageRole.Merchant ? "상" : target.Role == VillageRole.Innkeeper ? "숙" : target.Role == VillageRole.Shrine ? "샘" : "!";
                var label = Label(name, map, Vector2.zero, new Vector2(large ? 36 : 21, large ? 30 : 21), large ? 18 : 12, paper, true);
                label.alignment = TextAnchor.MiddleCenter;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero; label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchoredPosition = MapPosition(map, target.transform.position);
            }
            var player = Panel("Player position", map, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.one * (large ? 10 : 6), Vector2.zero, Color.white);
            maps.Add(new MapView { Root = map, Player = player });
        }

        private void MapArea(RectTransform map, float x, float z, float width, float height, Color color)
        {
            Panel("Map area", map, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(width / 51f * map.rect.width, height / 39f * map.rect.height),
                MapPosition(map, new Vector3(x, 0, z)), color);
        }

        private static Vector2 MapPosition(RectTransform map, Vector3 position) => new Vector2((position.x + 28f) / 51f * map.rect.width, (position.z + 14.5f) / 39f * map.rect.height);

        private void UpdateMaps()
        {
            for (int i = maps.Count - 1; i >= 0; i--)
            {
                if (maps[i].Root == null) { maps.RemoveAt(i); continue; }
                maps[i].Player.anchoredPosition = MapPosition(maps[i].Root, game.Worker.transform.position);
            }
        }
    }
}
