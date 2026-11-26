using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    [DefaultExecutionOrder(150), DisallowMultipleComponent, RequireComponent(typeof(CapsuleCollider))]
    public sealed class VillageMount : WorldInteractable
    {
        [SerializeField] private string displayName = "말";
        [SerializeField] private Transform model;
        [SerializeField, Tooltip("Root of the rider's animation relative to the horse's feet.")]
        private Transform riderRoot;
        [SerializeField] private AnimationClip riderPose;
        [SerializeField] private Transform leftStirrup, rightStirrup;
        [SerializeField, Min(.1f)] private float walkSpeed = 2.4f;
        [SerializeField, Min(.1f)] private float runSpeed = 7.2f;
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle_A");
        private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
        private static readonly int Run = Animator.StringToHash("Base Layer.Run");
        private Animator animator;
        private Transform driver;
        private Vector3 previous;
        private int gait;

        public override string DisplayName => displayName;
        public override bool ShowNameByDefault => string.IsNullOrEmpty(Rider);
        public override Vector3 NamePosition => transform.TransformPoint(NameOffset);
        public override string InteractionVerb => string.IsNullOrEmpty(Rider) ? "타기" : "탑승 중";
        public override string InteractionVerbFor(HarvestWorker worker) => worker.CurrentMount == this ? "내리기" : InteractionVerb;
        internal override InteractionCursor CursorFor(HarvestWorker worker) => InteractionCursor.Enter;
        internal string Rider { get; private set; }
        internal AnimationClip RiderPose => riderPose;
        internal Vector3 RiderOffset => riderRoot != null ? riderRoot.localPosition : Vector3.up;
        internal float Speed(bool walk) => walk ? walkSpeed : runSpeed;
        internal bool TryGetStirrups(out Vector3 left, out Vector3 right)
        {
            left = leftStirrup != null ? transform.InverseTransformPoint(leftStirrup.position) : default;
            right = rightStirrup != null ? transform.InverseTransformPoint(rightStirrup.position) : default;
            return leftStirrup != null && rightStirrup != null;
        }

        internal MountSave Capture()
        {
            var pose = driver != null ? driver : transform;
            return new MountSave { id = PersistentId, x = pose.position.x, z = pose.position.z, yaw = pose.eulerAngles.y };
        }

        private void Awake()
        {
            animator = model != null ? model.GetComponentInChildren<Animator>() : null;
            if (animator != null)
                animator.applyRootMotion = false;
            previous = transform.position;
        }

        internal void Present(string rider, Transform follow, Vector3 point, float yaw, bool immediate)
        {
            Rider = rider;
            if (driver == follow && driver != null)
                return;
            driver = follow;
            if (driver != null)
                transform.SetPositionAndRotation(driver.position, driver.rotation);
            else
                transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            if (immediate || driver == null)
                previous = transform.position;
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions) =>
            actions.Add(new WorldContextAction(InteractionVerbFor(village.Worker), () => RequestInteraction(village.Worker)));

        protected override void Interact(HarvestWorker worker) => worker.InteractWithMount(this);

        private void LateUpdate()
        {
            if (driver != null)
                transform.SetPositionAndRotation(driver.position, driver.rotation);
            float stride = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).magnitude;
            previous = transform.position;
            float speed = Time.deltaTime > 0 && stride < 2f ? stride / Time.deltaTime : 0f;
            int next = speed < .15f ? Idle : speed < (walkSpeed + runSpeed) * .5f ? Walk : Run;
            if (animator != null && next != gait && animator.HasState(0, next))
            {
                gait = next;
                animator.CrossFadeInFixedTime(gait, .12f, 0);
            }
        }
    }
}
