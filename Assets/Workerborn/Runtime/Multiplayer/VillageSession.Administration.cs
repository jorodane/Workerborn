using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

namespace Workerborn
{
    internal sealed partial class VillageSession
    {
        private const int AdminLogLimit = 24;
        private readonly Queue<string> adminLog = new Queue<string>();
        public DedicatedAdminStatus Administration { get; private set; }
        public bool CanManage => Online && LocalPlayerId != null && (SteamHost || Status?.canAnnounce == true);

        private bool CanManageProfile(string identity) => authority != null && identity != null &&
            (SteamHost && identity == localIdentity || authority.IsOperator(identity));

        private void CheckpointLocalHost()
        {
            if (village != null && localIdentity != null)
                authority.Checkpoint(localIdentity, village.CaptureSave());
        }

        public bool SendAdministration(ServerAdminAction action, string target = null, int value = 0)
        {
            if (!CanManage || village == null || leaveBy > 0 || exitRequested)
                return false;
            var packet = new SessionPacket { type = SessionMessage.Administration, adminAction = action, argument = target, value = value };
            if (!SteamHost)
                return host != null && Send(host, packet);
            AdministrationCommand(localIdentity, packet, null);
            return true;
        }

        private void AdministrationCommand(string requester, SessionPacket packet, Connection connection)
        {
            if (exitRequested)
                return;
            if (!CanManageProfile(requester))
            {
                if (connection != null)
                    Send(connection, new SessionPacket { type = SessionMessage.Administration, message = "서버 운영 권한이 필요해요." });
                return;
            }

            string feedback = null;
            if (packet.adminAction == ServerAdminAction.Snapshot)
            {
                if (connection != null)
                {
                    float now = Time.unscaledTime;
                    if (now < connection.NextAdminSnapshot)
                        return;
                    connection.NextAdminSnapshot = now + .5f;
                }
            }
            else
                feedback = ExecuteAdministration(packet.adminAction, packet.argument, packet.value);

            if (exitRequested || connection?.Leaving == true)
                return;
            var reply = new SessionPacket
            {
                type = SessionMessage.Administration,
                message = feedback,
                adminStatus = CanManageProfile(requester) ? CaptureAdministration() : null
            };
            if (connection == null)
                ApplyAdministrationReply(reply);
            else
                Send(connection, reply);
        }

        private void ApplyAdministrationReply(SessionPacket packet)
        {
            if (!CanManage)
                return;
            Administration = packet.adminStatus;
            if (!string.IsNullOrEmpty(packet.message))
                village?.Hud.ShowToast(packet.message);
        }

        private DedicatedAdminStatus CaptureAdministration()
        {
            var endpoint = listener?.LocalEndpoint as IPEndPoint;
            var active = connections.Where(connection => connection.Identity != null && !connection.Leaving)
                .ToDictionary(connection => connection.Identity);
            return new DedicatedAdminStatus
            {
                bind = endpoint?.Address.ToString() ?? "",
                port = endpoint?.Port ?? 0,
                connected = authority.ConnectedCount,
                characterLimit = authority.CharacterLimit,
                requirementCharacters = authority.World.requirementCharacters,
                characters = authority.Profiles.Select(profile => new DedicatedAdminCharacter
                {
                    id = profile.id,
                    name = profile.name,
                    online = authority.IsConnected(profile.identity),
                    banned = profile.banned,
                    host = SteamHost && profile.identity == localIdentity,
                    canManage = profile.canManage,
                    createdUtc = profile.createdUtc,
                    lastConnectedUtc = profile.lastConnectedUtc,
                    lastDisconnectedUtc = profile.lastDisconnectedUtc,
                    pingMilliseconds = active.TryGetValue(profile.identity, out var connection) ? connection.PingMilliseconds : -1
                }).ToArray(),
                recentLog = adminLog.ToArray()
            };
        }

        private string ExecuteAdministration(ServerAdminAction action, string target, int value)
        {
            string feedback;
            switch (action)
            {
                case ServerAdminAction.SetCharacterLimit:
                    TrySaveCharacterLimit(value, out feedback);
                    break;
                case ServerAdminAction.Save:
                    CheckpointLocalHost();
                    feedback = SaveWorld() ? "월드를 저장했어요." : "월드를 저장하지 못했어요.";
                    break;
                case ServerAdminAction.Stop:
                    if (TryStopHostedWorld())
                        return null;
                    feedback = "월드를 저장하지 못해 서버를 계속 실행 중이에요.";
                    break;
                case ServerAdminAction.Ban:
                case ServerAdminAction.Unban:
                case ServerAdminAction.DeleteCharacter:
                case ServerAdminAction.GrantOperator:
                case ServerAdminAction.RevokeOperator:
                    feedback = ChangeRoster(action, target);
                    break;
                default:
                    feedback = "지원하지 않는 운영 명령이에요.";
                    break;
            }
            RecordAdminOperation(feedback);
            return feedback;
        }

        private string ChangeRoster(ServerAdminAction action, string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64 || authority.FindCharacter(id) is not { } profile)
                return "이 월드에 해당 캐릭터가 없어요.";
            if (SteamHost && profile.identity == localIdentity)
                return "Steam 방장 캐릭터는 여기에서 변경할 수 없어요.";
            string display = $"‘{profile.name}’";
            if (action == ServerAdminAction.Ban || action == ServerAdminAction.Unban)
            {
                bool banned = action == ServerAdminAction.Ban;
                if (!authority.TrySetBan(id, banned, () => SaveWorld(), out string identity))
                    return $"{display} 차단 상태를 저장하지 못했어요.";
                if (banned)
                    RemoveClient(identity, "서버에서 이 캐릭터를 차단했어요.", deleted: false);
                return banned ? $"{display} 캐릭터를 차단했어요." : $"{display} 캐릭터의 차단을 해제했어요.";
            }
            if (action == ServerAdminAction.DeleteCharacter)
            {
                if (!authority.TryDeleteCharacter(id, () => SaveWorld(), out string identity))
                    return $"{display} 캐릭터를 삭제하지 못했어요.";
                RemoveClient(identity, "이 월드에서 캐릭터가 삭제됐어요.", deleted: true);
                return $"{display} 캐릭터의 월드 기록을 삭제했어요.";
            }
            bool grant = action == ServerAdminAction.GrantOperator;
            if (!authority.TrySetOperator(profile, grant, () => SaveWorld()))
                return $"{display} 운영 권한을 저장하지 못했어요.";
            return grant ? $"{display} 캐릭터에 운영 권한을 부여했어요." : $"{display} 캐릭터의 운영 권한을 해제했어요.";
        }

        private void RecordAdminOperation(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            if (adminLog.Count == AdminLogLimit)
                adminLog.Dequeue();
            adminLog.Enqueue(message);
            Debug.Log(message);
        }
    }
}
