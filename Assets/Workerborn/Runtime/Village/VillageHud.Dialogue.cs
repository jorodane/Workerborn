namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageInteractable dialogueTarget;

        public void OpenDialogue(VillageInteractable target, DialogueAction? topic = null)
        {
            CloseDialogue();
            CloseWorldContext();
            CloseQuantity();
            CancelItemDrag();
            game.Worker.Cancel();
            dialogueTarget = target;
            if (dialogueBackdrop != null)
                dialogueBackdrop.SetActive(true);
            (OpenWindow("conversation") as ConversationWindowView)?.Open(target, topic == DialogueAction.Shop ? null : topic);
            if (topic == DialogueAction.Shop && target.IsTopicAvailable(game, DialogueAction.Shop))
                OpenShop(target);
        }

        internal void SelectDialogueTopic(DialogueAction topic)
        {
            CloseQuantity();
            CloseWindow("shop");
            if (views.TryGetValue("conversation", out var panel) && panel is ConversationWindowView dialogue)
                dialogue.SelectTopic(topic);
        }

        private void SetTradeDialogue(bool trading)
        {
            if (dialogueTarget != null && views.TryGetValue("conversation", out var panel) && panel is ConversationWindowView dialogue)
                dialogue.SetTrading(trading);
        }

        public void CloseDialogue()
        {
            bool leavingGuide = dialogueTarget != null && dialogueTarget.Role == VillageRole.Guide;
            dialogueTarget = null;
            if (dialogueBackdrop != null)
                dialogueBackdrop.SetActive(false);
            if (IsShopOpen)
            {
                CloseQuantity();
                CloseWindow("shop");
            }
            if (views.TryGetValue("conversation", out var view))
            {
                view.gameObject.SetActive(false);
                windowFocus.Remove(view);
            }
            if (game != null)
            {
                game.ShowNpcPortrait(null);
                if (leavingGuide)
                    game.FinishGuideVisit();
            }
        }
    }
}
