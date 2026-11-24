using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageChatView chat;
        private PlayerOverheadView localOverhead;
        private float playerHealthVisibleUntil;
        internal bool IsChatTyping => chat != null && chat.IsTyping;

        private void InitializeChat()
        {
            chatAtStatus = chatLayer.anchoredPosition;
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

        internal void PositionPlayerOverhead(PlayerOverheadView overhead, Vector3 position, Vector3 namePosition)
        {
            var canvas = (RectTransform)overhead.transform.parent;
            bool visible = PickupNames.TryProject(game, canvas, position, namePosition, out var point);
            overhead.Position(point, visible);
            overhead.Nameplate.SetBox(WorkerbornInput.Held("Gameplay/Names"));
        }

        internal void ShowPlayerHealthUntil(float until) => playerHealthVisibleUntil = until;

        private void UpdatePlayerOverhead()
        {
            if (localOverhead == null)
                return;
            var targeting = game.Targeting;
            var state = game.State;
            bool show = !state.AwaitingIncarnation && (WorkerbornInput.Held("Gameplay/Names") || game.Worker.IsFighting ||
                Time.unscaledTime < playerHealthVisibleUntil ||
                targeting != null && (targeting.IsHoveringPlayer || targeting.IsOverNameplate(localOverhead.Nameplate)));
            localOverhead.Nameplate.SetHealth(show, state.Health, state.HealthLimit);
            PositionPlayerOverhead(localOverhead, game.PlayerActor.transform.position, game.PlayerActor.NamePosition);
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
