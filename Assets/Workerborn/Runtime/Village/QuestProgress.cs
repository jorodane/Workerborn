using System;
using System.Collections.Generic;

namespace Workerborn
{
    internal enum QuestProgressStatus { Inactive, Active, Completed }
    internal enum QuestFinish { None, Report, Delivery }

    internal readonly struct QuestObjective
    {
        internal readonly string Text;
        internal readonly bool Complete;

        internal QuestObjective(string text, bool complete = false)
        {
            Text = text;
            Complete = complete;
        }

        internal QuestObjective(string name, int amount, int required)
        {
            required = Math.Max(0, required);
            Text = $"{name} ({Math.Clamp(amount, 0, required):N0}/{required:N0})";
            Complete = amount >= required;
        }
    }

    internal readonly struct QuestProgress
    {
        internal readonly VillageQuestPage Page;
        internal readonly string Title;
        internal readonly int Importance;
        internal readonly QuestProgressStatus Status;
        internal readonly QuestObjective[] Objectives;
        internal readonly QuestFinish Finish;
        internal readonly string FinishText;
        internal bool ObjectivesComplete
        {
            get
            {
                if (Objectives.Length == 0)
                    return false;
                foreach (var objective in Objectives)
                    if (!objective.Complete)
                        return false;
                return true;
            }
        }

        internal string DisplayTitle => Status == QuestProgressStatus.Completed || ObjectivesComplete
            ? $"{Title} (완료)" : Title;

        private QuestProgress(VillageQuestPage page, string title, int importance, QuestProgressStatus status,
            QuestObjective[] objectives, QuestFinish finish = QuestFinish.None, string finishText = null)
        {
            Page = page;
            Title = title;
            Importance = importance;
            Status = status;
            Objectives = objectives;
            Finish = finish;
            FinishText = finishText;
        }

        internal static VillageQuestPage DisplayPage(VillageQuestPage page) => page switch
        {
            VillageQuestPage.TimberContract => VillageQuestPage.FarmFoundation,
            VillageQuestPage.LegendGuidance => VillageQuestPage.LegendTrial,
            _ => page
        };

        internal static QuestProgressStatus StatusFor(VillageState state, VillageQuestPage page, string player) => DisplayPage(page) switch
        {
            VillageQuestPage.FarmFoundation when state.Rules.Foundation != null =>
                state.FarmAvailable ? QuestProgressStatus.Completed : QuestProgressStatus.Active,
            VillageQuestPage.FirstWork when state.IntroClaimed => QuestProgressStatus.Completed,
            VillageQuestPage.FirstWork when state.FarmAvailable && state.IntroAccepted => QuestProgressStatus.Active,
            VillageQuestPage.Expansion when state.ExpansionQuestActive || state.FarmUpgradePending => QuestProgressStatus.Active,
            VillageQuestPage.Expansion when state.FarmAvailable && state.FarmLevel >= state.Rules.ExpandedStage => QuestProgressStatus.Completed,
            VillageQuestPage.FarmJob when state.Job == JobStatus.Completed => QuestProgressStatus.Completed,
            VillageQuestPage.FarmJob when state.Job == JobStatus.Active => QuestProgressStatus.Active,
            VillageQuestPage.LegendFounding when state.Rules.LegendFoundingTimber > 0 =>
                state.HasFoundedLegend(player) ? QuestProgressStatus.Completed : QuestProgressStatus.Active,
            VillageQuestPage.LegendTrial when state.Rules.LegendTrialTimber > 0 && state.Legend.founders.Length > 0 =>
                state.HasProvenLegend(player) ? QuestProgressStatus.Completed : QuestProgressStatus.Active,
            _ => QuestProgressStatus.Inactive
        };

        internal static void Read(VillageState state, string player, string farmer, string well, List<QuestProgress> result)
        {
            var rules = state.Rules;
            result.Clear();
            if (rules.Foundation is { } foundation &&
                Begin(VillageQuestPage.FarmFoundation, foundation.Title, foundation.Importance, out var farmStatus))
            {
                int timber = (int)Math.Min(state.TimberRequired,
                    (long)state.TimberDelivered + state.Inventory.Count(foundation.TimberItemId));
                var delivery = new QuestObjective(ItemCatalog.Get(foundation.TimberItemId).Name, timber, state.TimberRequired);
                switch (state.FoundationStage)
                {
                    case FarmFoundationStage.WaitingForContract:
                        Add(farmStatus, new[] { new QuestObjective($"{farmer}에게 목재 계약서 받기") });
                        break;
                    case FarmFoundationStage.CollectingTimber:
                        Add(farmStatus, new[] { delivery, new QuestObjective("농장 부지 정하기") },
                            delivery.Complete ? QuestFinish.Delivery : QuestFinish.None, $"{farmer}에게 납품");
                        break;
                    case FarmFoundationStage.SelectingSite:
                        Add(farmStatus, new[] { delivery, new QuestObjective("농장 부지 정하기") });
                        break;
                    case FarmFoundationStage.ReadyToRebirth:
                        Add(farmStatus, new[] { delivery, new QuestObjective("농장 부지 정하기", true) },
                            QuestFinish.Report, $"{well}에서 시대 전환");
                        break;
                }
            }

            if (Begin(VillageQuestPage.FirstWork, rules.IntroTitle, rules.IntroQuestImportance, out var introStatus))
                Add(introStatus, new[] { new QuestObjective("아르바이트 완료", state.Career, rules.IntroRequiredJobs) },
                    state.CanClaimIntroQuest ? QuestFinish.Report : QuestFinish.None, $"{farmer}에게 보고");

            if (Begin(VillageQuestPage.Expansion, rules.ExpansionTitle, rules.ExpansionQuestImportance, out var expansionStatus))
                Add(expansionStatus, new[] {
                    new QuestObjective("기부금", state.DonatedGold, state.ExpansionGoldRequired),
                    new QuestObjective(ItemCatalog.Get(rules.DonationItemId).Name, state.DonatedPotatoes, state.ExpansionItemsRequired)
                }, state.FarmUpgradePending || state.CanConfirmExpansion ? QuestFinish.Report : QuestFinish.None,
                    state.FarmUpgradePending ? $"{well}에서 시대 전환" : $"{farmer}에게 보고");

            if (Begin(VillageQuestPage.FarmJob, rules.JobTitle, rules.JobQuestImportance, out var jobStatus))
            {
                var goods = new QuestObjective(ItemCatalog.Get(rules.JobItemId).Name, state.Inventory.Count(rules.JobItemId), state.JobRequirement);
                Add(jobStatus, new[] { goods }, goods.Complete ? QuestFinish.Delivery : QuestFinish.None, $"{farmer}에게 납품");
            }

            if (Begin(VillageQuestPage.LegendFounding, "전설의 나무꾼 이야기", rules.LegendFoundingImportance, out var foundingStatus))
                Add(foundingStatus, new[] { new QuestObjective("누적 벌목", state.LifetimeTimber, rules.LegendFoundingTimber) },
                    state.CanRecordLegend(player) ? QuestFinish.Report : QuestFinish.None, $"{well}에 기록");

            string trial = state.HasLegendGuidance(player) ? "나무꾼에게 돌아온 기회" : "전설의 나무꾼 시련";
            if (Begin(VillageQuestPage.LegendTrial, trial, rules.LegendTrialImportance, out var trialStatus))
            {
                var timber = new QuestObjective(ItemCatalog.Get(ItemIds.Timber).Name, state.Inventory.Count(ItemIds.Timber), rules.LegendTrialTimber);
                if (state.LegendPlacesAvailable > 0)
                    Add(trialStatus, new[] { timber }, timber.Complete ? QuestFinish.Delivery : QuestFinish.None, $"{well}에 목재 제출");
                else
                    Add(trialStatus, new[] { timber, new QuestObjective("시련 개방 대기") });
            }

            bool Begin(VillageQuestPage page, string title, int importance, out QuestProgressStatus status)
            {
                status = StatusFor(state, page, player);
                if (status == QuestProgressStatus.Inactive)
                    return false;
                result.Add(new QuestProgress(page, title, importance, status, Array.Empty<QuestObjective>()));
                return status == QuestProgressStatus.Active;
            }

            void Add(QuestProgressStatus status, QuestObjective[] objectives, QuestFinish finish = QuestFinish.None, string finishText = null)
            {
                int last = result.Count - 1;
                var quest = result[last];
                result[last] = new QuestProgress(quest.Page, quest.Title, quest.Importance, status, objectives, finish,
                    finish == QuestFinish.None ? null : finishText);
            }
        }
    }
}
