using UnityEngine;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private enum Topic { Greeting, Job, IntroQuest, Contribution, Shop, Rest, Rebirth }
        private VillageInteractable dialogueTarget;
        private RectTransform dialogueDim;
        private RectTransform dialoguePanel;
        private RectTransform dialogueBody;
        private Topic topic;
        private bool rebirthConfirmation;

        public void OpenDialogue(VillageInteractable target)
        {
            CloseDialogue();
            dialogueTarget = target;
            topic = target.Role == VillageRole.NoticeBoard ? Topic.Job : Topic.Greeting;
            rebirthConfirmation = false;
            dialogueDim = Panel("Conversation focus", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.12f), true);
            dialogueDim.anchorMax = Vector2.one; dialogueDim.offsetMin = dialogueDim.offsetMax = Vector2.zero;
            RenderDialogue();
        }

        public void CloseDialogue()
        {
            dialogueTarget = null;
            if (dialoguePanel != null) { dialoguePanel.gameObject.SetActive(false); Destroy(dialoguePanel.gameObject); }
            if (dialogueDim != null) { dialogueDim.gameObject.SetActive(false); Destroy(dialogueDim.gameObject); }
            dialoguePanel = dialogueDim = null;
            rebirthConfirmation = false;
        }

        private void ChooseTopic(Topic next)
        {
            topic = next; rebirthConfirmation = false;
            RenderDialogue();
        }

        private void RenderDialogue()
        {
            if (dialogueTarget == null) return;
            if (dialoguePanel != null) { dialoguePanel.gameObject.SetActive(false); Destroy(dialoguePanel.gameObject); }
            dialoguePanel = Panel("NPC conversation", transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(1080, 310), new Vector2(0, 154), paper, true);
            var portrait = Panel("Speaker", dialoguePanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(188, 282), new Vector2(14, -14), dark);
            var initial = Label(dialogueTarget.Role == VillageRole.Shrine ? "기억" : dialogueTarget.DisplayName, portrait,
                new Vector2(14, -43), new Vector2(160, 99), 39, gold, true); initial.alignment = TextAnchor.MiddleCenter;
            Label(dialogueTarget.DisplayName, portrait, new Vector2(14, -164), new Vector2(160, 33), 24, paper, true).alignment = TextAnchor.MiddleCenter;
            Label(dialogueTarget.Subtitle, portrait, new Vector2(14, -211), new Vector2(160, 56), 13, pale).alignment = TextAnchor.MiddleCenter;
            dialogueBody = new GameObject("Conversation content", typeof(RectTransform)).GetComponent<RectTransform>();
            dialogueBody.SetParent(dialoguePanel, false); dialogueBody.anchorMin = dialogueBody.anchorMax = new Vector2(0, 1);
            dialogueBody.pivot = new Vector2(0, 1); dialogueBody.anchoredPosition = new Vector2(224, -19); dialogueBody.sizeDelta = new Vector2(610, 272);
            Label("대화 키워드", dialoguePanel, new Vector2(858, -20), new Vector2(200, 23), 13, ink, true);
            int keyword = 0;
            AddKeyword("개인적인 이야기", Topic.Greeting, ref keyword);
            switch (dialogueTarget.Role)
            {
                case VillageRole.Farmer:
                    AddKeyword("아르바이트", Topic.Job, ref keyword);
                    AddKeyword("마을의 새 일꾼", Topic.IntroQuest, ref keyword);
                    AddKeyword("농장 확장", Topic.Contribution, ref keyword);
                    break;
                case VillageRole.Merchant: AddKeyword("거래하기", Topic.Shop, ref keyword); break;
                case VillageRole.Innkeeper: AddKeyword("휴식과 수면", Topic.Rest, ref keyword); break;
                case VillageRole.Shrine: AddKeyword("환생", Topic.Rebirth, ref keyword); break;
                case VillageRole.NoticeBoard: AddKeyword("일거리 안내", Topic.Job, ref keyword); break;
            }
            Button("대화 끝내기  [Esc]", dialoguePanel, new Vector2(858, -258), new Vector2(200, 35), CloseDialogue);
            switch (topic)
            {
                case Topic.Job: RenderJobDialogue(); break;
                case Topic.IntroQuest: RenderIntroDialogue(); break;
                case Topic.Contribution: RenderContributionDialogue(); break;
                case Topic.Shop: RenderShopDialogue(); break;
                case Topic.Rest: RenderRestDialogue(); break;
                case Topic.Rebirth: RenderRebirthDialogue(); break;
                default: RenderGreeting(); break;
            }
        }

        private void AddKeyword(string title, Topic next, ref int index)
        {
            Button(title, dialoguePanel, new Vector2(858, -52 - index * 44), new Vector2(200, 36), () => ChooseTopic(next), next == topic);
            index++;
        }

        private void DialogueText(string title, string body)
        {
            Label(title, dialogueBody, Vector2.zero, new Vector2(610, 35), 23, ink, true);
            Label(body, dialogueBody, new Vector2(0, -51), new Vector2(610, 164), 17, ink);
        }

        private void RenderGreeting()
        {
            var state = game.State;
            switch (dialogueTarget.Role)
            {
                case VillageRole.Farmer:
                    DialogueText("흙 묻은 손으로, 미라가 웃는다.", state.Life > 1
                        ? "어서 와요. 처음 보는 얼굴인데 이상하게 익숙하네요.\n\n" + (state.FarmLevel > 1 ? "누군가 남긴 도움 덕분에 밭이 훨씬 넓어졌어요.\n일손도 더 많이 필요해졌죠. 오늘도 도와주실래요?" : "이 밭에서 일한 사람들의 노력은 사라지지 않아요.\n오늘도 감자를 캐고, 내일의 마을을 함께 준비해요.")
                        : "솔바람 마을에 온 걸 환영해요!\n감자는 직접 캐야 하고, 바닥에 떨어진 건 꼭 주워야 해요.\n\n아침 08:00부터 저녁 18:00까지 일거리를 받아요.\n시간 안에 감자를 가져오면 품삯과 경력을 드릴게요.");
                    Button("아르바이트 이야기", dialogueBody, new Vector2(0, -227), new Vector2(240, 36), () => ChooseTopic(Topic.Job), true);
                    break;
                case VillageRole.Merchant:
                    DialogueText("노아가 진열대를 정리하고 있다.", "일만 하다 보면 가방이 금방 무거워지죠.\n감자를 팔아 도구를 바꾸거나 더 큰 가방을 마련해 보세요.\n\n괭이는 일을 빠르게, 빵은 오늘을 조금 더 길게 해줄 거예요.\n물건은 이 삶에서 쓰는 거지만, 익힌 기술은 오래 남는답니다.");
                    Button("물건을 보고 싶어요", dialogueBody, new Vector2(0, -227), new Vector2(240, 36), () => ChooseTopic(Topic.Shop), true);
                    break;
                case VillageRole.Innkeeper:
                    DialogueText("레나가 따뜻한 물을 건넨다.", "해가 지면 일도 잠깐 내려놓는 게 좋아요.\n자정이 지나거나 에너지를 다 쓰면 쓰러지고 말아요.\n\n무료 잠자리에서도 쉴 수 있고, 10G면 편안한 객실을 드려요.\n잠들기 전에 오늘 받은 일거리는 마무리했는지 확인하세요.");
                    Button("쉬고 싶어요", dialogueBody, new Vector2(0, -227), new Vector2(240, 36), () => ChooseTopic(Topic.Rest), true);
                    break;
                case VillageRole.Shrine:
                    DialogueText("잔잔한 물결에 기억이 비친다.", "태어난 삶은 언젠가 끝나지만, 모든 것이 사라지지는 않는다.\n몸에 익힌 기술, 쌓인 경력, 사람들에게 남긴 도움.\n\n가진 것을 내려놓고도 다시 시작할 수 있다면,\n이 샘은 당신을 다음 삶으로 인도할 것이다.");
                    Button("다음 삶에 대해 묻는다", dialogueBody, new Vector2(0, -227), new Vector2(260, 36), () => ChooseTopic(Topic.Rebirth), true);
                    break;
                default:
                    DialogueText("솔바람 마을의 소식", "서쪽 농장에서는 일손을 구하고 있습니다.\n광장 북쪽의 노아는 식료품과 도구를, 동쪽의 레나는 잠자리를 제공합니다.\n\n북쪽 기억의 샘에서는 이전 삶의 흔적을 되돌아볼 수 있습니다.\n지도 [M]에서 마을의 위치를 확인해 보세요.");
                    Button("일거리 확인", dialogueBody, new Vector2(0, -227), new Vector2(240, 36), () => ChooseTopic(Topic.Job), true);
                    break;
            }
        }

        private void RenderJobDialogue()
        {
            var state = game.State;
            if (dialogueTarget.Role == VillageRole.NoticeBoard)
            {
                DialogueText("일손을 구합니다 · 미라의 감자 농장", $"접수·납품 08:00~18:00  ·  하루 한 번\n현재 의뢰: 감자 {state.OfferedJobRequirement}개 / {state.OfferedJobReward}G\n\n서쪽 농장의 미라에게 직접 일을 받아 주세요.\n경력이 쌓이고 농장이 커지면 더 큰 의뢰가 생깁니다.");
                Button("미라 찾아가기", dialogueBody, new Vector2(0, -227), new Vector2(190, 36), () => game.WalkTo(VillageRole.Farmer), true);
                var wait = Button("08:00까지 기다리기", dialogueBody, new Vector2(205, -227), new Vector2(200, 36), () => game.Perform(game.State.TryWaitForWork));
                wait.interactable = state.Minute < VillageState.JobStart;
                Button("여관 찾아가기", dialogueBody, new Vector2(420, -227), new Vector2(190, 36), () => game.WalkTo(VillageRole.Innkeeper));
                return;
            }
            if (state.Job == JobStatus.Active)
            {
                int count = state.Inventory.Count(ItemIds.Potato);
                DialogueText("진행 중 · 감자 납품", $"감자 {count}/{state.JobRequirement}개  ·  오늘 18:00까지\n보상 {state.JobReward}G · 경험치 {state.JobExperience} · 농장 경력 +1\n\n작물을 클릭해 수확하고, 바닥에 떨어진 감자를 주워오세요.\n준비가 되면 '납품하기'를 눌러 주세요.");
                var deliver = Button("감자 납품하기", dialogueBody, new Vector2(0, -227), new Vector2(240, 36), () => game.Perform(game.State.TryDeliverJob), true);
                deliver.interactable = count >= state.JobRequirement && state.JobWindowOpen;
            }
            else if (state.LastJobDay == state.Day)
            {
                DialogueText(state.Job == JobStatus.Completed ? "오늘도 수고 많았어요!" : "오늘의 일거리는 마감됐어요.",
                    "오늘 받을 수 있는 일거리는 여기까지예요.\n내일 아침에 다시 들러 주세요.\n\n남은 시간에는 개인 작업을 하거나 마을을 둘러보세요.\n첫 아르바이트를 마쳤다면 퀘스트 보상도 잊지 마세요.");
                Button("퀘스트 이야기", dialogueBody, new Vector2(0, -227), new Vector2(240, 36), () => ChooseTopic(Topic.IntroQuest), true);
            }
            else
            {
                DialogueText("아르바이트 · 감자 납품", $"의뢰 {state.CareerTier}등급 · 감자 {state.OfferedJobRequirement}개\n보상 {state.OfferedJobReward}G · 경험치 {state.OfferedJobExperience} · 농장 경력 +1\n\n접수·납품 시간은 08:00~18:00예요.\n물품을 가져온 뒤에도 마감 전에 보고해야 보상을 받아요.");
                var accept = Button(state.JobWindowOpen ? "아르바이트를 한다" : "지금은 접수 시간이 아니에요", dialogueBody,
                    new Vector2(0, -227), new Vector2(310, 36), () => game.Perform(game.State.TryAcceptJob), true);
                accept.interactable = state.JobWindowOpen;
            }
        }

        private void RenderIntroDialogue()
        {
            var state = game.State;
            DialogueText("퀘스트 · 마을의 새 일꾼", state.IntroClaimed
                ? "당신의 첫 일거리는 마을에 작은 기억을 남겼어요.\n\n이제 농장 확장을 도울 수도 있고,\n레벨 3이 되면 기억의 샘에서 환생할 수도 있어요.\n익힌 능력을 안고 다음 삶을 시작해 보세요."
                : "목표: 미라의 아르바이트를 한 번 완료하기\n보상: 30G · AP 2 · 경험치 15\n\n작은 약속을 끝까지 지키는 것부터 시작해 봐요.\n일을 마쳤다면 준비한 선물을 드릴게요.");
            var claim = Button(state.IntroClaimed ? "보상을 받은 퀘스트" : "퀘스트 보상 받기", dialogueBody, new Vector2(0, -227), new Vector2(260, 36),
                () => game.Perform(game.State.TryClaimIntroQuest), true);
            claim.interactable = !state.IntroClaimed && state.Career > 0;
            Button("농장 확장 이야기", dialogueBody, new Vector2(280, -227), new Vector2(260, 36), () => ChooseTopic(Topic.Contribution));
        }

        private void RenderContributionDialogue()
        {
            var state = game.State;
            string body = state.FarmLevel > 1 ? "당신이 남긴 도움으로 밭이 두 배로 넓어졌어요.\n더 큰 일거리를 맡길 수 있게 됐고요.\n\n당신의 주머니가 비어도, 마을은 그 도움을 기억할 거예요.\n정말 고마워요."
                : state.FarmUpgradePending ? "자금과 식량을 모두 마련했어요!\n밭을 넓히려면 조금 긴 시간이 필요해요.\n\n다음 삶에서 돌아왔을 때는 넓어진 밭을 보여드릴게요.\n기억의 샘에서 환생하면 공사가 마무리됩니다."
                : $"기부금 {state.DonatedGold}/100G · 감자 {state.DonatedPotatoes}/20개\n\n농장에 자금과 식량을 보태주면 밭을 넓힐 수 있어요.\n완료 후 환생하면 작물이 12개에서 24개로 늘어납니다.\n새로운 농장은 더 큰 일거리도 제공해요.";
            if (!state.IntroClaimed) body = "'마을의 새 일꾼' 퀘스트를 먼저 마무리해 주세요.\n그다음 농장 확장을 함께 준비할 수 있어요.\n\n필요한 자원: 100G와 감자 20개\n마을에 기부한 자원은 환생 후에도 남습니다.";
            DialogueText("세계에 남기는 일 · 농장 확장", body);
            if (state.FarmUpgradePending || state.FarmLevel > 1)
                Button("기억의 샘 찾아가기", dialogueBody, new Vector2(0, -227), new Vector2(270, 36), () => game.WalkTo(VillageRole.Shrine), true);
            else
            {
                var money = Button("25G 기부하기", dialogueBody, new Vector2(0, -227), new Vector2(270, 36), () => game.Perform(game.State.TryDonateGold), true);
                money.interactable = state.IntroClaimed && state.DonatedGold < 100 && state.Gold >= Mathf.Min(25, 100 - state.DonatedGold);
                var potatoes = Button("감자 5개 기부하기", dialogueBody, new Vector2(288, -227), new Vector2(284, 36), () => game.Perform(game.State.TryDonatePotatoes));
                potatoes.interactable = state.IntroClaimed && state.DonatedPotatoes < 20 && state.Inventory.Count(ItemIds.Potato) >= Mathf.Min(5, 20 - state.DonatedPotatoes);
            }
        }

        private void RenderShopDialogue()
        {
            var state = game.State;
            Label($"노아의 잡화점  ·  보유 {state.Gold:N0}G", dialogueBody, Vector2.zero, new Vector2(610, 34), 22, ink, true);
            string[] items = { ItemIds.Bread, ItemIds.Hoe, ItemIds.Bag };
            for (int i = 0; i < items.Length; i++)
            {
                string id = items[i]; var spec = ItemCatalog.Get(id); float y = -54 - i * 52;
                Label($"{spec.Name}  {spec.Price}G", dialogueBody, new Vector2(0, y), new Vector2(445, 24), 16, ink, true);
                string description = id == ItemIds.Bread ? "에너지 +25 · 인벤토리에서 사용" : id == ItemIds.Hoe ? "작업 속도 +25% · 에너지 소모 -1" : "소지품 공간 2줄 확장 · 2×2칸 차지";
                Label(description, dialogueBody, new Vector2(0, y - 26), new Vector2(445, 20), 12, ink);
                var buy = Button("구입", dialogueBody, new Vector2(472, y - 2), new Vector2(138, 37), () => game.Perform(() => game.State.TryBuy(id)), true);
                buy.interactable = state.Gold >= spec.Price && state.Inventory.CanAdd(id, 1) && !(id == ItemIds.Hoe && state.HasHoe) && !(id == ItemIds.Bag && state.BagExpanded);
            }
            Label($"감자 판매: 개당 3G  ·  보유 {state.Inventory.Count(ItemIds.Potato)}개", dialogueBody, new Vector2(0, -217), new Vector2(340, 23), 14, ink);
            var sellOne = Button("1개 판매", dialogueBody, new Vector2(350, -220), new Vector2(120, 35), () => game.Perform(() => game.State.TrySellPotatoes(1)));
            sellOne.interactable = state.Inventory.Count(ItemIds.Potato) >= 1;
            var sellFive = Button("5개 판매", dialogueBody, new Vector2(484, -220), new Vector2(126, 35), () => game.Perform(() => game.State.TrySellPotatoes(5)));
            sellFive.interactable = state.Inventory.Count(ItemIds.Potato) >= 5;
        }

        private void RenderRestDialogue()
        {
            var state = game.State;
            DialogueText("오늘의 일을 내려놓을 시간", $"현재 에너지 {state.Energy}/100  ·  보유 {state.Gold}G\n\n무료 잠자리: 다음 날 에너지 70\n편안한 객실: 10G, 다음 날 에너지 100\n" + (state.Job == JobStatus.Active ? "진행 중인 아르바이트는 마무리하지 못하면 실패합니다." : "다음 날 06:00에 일어나며 밭이 다시 준비됩니다."));
            Button("무료 잠자리에서 잔다", dialogueBody, new Vector2(0, -227), new Vector2(285, 36), () => game.Sleep(false));
            var room = Button("편안한 객실  10G", dialogueBody, new Vector2(304, -227), new Vector2(306, 36), () => game.Sleep(true), true);
            room.interactable = state.Gold >= 10;
        }

        private void RenderRebirthDialogue()
        {
            var state = game.State;
            if (rebirthConfirmation)
            {
                DialogueText("이 삶을 마치고 다음 삶으로", $"사라지는 것: 레벨 {state.Level}, {state.Gold}G, 모든 소지품·도구·가방\n남는 것: 수확 랭크 {RankName(state.SkillRank)}, AP {state.AP}, 경력 {state.Career}회\n\n" + (state.FarmUpgradePending ? "당신이 도운 농장 확장이 이번 환생 후 완성됩니다.\n" : "마을에 기부한 자원과 세계의 변화도 유지됩니다.\n") + "진행 중인 아르바이트는 내려놓게 됩니다. 환생할까요?");
                Button("삶을 마치고 환생한다", dialogueBody, new Vector2(0, -227), new Vector2(300, 36), game.Rebirth, true);
                Button("아직 이 삶에 머문다", dialogueBody, new Vector2(320, -227), new Vector2(290, 36), () => { rebirthConfirmation = false; RenderDialogue(); });
                return;
            }
            DialogueText("환생 · 다시 일하기 위해", $"현재 {state.Life}번째 생  ·  레벨 {state.Level}  ·  누적 레벨 {state.TotalLevel}\n\n환생 조건: '마을의 새 일꾼' 보상 수령 + 레벨 3\n개인 재산과 레벨은 잃지만 스킬·AP·경력은 이어집니다.\n" + (state.FarmUpgradePending ? "농장 확장이 준비됐어요. 환생 후 넓어진 밭을 만나세요." : "세계에 남긴 도움은 다음 삶에도 남습니다."));
            var rebirth = Button(state.CanRebirth ? "다음 삶을 준비한다" : "아직 환생 조건을 충족하지 못했어요", dialogueBody,
                new Vector2(0, -227), new Vector2(420, 36), () => { rebirthConfirmation = true; RenderDialogue(); }, true);
            rebirth.interactable = state.CanRebirth;
        }
    }
}
