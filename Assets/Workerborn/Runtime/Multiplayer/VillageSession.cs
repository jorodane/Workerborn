using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Workerborn
{
    internal enum SessionMessage { Hello = 1, Welcome, Checkpoint, Move, Command, World, Profile, Error, Leave }

    [Serializable]
    internal sealed class SessionPacket
    {
        public int version = VillageSession.ProtocolVersion;
        public SessionKind kind;
        public SessionMessage type;
        public string message, token, player, name, argument;
        public int request, value;
        public CampaignAction action;
        public VillageSave character;
        public VillageWorldRecord world;
        public CampaignStatus status;
        public PortableCharacter appearance;
        public SessionAvatar avatar;
        public SessionAvatar[] avatars;
    }

    [DefaultExecutionOrder(-200)]
    internal sealed class VillageSession : MonoBehaviour
    {
        public const int ProtocolVersion = 2;
        public const string MenuScene = "MainMenu";
        public const string VillageScene = "VillagePrototype";
        private const float ConnectionTimeout = 15f;
        private const float WorldInterval = .15f;
        private const float SaveInterval = 15f;
        private static readonly Encoding WireEncoding = new UTF8Encoding(false, true);

        private sealed class Connection
        {
            public VillageTcpPeer Tcp;
            public ulong Steam;
            public string Identity;
            public float LastSeen;
            public bool Leaving;
            public float CloseAfter;
        }

        public static VillageSession Instance { get; private set; }
        public static bool BootDedicated
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return Array.IndexOf(Environment.GetCommandLineArgs(), "-workerborn-server") >= 0;
#endif
            }
        }
        public bool IsDedicated { get; private set; }
        public bool Online => kind.HasValue;
        public bool HasLocalWorld => selectedWorld != null && !Online;
        public PortableCharacter SelectedCharacter { get; private set; }
        public bool SteamHost => kind == SessionKind.SteamFriends && authority != null;
        public bool Busy { get; private set; }
        public string MenuMessage { get; private set; }
        public string LocalPlayerId { get; private set; }
        public string LocalPlayerName => joinName;
        public VillageSave InitialCharacter { get; private set; }
        public CampaignStatus Status { get; private set; }
        public bool Submitting => waitingForRequest != 0 || leaveBy > 0;
        public string SessionLabel => kind switch
        {
            SessionKind.SteamFriends => "Steam 친구 방",
            SessionKind.Dedicated => "데디케이티드 서버",
            _ => "싱글플레이"
        };

        private readonly SteamVillageRooms steam = new SteamVillageRooms();
        private readonly List<Connection> connections = new List<Connection>();
        private readonly ConcurrentQueue<string> console = new ConcurrentQueue<string>();
        private VillagePrototype village;
        private VillageCampaign authority;
        private SessionKind? kind;
        private Connection host;
        private TcpListener listener;
        private TcpClient dialingClient;
        private Task dialing;
        private FileStream saveLock;
        private string savePath, addressKey, legacyAddressKey, joinName, localIdentity;
        private LocalWorld selectedWorld;
        private float lastHostMessage, nextNetwork, nextSave, connectBy, requestBy, nextHello, leaveBy;
        private int requestSequence, waitingForRequest, operationGeneration;
        private bool inviteWhenReady, exitRequested;
        private string exitMessage;
        private ulong pendingInvite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Instance != null)
                return;
            var root = new GameObject("Workerborn Session");
            DontDestroyOnLoad(root);
            Instance = root.AddComponent<VillageSession>();
            Instance.IsDedicated = BootDedicated;
            if (Instance.IsDedicated)
                Instance.kind = SessionKind.Dedicated;
        }

        private void Start()
        {
            if (IsDedicated)
            {
                Application.runInBackground = true;
                Application.targetFrameRate = 30;
                if (SceneManager.GetActiveScene().name != VillageScene)
                    SceneManager.LoadScene(VillageScene);
                return;
            }
            var args = Environment.GetCommandLineArgs();
            if (ulong.TryParse(Option(args, "+connect_lobby"), out ulong lobby) && lobby != 0)
                pendingInvite = lobby;
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamAppId")))
                steam.Initialize(RequestInvitation);
        }

        public void SinglePlayer(LocalWorld world, PortableCharacter character)
        {
            if (!Busy && !Online)
            {
                Select(world, character);
                OpenVillage();
            }
        }

        private void Select(LocalWorld world, PortableCharacter character)
        {
            selectedWorld = world;
            SelectedCharacter = character;
            joinName = character.name;
        }

        public LocalWorld LegacyFriendWorld()
        {
            if (!RequireSteam())
                return null;
            var world = new LocalWorld { id = LocalWorld.LegacyFriendId(steam.User), name = "기존 Steam 친구 마을" };
            return File.Exists(world.FilePath) || File.Exists(world.FilePath + ".bak") ? world : null;
        }

        public void ClearMenuMessage() => MenuMessage = "";

        public void CancelConnection()
        {
            if (Busy && Online && LocalPlayerId == null)
                FailConnect("접속을 취소했어요.");
        }

        public void HostFriends(LocalWorld world, PortableCharacter character)
        {
            if (Busy || Online || !RequireSteam())
                return;
            Select(world, character);
            BeginConnect(SessionKind.SteamFriends, "Steam 친구 방을 여는 중…");
            int generation = operationGeneration;
            steam.Host(ProtocolVersion, success =>
            {
                if (generation != operationGeneration)
                    return;
                if (!success)
                {
                    FailConnect("Steam 친구 방을 만들지 못했어요.");
                    return;
                }
                inviteWhenReady = true;
                OpenVillage();
            });
        }

        public IReadOnlyList<SteamVillageRooms.FriendRoom> FriendRooms()
        {
            if (Busy || Online || !RequireSteam())
                return Array.Empty<SteamVillageRooms.FriendRoom>();
            MenuMessage = "";
            return steam.Friends();
        }

        public void JoinFriend(ulong lobby, PortableCharacter character)
        {
            if (Busy || Online || lobby == 0 || !RequireSteam())
                return;
            Select(null, character);
            BeginConnect(SessionKind.SteamFriends, "Steam 친구 방에 접속하는 중…");
            int generation = operationGeneration;
            steam.Join(lobby, ProtocolVersion, success =>
            {
                if (generation != operationGeneration)
                    return;
                if (!success || steam.Owner == 0 || steam.Owner == steam.User)
                {
                    FailConnect("친구 방을 열 수 없어요. 친구의 게임 버전과 방 상태를 확인해 주세요.");
                    return;
                }
                host = new Connection { Steam = steam.Owner };
                nextHello = Time.unscaledTime;
            });
        }

        public void JoinDedicated(string address, PortableCharacter character)
        {
            if (Busy || Online)
                return;
            if (!VillageTcpPeer.TryEndpoint(address, out string server, out int port))
            {
                MenuMessage = "서버 주소를 확인해 주세요. 예: 127.0.0.1:7777";
                return;
            }
            Select(null, character);
            BeginConnect(SessionKind.Dedicated, "데디케이티드 서버에 접속하는 중…");
            legacyAddressKey = "workerborn.server." + server + ":" + port;
            addressKey = legacyAddressKey + "." + character.id;
            dialingClient = new TcpClient(AddressFamily.InterNetwork);
            try { dialing = dialingClient.ConnectAsync(server, port); }
            catch (SocketException) { FailConnect("서버에 연결하지 못했어요."); }
        }

        private void BeginConnect(SessionKind session, string message)
        {
            operationGeneration++;
            kind = session;
            SessionMessageText(message, true);
            connectBy = Time.unscaledTime + ConnectionTimeout;
            lastHostMessage = Time.unscaledTime;
            Application.runInBackground = true;
        }

        private bool RequireSteam()
        {
            if (steam.Initialize(RequestInvitation))
                return true;
            MenuMessage = "Steam 연결이 필요해요. Steam에서 게임을 실행한 뒤 다시 시도해 주세요.";
            return false;
        }

        private void RequestInvitation(ulong lobby)
        {
            pendingInvite = lobby;
            if (village != null)
                village.Hud.ShowToast("친구 방에 입장하려면 시작 화면으로 돌아와 주세요.");
        }

        public bool TryTakeInvite(out ulong lobby)
        {
            lobby = 0;
            if (pendingInvite == 0 || Busy || Online || village != null)
                return false;
            lobby = pendingInvite;
            pendingInvite = 0;
            return true;
        }

        private void OpenVillage()
        {
            Busy = true;
            SceneManager.LoadScene(VillageScene);
        }

        public void InitializeAuthority(VillageRules rules, IReadOnlyDictionary<string, string> sites, Vector3 spawn)
        {
            try
            {
                if (IsDedicated)
                {
                    var args = Environment.GetCommandLineArgs();
                    string bind = Option(args, "-bind") ?? "127.0.0.1";
                    string portText = Option(args, "-port") ?? VillageTcpPeer.DefaultPort.ToString();
                    if (!IPAddress.TryParse(bind, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                        !int.TryParse(portText, out int port) || port < 1 || port > 65535)
                        throw new ArgumentException("Use an IPv4 -bind and a -port between 1 and 65535.");
                    string path = Option(args, "-world") ?? Path.Combine(Application.persistentDataPath, "worlds", "dedicated.json");
                    OpenCampaign(path, rules, sites, spawn);
                    listener = new TcpListener(ip, port);
                    listener.Start(16);
                    _ = Task.Run(ReadConsole);
                    Debug.Log($"Workerborn dedicated server listening on {ip}:{port}. World: {savePath}");
                }
                else if (selectedWorld != null && SelectedCharacter != null)
                {
                    localIdentity = "local:" + SelectedCharacter.id;
                    string path = selectedWorld.FilePath;
                    CampaignRecord seed = null;
                    if (selectedWorld.FromLegacySolo && !File.Exists(path) && !File.Exists(path + ".bak"))
                        seed = ReadLegacySolo(rules);
                    OpenCampaign(path, rules, sites, spawn, seed);
                    if (selectedWorld.FromSteam && Online)
                        authority.AdoptLegacyIdentity("steam:" + steam.User, localIdentity);
                    if (!authority.Connect(localIdentity, joinName, out var profile, SelectedCharacter.look))
                        throw new InvalidOperationException("Local character profile unavailable.");
                    CommitInitial(profile, Online ? authority.Status(localIdentity, true) : null);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException ||
                ex is SocketException || ex is InvalidOperationException)
            {
                Debug.LogError("Workerborn could not start the session: " + ex.Message);
                if (IsDedicated)
                {
                    Shutdown(save: false);
                    Application.Quit(1);
                }
                else
                    QueueExit("선택한 월드의 저장 기록을 열지 못했어요.");
            }
        }

        private void OpenCampaign(string path, VillageRules rules, IReadOnlyDictionary<string, string> sites, Vector3 spawn,
            CampaignRecord seed = null)
        {
            savePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            saveLock = new FileStream(savePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            foreach (string candidate in new[] { savePath, savePath + ".bak" })
            {
                if (!File.Exists(candidate))
                    continue;
                try
                {
                    if (new FileInfo(candidate).Length > 128 * 1024 * 1024)
                        throw new InvalidDataException("World save exceeds the size limit.");
                    var record = JsonUtility.FromJson<CampaignRecord>(File.ReadAllText(candidate));
                    if (record == null)
                        throw new InvalidDataException("Invalid world save.");
                    authority = new VillageCampaign(rules, sites, spawn.x, spawn.z, record);
                    if (candidate != savePath && File.Exists(savePath))
                        File.Move(savePath, savePath + ".corrupt-" + DateTime.UtcNow.Ticks);
                    nextSave = Time.unscaledTime + SaveInterval;
                    return;
                }
                catch (Exception e) when (e is IOException || e is ArgumentException)
                {
                    Debug.LogWarning("Workerborn could not read a world record: " + e.Message);
                }
            }
            if (File.Exists(savePath) || File.Exists(savePath + ".bak"))
                throw new InvalidDataException("Both world records are unreadable.");
            authority = new VillageCampaign(rules, sites, spawn.x, spawn.z, seed);
            nextSave = Time.unscaledTime + SaveInterval;
        }

        private CampaignRecord ReadLegacySolo(VillageRules rules)
        {
            if (!VillageSaveStore.TryLoad(out var state, out var existing, out _, rules, repairBackup: false))
                throw new InvalidDataException("The previous single-player world could not be read.");
            var save = state.Export();
            save.playerX = existing.playerX;
            save.playerZ = existing.playerZ;
            save.cropRemaining = existing.cropRemaining;
            save.cropStates = existing.cropStates;
            save.animalStates = existing.animalStates;
            save.collectedPickupIds = existing.collectedPickupIds;
            save.drops = existing.drops;
            return new CampaignRecord
            {
                world = state.ExportWorld(),
                profiles = new[] { new CampaignProfile
                {
                    identity = localIdentity, id = "single-player", name = joinName,
                    enteredEra = state.Era, character = save
                } }
            };
        }

        public bool SaveSolo(VillageWorldRecord world, VillageSave character)
        {
            if (Online || authority == null)
                return false;
            authority.StoreSolo(localIdentity, world, character);
            return SaveWorld();
        }

        private void CommitInitial(CampaignProfile profile, CampaignStatus status)
        {
            LocalPlayerId = profile.id;
            InitialCharacter = profile.character;
            Status = status;
        }

        public void Attach(VillagePrototype game)
        {
            village = game;
            InitialCharacter = null;
            Busy = false;
            if (inviteWhenReady)
            {
                inviteWhenReady = false;
                InviteFriends();
            }
        }

        public void Detach(VillagePrototype game)
        {
            if (village == game)
                village = null;
        }

        public void InviteFriends()
        {
            if (!SteamHost || !steam.Invite())
                village?.Hud.ShowToast("Steam 오버레이에서 친구를 초대할 수 있어요. 오버레이 설정을 확인해 주세요.");
        }

        public bool Checkpoint(VillageSave character)
        {
            if (Submitting || !Online || character == null)
                return false;
            if (authority != null && localIdentity != null)
                return authority.Checkpoint(localIdentity, character);
            return host != null && Send(host, new SessionPacket { type = SessionMessage.Checkpoint, character = character });
        }

        public bool Submit(CampaignAction action, string argument = null, int value = 1)
        {
            if (!Online || village == null || Submitting)
                return false;
            var packet = new SessionPacket
            {
                type = SessionMessage.Command, action = action, argument = argument, value = value,
                request = ++requestSequence, character = village.CaptureSave()
            };
            if (authority != null && localIdentity != null)
            {
                CampaignCommand(localIdentity, packet, null);
                PublishWorld();
                return true;
            }
            if (host == null || !Send(host, packet))
            {
                village.Hud.ShowToast("서버에 요청을 보내지 못했어요.");
                return false;
            }
            waitingForRequest = packet.request;
            requestBy = Time.unscaledTime + ConnectionTimeout;
            return true;
        }

        public void ReturnToMenu()
        {
            if (leaveBy > 0 || waitingForRequest != 0)
                return;
            if (host != null && village != null &&
                Send(host, new SessionPacket { type = SessionMessage.Leave, character = village.CaptureSave() }))
            {
                leaveBy = Time.unscaledTime + 1.5f;
                village.Worker.Cancel();
                return;
            }
            village?.SaveGame(false);
            QueueExit("");
        }

        private void QueueExit(string message)
        {
            exitRequested = true;
            exitMessage = message;
        }

        private void Update()
        {
            if (exitRequested)
            {
                Shutdown();
                SessionMessageText(exitMessage, false);
                exitRequested = false;
                SceneManager.LoadScene(MenuScene);
                return;
            }
            steam.Poll(ReceiveSteam);
            float now = Time.unscaledTime;
            if (leaveBy > 0 && now >= leaveBy)
            {
                QueueExit("");
                return;
            }
            if (dialing?.IsCompleted == true)
            {
                if (dialing.IsFaulted || !dialingClient.Connected)
                {
                    _ = dialing.Exception;
                    FailConnect("서버에 연결하지 못했어요. 주소와 서버 상태를 확인해 주세요.");
                    return;
                }
                host = new Connection { Tcp = new VillageTcpPeer(dialingClient) };
                dialing = null;
                dialingClient = null;
                if (!Send(host, new SessionPacket
                {
                    type = SessionMessage.Hello, appearance = SelectedCharacter,
                    token = PlayerPrefs.GetString(addressKey, PlayerPrefs.GetString(legacyAddressKey, ""))
                }))
                {
                    FailConnect("서버에 연결하지 못했어요.");
                    return;
                }
            }
            if (Busy && Online && LocalPlayerId == null && now >= connectBy)
            {
                FailConnect("접속 응답 시간이 지났어요.");
                return;
            }
            if (host != null)
            {
                if (host.Tcp != null)
                    ReadTcp(host, FromHost);
                else if (LocalPlayerId == null && now >= nextHello)
                {
                    nextHello = now + 1f;
                    Send(host, new SessionPacket { type = SessionMessage.Hello, appearance = SelectedCharacter });
                }

                if (host != null && (host.Tcp?.Closed == true || now - lastHostMessage >= ConnectionTimeout ||
                    host.Steam != 0 && !steam.IsInRoom(host.Steam)))
                {
                    if (village == null && LocalPlayerId == null)
                        FailConnect("방이나 서버와 연결이 끊겼어요.");
                    else
                        QueueExit(leaveBy > 0 ? "" : "방이나 서버와 연결이 끊겼어요.");
                    return;
                }
            }

            if (waitingForRequest != 0 && now >= requestBy)
            {
                waitingForRequest = 0;
                QueueExit("서버 요청 시간이 지났어요. 방에 다시 입장해 주세요.");
                return;
            }
            if (authority != null && Online)
            {
                PollAuthority(now);
                if (authority == null)
                    return;
                if (authority.WillAdvance(Time.unscaledDeltaTime) && village != null)
                    Checkpoint(village.CaptureSave());
                if (authority.Tick(Time.unscaledDeltaTime))
                {
                    foreach (var connection in connections)
                        if (connection.Identity != null && !connection.Leaving)
                            SendProfile(connection.Identity, connection, null, 0, true, transition: true);
                    if (localIdentity != null)
                        SendProfile(localIdentity, null, null, 0, true, transition: true);
                    SaveWorld();
                }
                if (now >= nextSave)
                    SaveWorld();
            }
            if (now < nextNetwork)
                return;
            nextNetwork = now + WorldInterval;
            if (village != null && Online)
            {
                var pose = village.CaptureAvatar();
                if (authority != null && localIdentity != null)
                    authority.Move(localIdentity, pose);
                else if (host != null)
                    Send(host, new SessionPacket { type = SessionMessage.Move, avatar = pose });
            }
            if (authority != null && Online)
                PublishWorld();
        }

        private void PollAuthority(float now)
        {
            if (listener != null)
                for (int i = 0; i < 4 && listener.Pending(); i++)
                {
                    var client = listener.AcceptTcpClient();
                    if (connections.Count < 16)
                        connections.Add(new Connection { Tcp = new VillageTcpPeer(client), LastSeen = now });
                    else
                        client.Dispose();
                }

            for (int i = connections.Count - 1; i >= 0; i--)
            {
                var connection = connections[i];
                if (connection.Tcp != null)
                    ReadTcp(connection, FromClient);
                if (connection.Leaving && now < connection.CloseAfter && connection.Tcp?.Closed != true)
                    continue;
                if (!connection.Leaving && connection.Tcp?.Closed != true && now - connection.LastSeen < ConnectionTimeout &&
                    (connection.Steam == 0 || steam.IsInRoom(connection.Steam)))
                    continue;
                connection.Tcp?.Dispose();
                if (connection.Identity != null)
                    authority.Disconnect(connection.Identity);
                connections.RemoveAt(i);
            }

            for (int i = 0; i < 16 && authority != null && console.TryDequeue(out string command); i++)
                RunConsoleCommand(command);
        }

        private static void ReadTcp(Connection connection, Action<Connection, SessionPacket> consume)
        {
            for (int i = 0; i < 24 && !connection.Leaving && connection.Tcp.TryRead(out var data); i++)
            {
                if (!TryPacket(data, out var packet))
                {
                    connection.Leaving = true;
                    connection.Tcp.Dispose();
                    break;
                }
                consume(connection, packet);
            }
        }

        private void ReceiveSteam(ulong sender, byte[] data)
        {
            if (!TryPacket(data, out var packet) || kind != SessionKind.SteamFriends)
                return;
            if (authority != null)
            {
                var connection = connections.Find(candidate => candidate.Steam == sender);
                if (connection == null)
                {
                    if (packet.type != SessionMessage.Hello || connections.Count >= 12)
                        return;
                    connection = new Connection { Steam = sender, LastSeen = Time.unscaledTime };
                    connections.Add(connection);
                }
                FromClient(connection, packet);
            }
            else if (host?.Steam == sender)
                FromHost(host, packet);
        }

        private static bool TryPacket(byte[] data, out SessionPacket packet)
        {
            packet = null;
            try
            {
                packet = JsonUtility.FromJson<SessionPacket>(WireEncoding.GetString(data));
                return packet != null;
            }
            catch (ArgumentException) { return false; }
        }

        private bool Send(Connection connection, SessionPacket packet)
        {
            packet.kind = kind.GetValueOrDefault();
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            if (data.Length > VillageTcpPeer.MaxPayload)
                return false;
            return connection.Tcp != null ? connection.Tcp.Send(data) : steam.Send(connection.Steam, data);
        }

        private void FromClient(Connection connection, SessionPacket packet)
        {
            if (connection.Leaving)
                return;
            if (packet.version != ProtocolVersion || packet.kind != kind)
            {
                Send(connection, new SessionPacket { type = SessionMessage.Error, message = "서버와 게임 버전이 달라요." });
                return;
            }
            connection.LastSeen = Time.unscaledTime;
            if (packet.type == SessionMessage.Hello)
            {
                Welcome(connection, packet);
                return;
            }
            if (connection.Identity == null || connection.Leaving)
                return;
            switch (packet.type)
            {
                case SessionMessage.Checkpoint:
                    authority.Checkpoint(connection.Identity, packet.character);
                    break;
                case SessionMessage.Move:
                    authority.Move(connection.Identity, packet.avatar);
                    break;
                case SessionMessage.Command:
                    CampaignCommand(connection.Identity, packet, connection);
                    break;
                case SessionMessage.Leave:
                    authority.Checkpoint(connection.Identity, packet.character);
                    authority.Disconnect(connection.Identity);
                    connection.Identity = null;
                    SaveWorld();
                    Send(connection, new SessionPacket { type = SessionMessage.Leave });
                    connection.Leaving = true;
                    connection.CloseAfter = Time.unscaledTime + 1.5f;
                    break;
            }
        }

        private void Welcome(Connection connection, SessionPacket packet)
        {
            string token = null;
            if (connection.Identity == null)
            {
                var appearance = packet.appearance;
                if (appearance == null || !PortableCharacter.ValidId(appearance.id))
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, message = "입장할 캐릭터를 선택해 주세요." });
                    return;
                }
                string name = PortableCharacter.CleanName(appearance.name, "일꾼");
                string identity;
                if (connection.Steam != 0)
                {
                    string legacy = "steam:" + connection.Steam;
                    identity = legacy + ":" + appearance.id;
                    authority.AdoptLegacyIdentity(legacy, identity);
                }
                else if (string.IsNullOrEmpty(packet.token))
                    identity = "tcp:" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                else if (packet.token.Length == 68 && packet.token.StartsWith("tcp:", StringComparison.Ordinal) && authority.HasIdentity(packet.token))
                    identity = packet.token;
                else
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, value = 1, message = "이 서버에 이전 접속 기록이 없어요. 다시 입장해 주세요." });
                    return;
                }
                if (!authority.Connect(identity, name, out _, appearance.look))
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, message = "방이 가득 찼거나 이 캐릭터가 이미 접속 중이에요." });
                    return;
                }
                connection.Identity = identity;
            }
            if (connection.Steam == 0)
                token = connection.Identity;
            var profile = authority.Profile(connection.Identity);
            Send(connection, new SessionPacket
            {
                type = SessionMessage.Welcome, character = profile.character, player = profile.id, token = token,
                status = authority.Status(connection.Identity, false), name = profile.name
            });
        }

        private void CampaignCommand(string identity, SessionPacket packet, Connection connection)
        {
            string message = "시대 정보가 변경됐어요. 다시 시도해 주세요.";
            bool success = authority.Checkpoint(identity, packet.character) &&
                authority.Perform(identity, packet.action, packet.argument, packet.value, connection == null, out message);
            bool characterChanged = success && packet.action != CampaignAction.VoteForEra && packet.action != CampaignAction.AnnounceEra &&
                packet.action != CampaignAction.CancelAnnouncement && packet.action != CampaignAction.ParticipateInEra;
            SendProfile(identity, connection, message, packet.request, characterChanged, transition: false);
            if (success)
                SaveWorld();
        }

        private void SendProfile(string identity, Connection connection, string message, int request, bool includeCharacter, bool transition)
        {
            var packet = new SessionPacket
            {
                type = SessionMessage.Profile, character = includeCharacter ? authority.Profile(identity).character : null,
                status = authority.Status(identity, connection == null), world = authority.World, message = message,
                request = request, value = transition ? 1 : 0
            };
            if (connection == null)
                ApplyProfile(packet);
            else
                Send(connection, packet);
        }

        private void PublishWorld()
        {
            var avatars = authority.Avatars();
            foreach (var connection in connections)
            {
                if (connection.Identity == null || connection.Leaving)
                    continue;
                Send(connection, new SessionPacket
                {
                    type = SessionMessage.World, world = authority.World, avatars = avatars,
                    status = authority.Status(connection.Identity, false)
                });
            }
            if (village != null && localIdentity != null)
            {
                UpdateStatus(authority.Status(localIdentity, true));
                village.ApplySharedWorld(authority.World, avatars);
            }
        }

        private void FromHost(Connection connection, SessionPacket packet)
        {
            if (packet.version != ProtocolVersion || packet.kind != kind)
            {
                if (LocalPlayerId == null)
                    FailConnect("서버와 게임 버전이 달라요.");
                return;
            }
            lastHostMessage = Time.unscaledTime;
            if (packet.type == SessionMessage.Welcome && LocalPlayerId == null && packet.character != null && !string.IsNullOrEmpty(packet.player))
            {
                LocalPlayerId = packet.player;
                joinName = packet.name;
                InitialCharacter = packet.character;
                Status = packet.status;
                if (connection.Tcp != null && !string.IsNullOrEmpty(packet.token))
                {
                    PlayerPrefs.SetString(addressKey, packet.token);
                    PlayerPrefs.DeleteKey(legacyAddressKey);
                    PlayerPrefs.Save();
                }
                OpenVillage();
            }
            else if (packet.type == SessionMessage.Error && LocalPlayerId == null)
            {
                if (packet.value == 1 && addressKey != null)
                {
                    PlayerPrefs.DeleteKey(addressKey);
                    PlayerPrefs.DeleteKey(legacyAddressKey);
                    PlayerPrefs.Save();
                }
                FailConnect(packet.message);
            }
            else if (packet.type == SessionMessage.Leave && leaveBy > 0)
                QueueExit("");
            else if (packet.type == SessionMessage.Profile)
                ApplyProfile(packet);
            else if (packet.type == SessionMessage.World)
            {
                UpdateStatus(packet.status);
                if (village != null)
                    village.ApplySharedWorld(packet.world, packet.avatars);
            }
        }

        private void ApplyProfile(SessionPacket packet)
        {
            UpdateStatus(packet.status);
            if (packet.request != 0 && packet.request == waitingForRequest)
                waitingForRequest = 0;
            if (village != null)
                village.ApplySessionProfile(packet.character, packet.world, packet.message, packet.value == 1);
            else if (InitialCharacter != null && packet.character != null)
                InitialCharacter = packet.character;
        }

        private void UpdateStatus(CampaignStatus status)
        {
            if (status == null)
                return;
            if (status.deadline > 0 && status.deadline != Status?.deadline)
                village?.Hud.ShowToast("시대 전환이 예고됐어요. 메뉴의 공동 환생 창에서 참여를 결정할 수 있어요.");
            Status = status;
        }

        private void ReadConsole()
        {
            try
            {
                while (true)
                {
                    string command = Console.ReadLine();
                    if (command == null)
                        break;
                    if (command.Length <= 80 && console.Count < 16)
                        console.Enqueue(command);
                }
            }
            catch (IOException) { }
        }

        private void RunConsoleCommand(string command)
        {
            string[] words = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return;
            switch (words[0].ToLowerInvariant())
            {
                case "save": SaveWorld(); break;
                case "stop": Shutdown(); Application.Quit(); break;
                case "cancel": authority.Announce(CampaignAction.CancelAnnouncement, 0, out string canceled); Debug.Log(canceled); SaveWorld(); break;
                case "announce" when words.Length == 2 && int.TryParse(words[1], out int days):
                    authority.Announce(CampaignAction.AnnounceEra, days, out string announced);
                    Debug.Log(announced); SaveWorld(); break;
                default: Debug.Log("Workerborn server commands: announce <days>, cancel, save, stop"); break;
            }
        }

        private bool SaveWorld()
        {
            if (authority == null || savePath == null)
                return false;
            nextSave = Time.unscaledTime + SaveInterval;
            try
            {
                string temporary = savePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(authority.Export()));
                if (File.Exists(savePath))
                    File.Replace(temporary, savePath, savePath + ".bak");
                else
                    File.Move(temporary, savePath);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError("Workerborn could not save the world: " + e.Message);
                return false;
            }
        }

        private void FailConnect(string message)
        {
            Shutdown(save: false);
            SessionMessageText(message, false);
        }

        private void SessionMessageText(string message, bool busy)
        {
            MenuMessage = message;
            Busy = busy;
        }

        private void Shutdown(bool save = true)
        {
            if (save)
                SaveWorld();
            operationGeneration++;
            listener?.Stop();
            listener = null;
            foreach (var connection in connections)
                connection.Tcp?.Dispose();
            connections.Clear();
            host?.Tcp?.Dispose();
            host = null;
            dialingClient?.Dispose();
            dialingClient = null;
            if (dialing != null)
                _ = dialing.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            dialing = null;
            steam.Leave();
            saveLock?.Dispose();
            saveLock = null;
            authority = null;
            localIdentity = null;
            selectedWorld = null;
            SelectedCharacter = null;
            joinName = null;
            addressKey = legacyAddressKey = null;
            LocalPlayerId = null;
            InitialCharacter = null;
            Status = null;
            village = null;
            kind = null;
            Busy = false;
            waitingForRequest = 0;
            leaveBy = 0;
            inviteWhenReady = false;
        }

        private void OnApplicationQuit()
        {
            if (village != null && (Online || HasLocalWorld))
                village.SaveGame(false);
            Shutdown();
            steam.Dispose();
        }

        private static string Option(string[] args, string option)
        {
            int index = Array.IndexOf(args, option);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

    }
}
