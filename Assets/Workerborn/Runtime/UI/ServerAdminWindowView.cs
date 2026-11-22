using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class ServerAdminWindowView : VillageWindowView
    {
        [SerializeField] private TMP_Text overview;
        [SerializeField] private TMP_Text capacity;
        [SerializeField] private TMP_InputField characterLimit;
        [SerializeField] private Button applyLimit;
        [SerializeField] private Button era;
        [SerializeField] private Button save;
        [SerializeField] private Button stop;
        [SerializeField] private RectTransform roster;
        [SerializeField] private UiActionView rosterRow;
        [SerializeField] private TMP_Text player;
        [SerializeField] private UiActionView ban;
        [SerializeField] private UiActionView role;
        [SerializeField] private Button delete;
        [SerializeField] private ScrollRect logScroll;
        [SerializeField] private TMP_Text logText;
        [SerializeField] private GameObject confirmation;
        [SerializeField] private TMP_Text question;
        [SerializeField] private Button confirm;
        [SerializeField] private Button cancel;

        private readonly Dictionary<string, UiActionView> rows = new Dictionary<string, UiActionView>();
        private DedicatedAdminStatus rendered;
        private string selected;
        private Action pending;
        private float nextRequest;
        private int displayedLimit;

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            applyLimit.onClick.AddListener(ApplyLimit);
            era.onClick.AddListener(() =>
            {
                Hud.CloseWindow(Id);
                Hud.OpenWindow("era")?.Refresh();
            });
            save.onClick.AddListener(() => Send(ServerAdminAction.Save));
            stop.onClick.AddListener(() => Confirm("월드를 저장하고 모든 접속을 종료할까요?", () => Send(ServerAdminAction.Stop)));
            delete.onClick.AddListener(DeleteSelected);
            confirm.onClick.AddListener(() =>
            {
                var action = pending;
                CancelConfirmation();
                action?.Invoke();
            });
            cancel.onClick.AddListener(CancelConfirmation);
            CancelConfirmation();
        }

        private void Update()
        {
            if (Game == null || !Game.CanManageServer || Time.unscaledTime < nextRequest)
                return;
            nextRequest = Time.unscaledTime + 1.2f;
            Game.SendAdministration(ServerAdminAction.Snapshot);
            Refresh();
        }

        private void OnDisable()
        {
            nextRequest = 0;
            CancelConfirmation();
        }

        public override void Refresh()
        {
            if (Game == null)
                return;
            if (!Game.CanManageServer)
            {
                Hud.CloseWindow(Id);
                return;
            }
            var status = Game.ServerAdministration;
            applyLimit.interactable = status != null;
            if (status == null)
            {
                overview.text = "서버 정보를 불러오는 중이에요.";
                capacity.text = "";
                SetLog("");
                rendered = null;
                UpdateRoster(Array.Empty<DedicatedAdminCharacter>());
                return;
            }
            if (!characterLimit.isFocused && displayedLimit != status.characterLimit)
            {
                displayedLimit = status.characterLimit;
                characterLimit.SetTextWithoutNotify(displayedLimit.ToString());
            }
            if (ReferenceEquals(status, rendered))
                return;
            rendered = status;
            string endpoint = status.port > 0 ? $"TCP {status.bind}:{status.port}" : Game.SessionName;
            var samples = status.characters.Where(character => character.online && character.pingMilliseconds >= 0).ToArray();
            string rtt = samples.Length == 0 ? "—" : $"{samples.Average(character => character.pingMilliseconds):0} ms";
            overview.text = $"{endpoint}   ·   동시 접속 {status.connected}/{WorldCapacity.MaxConnections}명   ·   평균 지연 {rtt}";
            capacity.text = $"캐릭터 {status.characters.Length}/{status.characterLimit}명   ·   현재 시대 기준 {status.requirementCharacters}명";
            UpdateRoster(status.characters);
            SetLog(status.recentLog.Length == 0 ? "아직 운영 기록이 없어요." : string.Join("\n", status.recentLog));
        }

        private void UpdateRoster(DedicatedAdminCharacter[] characters)
        {
            var active = new HashSet<string>(characters.Select(character => character.id), StringComparer.OrdinalIgnoreCase);
            foreach (string id in rows.Keys.Where(id => !active.Contains(id)).ToArray())
            {
                rows[id].gameObject.SetActive(false);
                Destroy(rows[id].gameObject);
                rows.Remove(id);
            }
            if (selected == null || !active.Contains(selected))
                selected = characters.FirstOrDefault()?.id;
            foreach (var character in characters)
            {
                if (!rows.TryGetValue(character.id, out var row))
                {
                    row = Instantiate(rosterRow, roster);
                    rows.Add(character.id, row);
                }
                row.Bind($"{character.name}  ·  {character.RoleLabel}  ·  {character.Presence}", () =>
                {
                    selected = character.id;
                    RefreshSelected();
                });
            }
            RefreshSelected();
        }

        private DedicatedAdminCharacter SelectedCharacter => rendered?.characters.FirstOrDefault(character => character.id == selected);
        private static bool CanChange(DedicatedAdminCharacter character) => character != null && !character.host;

        private void RefreshSelected()
        {
            var character = SelectedCharacter;
            bool enabled = CanChange(character);
            ban.Bind(character?.banned == true ? "차단 해제" : "차단", ToggleBan, enabled);
            role.Bind(character?.canManage == true ? "운영 권한 해제" : "운영 권한 부여", ToggleRole, enabled);
            delete.interactable = enabled;
            if (character == null)
            {
                player.text = "캐릭터를 선택해 주세요.";
                return;
            }
            player.text = $"{character.name}  ·  {character.RoleLabel}  ·  {character.Presence}\n플레이어 ID  {character.id}\n\n" +
                $"생성  {DedicatedAdminCharacter.FormatDate(character.createdUtc)}\n최근 접속  {DedicatedAdminCharacter.FormatDate(character.lastConnectedUtc)}\n" +
                $"마지막 종료  {DedicatedAdminCharacter.FormatDate(character.lastDisconnectedUtc)}\n접속 시간  {character.SessionDuration}   ·   지연 {character.Latency}";
        }

        private void SetLog(string text)
        {
            if (logText.text == text)
                return;
            bool follow = logScroll.content.rect.height <= logScroll.viewport.rect.height + 1 ||
                logScroll.verticalNormalizedPosition <= .05f;
            logText.text = text;
            float height = logText.GetPreferredValues(text, logText.rectTransform.rect.width, Mathf.Infinity).y;
            logScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(height + 10, logScroll.viewport.rect.height));
            if (follow)
                logScroll.verticalNormalizedPosition = 0;
        }

        private void ApplyLimit()
        {
            if (int.TryParse(characterLimit.text, out int limit) && WorldCapacity.IsValid(limit))
                Send(ServerAdminAction.SetCharacterLimit, value: limit);
            else
                Hud.ShowToast($"캐릭터 정원을 1명부터 {WorldCapacity.MaxCharacters}명까지 입력해 주세요.");
        }

        private void ToggleBan()
        {
            var character = SelectedCharacter;
            if (CanChange(character))
                Send(character.banned ? ServerAdminAction.Unban : ServerAdminAction.Ban, character.id);
        }

        private void ToggleRole()
        {
            var character = SelectedCharacter;
            if (!CanChange(character))
                return;
            if (character.canManage)
                Confirm($"‘{character.name}’의 운영 권한을 해제할까요?", () => Send(ServerAdminAction.RevokeOperator, character.id));
            else
                Confirm($"‘{character.name}’에게 정원·캐릭터·운영 권한 관리와 서버 종료 권한을 부여할까요?",
                    () => Send(ServerAdminAction.GrantOperator, character.id));
        }

        private void DeleteSelected()
        {
            var character = SelectedCharacter;
            if (CanChange(character))
                Confirm($"‘{character.name}’의 이 월드 소지품과 성장 기록을 삭제할까요?",
                    () => Send(ServerAdminAction.DeleteCharacter, character.id));
        }

        private void Send(ServerAdminAction action, string target = null, int value = 0)
        {
            if (!Game.SendAdministration(action, target, value))
                Hud.ShowToast("서버에 운영 명령을 보내지 못했어요.");
            Refresh();
        }

        private void Confirm(string message, Action action)
        {
            question.text = message;
            pending = action;
            confirmation.SetActive(true);
        }

        private void CancelConfirmation()
        {
            pending = null;
            confirmation.SetActive(false);
        }
    }
}
