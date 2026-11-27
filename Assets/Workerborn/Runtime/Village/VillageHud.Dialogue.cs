using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageInteractable dialogueTarget;
        private bool tradeOpenedInventory;
        internal bool DialogueStartedInReach { get; private set; }
        private readonly Dictionary<VillageInteractable, NpcConversationBubbleView> npcConversationBubbles =
            new Dictionary<VillageInteractable, NpcConversationBubbleView>();
        internal VillageInteractable ConversationTarget => dialogueTarget;
        internal bool IsSpeakingWith(VillageInteractable target) => target != null && dialogueTarget == target;

        public void OpenDialogue(VillageInteractable target, DialogueAction? topic = null) =>
            OpenDialogueTopic(target, topic.HasValue ? game.FindDialogueTopic(target, topic.Value) : null);

        internal void OpenDialogueTopic(VillageInteractable target, DialogueTopicDefinition topic)
        {
            if (target == null || game.State.AwaitingIncarnation && target != game.RebirthArea?.Guide)
                return;
            CloseDialogue();
            HideDialogueStock();
            CloseWorldContext();
            dialogueTarget = target;
            DialogueStartedInReach = game.Worker.InReach(target.transform.position, target.InteractionDistance + .25f);
            var selected = game.IsDialogueTopicAvailable(target, topic) ? topic : null;
            (OpenWindow("conversation") as ConversationWindowView)?.OpenTopic(target, selected);
            if (selected?.action == DialogueAction.Shop)
                OpenShop(target);
        }

        internal bool HandleDialoguePageInput()
        {
            if (!IsDialogueOpen || game.AwaitingWorldCommand || game.IsWorldTransitioning || IsChatTyping || HasQuantityPrompt)
                return false;
            bool space = WorkerbornInput.Pressed("Gameplay/Jump");
            if (!space && !WorkerbornInput.Pressed("Gameplay/Chat"))
                return false;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<TMP_InputField>() is { isFocused: true })
                return false;
            return views.TryGetValue("conversation", out var panel) &&
                panel is ConversationWindowView dialogue && dialogue.TryAdvancePage(pagingOnly: !space);
        }

        internal void TryLeaveDialogue()
        {
            if (views.TryGetValue("conversation", out var panel) && panel is ConversationWindowView { CanLeave: true })
                CloseDialogue(animate: true);
        }

        internal void FinishDialogueWorldReply(bool success, string message)
        {
            if (views.TryGetValue("conversation", out var panel) && panel is ConversationWindowView dialogue)
                dialogue.FinishWorldReply(success, message);
        }

        public void CloseDialogue() => CloseDialogue(animate: false);

        internal void CloseDialogue(bool animate)
        {
            if (ReferenceEquals(dialogueTarget, null))
                return;
            bool wasTrading = views.TryGetValue("shop", out var shop) && shop.IsOpen;
            dialogueTarget = null;
            DialogueStartedInReach = false;
            if (wasTrading)
            {
                HideWindow("quantity", animate);
                CancelItemDrag();
            }
            HideWindow("conversation", animate);
            if (tradeOpenedInventory)
                HideWindow("inventory", animate);
            tradeOpenedInventory = false;
        }

        internal void HideDialogueStock() => HideWindow("shop", animate: false);

        internal Rect DialogueSpace(bool trading)
        {
            Rect bounds = CanvasRect.rect;
            if (!trading || !views.TryGetValue("inventory", out var inventory) || !inventory.IsOpen)
                return bounds;
            Rect bag = WindowDragHandle.BoundsInParent(inventory.Rect);
            float leftRoom = bag.xMin - bounds.xMin;
            float rightRoom = bounds.xMax - bag.xMax;
            if (Mathf.Max(leftRoom, rightRoom) < 320f)
                return bounds;
            if (leftRoom >= rightRoom)
                bounds.xMax = bag.xMin - 10f;
            else
                bounds.xMin = bag.xMax + 10f;
            return bounds;
        }

        internal void ShowNpcShopConversation(VillageInteractable merchant, VillageInteractable customer) =>
            NpcBubble(merchant).SetPartner(customer.DisplayName);

        internal void ShowNpcConversationLine(VillageInteractable speaker, string line) => NpcBubble(speaker).SetLine(line);

        internal Vector2 NpcSpeechNamePosition(VillageInteractable speaker, Vector2 position) =>
            npcConversationBubbles.TryGetValue(speaker, out var bubble) && bubble.isActiveAndEnabled ? bubble.NamePosition : position;

        private NpcConversationBubbleView NpcBubble(VillageInteractable speaker)
        {
            if (!npcConversationBubbles.TryGetValue(speaker, out var bubble))
            {
                var layer = pickupNames != null ? pickupNames.transform : transform;
                bubble = Instantiate(library.NpcConversation, layer);
                npcConversationBubbles.Add(speaker, bubble);
            }
            return bubble;
        }

        internal void HideNpcConversationBubble(VillageInteractable speaker)
        {
            if (npcConversationBubbles.Remove(speaker, out var bubble))
            {
                bubble.gameObject.SetActive(false);
                Destroy(bubble.gameObject);
            }
        }

        private void UpdateNpcConversationBubbles()
        {
            foreach (var (speaker, bubble) in npcConversationBubbles)
            {
                var layer = (RectTransform)bubble.transform.parent;
                if (speaker != null && speaker.isActiveAndEnabled && !IsSpeakingWith(speaker) &&
                    PickupNames.TryProject(game, layer, speaker.transform.position, speaker.NamePosition, out var point, out float depth))
                {
                    bubble.Place(point);
                    pickupNames?.IncludeDepth(bubble.transform, depth + .001f);
                }
                else
                    bubble.gameObject.SetActive(false);
            }
        }
    }
}
