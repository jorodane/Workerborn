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
        [SerializeField] private TMP_InputField playerName;
        [SerializeField] private TMP_Text status;
        private VillageSession session;
        private GameObject current;

        private void Awake()
        {
            session = VillageSession.Instance;
            if (session == null || session.IsDedicated)
            {
                gameObject.SetActive(false);
                return;
            }
            singlePlayer.onClick.AddListener(session.SinglePlayer);
            multiplayer.onClick.AddListener(() => Show(multiplayerPanel));
            invite.onClick.AddListener(session.HostFriends);
            joinFriend.onClick.AddListener(() => { Show(friendPanel); RefreshFriends(); });
            joinServer.onClick.AddListener(() => Show(serverPanel));
            back.onClick.AddListener(() =>
            {
                if (session.Busy)
                {
                    session.CancelConnection();
                    return;
                }
                Show(current == multiplayerPanel ? startPanel : multiplayerPanel);
            });
            refresh.onClick.AddListener(RefreshFriends);
            connect.onClick.AddListener(Connect);
            address.onSubmit.AddListener(_ => Connect());
            Show(startPanel);
        }

        private string EmptyListMessage { get; set; }
        private string previousMessage;

        private void Connect()
        {
            if (!session.Busy)
                session.JoinDedicated(address.text, playerName.text);
        }

        private void Show(GameObject panel)
        {
            current = panel;
            startPanel.SetActive(panel == startPanel);
            multiplayerPanel.SetActive(panel == multiplayerPanel);
            friendPanel.SetActive(panel == friendPanel);
            serverPanel.SetActive(panel == serverPanel);
            ClearMessages();
            back.gameObject.SetActive(panel != startPanel);
        }

        private void ClearMessages()
        {
            EmptyListMessage = null;
            session.ClearMenuMessage();
        }

        private void RefreshFriends()
        {
            EmptyListMessage = null;
            InventoryWindowView.Clear(friendList);
            var rooms = session.FriendRooms();
            foreach (var friend in rooms)
                Instantiate(room, friendList).Bind(friend.Name, () => session.JoinFriend(friend.Lobby));
            if (rooms.Count == 0)
                EmptyListMessage = "열려 있는 친구 방이 없어요.";
        }

        private void Update()
        {
            bool ready = !session.Busy;
            singlePlayer.interactable = multiplayer.interactable = invite.interactable = joinServer.interactable =
                joinFriend.interactable = refresh.interactable = connect.interactable = ready;
            address.interactable = playerName.interactable = ready;
            string message = string.IsNullOrEmpty(session.MenuMessage) ? EmptyListMessage : session.MenuMessage;
            if (message != previousMessage)
            {
                status.text = message ?? "";
                previousMessage = message;
            }
        }
    }
}
