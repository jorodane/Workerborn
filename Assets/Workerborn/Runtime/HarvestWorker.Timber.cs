using UnityEngine;

namespace Workerborn
{
    internal enum TimberThrowAim { None, Ready, OutOfRange, NoGround }

    public sealed partial class HarvestWorker
    {
        private bool choosingLog, aimingThrow;
        private TimberOrder axeCut;
        private TimberResult? axeCutResult;
        private AnimationClip axeCutClip;
        private float axeCutImpact;
        private int axeCutStamina;
        private int confirmedCut, plannedCut;
        private Vector3 lastCarryPosition;
        private bool trackingCarry;
        internal int CarriedLogs => village?.CarriedLogCount(CharacterId) ?? 0;
        internal bool CanSelectLog => choosingLog && !aimingThrow && CarriedLogs < CharacterState.LiftCapacity;
        internal bool AimingTimberThrow => aimingThrow && CarriedLogs > 0;
        internal SkillId? TimberSkill => AimingTimberThrow ? SkillId.Throwing :
            choosingLog || CarriedLogs > 0 ? SkillId.Lifting : null;
        private float CarrySpeed => CarriedLogs > 0 ? CharacterState.LiftSpeedPercent * .01f : 1f;
        internal TimberSwing CurrentTimberSwing => Action switch
        {
            WorkerAction.Harvesting when cropTarget is LoggingTree => TimberSwing.Fell,
            WorkerAction.Attacking when strikeTarget is TimberPiece => TimberSwing.Split,
            WorkerAction.Attacking when strikeTarget is LoggingTree => TimberSwing.Strike,
            _ => TimberSwing.None
        };
        internal float CurrentTimberProgress => StrikePhase;

        private void BeginAxeCut(TimberOrder order, AnimationClip clip, float impact, int stamina)
        {
            axeCut = order;
            axeCutClip = clip;
            axeCutImpact = impact > 0f ? Mathf.Clamp(impact, .05f, .95f) : .5f;
            axeCutStamina = stamina;
            confirmedCut = 0;
            StartAxeSwing();
        }

        private void StartAxeSwing()
        {
            confirmedCut = Mathf.Max(confirmedCut, village.TimberCutProgress(axeCut));
            plannedCut = CharacterState.TimberCutAfter(axeCut.action, confirmedCut);
            axeCutResult = null;
            float duration = axeCutClip != null ? axeCutClip.length : attackDelay;
            StartStrikeClock(duration / CharacterState.ActionSpeed);
            animationDriver?.BeginStrike(axeCutClip);
        }

        private void UpdateAxeCut()
        {
            bool felling = axeCut.action == TimberAction.Fell;
            WorldInteractable target = felling ? cropTarget : strikeTarget;
            if (CharacterState.Health <= 0)
            {
                Cancel();
                return;
            }

            float phase = StrikePhase;
            if (!struck)
            {
                if (IsLocallyControlled && village.AwaitingWorldCommand)
                {
                    strikeStartedAt += Time.deltaTime;
                    nextStrikeAt += Time.deltaTime;
                    return;
                }
                string tool = felling && cropTarget != null ? cropTarget.Definition.RequiredTool.Id : ItemIds.Axe;
                if (target == null || !target.CanInteract || CarriedLogs > 0 || !CharacterState.HasTool(tool) ||
                    !SharesMap(target.transform.position) || !InReach(target.transform.position, target.InteractionDistance + .15f))
                {
                    Cancel();
                    return;
                }
                double effort = Mathf.Min(1f, phase / axeCutImpact);
                double cost = (double)axeCutStamina * (plannedCut - confirmedCut) / TimberWork.FullCut;
                if (!CharacterState.SpendWorkProgress(cost, strikePaidProgress, effort))
                {
                    Cancel();
                    ReportWorkExhaustion();
                    return;
                }
                strikePaidProgress = effort;
                if (effort >= 1d)
                    HitWithAxe(target);
            }

            animationDriver?.SampleStrike(phase);
            if (phase < 1f || !axeCutResult.HasValue)
                return;
            if (axeCutResult == TimberResult.InProgress && target != null && target.CanInteract)
            {
                StartAxeSwing();
                return;
            }

            bool finished = axeCutResult is TimberResult.Succeeded or TimberResult.Failed;
            var tree = target as LoggingTree;
            Cancel();
            if (finished)
            {
                completedWorkSkill = SkillId.Woodcutting;
                workFeedbackUntil = Time.unscaledTime + .22f;
                if (tree != null)
                    tree.FinishFelling();
            }
            if (felling)
                HarvestAttemptFinished?.Invoke();
        }

        private void HitWithAxe(WorldInteractable target)
        {
            struck = true;
            if (target is LoggingTree tree)
                tree.ShowImpact(this, withAxe: true);
            else if (target is TimberPiece piece)
                piece.PlayAxeImpact();

            var order = axeCut;
            if (!village.RequestTimberWork(this, order, Confirm) && !axeCutResult.HasValue)
                Confirm(TimberResult.Rejected, 0);

            void Confirm(TimberResult result, int progress)
            {
                if (!ReferenceEquals(axeCut, order))
                    return;
                axeCutResult = result;
                if (result == TimberResult.Rejected)
                {
                    plannedCut = confirmedCut;
                    return;
                }
                confirmedCut = result == TimberResult.InProgress ? progress : TimberWork.FullCut;
                plannedCut = CharacterState.TimberCutAfter(order.action, confirmedCut);
            }
        }

        internal void PrepareTimberSkill(SkillId skill)
        {
            if (!SkillProgression.IsTimberActive(skill) || !CharacterState.IsSkillLearned(skill) ||
                !AcceptsInteractionCommands || CharacterState.AwaitingIncarnation || CharacterState.Health <= 0)
                return;
            if (skill == SkillId.Throwing)
            {
                if (CarriedLogs == 0)
                {
                    village.TimberNotice(this, "먼저 통나무를 들어 올려 주세요.");
                    return;
                }
                Cancel();
                aimingThrow = !aimingThrow;
                choosingLog = false;
                if (aimingThrow)
                    village.TimberNotice(this, $"던질 바닥을 클릭하세요. 최대 {CharacterState.ThrowRange:0.#}m");
                return;
            }
            if (CarriedLogs >= CharacterState.LiftCapacity)
            {
                village.TimberNotice(this, "지금은 통나무를 더 들 수 없어요.");
                return;
            }
            Cancel();
            aimingThrow = false;
            choosingLog = !choosingLog;
            if (choosingLog)
                village.TimberNotice(this, "들어 올릴 통나무를 클릭하세요.");
        }

        internal bool HandleTimberClick(WorldInteractable target)
        {
            if (!AcceptsInteractionCommands)
                return false;
            if (CanSelectLog && target is TimberPiece { Part: TimberPart.Log } log)
            {
                LiftLog(log);
                return true;
            }
            if (CarriedLogs == 0 || target == null)
                return false;
            village.TimberNotice(this, "들고 있는 통나무를 먼저 내려놓아 주세요.");
            return true;
        }

        internal void LiftLog(TimberPiece log)
        {
            if (log == null || log.Part != TimberPart.Log || !log.CanInteract || !AcceptsInteractionCommands ||
                CharacterState.AwaitingIncarnation || CarriedLogs >= CharacterState.LiftCapacity)
                return;
            if (DeferUntilStanding(() => LiftLog(log)))
                return;

            choosingLog = true;
            aimingThrow = false;
            if (InReach(log.transform.position, log.InteractionDistance))
                Lift();
            else
                CommandApproach(log, Lift);

            void Lift()
            {
                if (log == null || !log.CanInteract || !InReach(log.transform.position, log.InteractionDistance))
                    return;
                Cancel();
                Face(log.transform.position);
                village.RequestTimberWork(this, new TimberOrder { action = TimberAction.Lift, target = log.TargetId, part = TimberPart.Log });
            }
        }

        internal bool TryCancelTimberSkill()
        {
            if (TimberSkill == null)
                return false;
            choosingLog = aimingThrow = false;
            Cancel();
            if (CarriedLogs > 0)
                village.RequestTimberWork(this, new TimberOrder { action = TimberAction.Place });
            return true;
        }

        internal void ThrowLogs(TimberThrowAim aim, Vector3 destination)
        {
            if (!AimingTimberThrow || !AcceptsInteractionCommands)
                return;
            if (aim != TimberThrowAim.Ready)
            {
                village.TimberNotice(this, aim == TimberThrowAim.OutOfRange
                    ? $"던질 수 있는 범위는 {CharacterState.ThrowRange:0.#}m예요."
                    : "통나무를 던질 바닥을 클릭하세요.");
                return;
            }
            Cancel();
            Face(destination);
            var order = new TimberOrder { action = TimberAction.Throw, destination = TimberLocation.Planar(destination) };
            if (village.RequestTimberWork(this, order))
                animationDriver?.PlayTimberToss(village.Settings.Timber.Toss);
        }

        internal void RefreshTimberSkill()
        {
            if (CarriedLogs == 0)
                aimingThrow = false;
            if (CharacterState != null && CarriedLogs >= CharacterState.LiftCapacity)
                choosingLog = false;
            if (village != null)
                animationDriver?.SetCarrying(village.Settings.Timber?.Carry, CarriedLogs > 0);
            if (agent != null && Action == WorkerAction.Moving)
                agent.speed = TravelSpeed(IsWalking);
        }

        private bool UpdateTimberCarry()
        {
            if (CarriedLogs == 0 || village?.IsWorldTransitioning == true)
            {
                trackingCarry = false;
                return false;
            }
            Vector3 position = StandingPosition;
            float distance = trackingCarry ? Vector3.Distance(position, lastCarryPosition) : 0f;
            lastCarryPosition = position;
            trackingCarry = true;
            if (Action != WorkerAction.Moving || village.AwaitingWorldCommand || distance <= .0001f || distance > 1.5f)
                return false;

            float step = 1.5f + CharacterState.LiftStaminaSaving * .35f;
            if (!CharacterState.SpendWorkStamina(VillageState.WorkStaminaMultiplier * (double)distance / step) || CharacterState.Stamina <= 0d)
            {
                TryCancelTimberSkill();
                ReportWorkExhaustion("스태미나가 바닥나 통나무를 내려놓았어요.");
            }
            return true;
        }

        private void FinishTimberTravel()
        {
            choosingLog = aimingThrow = false;
            village?.ReleaseSoloTimber(this);
        }
    }
}
