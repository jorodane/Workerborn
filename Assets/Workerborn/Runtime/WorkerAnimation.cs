using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Workerborn
{
    public sealed class WorkerAnimation : MonoBehaviour
    {
        internal const float JumpSeconds = .55f;
        private const int Work = 2;
        private const int WorkBegin = 6;
        private const int WorkFinish = 7;
        private const int SitBegin = 8;
        private const int SitFinish = 9;
        private const int GestureInput = 10;
        private const double SitSpeed = .85d;
        [SerializeField] private AnimationClip groundSit;
        [SerializeField] private AnimationClip groundSitBegin;
        [SerializeField] private AnimationClip groundSitFinish;
        [SerializeField] private AnimationClip seatSit;
        [SerializeField] private AnimationClip seatSitBegin;
        [SerializeField] private AnimationClip seatSitFinish;
        [SerializeField] private AnimationClip jump;
        [SerializeField] private AnimationClip gatherSuccess;
        [SerializeField] private AnimationClip gatherFailure;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[GestureInput + 1];
        private readonly float[] lengths = new float[GestureInput + 1];
        private readonly float[] weights = new float[GestureInput + 1];
        private AnimationClip fallbackWork;
        private int workInput = Work;
        private int sitInput = -1;
        private float workDuration, beginDuration, finishDuration;
        private float loopCycles, loopImpact, lastLoopPosition;
        private bool timedWork;
        private int target;
        public void Initialize(Animator animator, AnimationClip idle, AnimationClip move, AnimationClip work, AnimationClip walk = null)
        {
            if (graph.IsValid())
                graph.Destroy();
            if (animator == null || idle == null)
                return;
            move = move != null ? move : idle;
            walk = walk != null ? walk : move;
            fallbackWork = work != null ? work : idle;
            timedWork = false;
            workInput = Work;
            sitInput = -1;
            target = 0;
            Gesture = WorkerGesture.None;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Workerborn existing character animations");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            var output = AnimationPlayableOutput.Create(graph, "Character", animator);
            output.SetSourcePlayable(mixer);
            AnimationClip[] source =
            {
                idle,
                move,
                fallbackWork,
                walk,
                groundSit != null ? groundSit : idle,
                seatSit != null ? seatSit : idle,
                idle,
                idle,
                idle,
                idle,
                idle
            };
            for (int i = 0; i < source.Length; i++)
            {
                clips[i] = default;
                weights[i] = i == 0 ? 1f : 0f;
                BindClip(i, source[i]);
            }

            graph.Play();
        }

        public int CurrentAction => target;
        public double ActionTime => graph.IsValid() ? clips[Input].GetTime() : 0d;
        internal WorkerGesture Gesture { get; private set; }
        internal float GestureProgress => Gesture == WorkerGesture.None || !graph.IsValid()
            ? 0f : Mathf.Clamp01((float)clips[GestureInput].GetTime() / lengths[GestureInput]);
        private int Input => sitInput >= 0 ? sitInput : target == Work ? workInput : target;

        private void BindClip(int input, AnimationClip clip, bool sampled = false)
        {
            if (!clips[input].IsValid() || clips[input].GetAnimationClip() != clip)
            {
                if (clips[input].IsValid())
                {
                    graph.Disconnect(mixer, input);
                    graph.DestroyPlayable(clips[input]);
                }
                clips[input] = AnimationClipPlayable.Create(graph, clip);
                clips[input].SetApplyFootIK(false);
                graph.Connect(clips[input], 0, mixer, input);
                mixer.SetInputWeight(input, weights[input]);
            }

            lengths[input] = Mathf.Max(.01f, clip.length);
            clips[input].SetSpeed(sampled ? 0d : 1d);
        }

        public void BeginStrike(AnimationClip clip)
        {
            if (!graph.IsValid())
                return;
            timedWork = true;
            workInput = Work;
            BindClip(Work, clip != null ? clip : fallbackWork, true);
            SampleStrike(0f);
            SetAction(Work);
        }

        public void SampleStrike(float progress)
        {
            if (graph.IsValid())
                clips[Work].SetTime(Mathf.Clamp(progress, 0, .999f) * lengths[Work]);
        }

        public void BeginWork(WorkAnimationDefinition animation, float duration)
        {
            if (!graph.IsValid())
                return;

            timedWork = animation != null && animation.Loop != null;
            workInput = Work;
            BindClip(Work, timedWork ? animation.Loop : fallbackWork, timedWork);
            clips[Work].SetTime(0d);
            SetAction(Work);
            if (!timedWork)
                return;

            workDuration = Mathf.Max(.1f, duration);
            beginDuration = animation.Begin != null ? Mathf.Min(animation.Begin.length, workDuration * .25f) : 0f;
            finishDuration = animation.Finish != null ? Mathf.Min(animation.Finish.length, workDuration * .25f) : 0f;
            if (beginDuration > 0)
                BindClip(WorkBegin, animation.Begin, true);
            if (finishDuration > 0)
                BindClip(WorkFinish, animation.Finish, true);
            loopCycles = Mathf.Max(1, Mathf.Round((workDuration - beginDuration - finishDuration) / lengths[Work]));
            loopImpact = animation.LoopImpact;
            lastLoopPosition = 0f;
            SampleWork(0f);
        }

        public bool SampleWork(float progress)
        {
            if (!timedWork || !graph.IsValid())
                return false;

            float elapsed = Mathf.Clamp01(progress) * workDuration;
            float finishAt = workDuration - finishDuration;
            float loopPosition = loopCycles * Mathf.Clamp01((elapsed - beginDuration) / (finishAt - beginDuration));
            bool impact = loopImpact > 0f && Mathf.FloorToInt(loopPosition - loopImpact) > Mathf.FloorToInt(lastLoopPosition - loopImpact);
            lastLoopPosition = loopPosition;
            float phase;
            if (elapsed < beginDuration)
            {
                workInput = WorkBegin;
                phase = elapsed / beginDuration;
            }
            else if (finishDuration > 0 && elapsed >= finishAt)
            {
                workInput = WorkFinish;
                phase = (elapsed - finishAt) / finishDuration;
            }
            else
            {
                workInput = Work;
                phase = Mathf.Repeat(loopPosition, 1f);
            }

            clips[workInput].SetTime(phase * lengths[workInput]);
            return impact;
        }

        public void SetAction(int action)
        {
            int next = Mathf.Clamp(action, 0, (int)WorkerMotion.SeatSit);
            if (next != (int)WorkerMotion.Idle && Gesture is WorkerGesture.GatherSuccess or WorkerGesture.GatherFailure)
                SetGesture(WorkerGesture.None);
            if (next == target)
                return;
            if (graph.IsValid())
            {
                sitInput = -1;
                if (next == (int)WorkerMotion.GroundSit || next == (int)WorkerMotion.SeatSit)
                    PlaySitTransition(SitBegin, next == (int)WorkerMotion.SeatSit ? seatSitBegin : groundSitBegin);
                else if (next == (int)WorkerMotion.Idle && target >= (int)WorkerMotion.GroundSit)
                    PlaySitTransition(SitFinish, target == (int)WorkerMotion.SeatSit ? seatSitFinish : groundSitFinish);
                if ((next == Work && !timedWork) || next >= (int)WorkerMotion.GroundSit)
                    clips[next].SetTime(0d);
                else if ((next == 1 || next == 3) && (target == 1 || target == 3))
                    clips[next].SetTime(clips[target].GetTime() % lengths[target] / lengths[target] * lengths[next]);
            }
            target = next;
        }

        internal void SetGesture(WorkerGesture gesture, float progress = 0f)
        {
            if (!graph.IsValid())
                return;
            AnimationClip clip = gesture switch
            {
                WorkerGesture.Jump => jump,
                WorkerGesture.GatherSuccess => gatherSuccess,
                WorkerGesture.GatherFailure => gatherFailure,
                _ => null
            };
            if (clip == null)
            {
                Gesture = WorkerGesture.None;
                clips[GestureInput].SetSpeed(0d);
                return;
            }

            Gesture = gesture;
            BindClip(GestureInput, clip);
            float duration = gesture == WorkerGesture.Jump ? JumpSeconds : Mathf.Min(1.15f, lengths[GestureInput]);
            clips[GestureInput].SetTime(Mathf.Clamp(progress, 0f, .999f) * lengths[GestureInput]);
            clips[GestureInput].SetSpeed(lengths[GestureInput] / duration);
        }

        private void PlaySitTransition(int input, AnimationClip clip)
        {
            if (clip == null)
                return;
            BindClip(input, clip);
            clips[input].SetTime(0);
            clips[input].SetSpeed(SitSpeed);
            sitInput = input;
        }

        private void Update()
        {
            if (!graph.IsValid())
                return;
            if (sitInput >= 0 && clips[sitInput].GetTime() >= lengths[sitInput])
            {
                if (sitInput == SitBegin)
                    clips[target].SetTime(0);
                sitInput = -1;
            }
            if (Gesture != WorkerGesture.None && clips[GestureInput].GetTime() >= lengths[GestureInput])
            {
                clips[GestureInput].SetTime(lengths[GestureInput] * .999f);
                SetGesture(WorkerGesture.None);
            }
            float total = 0f;
            int activeInput = Gesture != WorkerGesture.None ? GestureInput : Input;
            for (int i = 0; i < clips.Length; i++)
            {
                if ((i != Work || !timedWork) && i < WorkBegin && clips[i].GetTime() >= lengths[i])
                    clips[i].SetTime(clips[i].GetTime() % lengths[i]);
                weights[i] = Mathf.MoveTowards(weights[i], i == activeInput ? 1f : 0f, Time.deltaTime * 8f);
                total += weights[i];
            }

            for (int i = 0; i < clips.Length; i++)
                mixer.SetInputWeight(i, weights[i] / Mathf.Max(0.001f, total));
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
        }
    }
}
