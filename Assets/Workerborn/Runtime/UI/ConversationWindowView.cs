using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ConversationWindowView : VillageWindowView, IPointerClickHandler
    {
        [SerializeField] private TMP_Text speaker;
        [SerializeField] private TMP_Text body;
        [SerializeField] private RectTransform textArea;
        [SerializeField] private RectTransform choices;
        [SerializeField] private Button leave;
        [SerializeField] private TMP_Text leaveLabel;
        [SerializeField] private UiActionView dialogueChoice;
        [SerializeField] private GameObject jobInfo;
        [SerializeField] private TMP_Text jobTitle;
        [SerializeField] private TMP_Text jobDetails;
        [SerializeField, Min(1f)] private float charactersPerSecond = 70f;

        private readonly List<UiActionView> replies = new List<UiActionView>();
        private readonly List<RectTransform> visibleChoices = new List<RectTransform>();
        private VillageInteractable npc;
        private DialogueTopicDefinition topic;
        private bool atOpening;
        private bool confirmingRebirth, confirmingEra, trading, waitingForWorldReply;
        private enum LookPart { Body, Hair, Face, Outfit }
        private UiActionView nextPage;
        private int replyCount;
        private string presentedText, followUp;
        private string laidOutText;
        private Vector2 measuredTextSize;
        private float preferredTextWidth, textWidth, textHeight;
        private bool positioned;
        private WindowTransition transition;
        private float lineStarted, choicesOpenedAt;
        private int page, pageCount, pageStart, pageEnd;
        private int advancedFrame = -1;
        private bool jobWasOpen;
        private const float ChoiceHeight = 36f;
        private const float ChoiceGap = 8f;
        private const float ChoiceOffset = 24f;
        private const float HorizontalPadding = 16f;
        private const float VerticalPadding = 22f;
        private const float MaximumWidth = 504f;
        internal bool CanLeave => IsOpen && !waitingForWorldReply && (trading || page >= pageCount);

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            transition = GetComponent<WindowTransition>();
            leave.onClick.AddListener(() => hud.TryLeaveDialogue());
            leave.transform.SetAsFirstSibling();
            DisableButtonSelection(leave);
            nextPage = Instantiate(dialogueChoice, choices.transform);
            nextPage.Bind("다음", () => TryAdvancePage());
            DisableButtonSelection(nextPage.GetComponent<Button>());
            body.overflowMode = TextOverflowModes.Page;
        }

        public void Open(VillageInteractable target, DialogueAction? requestedTopic = null) =>
            OpenTopic(target, requestedTopic.HasValue ? Game.FindDialogueTopic(target, requestedTopic.Value) : null);

        internal void OpenTopic(VillageInteractable target, DialogueTopicDefinition selected)
        {
            npc = target;
            presentedText = followUp = null;
            trading = waitingForWorldReply = false;
            confirmingRebirth = confirmingEra = false;
            positioned = false;
            advancedFrame = -1;
            topic = selected;
            atOpening = selected == null || selected.action == DialogueAction.Greeting;
            visibleChoices.Clear();
            leave.gameObject.SetActive(false);
            nextPage.gameObject.SetActive(false);
            foreach (var reply in replies)
                reply.gameObject.SetActive(false);
            Refresh();
        }

        private void SelectTopic(DialogueTopicDefinition next)
        {
            if (!Game.IsDialogueTopicAvailable(npc, next))
            {
                Refresh();
                return;
            }
            atOpening = false;
            topic = next;
            if (next.action == DialogueAction.Shop)
            {
                Hud.OpenShop(npc);
                return;
            }
            presentedText = followUp = null;
            confirmingRebirth = confirmingEra = false;
            Refresh();
        }

        internal void SetTrading()
        {
            if (trading)
                return;
            trading = true;
            atOpening = false;
            if (topic?.action != DialogueAction.Shop)
                topic = Game.FindDialogueTopic(npc, DialogueAction.Shop);
            presentedText = followUp = null;
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                TryAdvancePage();
        }

        internal bool TryAdvancePage(bool pagingOnly = false)
        {
            if (npc == null || !IsOpen || Hud.HasQuantityPrompt || (pagingOnly && pageCount < 2))
                return false;
            if (advancedFrame == Time.frameCount)
                return true;
            if (body.maxVisibleCharacters < pageEnd)
                body.maxVisibleCharacters = pageEnd;
            else if (page < pageCount)
            {
                page++;
                MeasurePage(restart: true);
            }
            else
                return false;
            ShowPageChoices();
            advancedFrame = Time.frameCount;
            return true;
        }

        private void Update()
        {
            if (npc == null || !IsOpen)
                return;
            if (jobWasOpen != Game.State.JobWindowOpen)
                Refresh();
            if (body.maxVisibleCharacters >= pageEnd)
                return;
            body.maxVisibleCharacters = Mathf.Min(pageEnd,
                pageStart + Mathf.FloorToInt((Time.unscaledTime - lineStarted) * charactersPerSecond));
            if (body.maxVisibleCharacters >= pageEnd)
                ShowPageChoices();
        }

        private void ShowLine(string text)
        {
            bool newLine = presentedText != text;
            if (newLine)
            {
                presentedText = text;
                body.text = text;
                page = 1;
            }
            LayoutBubble();
            if (newLine || body.rectTransform.rect.size != measuredTextSize)
                MeasurePage(restart: newLine);
            ShowPageChoices();
        }

        private void MeasurePage(bool restart)
        {
            measuredTextSize = body.rectTransform.rect.size;
            int visible = body.maxVisibleCharacters;
            body.maxVisibleCharacters = int.MaxValue;
            body.ForceMeshUpdate();
            var info = body.textInfo;
            pageCount = Mathf.Max(1, info.pageCount);
            page = Mathf.Clamp(page, 1, pageCount);
            body.pageToDisplay = page;
            if (info.pageCount > 0 && info.characterCount > 0)
            {
                var range = info.pageInfo[page - 1];
                pageStart = Mathf.Clamp(range.firstCharacterIndex, 0, info.characterCount);
                pageEnd = Mathf.Clamp(range.lastCharacterIndex + 1, pageStart, info.characterCount);
            }
            else
                pageStart = pageEnd = 0;
            body.maxVisibleCharacters = restart ? pageStart : Mathf.Clamp(visible, pageStart, pageEnd);
            lineStarted = Time.unscaledTime - (body.maxVisibleCharacters - pageStart) / charactersPerSecond;
        }

        private void LateUpdate()
        {
            if (npc == null)
                return;
            if (IsClosing)
            {
                bool tracked = PickupNames.TryProject(Game, Hud.CanvasRect, npc.transform.position, npc.NamePosition, out var origin, out _);
                SetMotionOrigin(tracked, origin);
                return;
            }
            LayoutBubble();
            if (IsOpen && body.rectTransform.rect.size != measuredTextSize)
            {
                MeasurePage(restart: false);
                ShowPageChoices();
            }
            LayoutChoices();
        }

        private void SetMotionOrigin(bool tracked, Vector2 point)
        {
            transition?.SetMotionOrigin(tracked ? point - (Rect.anchoredPosition + Hud.CanvasRect.rect.center) : (Vector2?)null);
        }

        private void LayoutBubble()
        {
            Rect canvas = Hud.CanvasRect.rect;
            Rect space = Hud.DialogueSpace(trading);
            if (laidOutText != presentedText)
            {
                laidOutText = presentedText;
                preferredTextWidth = body.GetPreferredValues(presentedText).x;
                textWidth = 0f;
            }
            float bubbleWidth = Mathf.Clamp(preferredTextWidth + HorizontalPadding * 2f, 320f, MaximumWidth);
            float width = Mathf.Clamp(space.width - 24f, 1f, bubbleWidth);
            float availableTextWidth = Mathf.Max(1f, width - HorizontalPadding * 2f);
            if (textWidth != availableTextWidth)
            {
                textWidth = availableTextWidth;
                textHeight = Mathf.Clamp(body.GetPreferredValues(presentedText, textWidth, Mathf.Infinity).y, 24f, 112f);
            }
            int rows = trading ? 1 : Mathf.CeilToInt((replyCount + 1f) / ChoiceColumns(width));
            float replyHeight = ChoiceOffset + rows * (ChoiceHeight + ChoiceGap);
            float jobHeight = jobInfo.activeSelf ? ((RectTransform)jobInfo.transform).rect.height + 18f : 0f;
            float proseHeight = VerticalPadding * 2f + textHeight;
            float height = Mathf.Min(proseHeight, Mathf.Max(1f, space.height - replyHeight - jobHeight - 32f));
            Rect.sizeDelta = new Vector2(width, height);
            speaker.rectTransform.anchoredPosition = Vector2.down * WorldNameplateView.SpeechRimInset(height);
            textArea.offsetMin = new Vector2(HorizontalPadding, VerticalPadding);
            textArea.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);

            var point = new Vector2(space.center.x, space.center.y - 84f);
            bool tracked = PickupNames.TryProject(Game, Hud.CanvasRect, npc.transform.position, npc.NamePosition, out var speakerPoint, out _);
            if (tracked)
                point = speakerPoint;
            float left = space.xMin + width * .5f + 10f;
            float right = space.xMax - width * .5f - 10f;
            float bottom = space.yMin + height * .5f + replyHeight;
            float top = space.yMax - height * .5f - jobHeight - 18f;
            point.y += height * .5f + replyHeight;
            if (positioned)
                point = Vector2.Lerp(Rect.anchoredPosition + canvas.center, point, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            Rect.anchoredPosition = new Vector2(Mathf.Clamp(point.x, Mathf.Min(left, right), right),
                Mathf.Clamp(point.y, Mathf.Min(bottom, top), top)) - canvas.center;
            SetMotionOrigin(tracked, speakerPoint);
            positioned = true;
        }

        private static int ChoiceColumns(float width) => Mathf.Clamp(Mathf.FloorToInt((width + ChoiceGap) / 150f), 1, 5);

        private void LayoutChoices()
        {
            int columns = ChoiceColumns(Rect.rect.width);
            float width = Mathf.Min(164f, Mathf.Max(1f, (Rect.rect.width - ChoiceGap * (columns - 1)) / columns));
            for (int i = 0; i < visibleChoices.Count; i++)
            {
                int row = i / columns;
                int countInRow = Mathf.Min(columns, visibleChoices.Count - columns * row);
                var button = visibleChoices[i];
                button.sizeDelta = new Vector2(width, ChoiceHeight);
                Vector2 destination = new Vector2((i % columns - (countInRow - 1) * .5f) * (width + ChoiceGap),
                    -ChoiceHeight * .5f - row * (ChoiceHeight + ChoiceGap));
                float t = Mathf.Clamp01((Time.unscaledTime - choicesOpenedAt - i * .035f) / .25f);
                float remaining = t - 1f;
                float pop = 1f + remaining * remaining * (2.45f * remaining + 1.45f);
                button.localScale = Vector3.one * Mathf.LerpUnclamped(.18f, 1f, pop);
                button.anchoredPosition = Vector2.LerpUnclamped(Vector2.up * ChoiceOffset, destination, pop);
            }
        }

        private void ShowPageChoices()
        {
            bool more = page < pageCount;
            bool finishedTyping = body.maxVisibleCharacters >= pageEnd;
            visibleChoices.Clear();
            bool changed = PresentChoice(leave.transform, CanLeave);
            changed |= PresentChoice(nextPage.transform, more && finishedTyping);
            for (int i = 0; i < replies.Count; i++)
                changed |= PresentChoice(replies[i].transform, !more && i < replyCount);
            if (changed)
                choicesOpenedAt = Time.unscaledTime;
            LayoutChoices();
        }

        private bool PresentChoice(Transform choice, bool visible)
        {
            bool changed = choice.gameObject.activeSelf != visible;
            if (changed)
                choice.gameObject.SetActive(visible);
            if (visible)
                visibleChoices.Add((RectTransform)choice);
            return changed;
        }

        private void Reply(string text, Action callback, bool enabled = true)
        {
            if (replyCount == replies.Count)
            {
                var view = Instantiate(dialogueChoice, choices.transform);
                DisableButtonSelection(view.GetComponent<Button>());
                view.gameObject.SetActive(false);
                replies.Add(view);
            }
            replies[replyCount++].Bind(text, callback, enabled);
        }

        private static void DisableButtonSelection(Button button)
        {
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
        }

        private void LookOption(string label, LookPart part)
        {
            Reply(label, () =>
            {
                var look = Game.IncarnationLook;
                switch (part)
                {
                    case LookPart.Body: look.body = 1 - look.body; break;
                    case LookPart.Hair: look.hair = look.hair % CharacterLook.HairCount + 1; break;
                    case LookPart.Face: look.face = look.face % CharacterLook.FaceCount + 1; break;
                    case LookPart.Outfit: look.outfit = look.outfit % CharacterLook.OutfitCount + 1; break;
                }
                Game.PreviewIncarnationLook(look);
                Refresh();
            });
        }

        public override void Refresh()
        {
            if (npc == null)
                return;
            replyCount = 0;
            speaker.text = npc.DisplayName;
            leaveLabel.text = trading ? "거래 종료" : "대화 끝내기";
            var state = Game.State;
            jobWasOpen = state.JobWindowOpen;
            if (atOpening && followUp == null)
            {
                var fallback = npc.Role switch
                {
                    VillageRole.NoticeBoard => DialogueAction.Job,
                    VillageRole.Guide when state.AwaitingIncarnation && !state.IsSkillLearned(SkillId.SoulGrowth) => DialogueAction.GuideSoulGrowth,
                    VillageRole.Guide when state.Life > 1 => DialogueAction.GuideAfter,
                    _ => DialogueAction.Greeting
                };
                topic = Game.OpeningDialogueTopic(npc, fallback);
            }
            string prose = topic?.text ?? string.Empty;
            var action = topic?.action ?? DialogueAction.Greeting;
            bool available = atOpening || Game.IsDialogueTopicAvailable(npc, topic);
            jobInfo.SetActive(!trading && followUp == null && available && action == DialogueAction.Job && state.FarmAvailable);

            if (trading)
            {
                ShowLine(prose);
                return;
            }

            if (followUp != null)
            {
                ShowLine(followUp);
                return;
            }

            if (!available)
            {
                ShowLine(prose);
                return;
            }

            if (npc.Role == VillageRole.Guide && action is (DialogueAction.Greeting or DialogueAction.GuideAfter or DialogueAction.GuideSoulGrowth))
            {
                LookOption("체형", LookPart.Body);
                LookOption("머리", LookPart.Hair);
                LookOption("얼굴", LookPart.Face);
                LookOption("기본 의상", LookPart.Outfit);
                Reply("환생하기", Game.ConfirmIncarnation, state.AwaitingIncarnation);
                if (atOpening)
                    AddTopicChoices();
                var look = Game.IncarnationLook;
                ShowLine($"{prose}\n\n{state.Life}번째 생 · 레벨 1\n체형 {look.body + 1}   머리 {look.hair}   얼굴 {look.face}   기본 의상 {look.outfit}");
                return;
            }

            string response = TopicResponse(action, state, prose);
            if (atOpening)
                AddTopicChoices();
            ShowLine(response);
        }

        private void AddTopicChoices()
        {
            foreach (var entry in Game.DialogueTopicsFor(npc))
            {
                var next = entry.Topic;
                if (next.action == DialogueAction.Greeting || string.IsNullOrWhiteSpace(next.keyword) ||
                    (next == topic && next.action != DialogueAction.Shop))
                    continue;
                Reply(next.keyword, () => SelectTopic(next));
            }
        }

        private string TopicResponse(DialogueAction action, VillageState s, string prose)
        {
            var r = s.Rules;
            switch (action)
            {
                case DialogueAction.Foundation:
                    var quest = r.Foundation;
                    if (quest == null)
                        return prose;
                    if (s.CanSelectFarmSite)
                        return s.CanAdvanceEra ? $"{Game.FarmSiteName}에 농장을 준비할게. 기억의 우물에서 시대를 넘길 수 있어." : "목재는 잘 받았어. 농장 후보지의 팻말을 살펴봐 줘.";
                    if (s.FoundationStage <= FarmFoundationStage.CollectingTimber && s.Inventory.Count(quest.DocumentItemId) == 0)
                        Reply("계약서를 받는다", () => RespondToWorld(CampaignAction.AcceptFoundation));
                    int owned = s.Inventory.Count(quest.TimberItemId);
                    if (s.FoundationStage == FarmFoundationStage.CollectingTimber)
                        Reply(s.CanConfirmTimberDelivery ? "납품 완료를 확인한다" : "목재를 납품한다",
                            () => RespondToWorld(CampaignAction.DeliverTimber), s.CanConfirmTimberDelivery || owned > 0);
                    return $"{prose}\n\n누적 납품  {s.TimberDelivered:N0}/{s.TimberRequired:N0}  ·  소지 목재 {owned:N0}";
                case DialogueAction.Job:
                    return JobResponse(s, prose);
                case DialogueAction.IntroQuest:
                    if (!s.CanClaimIntroQuest)
                        return prose;
                    Reply("보상을 받는다", () => Respond(() => Game.Perform(Game.State.TryClaimIntroQuest), "고생했어. 다음에 또 함께 일하자."));
                    return $"{prose}\n\n{r.IntroDescription}\n보상  {r.IntroGold} Gold  ·  AP {r.IntroAP}";
                case DialogueAction.Contribution:
                    if (s.CanConfirmExpansion)
                        Reply("확장 준비를 확인한다", () => RespondToWorld(CampaignAction.DonateGold));
                    else
                    {
                        if (s.ExpansionQuestActive && s.DonatedGold < s.ExpansionGoldRequired)
                            Reply($"{r.DonationGoldStep} Gold 기부", () => RespondToWorld(CampaignAction.DonateGold));
                        if (s.ExpansionQuestActive && s.DonatedPotatoes < s.ExpansionItemsRequired)
                            Reply("물품을 기부한다", () => RespondToWorld(CampaignAction.DonateItems));
                    }
                    return $"{prose}\n\n{r.ExpansionDescription}\n자금 {s.DonatedGold:N0}/{s.ExpansionGoldRequired:N0} Gold\n" +
                        $"{ItemCatalog.Get(r.DonationItemId).Name} {s.DonatedPotatoes:N0}/{s.ExpansionItemsRequired:N0}";
                case DialogueAction.Rest:
                    if (!Game.CanEnterGuestRoom(npc))
                        return "여관 안에서 이야기해 주면 방을 안내해 줄게.";
                    Reply("내 방으로 들어간다", () => Game.EnterGuestRoom(npc));
                    string wake = Game.IsOnline ? "" : $"\n기상  {VillageClockView.TimeLabel(r.WakeMinute)}";
                    return $"{prose}{wake}\n\n방에서 침대를 눌러 잠을 청할 수 있어요.";
                case DialogueAction.Rebirth:
                    return RebirthResponse(s, prose);
                case DialogueAction.Legend:
                    var recent = Array.ConvertAll(s.Legend.founders, founder => founder.name);
                    string names = recent.Length > 0 ? "\n기록된 나무꾼  " + string.Join(", ", recent, 0, Math.Min(recent.Length, 6)) : "";
                    Reply("전설을 기록한다", () => RespondToWorld(CampaignAction.RecordLegend), s.CanRecordLegend(Game.LegendPlayerId));
                    Reply("시련을 마친다", () => RespondToWorld(CampaignAction.ProveLegend), s.CanProveLegend(Game.LegendPlayerId));
                    return $"벌목 업적  {s.LifetimeTimber:N0}/{r.LegendFoundingTimber:N0}\n" +
                        $"목재 {r.LegendTrialTimber:N0}개를 가져오면 시련을 마칠 수 있어요. 남은 인정 인원 {s.LegendPlacesAvailable}명." + names;
                default:
                    return prose;
            }
        }

        private string JobResponse(VillageState s, string prose)
        {
            if (!s.FarmAvailable)
                return "농장이 완공되면 여기에서 일자리를 안내합니다.";
            var r = s.Rules;
            bool active = s.Job == JobStatus.Active;
            bool workedToday = s.LastJobDay == s.Day;
            bool closed = workedToday && !active || s.Minute >= r.JobEnd;
            jobTitle.text = r.JobTitle;
            string status = active ? "진행 중" : closed ? "마감" : jobWasOpen ? "접수 중" : "접수 전";
            string progress = active ? $"{s.Inventory.Count(r.JobItemId):N0}/{s.JobRequirement:N0}" : $"{s.OfferedJobRequirement:N0}개";
            int gold = active ? s.JobReward : s.OfferedJobReward;
            int experience = s.ScaledExperience(active ? s.JobExperience : s.OfferedJobExperience);
            string rewardId = active ? s.JobRewardItemId : s.OfferedJobTier.RewardItemId;
            int rewardCount = active ? s.JobRewardItemCount : s.OfferedJobTier.RewardItemCount;
            string reward = rewardCount > 0 ? $"\n{ItemCatalog.Get(rewardId).Name} ×{rewardCount:N0}" : "";
            jobDetails.text = $"{VillageClockView.TimeLabel(r.JobStart)}–{VillageClockView.TimeLabel(r.JobEnd)}  ·  {status}\n" +
                $"{ItemCatalog.Get(r.JobItemId).Name}  {progress}\n보수  {gold:N0} Gold  ·  경험치 {experience:N0}{reward}";

            if (npc.Role == VillageRole.NoticeBoard)
                return prose;
            if (active)
                Reply("납품한다", () => Respond(() => Game.Perform(s.TryDeliverJob), "잘 받았어. 오늘 일은 여기까지야. 고마워."),
                    jobWasOpen && s.Inventory.Count(r.JobItemId) >= s.JobRequirement);
            else if (!closed && jobWasOpen)
                Reply("일을 맡는다", () => Respond(() => Game.Perform(s.TryAcceptJob), "계약서를 챙겨 가. 해 지기 전에 다시 와 줘."));
            return closed ? "오늘 일은 마감했어. 내일 다시 들러 줘." : prose;
        }

        private string RebirthResponse(VillageState s, string prose)
        {
            if (confirmingRebirth || confirmingEra)
            {
                string text = (confirmingEra ? "시대 전환" : "일반 환생") + "\n이번 생의 레벨·능력치·소지품·장비·골드가 초기화됩니다." +
                    $"\n\n스킬 랭크·AP {s.AP}·아르바이트 경력 {s.Career}회는 유지됩니다.";
                if (confirmingEra)
                    Reply("다음 시대로 간다", Game.AdvanceEra);
                else
                    Reply("다음 생을 준비한다", Game.Rebirth);
                return confirmingEra ? text + $"\n\n마을이 {s.Era + 1}시대로 넘어갑니다." : text;
            }

            Reply("일반 환생", () => { confirmingRebirth = true; Refresh(); }, s.CanRebirth);
            if (Game.IsOnline)
                Reply("공동 환생", Game.AdvanceEra);
            else
                Reply("시대 전환", () => { confirmingEra = true; Refresh(); }, s.CanAdvanceEra);
            return $"{prose}\n\n현재 {s.Era}시대\n" + (s.CanRebirth
                ? "일반 환생 가능"
                : $"일반 환생까지 인게임 {Math.Ceiling(s.RebirthMinutesLeft / 60d):N0}시간") +
                (s.CanAdvanceEra ? "\n시대 퀘스트를 완료했습니다." : "\n시대 퀘스트가 진행 중입니다.");
        }

        private void Respond(Func<bool> operation, string response)
        {
            if (operation())
                EndWith(response ?? Game.State.LastMessage);
        }

        private void RespondToWorld(CampaignAction action)
        {
            if (!Game.IsOnline)
            {
                Respond(() => Game.PerformWorld(action), null);
                return;
            }
            if (Game.AwaitingWorldCommand)
                return;
            waitingForWorldReply = true;
            if (!Game.PerformWorld(action))
                waitingForWorldReply = false;
            ShowPageChoices();
        }

        internal void FinishWorldReply(bool success, string response)
        {
            if (!waitingForWorldReply)
                return;
            waitingForWorldReply = false;
            if (success)
                EndWith(response);
            else
                ShowPageChoices();
        }

        private void EndWith(string response)
        {
            if (!IsOpen || !Hud.IsSpeakingWith(npc))
                return;
            followUp = string.IsNullOrEmpty(response) ? "그래. 또 이야기하자." : response;
            Refresh();
        }
    }
}
