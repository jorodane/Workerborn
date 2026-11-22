using System.Collections.Generic;

namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageInteractable dialogueTarget;
        private readonly Dictionary<string, bool> dialogueWindowStates = new Dictionary<string, bool>();

        public void OpenDialogue(VillageInteractable target, DialogueAction? topic = null)
        {
            if (target == null)
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

        public void CloseDialogue()
        {
            if (dialogueTarget == null)
                return;
            bool leavingGuide = dialogueTarget.Role == VillageRole.Guide;
            dialogueTarget = null;
            if (IsShopOpen)
            {
                CloseQuantity();
                HideWindow("shop");
                CancelItemDrag();
            }
            HideWindow("conversation");
            RestoreDialogueWindows();
            SetDialoguePresentation(false);
            game.ShowNpcPortrait(null);
            if (leavingGuide)
                game.FinishGuideVisit();
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
                foreach (var animation in dialogueBackdrop.GetComponentsInChildren<WindowOpeningAnimation>())
                    animation.Play();
        }

        private void SuspendDialogueWindows()
        {
            foreach (var view in views.Values)
            {
                if (!view.gameObject.activeSelf)
                    continue;
                dialogueWindowStates.Add(view.Id, true);
                view.gameObject.SetActive(false);
            }
        }

        private void RestoreDialogueWindows()
        {
            foreach (var entry in dialogueWindowStates)
            {
                if (!views.TryGetValue(entry.Key, out var view) || view.gameObject.activeSelf == entry.Value)
                    continue;
                if (entry.Value)
                {
                    view.gameObject.SetActive(true);
                    if (view.IsFloating)
                        WindowDragHandle.KeepOnScreen(view.Rect);
                }
                else
                    HideWindow(entry.Key);
            }
            dialogueWindowStates.Clear();
            RefreshWindows();
        }
    }
}
