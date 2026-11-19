using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Workerborn
{
    public sealed class WorkerAnimation : MonoBehaviour
    {
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[4];
        private readonly float[] lengths = new float[4];
        private readonly float[] weights =
        {
            1f,
            0f,
            0f,
            0f
        };
        private int target;
        public void Initialize(Animator animator, AnimationClip idle, AnimationClip move, AnimationClip work, AnimationClip walk = null)
        {
            if (graph.IsValid())
                graph.Destroy();
            if (animator == null || idle == null)
                return;
            move = move != null ? move : idle;
            walk = walk != null ? walk : move;
            work = work != null ? work : idle;
            target = 0;
            weights[0] = 1f;
            weights[1] = weights[2] = weights[3] = 0f;
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
                work,
                walk
            };
            for (int i = 0; i < source.Length; i++)
            {
                clips[i] = AnimationClipPlayable.Create(graph, source[i]);
                clips[i].SetApplyFootIK(false);
                lengths[i] = Mathf.Max(0.01f, source[i].length);
                graph.Connect(clips[i], 0, mixer, i);
                mixer.SetInputWeight(i, weights[i]);
            }

            graph.Play();
        }

        public int CurrentAction => target;
        public double ActionTime => graph.IsValid() ? clips[target].GetTime() : 0d;

        public void SetAction(int action)
        {
            int next = Mathf.Clamp(action, 0, clips.Length - 1);
            if (next == target)
                return;
            if (graph.IsValid())
            {
                if (next == 2)
                    clips[next].SetTime(0d);
                else if ((next == 1 || next == 3) && (target == 1 || target == 3))
                    clips[next].SetTime(clips[target].GetTime() % lengths[target] / lengths[target] * lengths[next]);
            }
            target = next;
        }

        private void Update()
        {
            if (!graph.IsValid())
                return;
            float total = 0f;
            for (int i = 0; i < clips.Length; i++)
            {
                // The supplied sword attack is non-looping; repeat it as a temporary work gesture.
                if (clips[i].GetTime() >= lengths[i])
                    clips[i].SetTime(clips[i].GetTime() % lengths[i]);
                weights[i] = Mathf.MoveTowards(weights[i], i == target ? 1f : 0f, Time.deltaTime * 8f);
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
