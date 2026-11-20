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
            if (topic == DialogueAction.Shop && target.IsTopicAvailable(game, DialogueAction.Shop))
            {
                OpenShop(target);
                return;
            }
            dialogueTarget = target;
            (OpenWindow("conversation") as ConversationWindowView)?.Open(target, topic);
        }

        public void CloseDialogue()
        {
            bool leavingGuide = dialogueTarget != null && dialogueTarget.Role == VillageRole.Guide;
            dialogueTarget = null;
            if (views.TryGetValue("conversation", out var view))
                view.gameObject.SetActive(false);
            if (game != null)
            {
                game.ShowNpcPortrait(null);
                if (leavingGuide)
                    game.FinishGuideVisit();
            }
        }
    }
}
