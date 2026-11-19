using UnityEngine;

namespace Workerborn
{
    public enum DialogueAction { Greeting, Job, IntroQuest, Contribution, Shop, Rest, Rebirth, Foundation, GuideAfter }

    [System.Serializable]
    public sealed class DialogueTopicDefinition
    {
        public string keyword;
        public DialogueAction action;
        [TextArea(3, 10)] public string text;
    }

    [CreateAssetMenu(menuName = "Workerborn/Content/Dialogue", fileName = "Dialogue")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        [SerializeField] private DialogueTopicDefinition[] topics;
        public DialogueTopicDefinition[] Topics => topics;
        public string TextFor(DialogueAction action)
        {
            if (topics != null) foreach (var topic in topics)
                if (topic.action == action) return topic.text;
            return string.Empty;
        }
    }
}
