using System.Collections.Generic;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageInteractable dialogueTarget;
        private readonly Dictionary<string, bool> dialogueWindowStates = new Dictionary<string, bool>();
        private WindowTransition[] backdropTransitions = System.Array.Empty<WindowTransition>();
        private bool dialogueClosing;
        internal bool IsSpeakingWith(VillageInteractable target) => target != null && dialogueTarget == target;

        public void OpenDialogue(VillageInteractable target, DialogueAction? topic = null)
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
            (OpenWindow("conversation") as ConversationWindowView)?.Open(target, topic == DialogueAction.Shop ? null : topic);
            if (topic == DialogueAction.Shop && target.IsTopicAvailable(game, DialogueAction.Shop))
                OpenShop(target);
        }

        internal void SelectDialogueTopic(DialogueAction topic)
        {
            CloseQuantity();
            if (views.TryGetValue("conversation", out var panel) && panel is ConversationWindowView dialogue)
                dialogue.SelectTopic(topic);
        }

        public void CloseDialogue() => CloseDialogue(animate: false);

        internal void CloseDialogue(bool animate)
        {
            if (dialogueTarget == null)
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
            game.ShowNpcPortrait(null);
        }

        private void SetDialoguePresentation(bool active)
        {
            if (normalHud != null)
                normalHud.SetActive(!active);
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
    }
}
