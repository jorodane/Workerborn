using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class FrontEndMenu : MonoBehaviour
    {
        [SerializeField] private Button singlePlayer;
        [SerializeField] private Button multiplayer;
        [SerializeField] private Button invite;
        [SerializeField] private Button joinFriend;
        [SerializeField] private Button joinServer;
        [SerializeField] private Button back;
        [SerializeField] private Button refresh;
        [SerializeField] private Button connect;
        [SerializeField] private GameObject startPanel;
        [SerializeField] private GameObject multiplayerPanel;
        [SerializeField] private GameObject friendPanel;
        [SerializeField] private GameObject serverPanel;
        [SerializeField] private RectTransform friendList;
        [SerializeField] private UiActionView room;
        [SerializeField] private TMP_InputField address;
        [SerializeField] private TMP_Text status;
        [SerializeField] private LaunchSetupView setup;
        private VillageSession session;
        private GameObject current, previous;
        private LaunchDirectory directory;
        private string emptyListMessage, previousMessage;

        private void Awake()
        {
            session = VillageSession.Instance;
            if (session == null || session.IsDedicated)
            {
                gameObject.SetActive(false);
                return;
            }
            directory = LaunchDirectory.Load();
            singlePlayer.onClick.AddListener(() => SelectProfile(startPanel, true, session.SinglePlayer));
            multiplayer.onClick.AddListener(() => Show(multiplayerPanel));
            invite.onClick.AddListener(() => SelectProfile(multiplayerPanel, true, session.HostFriends, true));
            joinFriend.onClick.AddListener(() => { Show(friendPanel); RefreshFriends(); });
            joinServer.onClick.AddListener(() => Show(serverPanel));
            back.onClick.AddListener(Back);
            refresh.onClick.AddListener(RefreshFriends);
            connect.onClick.AddListener(Connect);
            address.onSubmit.AddListener(_ => Connect());
            Show(startPanel);
        }

        private void Back()
        {
            if (session.Busy)
            {
                session.CancelConnection();
                return;
            }
            if (current == setup.gameObject)
            {
                if (!setup.Back())
                    Show(previous);
                return;
            }
            Show(current == multiplayerPanel ? startPanel : multiplayerPanel);
        }

        private void SelectProfile(GameObject returnTo, bool needsWorld, Action<LocalWorld, PortableCharacter> begin, bool showOldSteamWorld = false)
        {
            previous = returnTo;
            Show(setup.gameObject);
            setup.Open(directory, needsWorld ? directory.AvailableWorlds(showOldSteamWorld ? session.LegacyFriendWorld() : null) : null, begin);
        }

        private void Connect()
        {
            if (session.Busy)
                return;
            if (!VillageTcpPeer.TryEndpoint(address.text, out _, out _))
            {
                emptyListMessage = "서버 주소를 확인해 주세요. 예: 127.0.0.1:7777";
                return;
            }
            string endpoint = address.text;
            SelectProfile(serverPanel, false, (_, character) => session.JoinDedicated(endpoint, character));
        }

        private void Show(GameObject panel)
        {
            current = panel;
            startPanel.SetActive(panel == startPanel);
            multiplayerPanel.SetActive(panel == multiplayerPanel);
            friendPanel.SetActive(panel == friendPanel);
            serverPanel.SetActive(panel == serverPanel);
            setup.gameObject.SetActive(panel == setup.gameObject);
            emptyListMessage = null;
            session.ClearMenuMessage();
            back.gameObject.SetActive(panel != startPanel);
        }

        private void RefreshFriends()
        {
            emptyListMessage = null;
            InventoryWindowView.Clear(friendList);
            var rooms = session.FriendRooms();
            foreach (var friend in rooms)
                Instantiate(room, friendList).Bind(friend.Name, () =>
                    SelectProfile(friendPanel, false, (_, character) => session.JoinFriend(friend.Lobby, character)));
            if (rooms.Count == 0)
                emptyListMessage = "열려 있는 친구 방이 없어요.";
        }

        private void Update()
        {
            if (session.TryTakeInvite(out ulong lobby))
                SelectProfile(multiplayerPanel, false, (_, character) => session.JoinFriend(lobby, character));
            bool ready = !session.Busy;
            singlePlayer.interactable = multiplayer.interactable = invite.interactable = joinServer.interactable =
                joinFriend.interactable = refresh.interactable = connect.interactable = ready;
            address.interactable = ready;
            if (current == setup.gameObject)
                setup.SetBusy(!ready);
            string message = string.IsNullOrEmpty(session.MenuMessage) ? emptyListMessage : session.MenuMessage;
            if (message != previousMessage)
            {
                status.text = message ?? "";
                previousMessage = message;
            }
        }
    }
}
