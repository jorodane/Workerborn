using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Workerborn
{
    public sealed partial class WorkerAnimation
    {
        private AnimationScriptPlayable gazePlayable;
        private Transform gazeHead, leftEye, rightEye;
        private CharacterGaze wantedGaze, shownGaze;

        internal CharacterGaze Gaze => shownGaze;
        internal Vector3? EyePosition
        {
            get
            {
                if (leftEye != null && rightEye != null)
                    return (leftEye.position + rightEye.position) * .5f;
                return gazeHead != null ? gazeHead.position + Vector3.up * .08f : (Vector3?)null;
            }
        }

        private void BindGaze()
        {
            gazePlayable = default;
            gazeHead = leftEye = rightEye = null;
            wantedGaze = shownGaze = default;
            if (!humanoid)
                return;
            gazeHead = rig.GetBoneTransform(HumanBodyBones.Head);
            if (gazeHead == null)
                return;
            leftEye = rig.GetBoneTransform(HumanBodyBones.LeftEye);
            rightEye = rig.GetBoneTransform(HumanBodyBones.RightEye);
            Quaternion worldToBody = Quaternion.Inverse(transform.rotation);
            var job = new GazePose
            {
                Body = rig.BindSceneTransform(transform),
                Head = rig.BindStreamTransform(gazeHead),
                HeadBasis = worldToBody * gazeHead.rotation
            };
            if (leftEye != null)
            {
                job.LeftEye = rig.BindStreamTransform(leftEye);
                job.LeftEyeBasis = worldToBody * leftEye.rotation;
                job.HasLeftEye = 1;
            }
            if (rightEye != null)
            {
                job.RightEye = rig.BindStreamTransform(rightEye);
                job.RightEyeBasis = worldToBody * rightEye.rotation;
                job.HasRightEye = 1;
            }
            gazePlayable = AnimationScriptPlayable.Create(graph, job, 1);
        }

        private void ConnectPoseOutput(Playable source)
        {
            if (gazePlayable.IsValid())
            {
                graph.Connect(source, 0, gazePlayable, 0);
                gazePlayable.SetInputWeight(0, 1f);
                source = gazePlayable;
            }
            animationOutput.SetSourcePlayable(source);
        }

        internal void SetGazeTarget(Vector3? point, float attention = 1f)
        {
            wantedGaze = default;
            if (point is not Vector3 targetPosition || EyePosition is not Vector3 eyes)
                return;
            Vector3 direction = transform.InverseTransformDirection(targetPosition - eyes);
            wantedGaze = CharacterGaze.Toward(direction.x, direction.y, direction.z, attention);
        }

        internal void SetGazePose(CharacterGaze pose) => wantedGaze = pose.Validated();

        // Teleporting or disabling the actor invalidates its old focus immediately.
        internal void ClearGaze()
        {
            bool hadGaze = shownGaze.weight > 0f;
            wantedGaze = shownGaze = default;
            if (hadGaze && gazePlayable.IsValid())
                WriteGaze();
        }

        private void UpdateGaze()
        {
            if (!gazePlayable.IsValid() || shownGaze.weight == 0f && wantedGaze.weight == 0f)
                return;
            float blend = 1f - Mathf.Exp(-12f * Time.deltaTime);
            shownGaze.yaw = Mathf.Lerp(shownGaze.yaw, wantedGaze.yaw, blend);
            shownGaze.pitch = Mathf.Lerp(shownGaze.pitch, wantedGaze.pitch, blend);
            shownGaze.weight = Mathf.Lerp(shownGaze.weight, wantedGaze.weight, blend);
            if (wantedGaze.weight == 0f && shownGaze.weight < .002f)
                shownGaze = default;
            WriteGaze();
        }

        private void WriteGaze()
        {
            var job = gazePlayable.GetJobData<GazePose>();
            job.Look = shownGaze;
            if (showingConversationGesture)
                job.Look.weight *= .8f;
            gazePlayable.SetJobData(job);
        }

        private struct GazePose : IAnimationJob
        {
            public TransformSceneHandle Body;
            public TransformStreamHandle Head, LeftEye, RightEye;
            public Quaternion HeadBasis, LeftEyeBasis, RightEyeBasis;
            public int HasLeftEye, HasRightEye;
            public CharacterGaze Look;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                if (Look.weight <= 0f || !Body.IsValid(stream) || !Head.IsValid(stream))
                    return;
                Quaternion aim = Body.GetRotation(stream) * Quaternion.Euler(Look.pitch, Look.yaw, 0f);
                Quaternion head = Quaternion.Slerp(Head.GetRotation(stream), aim * HeadBasis, Look.weight * .84f);
                Head.SetRotation(stream, head);

                if (HasLeftEye == 0 && HasRightEye == 0)
                    return;
                Quaternion face = head * Quaternion.Inverse(HeadBasis);
                Vector3 direction = Quaternion.Inverse(face) * (aim * Vector3.forward);
                var eyeTurn = CharacterGaze.Toward(direction.x, direction.y, direction.z);
                Quaternion eyes = face * Quaternion.Euler(Mathf.Clamp(eyeTurn.pitch, -10f, 10f),
                    Mathf.Clamp(eyeTurn.yaw, -14f, 14f), 0f);
                if (HasLeftEye != 0 && LeftEye.IsValid(stream))
                    LeftEye.SetRotation(stream, Quaternion.Slerp(LeftEye.GetRotation(stream), eyes * LeftEyeBasis, Look.weight));
                if (HasRightEye != 0 && RightEye.IsValid(stream))
                    RightEye.SetRotation(stream, Quaternion.Slerp(RightEye.GetRotation(stream), eyes * RightEyeBasis, Look.weight));
            }
        }
    }
}
