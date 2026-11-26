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
        private const int RideInput = 11;
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
        private Animator rig;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationPlayableOutput animationOutput;
        private AnimationLayerMixerPlayable carryLayer;
        private AnimationClipPlayable carryPose;
        private AnimationScriptPlayable stirrupPose;
        private AvatarMask carryMask;
        private AnimationClip timberToss;
        private bool humanoid, carrying;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[RideInput + 1];
        private readonly float[] lengths = new float[RideInput + 1];
        private readonly float[] weights = new float[RideInput + 1];
        private bool riding;
        private AnimationClip fallbackWork;
        private int workInput = Work;
        private int sitInput = -1;
        private float workDuration, beginDuration, finishDuration;
        private float loopCycles, loopImpact, lastLoopPosition;
        private bool timedWork;
        private int target;

        internal void UseDefaultGestures(WorkerAnimation source)
        {
            if (source == null || source == this)
                return;
            if (jump == null)
                jump = source.jump;
            if (gatherSuccess == null)
                gatherSuccess = source.gatherSuccess;
            if (gatherFailure == null)
                gatherFailure = source.gatherFailure;
        }

        public void Initialize(Animator animator, AnimationClip idle, AnimationClip move, AnimationClip work, AnimationClip walk = null)
        {
            if (graph.IsValid())
                graph.Destroy();
            ClearCarryLayer();
            if (animator == null || idle == null)
                return;
            move = move != null ? move : idle;
            walk = walk != null ? walk : move;
            fallbackWork = work != null ? work : idle;
            timedWork = false;
            riding = false;
            workInput = Work;
            sitInput = -1;
            target = 0;
            Gesture = WorkerGesture.None;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            humanoid = animator.isHuman;
            rig = animator;
            stirrupPose = default;
            graph = PlayableGraph.Create("Workerborn existing character animations");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            animationOutput = AnimationPlayableOutput.Create(graph, "Character", animator);
            animationOutput.SetSourcePlayable(mixer);
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
                    if (input == RideInput && stirrupPose.IsValid())
                    {
                        graph.DestroyPlayable(stirrupPose);
                        stirrupPose = default;
                    }
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
                WorkerGesture.TimberToss => timberToss,
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

        internal void ConfigureTimberToss(AnimationClip clip) => timberToss = clip;

        internal void SetRiding(VillageMount mount, Transform riderVisual = null)
        {
            AnimationClip pose = mount != null ? mount.RiderPose : null;
            riding = pose != null;
            if (!riding || !graph.IsValid())
                return;
            SetGesture(WorkerGesture.None);
            BindClip(RideInput, pose, sampled: true);
            clips[RideInput].SetTime(0d);
            if (!humanoid || !pose.isHumanMotion || riderVisual == null)
                return;

            // The rider and the stirrups inherit the same animated saddle transform.
            var job = stirrupPose.IsValid() ? stirrupPose.GetJobData<StirrupPose>() :
                new StirrupPose { Root = rig.BindSceneTransform(riderVisual) };
            bool hasStirrups = mount.TryGetStirrups(out job.Left, out job.Right);
            job.Enabled = hasStirrups ? 1 : 0;
            if (stirrupPose.IsValid())
                stirrupPose.SetJobData(job);
            else if (hasStirrups)
            {
                graph.Disconnect(mixer, RideInput);
                stirrupPose = AnimationScriptPlayable.Create(graph, job, 1);
                graph.Connect(clips[RideInput], 0, stirrupPose, 0);
                graph.Connect(stirrupPose, 0, mixer, RideInput);
                mixer.SetInputWeight(RideInput, weights[RideInput]);
            }
        }

        private struct StirrupPose : IAnimationJob
        {
            public TransformSceneHandle Root;
            public Vector3 Left, Right;
            public int Enabled;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                if (Enabled == 0 || !stream.isHumanStream || !Root.IsValid(stream))
                    return;
                var body = stream.AsHuman();
                Vector3 position = Root.GetPosition(stream);
                Quaternion rotation = Root.GetRotation(stream);
                PositionFoot(body, AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, Left, position, rotation);
                PositionFoot(body, AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, Right, position, rotation);
                body.SolveIK();
            }

            private static void PositionFoot(AnimationHumanStream body, AvatarIKGoal goal, AvatarIKHint knee,
                Vector3 offset, Vector3 position, Quaternion rotation)
            {
                body.SetGoalPosition(goal, position + rotation * offset);
                body.SetGoalWeightPosition(goal, 1f);
                var bend = new Vector3(offset.x * 1.2f, offset.y + .34f, offset.z + .28f);
                body.SetHintPosition(knee, position + rotation * bend);
                body.SetHintWeightPosition(knee, .85f);
            }
        }

        internal void PlayTimberToss(AnimationClip clip)
        {
            ConfigureTimberToss(clip);
            SetGesture(WorkerGesture.TimberToss);
        }

        internal void SetCarrying(AnimationClip clip, bool active)
        {
            carrying = active;
            if (!active || !graph.IsValid() || !humanoid || clip == null || carryLayer.IsValid())
                return;
            carryMask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                carryMask.SetHumanoidBodyPartActive(part, part is AvatarMaskBodyPart.Body or
                    AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm or
                    AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers);
            }
            carryLayer = AnimationLayerMixerPlayable.Create(graph, 2);
            carryPose = AnimationClipPlayable.Create(graph, clip);
            carryPose.SetApplyFootIK(false);
            graph.Connect(mixer, 0, carryLayer, 0);
            graph.Connect(carryPose, 0, carryLayer, 1);
            carryLayer.SetLayerMaskFromAvatarMask(1, carryMask);
            carryLayer.SetInputWeight(0, 1f);
            carryLayer.SetInputWeight(1, 0f);
            animationOutput.SetSourcePlayable(carryLayer);
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
            int activeInput = riding ? RideInput : Gesture != WorkerGesture.None ? GestureInput : Input;
            for (int i = 0; i < clips.Length; i++)
            {
                if ((i != Work || !timedWork) && i < WorkBegin && clips[i].GetTime() >= lengths[i])
                    clips[i].SetTime(clips[i].GetTime() % lengths[i]);
                weights[i] = Mathf.MoveTowards(weights[i], i == activeInput ? 1f : 0f, Time.deltaTime * 8f);
                total += weights[i];
            }

            for (int i = 0; i < clips.Length; i++)
                mixer.SetInputWeight(i, weights[i] / Mathf.Max(0.001f, total));
            if (carryLayer.IsValid())
            {
                float targetWeight = carrying && Gesture == WorkerGesture.None ? 1f : 0f;
                carryLayer.SetInputWeight(1, Mathf.MoveTowards(carryLayer.GetInputWeight(1), targetWeight, Time.deltaTime * 9f));
                double duration = carryPose.GetAnimationClip().length;
                if (duration > 0d && carryPose.GetTime() >= duration)
                    carryPose.SetTime(carryPose.GetTime() % duration);
            }
        }

        private void ClearCarryLayer()
        {
            if (carryMask != null)
                Destroy(carryMask);
            carryMask = null;
            carryLayer = default;
            carrying = false;
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
            ClearCarryLayer();
        }
    }
}
