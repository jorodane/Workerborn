using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageInteractable dialogueTarget;
        private readonly Dictionary<string, bool> dialogueWindowStates = new Dictionary<string, bool>();
        private WindowTransition[] backdropTransitions = System.Array.Empty<WindowTransition>();
        private bool dialogueClosing;
        private readonly Dictionary<VillageInteractable, NpcConversationBubbleView> npcConversationBubbles =
            new Dictionary<VillageInteractable, NpcConversationBubbleView>();
        internal bool IsSpeakingWith(VillageInteractable target) => target != null && dialogueTarget == target;

        public void OpenDialogue(VillageInteractable target, DialogueAction? topic = null) =>
            OpenDialogueTopic(target, topic.HasValue ? game.FindDialogueTopic(target, topic.Value) : null);

        internal void OpenDialogueTopic(VillageInteractable target, DialogueTopicDefinition topic)
        {
            if (target == null || game.State.AwaitingIncarnation && target != game.RebirthArea?.Guide)
                return;
            CloseDialogue();
            CloseWorldContext();
            CloseQuantity();
            CancelItemDrag();
            game.Worker.Cancel();
            dialogueTarget = target;
            SuspendDialogueWindows();
            SetDialoguePresentation(true);
            var selected = game.IsDialogueTopicAvailable(target, topic) ? topic : null;
            (OpenWindow("conversation") as ConversationWindowView)?.OpenTopic(target, selected);
            if (selected?.action == DialogueAction.Shop)
                OpenShop(target);
        }

        private bool HandleDialoguePageInput()
        {
            if (!HasModal || IsChatTyping || HasQuantityPrompt)
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
            {
                if (dialogueClosing && !animate)
                    FinishDialogueClose();
                return;
            }
            dialogueTarget = null;
            if (IsShopOpen)
            {
                HideWindow("quantity", animate);
                HideWindow("shop", animate);
                CancelItemDrag();
            }
            HideWindow("conversation", animate);
            foreach (var entry in dialogueWindowStates)
                if (!entry.Value)
                    HideWindow(entry.Key, animate);

            if (animate)
            {
                foreach (var transition in backdropTransitions)
                    transition.PlayClosing(hideOnComplete: false);
                if (HasDialogueCloseAnimation())
                {
                    dialogueClosing = true;
                    return;
                }
            }
            FinishDialogueClose();
        }

        private void UpdateDialogueClose()
        {
            if (dialogueClosing && !HasDialogueCloseAnimation())
                FinishDialogueClose();
        }

        private bool HasDialogueCloseAnimation()
        {
            foreach (var transition in backdropTransitions)
                if (transition.IsClosing && transition.IsPlaying)
                    return true;
            foreach (var view in views.Values)
                if (view.IsClosing)
                    return true;
            return false;
        }

        private void FinishDialogueClose()
        {
            dialogueClosing = false;
            foreach (var view in views.Values)
                if (view.IsClosing)
                    view.Hide(animate: false);
            RestoreDialogueWindows();
            SetDialoguePresentation(false);
        }

        private void SetDialoguePresentation(bool active)
        {
            if (normalHud != null)
                normalHud.SetActive(!active);
            UpdateItemShortcutPresentation();
            chat?.SetDialogueMode(active);
            if (dialogueBackdrop == null)
                return;
            dialogueBackdrop.SetActive(active);
            if (active)
                foreach (var transition in backdropTransitions)
                    transition.PlayOpening();
        }

        private void SuspendDialogueWindows()
        {
            foreach (var view in views.Values)
            {
                if (!view.gameObject.activeSelf)
                    continue;
                if (view.IsOpen)
                    dialogueWindowStates.Add(view.Id, true);
                view.Hide(animate: false);
            }
        }

        private void RestoreDialogueWindows()
        {
            foreach (var entry in dialogueWindowStates)
            {
                if (!CanShowInCurrentLife(entry.Key) || !views.TryGetValue(entry.Key, out var view) || view.IsOpen == entry.Value)
                    continue;
                if (entry.Value)
                {
                    view.gameObject.SetActive(true);
                    if (view.IsFloating)
                        WindowDragHandle.KeepOnScreen(view.Rect);
                }
                else
                    HideWindow(entry.Key, animate: false);
            }
            dialogueWindowStates.Clear();
            RefreshWindows();
        }

        internal Rect DialogueSpace(bool trading)
        {
            Rect bounds = CanvasRect.rect;
            if (!trading || !views.TryGetValue("inventory", out var inventory) || !inventory.IsOpen)
                return bounds;
            Rect bag = WindowDragHandle.BoundsInParent(inventory.Rect);
            float leftRoom = bag.xMin - bounds.xMin;
            float rightRoom = bounds.xMax - bag.xMax;
            if (Mathf.Max(leftRoom, rightRoom) < 405f)
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
                    pickupNames?.IncludeDepth(bubble.transform, depth);
                }
                else
                    bubble.gameObject.SetActive(false);
            }
        }
    }
}
