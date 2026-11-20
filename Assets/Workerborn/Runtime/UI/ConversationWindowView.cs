using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ConversationWindowView : VillageWindowView
    {
        [SerializeField]
        private TMP_Text speaker;
        [SerializeField]
        private TMP_Text occupation;
        [SerializeField]
        private TMP_Text bodyTitle;
        [SerializeField]
        private TMP_Text body;
        [SerializeField]
        private RectTransform keywords;
        [SerializeField]
        private RectTransform actions;
        [SerializeField]
        private Button leave;
        [SerializeField]
        private RawImage portrait;
        private VillageInteractable npc;
        private DialogueAction topic;
        private bool confirmingRebirth;
        private bool confirmingEra;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            leave.onClick.AddListener(hud.CloseDialogue);
            portrait.texture = game.NpcPortraitTexture;
        }

        public void Open(VillageInteractable target, DialogueAction? requestedTopic = null)
        {
            npc = target;
            confirmingRebirth = confirmingEra = false;
            bool beforeFarm = !Game.State.FarmAvailable;
            topic = npc.Role switch
            {
                VillageRole.Farmer => beforeFarm ? DialogueAction.Foundation : DialogueAction.Job,
                VillageRole.NoticeBoard => DialogueAction.Job,
                VillageRole.Shrine => DialogueAction.Rebirth,
                VillageRole.Guide when !beforeFarm => DialogueAction.GuideAfter,
                _ => DialogueAction.Greeting
            };
            if (requestedTopic.HasValue && npc.IsTopicAvailable(Game, requestedTopic.Value))
                topic = requestedTopic.Value;
            InventoryWindowView.Clear(keywords);
            var topics = npc.Definition != null && npc.Definition.Dialogue != null ? npc.Definition.Dialogue.Topics : null;
            if (topics != null)
                foreach (var entry in topics)
                {
                    if (string.IsNullOrWhiteSpace(entry.keyword) || !npc.IsTopicAvailable(Game, entry.action))
                        continue;
                    var selected = entry.action;
                    var button = Instantiate(Hud.Library.ActionButton, keywords);
                    button.Bind(entry.keyword, () =>
                    {
                        if (selected == DialogueAction.Shop)
                        {
                            Hud.OpenShop(npc);
                            return;
                        }
                        topic = selected;
                        confirmingRebirth = confirmingEra = false;
                        Refresh();
                    });
                }

            if (npc.Role == VillageRole.Shrine)
                Instantiate(Hud.Library.ActionButton, keywords).Bind("전설의 나무꾼", () =>
                {
                    topic = DialogueAction.Legend;
                    Refresh();
                });

            var actor = npc.Role != VillageRole.Guide ? npc.GetComponent<CharacterActor>() : null;
            Game.ShowNpcPortrait(actor);
            portrait.gameObject.SetActive(actor != null);
            Refresh();
        }

        private void Action(string text, System.Action callback, bool enabled = true)
        {
            Instantiate(Hud.Library.ActionButton, actions).Bind(text, callback, enabled);
        }

        public override void Refresh()
        {
            if (npc == null)
                return;
            speaker.text = npc.DisplayName;
            occupation.text = npc.Subtitle;
            var s = Game.State;
            var r = s.Rules;
            InventoryWindowView.Clear(actions);
            bodyTitle.text = npc.DisplayName;
            string prose = npc.Definition != null && npc.Definition.Dialogue != null ? npc.Definition.Dialogue.TextFor(topic) : "";
            body.text = prose;
            if (npc.Role == VillageRole.Guide)
            {
                Action("마을로 내려간다", Hud.CloseDialogue);
                return;
            }
            switch (topic)
            {
                case DialogueAction.Foundation:
                    var quest = r.Foundation;
                    if (quest == null)
                        break;
                    bodyTitle.text = quest.Title;
                    body.text = prose + $"\n\n누적 납품  {s.TimberDelivered:N0} / {quest.TimberCount:N0}";
                    if (s.FoundationStage <= FarmFoundationStage.CollectingTimber && s.Inventory.Count(quest.DocumentItemId) == 0)
                        Action("계약서를 받는다", () => Game.PerformWorld(CampaignAction.AcceptFoundation));
                    if (s.FoundationStage == FarmFoundationStage.CollectingTimber)
                    {
                        int owned = s.Inventory.Count(quest.TimberItemId);
                        body.text += $"  ·  소지 목재 {owned:N0}";
                        Action("목재를 납품한다", () => Game.PerformWorld(CampaignAction.DeliverTimber), owned > 0);
                    }
                    if (s.CanSelectFarmSite)
                        body.text = s.CanAdvanceEra ? $"{Game.FarmSiteName}에 농장을 준비할게. 기억의 우물에서 시대를 넘길 수 있어." : "목재는 잘 받았어. 농장 후보지의 팻말을 살펴봐 줘.";
                    break;
                case DialogueAction.Job:
                    bodyTitle.text = r.JobTitle;
                    if (!s.FarmAvailable)
                    {
                        body.text = "농장이 완공되면 여기에서 일자리를 안내합니다.";
                        break;
                    }
                    body.text = prose + $"\n\n{r.JobStart / 60:00}:{r.JobStart % 60:00} ~ {r.JobEnd / 60:00}:{r.JobEnd % 60:00}\n";
                    if (s.Job == JobStatus.Active)
                    {
                        body.text += $"{ItemCatalog.Get(r.JobItemId).Name}  {s.Inventory.Count(r.JobItemId)}/{s.JobRequirement}\n보수  {s.JobReward} Gold  ·  경험치 {s.JobExperience}";
                        if (s.JobRewardItemCount > 0)
                            body.text += $"\n{ItemCatalog.Get(s.JobRewardItemId).Name} ×{s.JobRewardItemCount}";
                        if (npc.Role != VillageRole.NoticeBoard)
                            Action("보고한다", () => Game.Perform(s.TryDeliverJob), s.JobWindowOpen && s.Inventory.Count(r.JobItemId) >= s.JobRequirement);
                    }
                    else
                    {
                        var item = ItemCatalog.Get(r.JobItemId);
                        body.text += $"{item.Name} {s.OfferedJobRequirement}개\n납품 보수 {s.OfferedJobReward} Gold  ·  일반 판매 {item.Price * s.OfferedJobRequirement} Gold";
                        if (s.OfferedJobTier.RewardItemCount > 0)
                            body.text += $"\n{ItemCatalog.Get(s.OfferedJobTier.RewardItemId).Name} ×{s.OfferedJobTier.RewardItemCount}";
                        if (npc.Role != VillageRole.NoticeBoard)
                            Action(s.LastJobDay == s.Day ? "오늘의 일 마감" : "계약서를 받는다", () => Game.Perform(s.TryAcceptJob), s.LastJobDay != s.Day && s.JobWindowOpen);
                    }

                    break;
                case DialogueAction.IntroQuest:
                    bodyTitle.text = r.IntroTitle;
                    body.text = r.IntroDescription + $"\n\n보상  {r.IntroGold} Gold  ·  AP {r.IntroAP}";
                    Action(s.IntroClaimed ? "완료" : "보상을 받는다", () => Game.Perform(s.TryClaimIntroQuest), !s.IntroClaimed && s.Career >= r.IntroRequiredJobs);
                    break;
                case DialogueAction.Contribution:
                    bodyTitle.text = r.ExpansionTitle;
                    body.text = r.ExpansionDescription + $"\n\n자금 {s.DonatedGold}/{r.DonationGold} Gold\n{ItemCatalog.Get(r.DonationItemId).Name} {s.DonatedPotatoes}/{r.DonationItems}";
                    bool accepts = s.FarmAvailable && !s.FarmUpgradePending && s.FarmLevel < r.ExpandedStage;
                    Action($"{r.DonationGoldStep} Gold 기부", () => Game.PerformWorld(CampaignAction.DonateGold), accepts);
                    Action("물품 기부", () => Game.PerformWorld(CampaignAction.DonateItems), accepts);
                    break;
                case DialogueAction.Rest:
                    body.text = Game.IsOnline ? prose + $"\n\n무료 잠자리  ·  스태미나 {r.FreeRestEnergy}\n객실  {r.RoomPrice} Gold  ·  완전 회복" : prose + $"\n\n기상  {r.WakeMinute / 60:00}:{r.WakeMinute % 60:00}\n무료 잠자리  ·  스태미나 {r.FreeRestEnergy}\n객실  {r.RoomPrice} Gold  ·  완전 회복";
                    Action("무료 잠자리", () => Game.Sleep(false));
                    Action("객실을 빌린다", () => Game.Sleep(true), s.Gold >= r.RoomPrice);
                    break;
                case DialogueAction.Rebirth:
                    if (confirmingRebirth || confirmingEra)
                    {
                        bodyTitle.text = confirmingEra ? "시대 전환" : "일반 환생";
                        body.text = $"이번 생의 레벨·능력치·소지품·장비·골드가 초기화됩니다.\n\n스킬 랭크·AP {s.AP}·아르바이트 경력 {s.Career}회는 유지됩니다.";
                        if (confirmingEra)
                        {
                            body.text += $"\n\n마을이 {s.Era + 1}시대로 넘어갑니다.";
                            Action("다음 시대로 간다", Game.AdvanceEra);
                        }
                        else
                            Action("환생한다", Game.Rebirth);
                        Action("돌아간다", () =>
                        {
                            confirmingRebirth = confirmingEra = false;
                            Refresh();
                        });
                    }
                    else
                    {
                        body.text = $"현재 {s.Era}시대\n" + (s.CanRebirth
                            ? "일반 환생 가능"
                            : $"일반 환생까지 인게임 {System.Math.Ceiling(s.RebirthMinutesLeft / 60d):N0}시간") +
                            (s.CanAdvanceEra ? "\n시대 퀘스트를 완료했습니다." : "\n시대 퀘스트가 진행 중입니다.");
                        Action("일반 환생", () => { confirmingRebirth = true; Refresh(); }, s.CanRebirth);
                        if (Game.IsOnline)
                            Action("공동 환생", Game.AdvanceEra);
                        else
                            Action("시대 전환", () => { confirmingEra = true; Refresh(); }, s.CanAdvanceEra);
                    }
                    break;
                case DialogueAction.Legend:
                    bodyTitle.text = "전설의 나무꾼";
                    var founders = s.Legend.founders;
                    var recent = System.Array.ConvertAll(founders, founder => founder.name);
                    int capacity = s.LegendPlacesAvailable;
                    body.text = $"벌목 업적  {s.LifetimeTimber:N0}/{r.LegendFoundingTimber:N0}\n" +
                        $"목재 {r.LegendTrialTimber:N0}개를 가져오면 시련을 마칠 수 있어요. 남은 인정 인원 {capacity}명.";
                    if (recent.Length > 0)
                        body.text += "\n기록된 나무꾼  " + string.Join(", ", System.Linq.Enumerable.Take(recent, 6));
                    Action("전설을 기록한다", () => Game.PerformWorld(CampaignAction.RecordLegend), s.CanRecordLegend(Game.LegendPlayerId));
                    Action("시련을 마친다", () => Game.PerformWorld(CampaignAction.ProveLegend), s.CanProveLegend(Game.LegendPlayerId));
                    break;
            }
        }
    }
}
