using UnityEngine;

namespace Workerborn
{
    internal sealed partial class NpcRoutine
    {
        private const float GreetingRange = 4f;
        private ConversationEncounter conversation;
        private NpcConversation lastConversation;
        private float nextGreetingCheck, nextNearbyConversationCheck, conversationCooldown;

        private bool PlayerEngaged => game.Hud.IsSpeakingWith(npc);
        private bool CanSocialize => npc != null && npc.CanInteract && IsAvailableResident && character.isActiveAndEnabled &&
            !character.IsEating && !character.IsStandingUp && character.CarriedLogs == 0 && character.CurrentMount == null &&
            (character.Action is WorkerAction.Idle or WorkerAction.Moving or WorkerAction.Sitting) &&
            merchant == null && customer == null;
        private bool FreeToTalk => character.Action is WorkerAction.Idle or WorkerAction.Sitting;
        private bool ReadyToMeet => conversation == null && Time.time >= conversationCooldown && CanSocialize && !PlayerEngaged;
        private bool HoldingConversation => conversation is { Passing: false, Started: true };
        internal HarvestWorker ConversationGazePartner
        {
            get
            {
                if (conversation is { Started: true } meeting)
                    return meeting.Initiator == this ? meeting.Partner.character : meeting.Initiator.character;
                if (customer != null)
                    return customer.character;
                return AtCheckout ? merchant.Routine.character : null;
            }
        }

        private void SetFirstConversationChecks()
        {
            nextGreetingCheck = Time.time + Random.Range(.6f, 1.4f);
            nextNearbyConversationCheck = Time.time + Random.Range(8f, 14f);
        }

        private void TryPassingGreeting()
        {
            if (character.Action != WorkerAction.Moving || !ReadyToMeet || Time.time < nextGreetingCheck)
                return;
            nextGreetingCheck = Time.time + Random.Range(.65f, 1.3f);
            TryFindConversation(passing: true, GreetingRange);
        }

        private void TryNearbyConversation()
        {
            if (!ReadyToMeet || !FreeToTalk || Time.time < nextNearbyConversationCheck)
                return;
            nextNearbyConversationCheck = Time.time + Random.Range(2.5f, 4.5f);
            TryFindConversation(passing: false, GreetingRange);
        }

        internal bool TryFindConversation(bool passing, float range)
        {
            if (!ReadyToMeet || (passing ? character.Action != WorkerAction.Moving : !FreeToTalk))
                return false;
            NpcRoutine partner = null;
            NpcConversation selected = null;
            float bestDistance = range * range;
            foreach (var subject in WorldInteractable.Active)
            {
                if (subject is not VillageInteractable target || target.Routine is not { } candidate || candidate == this ||
                    candidate.game != game || !candidate.ReadyToMeet || !passing && !candidate.FreeToTalk)
                    continue;
                Vector3 point = candidate.character.StandingPosition;
                float distance = (point - character.StandingPosition).sqrMagnitude;
                if (distance >= bestDistance || !character.SharesMap(point) || !character.HasDirectPathTo(candidate.character) ||
                    !passing && !character.InReach(point, target.InteractionDistance))
                    continue;
                var topic = SelectConversation(candidate, passing);
                if (topic == null)
                    continue;
                partner = candidate;
                selected = topic;
                bestDistance = distance;
            }
            if (partner == null)
                return false;

            var meeting = new ConversationEncounter(this, partner, selected, passing);
            conversation = partner.conversation = meeting;
            if (passing)
                meeting.Start();
            else
                partner.npc.RequestInteraction(character);
            if (!meeting.Started)
                meeting.End();
            return meeting.Started;
        }

        private NpcConversation SelectConversation(NpcRoutine partner, bool passing)
        {
            NpcConversation chosen = null;
            int count = 0;
            foreach (var candidate in game.Settings.NpcConversations)
            {
                if (candidate == null || !MatchesConversation(candidate, partner, passing))
                    continue;
                // Within the same priority, prefer a conversation these two did not just have.
                if (chosen == null || candidate.Priority > chosen.Priority ||
                    candidate.Priority == chosen.Priority && chosen == lastConversation && candidate != lastConversation)
                {
                    chosen = candidate;
                    count = 1;
                }
                else if (candidate.Priority == chosen.Priority && candidate != lastConversation && Random.Range(0, ++count) == 0)
                    chosen = candidate;
            }
            return chosen;
        }

        private bool MatchesConversation(NpcConversation topic, NpcRoutine partner, bool passing) =>
            topic.Matches(passing, npc.Definition, character.CharacterState, RoutineActivity,
                partner.npc.Definition, partner.character.CharacterState, partner.RoutineActivity);

        internal void ArrivedAtConversation(VillageInteractable target)
        {
            if (merchant == target)
                ArrivedAtShop(target);
            else if (conversation is { Passing: false } meeting && meeting.Initiator == this && meeting.Partner.npc == target)
                meeting.Start();
        }

        private bool ContinueConversation()
        {
            if (conversation?.Initiator == this)
                conversation.Update();
            return HoldingConversation;
        }

        private sealed class ConversationEncounter
        {
            internal readonly NpcRoutine Initiator, Partner;
            internal readonly bool Passing;
            internal bool Started { get; private set; }
            private NpcConversation topic;
            private float lineEndsAt;
            private int lineIndex;
            private NpcRoutine speaking;

            internal ConversationEncounter(NpcRoutine initiator, NpcRoutine partner, NpcConversation topic, bool passing)
            {
                Initiator = initiator;
                Partner = partner;
                this.topic = topic;
                Passing = passing;
            }

            private bool CanContinue => Initiator != null && Partner != null && Initiator.CanSocialize && Partner.CanSocialize &&
                Initiator.character.SharesMap(Partner.character.StandingPosition);

            internal void Start()
            {
                if (Started)
                    return;
                if (!CanContinue || !Passing && (!Initiator.FreeToTalk || !Partner.FreeToTalk || !WithinConversationReach()))
                {
                    End();
                    return;
                }
                if (!Initiator.MatchesConversation(topic, Partner, Passing))
                    topic = Initiator.SelectConversation(Partner, Passing);
                if (topic == null)
                {
                    End();
                    return;
                }
                Started = true;
                Initiator.lastConversation = Partner.lastConversation = topic;
                if (!Passing)
                {
                    Initiator.character.FaceConversationPartner(Partner.character.StandingPosition);
                    Partner.character.FaceConversationPartner(Initiator.character.StandingPosition);
                }
                NextLine();
            }

            internal void Update()
            {
                if (!CanContinue)
                {
                    End();
                    return;
                }
                if (!WithinConversationReach() || !Passing && (!Initiator.FreeToTalk || !Partner.FreeToTalk))
                    End();
                else if (Time.time >= lineEndsAt)
                    NextLine();
            }

            private bool WithinConversationReach()
            {
                float reach = Passing ? 13f : Partner.npc.InteractionDistance + .65f;
                return Initiator.character.InReach(Partner.character.StandingPosition, reach);
            }

            private void NextLine()
            {
                ClearLine();
                var lines = topic.Lines;
                while (lineIndex < lines.Length)
                {
                    var line = lines[lineIndex++];
                    if (string.IsNullOrWhiteSpace(line.Text))
                        continue;
                    speaking = line.PartnerSpeaks ? Partner : Initiator;
                    float seconds = Passing ? Mathf.Clamp(.85f + line.Text.Length * .036f, 1.1f, 1.6f) :
                        Mathf.Clamp(1.1f + line.Text.Length * .046f, 1.45f, 2.9f);
                    lineEndsAt = Time.time + seconds;
                    speaking.game.Hud.ShowNpcConversationLine(speaking.npc, line.Text);
                    speaking.character.PlayConversationGesture(line.Gesture, seconds);
                    return;
                }
                End();
            }

            private void ClearLine()
            {
                if (speaking == null)
                    return;
                if (speaking.game != null && speaking.game.Hud != null)
                    speaking.game.Hud.HideNpcConversationBubble(speaking.npc);
                if (speaking.character != null)
                    speaking.character.StopConversationGesture();
                speaking = null;
            }

            internal void End()
            {
                ClearLine();
                if (Initiator != null)
                    Release(Initiator);
                if (Partner != null)
                    Release(Partner);
            }

            private void Release(NpcRoutine person)
            {
                if (person.conversation != this)
                    return;
                person.conversation = null;
                person.conversationCooldown = Time.time + (Started ? Random.Range(28f, 48f) : Random.Range(4f, 7f));
            }
        }
    }
}
