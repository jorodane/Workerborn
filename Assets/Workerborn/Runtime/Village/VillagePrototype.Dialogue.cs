using System.Collections.Generic;

namespace Workerborn
{
    public sealed partial class VillagePrototype
    {
        internal IEnumerable<(DialogueTopicDefinition Topic, bool OpenOnTalk)> DialogueTopicsFor(VillageInteractable npc)
        {
            var recipient = npc != null ? npc.Definition : null;
            if (recipient == null)
                yield break;
            var activity = npc.CurrentActivity;

            if (recipient.Dialogue != null)
                foreach (var topic in recipient.Dialogue.Topics)
                    if (topic != null && topic.IsAvailable(State, activity))
                        yield return (topic, false);

            var (introduction, expansion, foundation) = settings.GetQuests(FindInteraction(VillageRole.Farmer)?.Definition);
            foreach (var quest in new[] { introduction, expansion, foundation })
                if (quest != null)
                    foreach (var entry in AvailableOffers(quest.DialogueOffers, recipient, activity, quest.DialogueStage(State)))
                        yield return entry;

            foreach (var entry in AvailableOffers(settings.DialogueOffers, recipient, activity, DialogueQuestStage.None))
                yield return entry;
        }

        private IEnumerable<(DialogueTopicDefinition Topic, bool OpenOnTalk)> AvailableOffers(
            DialogueOffer[] offers, NpcDefinition recipient, NpcActivity activity, DialogueQuestStage stage)
        {
            foreach (var offer in offers)
                if (offer != null && offer.Recipient == recipient && offer.Topic != null && offer.Topic.IsAvailable(State, activity, stage))
                    yield return (offer.Topic, offer.OpenOnTalk);
        }

        internal DialogueTopicDefinition FindDialogueTopic(VillageInteractable npc, DialogueAction action)
        {
            foreach (var entry in DialogueTopicsFor(npc))
                if (entry.Topic.action == action)
                    return entry.Topic;
            return null;
        }

        internal bool IsDialogueTopicAvailable(VillageInteractable npc, DialogueTopicDefinition topic)
        {
            if (topic == null)
                return false;
            foreach (var entry in DialogueTopicsFor(npc))
                if (entry.Topic == topic)
                    return true;
            return false;
        }

        internal DialogueTopicDefinition OpeningDialogueTopic(VillageInteractable npc, DialogueAction fallbackAction)
        {
            DialogueTopicDefinition fallback = null;
            foreach (var entry in DialogueTopicsFor(npc))
            {
                if (entry.OpenOnTalk)
                    return entry.Topic;
                if (fallback == null && entry.Topic.action == fallbackAction)
                    fallback = entry.Topic;
            }
            return fallback;
        }
    }
}
