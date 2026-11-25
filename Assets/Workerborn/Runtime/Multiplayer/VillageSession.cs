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
    internal enum SessionMessage
    {
        Hello = 1, Welcome, Checkpoint, Move, Command, World, Profile, Error, Leave, Chat, Ping, Pong,
        RemoveClient, DiscoverWorld, Administration, GroundTransfer, GatheredDrops, GroundItems, TimberWork
    }

    [Serializable]
    internal sealed class SessionPacket
    {
        public int version = VillageSession.ProtocolVersion;
        public SessionKind kind;
        public SessionMessage type;
        public string message, token, player, name, argument, worldId;
        public int request, value;
        public CampaignAction action;
        public VillageSave character;
        public VillageWorldRecord world;
        public WoodlandSave woodland;
        public TimberOrder timberOrder;
        public TimberYield[] timberAcquired;
        public CampaignStatus status;
        public PortableCharacter appearance;
        public SessionAvatar avatar;
        public SessionAvatar[] avatars;
        public GroundDropSave groundDrop;
        public GroundDropSave[] groundDrops;
        public string[] removedGroundDrops;
        public bool automaticHarvest;
        public string[] automaticHarvestIds;
        public HarvestPickupResult[] automaticHarvests;
        public string collectedDropId;
        public int collectedQuantity;
        public long pickupUtc;
        public ServerAdminAction adminAction;
        public DedicatedAdminStatus adminStatus;
    }

    [DefaultExecutionOrder(-200)]
    internal sealed partial class VillageSession : MonoBehaviour
    {
        public const int ProtocolVersion = 19;
        public const string MenuScene = "MainMenu";
        public const string VillageScene = "VillagePrototype";
        private const float ConnectionTimeout = 15f;
        private const float WorldInterval = .15f;
        private const float SaveInterval = 15f;
        private const float ChatInterval = .35f;
        private const float PingInterval = 4f;
        private static readonly Encoding WireEncoding = new UTF8Encoding(false, true);

        private sealed class Connection
        {
            public VillageTcpPeer Tcp;
            public ulong Steam;
            public string Identity;
            public float LastSeen;
            public bool Leaving;
            public float CloseAfter;
            public float NextChat;
            public int PendingPing;
            public float PingSentAt;
            public int PingMilliseconds = -1;
            public float NextAdminSnapshot;
            public int TimberRevision = -1;
            public readonly HashSet<string> PendingGroundDrops = new HashSet<string>();
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
        public bool HasLocalSave => HasLocalWorld && selectedWorld.HasSave;
        public PortableCharacter SelectedCharacter { get; private set; }
        public bool SteamHost => kind == SessionKind.SteamFriends && authority != null;
        public bool Busy { get; private set; }
        public string MenuMessage { get; private set; }
        public string LocalPlayerId { get; private set; }
        public string LocalPlayerName => joinName;
        public VillageSave InitialCharacter { get; private set; }
        public CampaignStatus Status { get; private set; }
        public bool Submitting => waitingForRequest != 0 || leaveBy > 0;
        internal bool AwaitingAutomaticHarvest => waitingForRequest != 0 && waitingForAutomaticHarvest;
        public string SessionLabel => kind switch
        {
            SessionKind.SteamFriends => "Steam 친구 방",
            SessionKind.Dedicated => "데디케이티드 서버",
            _ => "싱글플레이"
        };

        private readonly SteamVillageRooms steam = new SteamVillageRooms();
        private readonly List<Connection> connections = new List<Connection>();
        private readonly ConcurrentQueue<string> console = new ConcurrentQueue<string>();
        private DedicatedAdminPipe admin;
        private bool adminStopFailed;
        private float nextAdminStopAttempt;
        private float nextPing;
        private int pingSequence;
        private VillagePrototype village;
        private VillageCampaign authority;
        private SessionKind? kind;
        private Connection host;
        private TcpListener listener;
        private TcpClient dialingClient;
        private Task dialing;
        private FileStream saveLock;
        private string savePath, addressKey, legacyAddressKey, dedicatedWorldKey, joinName, localIdentity;
        private bool usingLegacyToken;
        private LocalWorld selectedWorld;
        private float lastHostMessage, nextNetwork, nextSave, connectBy, requestBy, nextHello, leaveBy;
        private int requestSequence, waitingForRequest, operationGeneration;
        private bool waitingForAutomaticHarvest;
        private bool inviteWhenReady, exitRequested;
        private string exitMessage;
        private ulong pendingInvite;
        private float nextLocalChat;
        private VillageSave pendingSoloReload;
        private string reloadNotice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Application.runInBackground = true;
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

        private void ConfigureCloudArea(RebirthZone zone)
        {
            if (zone == null || zone.Floor == null || zone.Spawn == null)
                return;
            Bounds area = zone.Area;
            Transform spawn = zone.Spawn;
            Vector3 position = spawn.position;
            authority.ConfigureCloudArea((x, z) => area.Contains(new Vector3(x, area.center.y, z)),
                new SessionAvatar { x = position.x, y = position.y, z = position.z, yaw = spawn.eulerAngles.y });
        }

        public void InitializeAuthority(VillageRules rules, IReadOnlyDictionary<string, string> sites, Vector3 spawn, RebirthZone cloudArea)
        {
            try
            {
                if (IsDedicated)
                {
                    var args = Environment.GetCommandLineArgs();
                    string bind = Option(args, "-bind") ?? DedicatedAdminProtocol.DefaultBind;
                    string portText = Option(args, "-port") ?? VillageTcpPeer.DefaultPort.ToString();
                    if (!IPAddress.TryParse(bind, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                        !int.TryParse(portText, out int port) || port < 1 || port > 65535)
                        throw new ArgumentException("Use an IPv4 -bind and a -port between 1 and 65535.");
                    string limitText = Option(args, "-character-limit");
                    int characterLimit = 0;
                    if (Array.IndexOf(args, "-character-limit") >= 0 &&
                        (!int.TryParse(limitText, out characterLimit) || !WorldCapacity.IsValid(characterLimit)))
                        throw new ArgumentException($"Use -character-limit between 1 and {WorldCapacity.MaxCharacters}.");
                    string path = Option(args, "-world") ?? Path.Combine(Application.persistentDataPath, "worlds", "dedicated.json");
                    if (Array.IndexOf(args, DedicatedAdminProtocol.PipeOption) >= 0)
                    {
                        if (!Guid.TryParseExact(Option(args, DedicatedAdminProtocol.PipeOption), "N", out var pipeId))
                            throw new ArgumentException("Invalid server manager instance ID.");
                        admin = new DedicatedAdminPipe(pipeId, console);
                    }
                    OpenCampaign(path, rules, sites, spawn, initialCharacterLimit: characterLimit, requireCharacterLimit: true);
                    ConfigureCloudArea(cloudArea);
                    listener = new TcpListener(ip, port);
                    listener.Start(16);
                    if (!SaveWorld())
                        throw new IOException("The server world could not be saved.");
                    if (admin == null)
                        _ = Task.Run(ReadConsole);
                    Debug.Log($"Workerborn dedicated server listening on {ip}:{port}. World: {savePath}. Characters: {authority.CreatedCharacters}/{authority.CharacterLimit}.");
                }
                else if (selectedWorld != null && SelectedCharacter != null)
                {
                    localIdentity = "local:" + SelectedCharacter.id;
                    string path = selectedWorld.FilePath;
                    CampaignRecord seed = null;
                    if (selectedWorld.FromLegacySolo && !File.Exists(path) && !File.Exists(path + ".bak"))
                        seed = ReadLegacySolo(rules);
                    OpenCampaign(path, rules, sites, spawn, seed, Online ? selectedWorld.initialCharacterLimit : 0,
                        requireCharacterLimit: Online && seed == null);
                    ConfigureCloudArea(cloudArea);
                    if (selectedWorld.FromSteam && Online)
                        authority.AdoptLegacyIdentity("steam:" + steam.User, localIdentity);
                    if (!authority.Connect(localIdentity, joinName, out var profile, SelectedCharacter.look))
                    {
                        QueueExit(JoinFailure(localIdentity));
                        return;
                    }
                    if (Online && !SaveWorld())
                        throw new IOException("The hosted world could not be saved.");
                    CommitInitial(profile, Online ? authority.Status(localIdentity, true) : null);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is UnauthorizedAccessException ||
                ex is SocketException || ex is InvalidOperationException || ex is TimeoutException)
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
            CampaignRecord seed = null, int initialCharacterLimit = 0, bool requireCharacterLimit = false)
        {
            savePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            saveLock = new FileStream(savePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            authority = ReadCampaign(rules, sites, spawn);
            if (authority == null)
            {
                if (requireCharacterLimit && initialCharacterLimit == 0)
                    throw new ArgumentException($"Choose -character-limit 1 to {WorldCapacity.MaxCharacters} when creating a server world.");
                authority = new VillageCampaign(rules, sites, spawn.x, spawn.z, seed, initialCharacterLimit);
            }
            nextSave = Time.unscaledTime + SaveInterval;
        }

        private VillageCampaign ReadCampaign(VillageRules rules, IReadOnlyDictionary<string, string> sites, Vector3 spawn)
        {
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
                    var restored = new VillageCampaign(rules, sites, spawn.x, spawn.z, record);
                    if (candidate != savePath && File.Exists(savePath))
                        File.Move(savePath, savePath + ".corrupt-" + DateTime.UtcNow.Ticks);
                    return restored;
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Workerborn could not read a world record: " + e.Message);
                }
            }
            if (File.Exists(savePath) || File.Exists(savePath + ".bak"))
                throw new InvalidDataException("Both world records are unreadable.");
            return null;
        }

        private CampaignRecord ReadLegacySolo(VillageRules rules)
        {
            if (!VillageSaveStore.TryLoad(out var state, out var existing, out _, rules, repairBackup: false))
                throw new InvalidDataException("The previous single-player world could not be read.");
            var save = state.Export();
            save.CopyPoseAndAction(existing);
            save.characters = existing.characters;
            save.cropRemaining = existing.cropRemaining;
            save.woodland = existing.woodland;
            save.cropStates = existing.cropStates;
            save.animalStates = existing.animalStates;
            save.collectedPickupIds = existing.collectedPickupIds;
            save.drops = existing.drops;
            return new CampaignRecord
            {
                world = state.ExportWorld(),
                woodland = existing.woodland,
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

        internal bool TryReloadSolo(VillageRules rules, IReadOnlyDictionary<string, string> sites, Vector3 spawn, bool usesSelectedWorld)
        {
            if (Busy || Online || exitRequested)
                return false;
            if (usesSelectedWorld)
            {
                if (localIdentity == null || savePath == null || SelectedCharacter == null)
                    return false;
                VillageCampaign restored;
                try { restored = ReadCampaign(rules, sites, spawn); }
                catch (Exception error) when (error is IOException || error is InvalidDataException ||
                    error is UnauthorizedAccessException || error is ArgumentException)
                {
                    Debug.LogWarning("Workerborn could not reload the world: " + error.Message);
                    return false;
                }
                if (restored == null || !restored.Connect(localIdentity, joinName, out var profile, SelectedCharacter.look))
                    return false;
                authority = restored;
                CommitInitial(profile, null);
                pendingSoloReload = profile.character;
                reloadNotice = "저장한 월드를 불러왔어요.";
            }
            else
            {
                if (!VillageSaveStore.TryLoad(out _, out var save, out string notice, rules))
                    return false;
                pendingSoloReload = save;
                reloadNotice = notice;
            }
            OpenVillage();
            return true;
        }

        internal bool TakeSoloReload(out VillageSave save, out string notice)
        {
            save = pendingSoloReload;
            notice = reloadNotice;
            pendingSoloReload = null;
            reloadNotice = null;
            return save != null;
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
            if (Online)
            {
                game.RefreshSharedGroundPickups();
                game.PresentWoodland(StartingWoodland);
            }
            initialWoodland = null;
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
            if (pendingGatheredDrops.Count > 0)
                return FlushGatheredDrops(character);
            if (authority != null && localIdentity != null)
                return authority.Checkpoint(localIdentity, character);
            return host != null && Send(host, new SessionPacket { type = SessionMessage.Checkpoint, character = character });
        }

        public bool SendChat(string message)
        {
            float now = Time.unscaledTime;
            if (!Online || village == null || leaveBy > 0f || now < nextLocalChat)
                return false;
            bool sent = authority != null && localIdentity != null ? RelayChat(localIdentity, message)
                : host != null && Send(host, new SessionPacket { type = SessionMessage.Chat, message = message });
            if (sent)
                nextLocalChat = now + ChatInterval;
            return sent;
        }

        private bool RelayChat(string identity, string message)
        {
            if (!VillageChatText.TryNormalize(message, out string text))
                return false;
            var sender = authority.Profile(identity);
            var packet = new SessionPacket { type = SessionMessage.Chat, player = sender.id, name = sender.name, message = text };
            foreach (var connection in connections)
                if (connection.Identity != null && !connection.Leaving)
                    Send(connection, packet);
            village?.ReceiveChat(sender.id, sender.name, text);
            return true;
        }

        public bool Submit(CampaignAction action, string argument = null, int value = 1) => SubmitCommand(new SessionPacket
        {
            type = SessionMessage.Command, action = action, argument = argument, value = value
        });

        private bool SubmitCommand(SessionPacket packet)
        {
            if (!Online || village == null || Submitting)
                return false;
            packet.request = ++requestSequence;
            packet.character = village.CaptureSave();
            if (!FlushGatheredDrops(packet.character))
                return false;
            if (authority != null && localIdentity != null)
            {
                if (packet.type == SessionMessage.GroundTransfer)
                    GroundTransfer(localIdentity, packet, null);
                else if (packet.type == SessionMessage.TimberWork)
                    TimberCommand(localIdentity, packet, null);
                else
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
            waitingForAutomaticHarvest = packet.automaticHarvest;
            requestBy = Time.unscaledTime + ConnectionTimeout;
            return true;
        }

        public void ReturnToMenu()
        {
            if (leaveBy > 0 || waitingForRequest != 0)
                return;
            if (host != null && village != null)
            {
                var character = village.CaptureSave();
                if (!FlushGatheredDrops(character))
                {
                    village.Hud.ShowToast("채집 결과를 서버에 보내고 있어요. 잠시 후 다시 나가 주세요.");
                    return;
                }
                if (Send(host, new SessionPacket { type = SessionMessage.Leave, character = character }))
                {
                    leaveBy = Time.unscaledTime + 1.5f;
                    village.Worker.Cancel();
                    return;
                }
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
                exitRequested = false;
                if (IsDedicated)
                {
                    Shutdown(save: false);
                    Application.Quit();
                    return;
                }
                Shutdown();
                SessionMessageText(exitMessage, false);
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
                if (!Send(host, new SessionPacket { type = SessionMessage.DiscoverWorld }))
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
            if (now >= nextNetwork && village != null && !Submitting && pendingGatheredDrops.Count > 0)
                FlushGatheredDrops(village.CaptureSave());
            if (authority != null && Online)
            {
                PollAuthority(now);
                if (authority == null || exitRequested)
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
            if (exitRequested)
                return;
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
                if (exitRequested)
                    return;
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

            for (int i = 0; i < 16 && !exitRequested && console.TryDequeue(out string command); i++)
                RunConsoleCommand(command);
            if (exitRequested)
                return;
            if (IsDedicated && admin?.Closed == true && now >= nextAdminStopAttempt)
            {
                nextAdminStopAttempt = now + SaveInterval;
                if (TryStopHostedWorld())
                    return;
            }
            if (now >= nextPing)
                ProbeClientLatency(now);
        }

        private void ProbeClientLatency(float now)
        {
            nextPing = now + PingInterval;
            if (++pingSequence == 0)
                pingSequence = 1;
            foreach (var connection in connections)
            {
                if (connection.Identity == null || connection.Leaving)
                    continue;
                if (connection.PendingPing != 0)
                    connection.PingMilliseconds = -1;
                if (Send(connection, new SessionPacket { type = SessionMessage.Ping, request = pingSequence }))
                {
                    connection.PendingPing = pingSequence;
                    connection.PingSentAt = now;
                }
            }
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
                    if (packet.type != SessionMessage.Hello || connections.Count >= WorldCapacity.MaxConnections)
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
            if (connection.Leaving || exitRequested)
                return;
            if (packet.version != ProtocolVersion || packet.kind != kind)
            {
                Send(connection, new SessionPacket { type = SessionMessage.Error, message = "서버와 게임 버전이 달라요." });
                return;
            }
            connection.LastSeen = Time.unscaledTime;
            if (packet.type == SessionMessage.DiscoverWorld && connection.Tcp != null && connection.Identity == null)
            {
                Send(connection, new SessionPacket { type = SessionMessage.DiscoverWorld, worldId = authority.WorldId });
                return;
            }
            if (packet.type == SessionMessage.Hello)
            {
                Welcome(connection, packet);
                return;
            }
            if (connection.Identity == null || connection.Leaving)
                return;
            switch (packet.type)
            {
                case SessionMessage.Pong when packet.request != 0 && packet.request == connection.PendingPing:
                    connection.PingMilliseconds = Mathf.RoundToInt((Time.unscaledTime - connection.PingSentAt) * 1000f);
                    connection.PendingPing = 0;
                    break;
                case SessionMessage.Checkpoint:
                    authority.Checkpoint(connection.Identity, packet.character);
                    break;
                case SessionMessage.Move:
                    authority.Move(connection.Identity, packet.avatar);
                    break;
                case SessionMessage.Command:
                    CampaignCommand(connection.Identity, packet, connection);
                    break;
                case SessionMessage.GroundTransfer:
                    GroundTransfer(connection.Identity, packet, connection);
                    break;
                case SessionMessage.TimberWork:
                    TimberCommand(connection.Identity, packet, connection);
                    break;
                case SessionMessage.GatheredDrops:
                    PublishGatheredDrops(connection.Identity, packet.character, packet.groundDrops);
                    break;
                case SessionMessage.Administration:
                    AdministrationCommand(connection.Identity, packet, connection);
                    break;
                case SessionMessage.Chat:
                    if (Time.unscaledTime >= connection.NextChat && RelayChat(connection.Identity, packet.message))
                        connection.NextChat = Time.unscaledTime + ChatInterval;
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
                else if (packet.token.Length == 68 && packet.token.StartsWith("tcp:", StringComparison.Ordinal) &&
                    authority.MatchesPortableCharacter(packet.token, appearance.id))
                    identity = packet.token;
                else
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, value = 1, message = "이 서버에 이전 접속 기록이 없어요. 다시 입장해 주세요." });
                    return;
                }
                string portableId = connection.Tcp == null ? null : appearance.id;
                if (authority.IsBanned(identity, portableId))
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, message = "서버에서 차단된 캐릭터예요." });
                    return;
                }
                if (connection.Tcp != null && string.IsNullOrEmpty(packet.token) && authority.HasPortableCharacter(portableId))
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, message = "이 월드에서 사용하던 캐릭터예요. 기존 접속 기록으로 입장해 주세요." });
                    return;
                }
                bool newDedicatedCharacter = connection.Tcp != null && string.IsNullOrEmpty(packet.token);
                if (!authority.Connect(identity, name, out var joined, appearance.look, portableId))
                {
                    Send(connection, new SessionPacket { type = SessionMessage.Error, message = JoinFailure(identity) });
                    return;
                }
                if (newDedicatedCharacter && !SaveWorld())
                {
                    authority.TryDeleteCharacter(joined.id, () => true, out _);
                    Send(connection, new SessionPacket { type = SessionMessage.Error, message = "월드에 새 캐릭터를 저장하지 못했어요." });
                    return;
                }
                connection.Identity = identity;
                foreach (var drop in authority.SharedGroundDrops)
                    connection.PendingGroundDrops.Add(drop.id);
            }
            if (connection.Steam == 0)
                token = connection.Identity;
            var profile = authority.Profile(connection.Identity);
            Send(connection, new SessionPacket
            {
                type = SessionMessage.Welcome, character = profile.character, player = profile.id, token = token, woodland = authority.Woodland,
                status = authority.Status(connection.Identity, CanManageProfile(connection.Identity)), name = profile.name
            });
        }

        private string JoinFailure(string identity) => !authority.HasIdentity(identity) && !authority.HasFreeCharacterSlot
            ? $"월드 캐릭터 정원 {authority.CharacterLimit}명이 찼어요. 이 월드에서 만들었던 캐릭터로 입장해 주세요."
            : "방이 가득 찼거나 이 캐릭터가 이미 접속 중이에요.";

        private void CampaignCommand(string identity, SessionPacket packet, Connection connection)
        {
            if (packet.action == CampaignAction.SetCharacterLimit)
            {
                string feedback = CanManageProfile(identity) ? ExecuteAdministration(ServerAdminAction.SetCharacterLimit, null, packet.value)
                    : "월드 캐릭터 정원을 변경하려면 운영 권한이 필요해요.";
                SendProfile(identity, connection, feedback, packet.request, includeCharacter: false, transition: false);
                return;
            }
            string message = "시대 정보가 변경됐어요. 다시 시도해 주세요.";
            bool success = authority.Checkpoint(identity, packet.character) &&
                authority.Perform(identity, packet.action, packet.argument, packet.value, CanManageProfile(identity), out message);
            bool characterChanged = success && packet.action != CampaignAction.VoteForEra && packet.action != CampaignAction.AnnounceEra &&
                packet.action != CampaignAction.CancelAnnouncement && packet.action != CampaignAction.ParticipateInEra && packet.action != CampaignAction.BuildFacility;
            SendProfile(identity, connection, message, packet.request, characterChanged, transition: false);
            if (success)
            {
                if (packet.action == CampaignAction.AnnounceEra || packet.action == CampaignAction.CancelAnnouncement)
                    RecordAdminOperation(message);
                SaveWorld();
            }
        }

        private void SendProfile(string identity, Connection connection, string message, int request, bool includeCharacter, bool transition,
            string collectedDropId = null, int collectedQuantity = 0, GroundDropSave remainder = null,
            HarvestPickupResult[] automaticHarvests = null, TimberYield[] timberAcquired = null)
        {
            var packet = new SessionPacket
            {
                type = SessionMessage.Profile, character = includeCharacter ? authority.Profile(identity).character : null,
                status = authority.Status(identity, CanManageProfile(identity)), world = authority.World, message = message,
                request = request, value = transition ? 1 : 0, collectedDropId = collectedDropId,
                collectedQuantity = collectedQuantity, groundDrop = remainder,
                automaticHarvest = automaticHarvests != null, automaticHarvests = automaticHarvests,
                timberAcquired = timberAcquired
            };
            if (connection == null)
                ApplyProfile(packet);
            else
                Send(connection, packet);
        }

        private void PublishWorld()
        {
            var avatars = authority.Avatars();
            var woodland = authority.Woodland;
            long now = GroundDropSave.UtcNow;
            authority.ReleaseExpiredDrops(now, SyncGroundChange);
            foreach (var connection in connections)
            {
                if (connection.Identity == null || connection.Leaving)
                    continue;
                bool receiveTimber = woodland != null && woodland.revision != connection.TimberRevision;
                bool sent = Send(connection, new SessionPacket
                {
                    type = SessionMessage.World, world = authority.World, avatars = avatars, pickupUtc = now,
                    woodland = receiveTimber ? woodland : null,
                    status = authority.Status(connection.Identity, CanManageProfile(connection.Identity))
                });
                if (sent && receiveTimber)
                    connection.TimberRevision = woodland.revision;
                SendGroundChanges(connection);
            }
            if (village != null && localIdentity != null)
            {
                UpdateStatus(authority.Status(localIdentity, CanManageProfile(localIdentity)));
                village.ApplySharedWorld(authority.World, avatars);
                village.PresentWoodland(woodland);
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
            if (packet.type == SessionMessage.DiscoverWorld && connection.Tcp != null && dedicatedWorldKey == null &&
                Guid.TryParseExact(packet.worldId, "N", out var worldId))
            {
                dedicatedWorldKey = "workerborn.world." + worldId.ToString("N") + "." + SelectedCharacter.id;
                string token = PlayerPrefs.GetString(dedicatedWorldKey, "");
                if (string.IsNullOrEmpty(token))
                {
                    token = PlayerPrefs.GetString(addressKey, PlayerPrefs.GetString(legacyAddressKey, ""));
                    usingLegacyToken = !string.IsNullOrEmpty(token);
                }
                SendDedicatedHello(connection, token);
            }
            else if (packet.type == SessionMessage.Welcome && LocalPlayerId == null && packet.character != null && !string.IsNullOrEmpty(packet.player) &&
                (connection.Tcp == null || dedicatedWorldKey != null))
            {
                LocalPlayerId = packet.player;
                joinName = packet.name;
                InitialCharacter = packet.character;
                initialWoodland = packet.woodland;
                Status = packet.status;
                if (connection.Tcp != null && !string.IsNullOrEmpty(packet.token))
                {
                    PlayerPrefs.SetString(dedicatedWorldKey, packet.token);
                    if (usingLegacyToken)
                    {
                        if (PlayerPrefs.GetString(addressKey, "") == packet.token)
                            PlayerPrefs.DeleteKey(addressKey);
                        if (PlayerPrefs.GetString(legacyAddressKey, "") == packet.token)
                            PlayerPrefs.DeleteKey(legacyAddressKey);
                    }
                    PlayerPrefs.Save();
                }
                usingLegacyToken = false;
                OpenVillage();
            }
            else if (packet.type == SessionMessage.Error && LocalPlayerId == null)
            {
                if (packet.value == 1 && usingLegacyToken)
                {
                    usingLegacyToken = false;
                    SendDedicatedHello(connection, "");
                    return;
                }
                if (packet.value == 1)
                    ForgetDedicatedToken();
                FailConnect(packet.message);
            }
            else if (packet.type == SessionMessage.Leave && leaveBy > 0)
                QueueExit("");
            else if (packet.type == SessionMessage.Ping && LocalPlayerId != null)
                Send(connection, new SessionPacket { type = SessionMessage.Pong, request = packet.request });
            else if (packet.type == SessionMessage.RemoveClient && LocalPlayerId != null)
            {
                if (packet.value == 1 && connection.Tcp != null)
                    ForgetDedicatedToken();
                QueueExit(packet.message);
            }
            else if (packet.type == SessionMessage.Administration && LocalPlayerId != null)
                ApplyAdministrationReply(packet);
            else if (packet.type == SessionMessage.Profile)
                ApplyProfile(packet);
            else if (packet.type == SessionMessage.Chat && village != null && !string.IsNullOrEmpty(packet.player) &&
                VillageChatText.TryNormalize(packet.message, out string text))
                village.ReceiveChat(packet.player, PortableCharacter.CleanName(packet.name, "일꾼"), text);
            else if (packet.type == SessionMessage.World)
            {
                SynchronizePickupClock(packet.pickupUtc);
                UpdateStatus(packet.status);
                if (village != null)
                {
                    village.ApplySharedWorld(packet.world, packet.avatars);
                    village.PresentWoodland(packet.woodland);
                }
                else if (packet.woodland != null)
                    initialWoodland = packet.woodland;
            }
            else if (packet.type == SessionMessage.GroundItems && LocalPlayerId != null)
                ReceiveGroundChanges(packet);
        }

        private void SendDedicatedHello(Connection connection, string token)
        {
            if (!Send(connection, new SessionPacket { type = SessionMessage.Hello, appearance = SelectedCharacter, token = token }))
                FailConnect("서버에 접속 기록을 보내지 못했어요.");
        }

        private void ForgetDedicatedToken()
        {
            if (dedicatedWorldKey == null)
                return;
            PlayerPrefs.DeleteKey(dedicatedWorldKey);
            PlayerPrefs.Save();
        }

        private void ApplyProfile(SessionPacket packet)
        {
            UpdateStatus(packet.status);
            if (packet.request != 0 && packet.request == waitingForRequest)
            {
                waitingForRequest = 0;
                waitingForAutomaticHarvest = false;
            }
            if (village != null)
            {
                var harvests = packet.automaticHarvest ? packet.automaticHarvests ?? Array.Empty<HarvestPickupResult>() : null;
                if (harvests != null)
                    ApplyAutomaticHarvestLedger(harvests);
                village.ApplySessionProfile(packet.character, packet.world, packet.message, packet.value == 1, harvests);
                if (packet.timberAcquired is { Length: > 0 })
                    village.PlayTimberAcquisitions(village.Worker, packet.timberAcquired);
                if (!string.IsNullOrEmpty(packet.collectedDropId))
                    ApplyGroundPickupReply(packet);
                if (!packet.automaticHarvest && packet.request != 0 && packet.request == requestSequence)
                    village.Hud.FinishDialogueWorldReply(packet.character != null, packet.message);
            }
            else if (InitialCharacter != null && packet.character != null)
                InitialCharacter = packet.character;
        }

        private void UpdateStatus(CampaignStatus status)
        {
            if (status == null)
                return;
            if (status.deadline > 0 && status.deadline != Status?.deadline)
                village?.Hud.ShowToast("시대 전환이 예고됐어요. 메뉴의 공동 환생 창에서 참여를 결정할 수 있어요.");
            bool changed = Status?.canAnnounce != status.canAnnounce;
            Status = status;
            if (!status.canAnnounce)
                Administration = null;
            if (changed)
                village?.Hud.RefreshWindows();
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
                    if (command.Length <= DedicatedAdminProtocol.MaxCommandLength && console.Count < 16)
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
                case "save":
                    ExecuteAdministration(ServerAdminAction.Save, null, 0);
                    break;
                case "stop":
                    adminStopFailed = !TryStopHostedWorld();
                    if (adminStopFailed)
                        PublishAdminStatus();
                    break;
                case "status": PublishAdminStatus(); break;
                case "cancel": authority.Announce(CampaignAction.CancelAnnouncement, 0, out string canceled); RecordAdminOperation(canceled); SaveWorld(); break;
                case "announce" when words.Length == 2 && int.TryParse(words[1], out int days):
                    authority.Announce(CampaignAction.AnnounceEra, days, out string announced);
                    RecordAdminOperation(announced); SaveWorld(); break;
                case "character-limit" when words.Length == 1:
                    Debug.Log($"Characters: {authority.CreatedCharacters}/{authority.CharacterLimit}. Current quest scale: {authority.World.requirementCharacters}.");
                    break;
                case "character-limit" when words.Length == 2 && int.TryParse(words[1], out int limit):
                    ExecuteAdministration(ServerAdminAction.SetCharacterLimit, null, limit);
                    break;
                case "ban" when words.Length == 2: ExecuteAdministration(ServerAdminAction.Ban, words[1], 0); break;
                case "unban" when words.Length == 2: ExecuteAdministration(ServerAdminAction.Unban, words[1], 0); break;
                case "delete-character" when words.Length == 2: ExecuteAdministration(ServerAdminAction.DeleteCharacter, words[1], 0); break;
                case "grant-operator" when words.Length == 2: ExecuteAdministration(ServerAdminAction.GrantOperator, words[1], 0); break;
                case "revoke-operator" when words.Length == 2: ExecuteAdministration(ServerAdminAction.RevokeOperator, words[1], 0); break;
                default:
                    Debug.Log("Workerborn server commands: character-limit [count], announce <days>, cancel, save, stop, ban <id>, unban <id>, delete-character <id>, grant-operator <id>, revoke-operator <id>");
                    break;
            }
        }

        private void PublishAdminStatus()
        {
            if (admin == null || listener == null)
                return;
            var status = CaptureAdministration();
            status.stopFailed = adminStopFailed;
            adminStopFailed = false;
            admin.Publish(JsonUtility.ToJson(status));
        }

        private void RemoveClient(string identity, string message, bool deleted)
        {
            var connection = connections.Find(candidate => candidate.Identity == identity && !candidate.Leaving);
            if (connection == null)
                return;
            Send(connection, new SessionPacket { type = SessionMessage.RemoveClient, message = message, value = deleted ? 1 : 0 });
            connection.Identity = null;
            connection.Leaving = true;
            connection.CloseAfter = Time.unscaledTime + 1.5f;
        }

        private bool TryStopHostedWorld()
        {
            CheckpointLocalHost();
            var connected = authority.Profiles.Where(profile => authority.IsConnected(profile.identity))
                .Select(profile => (profile, previousEnd: profile.lastDisconnectedUtc)).ToArray();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var entry in connected)
                entry.profile.lastDisconnectedUtc = now;
            if (!SaveWorld())
            {
                foreach (var entry in connected)
                    entry.profile.lastDisconnectedUtc = entry.previousEnd;
                return false;
            }
            Debug.Log("월드를 저장하고 서버를 종료해요.");
            QueueExit(IsDedicated ? "" : "월드를 저장하고 친구 방을 닫았어요.");
            return true;
        }

        private bool TrySaveCharacterLimit(int limit, out string message)
        {
            int previousLimit = authority.World.characterLimit;
            int previousRequirements = authority.World.requirementCharacters;
            if (!authority.TrySetCharacterLimit(limit, out message))
                return false;
            if (SaveWorld())
                return true;

            authority.World.characterLimit = previousLimit;
            authority.World.requirementCharacters = previousRequirements;
            message = "캐릭터 정원 변경을 저장하지 못했어요.";
            return false;
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
            admin?.Dispose();
            admin = null;
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
            addressKey = legacyAddressKey = dedicatedWorldKey = null;
            usingLegacyToken = false;
            LocalPlayerId = null;
            InitialCharacter = null;
            Status = null;
            initialWoodland = null;
            pendingSoloReload = null;
            reloadNotice = null;
            Administration = null;
            adminLog.Clear();
            village = null;
            kind = null;
            Busy = false;
            waitingForRequest = 0;
            waitingForAutomaticHarvest = false;
            receivedGroundDrops.Clear();
            pendingGatheredDrops.Clear();
            pickupClockAtSync = 0;
            leaveBy = 0;
            nextLocalChat = 0;
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
