namespace Workerborn
{
    public sealed partial class VillageHud
    {
        private VillageInteractable dialogueTarget;
        public void OpenDialogue(VillageInteractable target)
        {
            CloseDialogue();
            CancelItemDrag();
            dialogueTarget = target;
            (OpenWindow("conversation") as ConversationWindowView)?.Open(target);
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
