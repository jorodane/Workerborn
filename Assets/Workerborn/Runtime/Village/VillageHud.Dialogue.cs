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
            dialogueTarget = null;
            if (views.TryGetValue("conversation", out var view))
                view.gameObject.SetActive(false);
            if (game != null)
                game.ShowNpcPortrait(null);
        }
    }
}
