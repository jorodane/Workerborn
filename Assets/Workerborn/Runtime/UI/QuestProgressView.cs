using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    public sealed class QuestProgressView : MonoBehaviour
    {
        [SerializeField] private QuestProgressCellView[] cells;
        private readonly List<QuestProgress> progress = new List<QuestProgress>();
        private readonly List<QuestProgress> visible = new List<QuestProgress>();
        private readonly HashSet<VillageQuestPage> activeQuests = new HashSet<VillageQuestPage>();
        private readonly Dictionary<VillageQuestPage, float> completedUntil = new Dictionary<VillageQuestPage, float>();
        private const float CompletionSeconds = 4f;
        private VillagePrototype game;
        private float nextExpiry = float.PositiveInfinity;
        private int legendPlaces;

        internal void Bind(VillagePrototype village)
        {
            game = village;
            activeQuests.Clear();
            completedUntil.Clear();
            Refresh();
        }

        private void LateUpdate()
        {
            if (game != null && (Time.unscaledTime >= nextExpiry || legendPlaces != game.State.LegendPlacesAvailable))
                Refresh();
        }

        internal void Refresh()
        {
            if (game == null)
                return;
            var state = game.State;
            legendPlaces = state.LegendPlacesAvailable;
            string farmer = game.FindInteraction(VillageRole.Farmer)?.DisplayName ?? "미라";
            string well = game.FindInteraction(VillageRole.Shrine)?.DisplayName ?? "기억의 우물";
            QuestProgress.Read(state, game.LegendPlayerId, farmer, well, progress);

            visible.Clear();
            float now = Time.unscaledTime;
            nextExpiry = float.PositiveInfinity;
            foreach (var quest in progress)
            {
                if (quest.Status == QuestProgressStatus.Active)
                {
                    completedUntil.Remove(quest.Page);
                    activeQuests.Add(quest.Page);
                }
                else if (activeQuests.Remove(quest.Page))
                    completedUntil[quest.Page] = now + CompletionSeconds;

                if (!state.ShowsQuestProgress(quest.Page))
                    continue;
                if (quest.Status == QuestProgressStatus.Active)
                    visible.Add(quest);
                else if (completedUntil.TryGetValue(quest.Page, out float until) && until > now)
                {
                    nextExpiry = Mathf.Min(nextExpiry, until);
                    visible.Add(quest);
                }
            }

            // Failed or otherwise withdrawn quests must not be treated as completed later.
            activeQuests.RemoveWhere(page => QuestProgress.StatusFor(state, page, game.LegendPlayerId) != QuestProgressStatus.Active);
            visible.Sort(CompareImportance);
            for (int i = 0; i < cells.Length; i++)
            {
                if (i < visible.Count)
                    cells[i].Show(visible[i]);
                else
                    cells[i].gameObject.SetActive(false);
            }
        }

        private static int CompareImportance(QuestProgress left, QuestProgress right)
        {
            int importance = right.Importance.CompareTo(left.Importance);
            return importance != 0 ? importance : left.Page.CompareTo(right.Page);
        }
    }
}
