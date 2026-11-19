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
        private bool confirming;
        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            leave.onClick.AddListener(hud.CloseDialogue);
            portrait.texture = game.NpcPortraitTexture;
        }

        public void Open(VillageInteractable target)
        {
            npc = target;
            confirming = false;
            bool beforeFarm = !Game.State.FarmAvailable;
            topic = npc.Role switch
            {
                VillageRole.Farmer => beforeFarm ? DialogueAction.Foundation : DialogueAction.Job,
                VillageRole.NoticeBoard => DialogueAction.Job,
                VillageRole.Shrine when Game.State.CanRebirth => DialogueAction.Rebirth,
                VillageRole.Guide when !beforeFarm => DialogueAction.GuideAfter,
                _ => DialogueAction.Greeting
            };
            InventoryWindowView.Clear(keywords);
            var topics = npc.Definition != null && npc.Definition.Dialogue != null ? npc.Definition.Dialogue.Topics : null;
            if (topics != null)
                foreach (var entry in topics)
                {
                    if (string.IsNullOrWhiteSpace(entry.keyword) || npc.Role == VillageRole.Farmer
                        && (beforeFarm ? entry.action != DialogueAction.Foundation : entry.action == DialogueAction.Foundation))
                        continue;
                    var selected = entry.action;
                    var button = Instantiate(Hud.Library.ActionButton, keywords);
                    button.Bind(entry.keyword, () =>
                    {
                        topic = selected;
                        confirming = false;
                        Refresh();
                    });
                }

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
                    body.text = prose + $"\n\n{ItemCatalog.Get(quest.TimberItemId).Name} {quest.TimberCount}개";
                    if (s.FoundationStage == FarmFoundationStage.WaitingForContract)
                        Action("계약서를 받는다", () => Game.Perform(s.TryAcceptFoundation));
                    else if (s.FoundationStage == FarmFoundationStage.CollectingTimber)
                    {
                        int owned = s.Inventory.Count(quest.TimberItemId);
                        body.text += $"  ({owned}/{quest.TimberCount})";
                        Action("목재를 납품한다", () => Game.Perform(s.TryDeliverTimber), owned >= quest.TimberCount);
                    }
                    else
                        body.text = s.CanRebirth ? $"{Game.FarmSiteName}에 농장을 준비할게. 기억의 우물에서 다음 삶을 준비해." : "목재는 잘 받았어. 농장 후보지의 팻말을 살펴봐 줘.";
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
                    bool accepts = s.IntroClaimed && !s.FarmUpgradePending && s.FarmLevel < r.ExpandedStage;
                    Action($"{r.DonationGoldStep} Gold 기부", () => Game.Perform(s.TryDonateGold), accepts);
                    Action("물품 기부", () => Game.Perform(s.TryDonatePotatoes), accepts);
                    break;
                case DialogueAction.Shop:
                    Action("거래한다", () => Hud.OpenShop(npc));
                    break;
                case DialogueAction.Rest:
                    body.text = prose + $"\n\n기상  {r.WakeMinute / 60:00}:{r.WakeMinute % 60:00}\n무료 잠자리  ·  스태미나 {r.FreeRestEnergy}\n객실  {r.RoomPrice} Gold  ·  완전 회복";
                    Action("무료 잠자리", () => Game.Sleep(false));
                    Action("객실을 빌린다", () => Game.Sleep(true), s.Gold >= r.RoomPrice);
                    break;
                case DialogueAction.Rebirth:
                    if (confirming)
                    {
                        body.text = $"이번 생의 레벨·소지품·장비·골드가 초기화됩니다.\n\n채집 스킬·AP {s.AP}·아르바이트 경력 {s.Career}회와 마을에 남긴 변화는 유지됩니다.";
                        if (!s.FarmAvailable)
                            body.text += $"\n\n다음 생에는 {Game.FarmSiteName}에 농장이 완성됩니다.";
                        Action("환생한다", Game.Rebirth);
                        Action("돌아간다", () =>
                        {
                            confirming = false;
                            Refresh();
                        });
                    }
                    else
                    {
                        body.text = prose + (s.FarmAvailable ? $"\n\n조건  ·  {r.IntroTitle} 완료, 레벨 {r.RebirthLevel}" : "\n\n목재를 납품하고 농장 부지를 정하면 다음 삶으로 갈 수 있습니다.");
                        Action("다음 삶을 준비한다", () =>
                        {
                            confirming = true;
                            Refresh();
                        }, s.CanRebirth);
                    }

                    break;
            }
        }
    }
}
