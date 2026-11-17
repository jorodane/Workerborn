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
            var authoredTopics = dialogueTarget.Definition != null && dialogueTarget.Definition.Dialogue != null ? dialogueTarget.Definition.Dialogue.Topics : null;
            if (authoredTopics != null && authoredTopics.Length > 0)
                foreach (var entry in authoredTopics) AddKeyword(entry.keyword, (Topic)entry.action, ref keyword);
            else AddKeyword("이야기", Topic.Greeting, ref keyword);
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
            string text = dialogueTarget.Definition != null && dialogueTarget.Definition.Dialogue != null
                ? dialogueTarget.Definition.Dialogue.TextFor(DialogueAction.Greeting) : string.Empty;
            DialogueText(dialogueTarget.DisplayName, text);
        }

        private void RenderJobDialogue()
        {
            var state = game.State; var rules = state.Rules;
            string item = ItemCatalog.Get(rules.JobItemId).Name;
            string schedule = $"{rules.JobStart / 60:00}:{rules.JobStart % 60:00} ~ {rules.JobEnd / 60:00}:{rules.JobEnd % 60:00}";
            if (dialogueTarget.Role == VillageRole.NoticeBoard)
            {
                DialogueText(rules.JobTitle, $"{schedule}\n{item} {state.OfferedJobRequirement}개 · 보수 {state.OfferedJobReward}G"); return;
            }
            if (state.Job == JobStatus.Active)
            {
                int count = state.Inventory.Count(rules.JobItemId);
                DialogueText(rules.JobTitle, $"{item} {count}/{state.JobRequirement}개\n마감 {rules.JobEnd / 60:00}:{rules.JobEnd % 60:00}\n보수 {state.JobReward}G · 경험치 {state.JobExperience}");
                Button("보고한다", dialogueBody, new Vector2(0, -227), new Vector2(220, 34), () => game.Perform(game.State.TryDeliverJob), true).interactable = count >= state.JobRequirement && state.JobWindowOpen;
            }
            else if (state.LastJobDay == state.Day)
                DialogueText(rules.JobTitle, state.Job == JobStatus.Completed ? "오늘 일은 여기까지예요. 수고 많으셨어요." : "오늘 접수한 일은 마감되었습니다.");
            else
            {
                string description = dialogueTarget.Definition != null && dialogueTarget.Definition.Job != null ? dialogueTarget.Definition.Job.Description : "";
                DialogueText(rules.JobTitle, description + $"\n\n{schedule}\n{item} {state.OfferedJobRequirement}개 · 보수 {state.OfferedJobReward}G");
                Button("일을 받는다", dialogueBody, new Vector2(0, -227), new Vector2(220, 34), () => game.Perform(game.State.TryAcceptJob), true).interactable = state.JobWindowOpen;
            }
        }

        private void RenderIntroDialogue()
        {
            var state = game.State; var rules = state.Rules;
            DialogueText(rules.IntroTitle, state.IntroClaimed ? "완료한 이야기입니다." : rules.IntroDescription + $"\n\n보상 {rules.IntroGold}G · AP {rules.IntroAP} · 경험치 {rules.IntroExperience}");
            Button("보상을 받는다", dialogueBody, new Vector2(0, -227), new Vector2(220, 34), () => game.Perform(game.State.TryClaimIntroQuest), true).interactable = !state.IntroClaimed && state.Career >= rules.IntroRequiredJobs;
        }

        private void RenderContributionDialogue()
        {
            var state = game.State; var rules = state.Rules;
            DialogueText(rules.ExpansionTitle, rules.ExpansionDescription + $"\n\n자금 {state.DonatedGold}/{rules.DonationGold}G\n{ItemCatalog.Get(rules.DonationItemId).Name} {state.DonatedPotatoes}/{rules.DonationItems}개" +
                (state.FarmUpgradePending ? "\n준비 완료. 다음 환생 후 완성됩니다." : state.FarmLevel >= rules.ExpandedStage ? "\n완성되었습니다." : ""));
            bool accepts = state.IntroClaimed && !state.FarmUpgradePending && state.FarmLevel < rules.ExpandedStage;
            Button($"{rules.DonationGoldStep}G 기부", dialogueBody, new Vector2(0, -227), new Vector2(220, 34), () => game.Perform(game.State.TryDonateGold)).interactable = accepts && state.DonatedGold < rules.DonationGold;
            Button($"물품 {rules.DonationItemStep}개 기부", dialogueBody, new Vector2(240, -227), new Vector2(240, 34), () => game.Perform(game.State.TryDonatePotatoes)).interactable = accepts && state.DonatedPotatoes < rules.DonationItems;
        }

        private void RenderShopDialogue()
        {
            var state = game.State;
            Label(dialogueTarget.DisplayName + " · 거래", dialogueBody, Vector2.zero, new Vector2(610, 30), 20, ink, true);
            var viewport = Panel("Stock list", dialogueBody, new Vector2(0, 1), new Vector2(0, 1), new Vector2(610, 163), new Vector2(0, -43), Color.clear, true);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var stock = dialogueTarget.Definition != null ? dialogueTarget.Definition.ShopItems : null;
            int count = stock != null ? stock.Length : 0;
            var content = Panel("Stock", viewport, new Vector2(0, 1), new Vector2(0, 1), new Vector2(610, Mathf.Max(163, count * 50)), Vector2.zero, Color.clear);
            var scroll = viewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 25f;
            for (int i = 0; i < count; i++)
            {
                var definition = stock[i]; if (definition == null) continue;
                string id = definition.Id; var spec = ItemCatalog.Get(id); float y = -i * 50f;
                Label(spec.Name, content, new Vector2(0, y), new Vector2(340, 25), 14, ink);
                Label(spec.Price + "G", content, new Vector2(350, y), new Vector2(100, 25), 13, ink);
                Button("구입", content, new Vector2(475, y - 2), new Vector2(125, 30), () => game.Perform(() => game.State.TryBuy(id))).interactable = state.Gold >= spec.Price;
            }
            Label($"소지금 {state.Gold:N0}G", dialogueBody, new Vector2(0, -227), new Vector2(270, 32), 14, ink);
            Button("감자 1개 판매", dialogueBody, new Vector2(300, -227), new Vector2(145, 32), () => game.Perform(() => game.State.TrySellPotatoes(1))).interactable = state.Inventory.Count(ItemIds.Potato) >= 1;
            Button("감자 5개 판매", dialogueBody, new Vector2(460, -227), new Vector2(145, 32), () => game.Perform(() => game.State.TrySellPotatoes(5))).interactable = state.Inventory.Count(ItemIds.Potato) >= 5;
        }

        private void RenderRestDialogue()
        {
            var rules = game.State.Rules;
            DialogueText("잠자리", $"에너지 {game.State.Energy}/100\n\n무료 잠자리 · 회복 {rules.FreeRestEnergy}\n객실 {rules.RoomPrice}G · 완전 회복\n기상 {rules.WakeMinute / 60:00}:{rules.WakeMinute % 60:00}");
            Button("무료 잠자리", dialogueBody, new Vector2(0, -227), new Vector2(220, 34), () => game.Sleep(false));
            Button("객실을 빌린다", dialogueBody, new Vector2(240, -227), new Vector2(220, 34), () => game.Sleep(true), true).interactable = game.State.Gold >= rules.RoomPrice;
        }

        private void RenderRebirthDialogue()
        {
            var state = game.State;
            if (rebirthConfirmation)
            {
                DialogueText("환생", $"레벨 {state.Level}, {state.Gold}G, 소지품과 장비를 내려놓습니다.\n수확 랭크 {RankName(state.SkillRank)}, AP {state.AP}, 경력 {state.Career}회는 남습니다.\n\n" + (state.FarmUpgradePending ? "농장 확장이 다음 삶에 완성됩니다." : "마을에 남긴 변화도 이어집니다."));
                Button("환생한다", dialogueBody, new Vector2(0, -227), new Vector2(220, 34), game.Rebirth, true);
                Button("돌아간다", dialogueBody, new Vector2(240, -227), new Vector2(220, 34), () => { rebirthConfirmation = false; RenderDialogue(); });
                return;
            }
            string text = dialogueTarget.Definition != null && dialogueTarget.Definition.Dialogue != null ? dialogueTarget.Definition.Dialogue.TextFor(DialogueAction.Rebirth) : "";
            DialogueText("기억의 샘", text + $"\n\n환생 조건 · {state.Rules.IntroTitle} 완료, 레벨 {state.Rules.RebirthLevel}");
            Button("다음 삶을 준비한다", dialogueBody, new Vector2(0, -227), new Vector2(260, 34), () => { rebirthConfirmation = true; RenderDialogue(); }, true).interactable = state.CanRebirth;
        }
    }
}
