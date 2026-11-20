using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class EraWindowView : VillageWindowView
    {
        [SerializeField] private TMP_Text summary;
        [SerializeField] private TMP_Text voting;
        [SerializeField] private TMP_Text participation;
        [SerializeField] private TMP_Text countdown;
        [SerializeField] private Button vote;
        [SerializeField] private Button participate;
        [SerializeField] private Button announce;
        [SerializeField] private Button cancel;
        [SerializeField] private TMP_InputField days;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            vote.onClick.AddListener(() => Game.PerformWorld(CampaignAction.VoteForEra, value: Game.EraStatus.voted ? 0 : 1));
            participate.onClick.AddListener(() => Game.PerformWorld(CampaignAction.ParticipateInEra, value: Game.EraStatus.optedIn ? 0 : 1));
            announce.onClick.AddListener(() =>
            {
                if (int.TryParse(days.text, out int duration) && duration >= 1 && duration <= 365)
                    Game.PerformWorld(CampaignAction.AnnounceEra, value: duration);
                else
                    Hud.ShowToast("예고 기간을 인게임 1일부터 365일까지 입력해 주세요.");
            });
            cancel.onClick.AddListener(() => Game.PerformWorld(CampaignAction.CancelAnnouncement));
        }

        private float nextRefresh;

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + .25f;
            Refresh();
        }

        public override void Refresh()
        {
            if (Game == null)
                return;
            var s = Game.State;
            var status = Game.EraStatus;
            bool ready = s.CanAdvanceEra && status != null;
            bool announced = status != null && status.deadline > 0;
            bool operatorControls = status?.canAnnounce == true;
            string progress = s.CanAdvanceEra ? "시대 퀘스트 완료" : "시대 퀘스트 진행 중";
            summary.text = $"{s.Era}시대   ›   {s.Era + 1}시대\n{progress}";
            voting.text = status == null ? "전환에 동의" : $"전환 동의 {status.votes}/{status.connected}" + (status.voted ? " · 동의 취소" : "");
            vote.interactable = ready;
            participate.interactable = ready && announced;
            participation.text = status?.optedIn == true ? "예고된 환생에 참여 중" : "예고된 환생에 참여";
            days.gameObject.SetActive(operatorControls);
            announce.gameObject.SetActive(operatorControls);
            announce.interactable = ready;
            cancel.gameObject.SetActive(operatorControls);
            cancel.interactable = announced;
            if (announced)
            {
                var date = TimeSpan.FromMinutes(Math.Max(0, Math.Ceiling(status.deadline - s.WorldMinute)));
                countdown.text = $"예고된 전환까지 {date.Days}일 {date.Hours:00}시간 {date.Minutes:00}분";
            }
            else
                countdown.text = "시대 전환 예고 없음";
        }
    }
}
