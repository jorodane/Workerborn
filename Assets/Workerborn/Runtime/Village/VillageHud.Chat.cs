using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageChatView chat;
        private PlayerOverheadView localOverhead;
        internal bool IsChatTyping => chat != null && chat.IsTyping;

        private void InitializeChat()
        {
            chat = Instantiate(library.Chat, chatLayer);
            chat.Bind(game.TrySendChat, chatLayer);
            localOverhead = CreatePlayerOverhead(game.LegendPlayerName);
        }

        internal PlayerOverheadView CreatePlayerOverhead(string playerName)
        {
            var overhead = Instantiate(library.PlayerOverhead, pickupNames != null ? pickupNames.transform : transform);
            overhead.SetName(playerName);
            return overhead;
        }

        internal void PositionPlayerOverhead(PlayerOverheadView overhead, Vector3 position)
        {
            overhead.Position(game.WorldCamera, (RectTransform)overhead.transform.parent, position, game.SharesMap(position));
        }

        internal void ShowChat(string speaker, string message, bool local)
        {
            chat?.Append(speaker, message);
            if (local)
                localOverhead?.Say(message);
        }

        private bool HandleChatInput()
        {
            if (IsChatTyping)
            {
                if (WorkerbornInput.Pressed("Gameplay/Cancel"))
                    chat.LeaveInput();
                return true;
            }
            if (chat == null || !WorkerbornInput.Pressed("Gameplay/Chat"))
                return false;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<TMP_InputField>() is { isFocused: true })
                return false;
            chat.OpenInput();
            return true;
        }
    }
}
