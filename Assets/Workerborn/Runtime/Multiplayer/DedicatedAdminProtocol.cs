using System;
using System.Globalization;

namespace Workerborn
{
    internal static class DedicatedAdminProtocol
    {
        public const string PipeOption = "-workerborn-admin-pipe";
        public const string DefaultBind = "127.0.0.1";
        public const int DefaultPort = 7777;
        public const int MaxCommandLength = 80;

        public static string PipeName(Guid instance) => "workerborn-server-" + instance.ToString("N");
    }

    internal enum ServerAdminAction
    {
        Snapshot, SetCharacterLimit, Ban, Unban, DeleteCharacter, GrantOperator, RevokeOperator, Save, Stop
    }

    [Serializable]
    internal sealed class DedicatedAdminStatus
    {
        public string bind = DedicatedAdminProtocol.DefaultBind;
        public int port = DedicatedAdminProtocol.DefaultPort;
        public int connected = 0;
        public int characterLimit = WorldCapacity.DefaultNewWorld;
        public int requirementCharacters = 1;
        public bool stopFailed = false;
        public DedicatedAdminCharacter[] characters = Array.Empty<DedicatedAdminCharacter>();
        public string[] recentLog = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class DedicatedAdminCharacter
    {
        public string id = "", name = "";
        public bool online = false, banned = false, canManage = false, host = false;
        public long createdUtc = 0, lastConnectedUtc = 0, lastDisconnectedUtc = 0;
        public int pingMilliseconds = -1;

        public string RoleLabel => host ? "방장" : canManage ? "운영자" : "일반";
        public string Presence => banned ? "차단됨" : online ? "접속 중" : "오프라인";
        public string Latency => pingMilliseconds < 0 ? "—" : $"{pingMilliseconds} ms";
        public string SessionDuration
        {
            get
            {
                long end = online ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : lastDisconnectedUtc;
                if (lastConnectedUtc <= 0 || end < lastConnectedUtc || end > 253402300799L)
                    return "—";
                var duration = TimeSpan.FromSeconds(end - lastConnectedUtc);
                return $"{(long)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
            }
        }

        public static string FormatDate(long utc) => utc > 0 && utc <= 253402300799L
            ? DateTimeOffset.FromUnixTimeSeconds(utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "—";
    }
}
