using System;
using System.Collections.Generic;

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID
using System.Runtime.InteropServices;
using Steamworks;
#endif

namespace Workerborn
{
    internal sealed class SteamVillageRooms : IDisposable
    {
        public readonly struct FriendRoom
        {
            public readonly ulong Lobby;
            public readonly string Name;
            public FriendRoom(ulong lobby, string name) { Lobby = lobby; Name = name; }
        }

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID
        private readonly IntPtr[] buffer = new IntPtr[16];
        private const int Channel = 25;
        private const string GameKey = "workerborn_protocol";
        private bool ready;
        private Callback<GameLobbyJoinRequested_t> invitation;
        private Callback<SteamNetworkingMessagesSessionRequest_t> networkingRequest;
        private CallResult<LobbyCreated_t> created;
        private CallResult<LobbyEnter_t> entered;
        private CSteamID lobby;
        private int roomRequest;
        public ulong User => SteamUser.GetSteamID().m_SteamID;
        public ulong Owner => lobby.IsValid() ? SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID : 0;
        public string PlayerName => SteamFriends.GetPersonaName();

        public bool Initialize(Action<ulong> invited)
        {
            if (ready)
                return true;
            try
            {
                ready = SteamAPI.Init();
                if (!ready)
                    return false;
                invitation = Callback<GameLobbyJoinRequested_t>.Create(value => invited(value.m_steamIDLobby.m_SteamID));
                networkingRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(value =>
                {
                    var remote = value.m_identityRemote;
                    if (IsInRoom(remote.GetSteamID64()))
                        SteamNetworkingMessages.AcceptSessionWithUser(ref remote);
                });
                created = CallResult<LobbyCreated_t>.Create();
                entered = CallResult<LobbyEnter_t>.Create();
                SteamNetworkingUtils.InitRelayNetworkAccess();
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is InvalidOperationException)
            {
                Dispose();
                return false;
            }
        }

        public void Host(int version, Action<bool> completed)
        {
            if (created.IsActive() || entered.IsActive())
            {
                completed(false);
                return;
            }
            int request = ++roomRequest;
            created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, WorldCapacity.MaxConnections), (value, failed) =>
            {
                bool joined = !failed && value.m_eResult == EResult.k_EResultOK;
                if (request != roomRequest)
                {
                    if (joined)
                        SteamMatchmaking.LeaveLobby(new CSteamID(value.m_ulSteamIDLobby));
                    return;
                }
                if (!joined)
                {
                    completed(false);
                    return;
                }
                lobby = new CSteamID(value.m_ulSteamIDLobby);
                SteamMatchmaking.SetLobbyData(lobby, GameKey, version.ToString());
                completed(true);
            });
        }

        public void Join(ulong id, int version, Action<bool> completed)
        {
            if (created.IsActive() || entered.IsActive())
            {
                completed(false);
                return;
            }
            int request = ++roomRequest;
            entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)), (value, failed) =>
            {
                bool joined = !failed && value.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
                if (request != roomRequest)
                {
                    if (joined)
                        SteamMatchmaking.LeaveLobby(new CSteamID(value.m_ulSteamIDLobby));
                    return;
                }
                if (!joined)
                {
                    completed(false);
                    return;
                }
                lobby = new CSteamID(value.m_ulSteamIDLobby);
                if (SteamMatchmaking.GetLobbyData(lobby, GameKey) != version.ToString())
                {
                    Leave();
                    completed(false);
                    return;
                }
                completed(true);
            });
        }

        public IReadOnlyList<FriendRoom> Friends()
        {
            var rooms = new List<FriendRoom>();
            var seen = new HashSet<ulong>();
            var flags = EFriendFlags.k_EFriendFlagImmediate;
            for (int i = 0, count = SteamFriends.GetFriendCount(flags); i < count; i++)
            {
                var friend = SteamFriends.GetFriendByIndex(i, flags);
                if (SteamFriends.GetFriendGamePlayed(friend, out var game) && game.m_gameID.AppID() == SteamUtils.GetAppID() &&
                    game.m_steamIDLobby.IsValid() && seen.Add(game.m_steamIDLobby.m_SteamID))
                    rooms.Add(new FriendRoom(game.m_steamIDLobby.m_SteamID, SteamFriends.GetFriendPersonaName(friend)));
            }
            return rooms;
        }

        public bool Invite()
        {
            if (!ready || !lobby.IsValid() || !SteamUtils.IsOverlayEnabled())
                return false;
            SteamFriends.ActivateGameOverlayInviteDialog(lobby);
            return true;
        }

        public bool IsInRoom(ulong user)
        {
            if (!ready || !lobby.IsValid() || user == 0 || user == User)
                return false;
            for (int i = 0, count = SteamMatchmaking.GetNumLobbyMembers(lobby); i < count; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == user)
                    return true;
            return false;
        }

        public string NameOf(ulong user) => SteamFriends.GetFriendPersonaName(new CSteamID(user));

        public void Poll(Action<ulong, byte[]> received)
        {
            if (!ready)
                return;
            SteamAPI.RunCallbacks();
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, buffer, buffer.Length);
            for (int i = 0; i < count; i++)
                try
                {
                    var message = SteamNetworkingMessage_t.FromIntPtr(buffer[i]);
                    ulong sender = message.m_identityPeer.GetSteamID64();
                    if (!IsInRoom(sender) || message.m_cbSize < 1 || message.m_cbSize > VillageTcpPeer.MaxPayload)
                        continue;
                    var data = new byte[message.m_cbSize];
                    Marshal.Copy(message.m_pData, data, 0, data.Length);
                    received(sender, data);
                }
                finally { SteamNetworkingMessage_t.Release(buffer[i]); }
        }

        public bool Send(ulong user, byte[] data)
        {
            if (!IsInRoom(user) || data.Length > VillageTcpPeer.MaxPayload)
                return false;
            var remote = new SteamNetworkingIdentity();
            remote.SetSteamID64(user);
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return SteamNetworkingMessages.SendMessageToUser(ref remote, handle.AddrOfPinnedObject(), (uint)data.Length,
                    Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession,
                    Channel) == EResult.k_EResultOK;
            }
            finally { handle.Free(); }
        }

        public void Leave()
        {
            if (!ready)
                return;
            roomRequest++;
            if (lobby.IsValid())
            {
                if (Owner == User)
                    SteamMatchmaking.SetLobbyJoinable(lobby, false);
                for (int i = 0, count = SteamMatchmaking.GetNumLobbyMembers(lobby); i < count; i++)
                {
                    var member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                    if (member.m_SteamID == User)
                        continue;
                    var remote = new SteamNetworkingIdentity();
                    remote.SetSteamID(member);
                    SteamNetworkingMessages.CloseSessionWithUser(ref remote);
                }
                SteamMatchmaking.LeaveLobby(lobby);
            }
            lobby = CSteamID.Nil;
        }

        public void Dispose()
        {
            if (!ready)
                return;
            Leave();
            invitation?.Dispose();
            networkingRequest?.Dispose();
            created?.Dispose();
            entered?.Dispose();
            SteamAPI.Shutdown();
            ready = false;
        }
#else
        public ulong User => 0;
        public ulong Owner => 0;
        public string PlayerName => "";
        public bool Initialize(Action<ulong> invited) => false;
        public void Host(int version, Action<bool> completed) => completed(false);
        public void Join(ulong id, int version, Action<bool> completed) => completed(false);
        public IReadOnlyList<FriendRoom> Friends() => Array.Empty<FriendRoom>();
        public bool Invite() => false;
        public bool IsInRoom(ulong user) => false;
        public string NameOf(ulong user) => "";
        public void Poll(Action<ulong, byte[]> received) { }
        public bool Send(ulong user, byte[] data) => false;
        public void Leave() { }
        public void Dispose() { }
#endif
    }
}
