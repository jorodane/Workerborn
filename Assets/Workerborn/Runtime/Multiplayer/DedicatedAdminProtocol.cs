using System;

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
    }

    [Serializable]
    internal sealed class DedicatedAdminCharacter
    {
        public string id = "", name = "";
        public bool online = false, banned = false;
        public long createdUtc = 0, lastConnectedUtc = 0, lastDisconnectedUtc = 0;
        public int pingMilliseconds = -1;
    }
}
