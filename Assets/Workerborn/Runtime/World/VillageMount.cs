using System.Collections.Generic;
using UnityEngine;

namespace Workerborn
{
    [DefaultExecutionOrder(150), DisallowMultipleComponent, RequireComponent(typeof(CapsuleCollider))]
    public sealed class VillageMount : WorldInteractable
    {
        [SerializeField] private string displayName = "말";
        [SerializeField] private Transform model;
        [SerializeField] private SkinnedMeshRenderer skin;
        [SerializeField] private Transform saddleBone;
        [SerializeField] private Transform saddle;
        [SerializeField] private Transform riderRoot;
        [SerializeField] private AnimationClip riderPose;
        [SerializeField] private Transform leftStirrup;
        [SerializeField] private Transform rightStirrup;
        [SerializeField, Min(.1f)] private float walkSpeed = 2.4f;
        [SerializeField, Min(.1f)] private float runSpeed = 7.2f;
        [SerializeField] private AnimationClip attackAnimation;
        [SerializeField, Min(1)] private int attackDamage = 12;
        [SerializeField, Min(.1f)] private float attackReach = 2.2f;
        [SerializeField, Range(.05f, .95f)] private float attackImpact = .52f;
        private static readonly int Idle = Animator.StringToHash("Base Layer.Idle_A");
        private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
        private static readonly int Run = Animator.StringToHash("Base Layer.Run");
        private static readonly int Attack = Animator.StringToHash("Base Layer.Attack");
        private Animator animator;
        private Transform driver;
        private HarvestWorker localDriver;
        private RemoteVillagePlayer remoteDriver;
        private Vector3 previous;
        private int motion;
        private bool attackRequested;
        private float attackEndsAt;

        public override string DisplayName => displayName;
        public override bool ShowNameByDefault => string.IsNullOrEmpty(Rider);
        public override Vector3 NamePosition => transform.TransformPoint(NameOffset);
        public override string InteractionVerb => string.IsNullOrEmpty(Rider) ? "C로 타기" : "탑승 중";
        public override string InteractionVerbFor(HarvestWorker worker) =>
            string.IsNullOrEmpty(Rider) && !worker.InReach(transform.position, InteractionDistance) ? "다가가기" : InteractionVerb;
        internal override InteractionCursor CursorFor(HarvestWorker worker) => InteractionCursor.Enter;
        internal string Rider { get; private set; }
        internal AnimationClip RiderPose => riderPose;
        internal int AttackDamage => attackDamage;
        internal float AttackReach => attackReach;
        internal float AttackImpact => attackImpact;
        internal float AttackSeconds => Mathf.Max(.2f, attackAnimation != null ? attackAnimation.length : .45f);
        internal float Speed(bool walk) => walk ? walkSpeed : runSpeed;
        internal bool CoversPointer(Ray ray) => skin != null && skin.bounds.IntersectRay(ray);

        internal void PlaceRider(Transform visual, Vector3 basePosition, Quaternion baseRotation)
        {
            if (riderRoot == null)
                return;
            Vector3 offset = transform.InverseTransformPoint(riderRoot.position);
            Quaternion facing = Quaternion.Inverse(transform.rotation) * riderRoot.rotation;
            visual.SetLocalPositionAndRotation(basePosition + offset, facing * baseRotation);
        }

        internal bool TryGetStirrups(out Vector3 left, out Vector3 right)
        {
            left = right = default;
            if (riderRoot == null || leftStirrup == null || rightStirrup == null)
                return false;
            left = riderRoot.InverseTransformPoint(leftStirrup.position);
            right = riderRoot.InverseTransformPoint(rightStirrup.position);
            return true;
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
            if (saddle != null && saddleBone != null)
                saddle.SetParent(saddleBone, worldPositionStays: true);
            previous = transform.position;
        }

        internal void Present(string rider, Transform follow, Vector3 point, float yaw, bool immediate)
        {
            Rider = rider;
            if (driver == follow && driver != null)
                return;
            driver = follow;
            localDriver = driver != null ? driver.GetComponent<HarvestWorker>() : null;
            remoteDriver = driver != null && localDriver == null ? driver.GetComponent<RemoteVillagePlayer>() : null;
            attackRequested = false;
            attackEndsAt = 0f;
            if (driver != null)
                transform.SetPositionAndRotation(driver.position, driver.rotation);
            else
                transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            if (immediate || driver == null)
                previous = transform.position;
        }

        public override void AddContextActions(VillagePrototype village, List<WorldContextAction> actions)
        {
            if (village.Worker.CanMount && string.IsNullOrEmpty(Rider) &&
                !village.Worker.InReach(transform.position, InteractionDistance))
                actions.Add(new WorldContextAction("말에게 다가가기", () => RequestInteraction(village.Worker)));
        }

        protected override void Interact(HarvestWorker worker) => worker.InteractWithMount(this);

        private void Update()
        {
            if (driver != null)
                transform.SetPositionAndRotation(driver.position, driver.rotation);
            float stride = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).magnitude;
            previous = transform.position;
            if (animator == null)
                return;

            bool attacking = localDriver != null ? localDriver.Action == WorkerAction.Attacking :
                remoteDriver != null && remoteDriver.IsUsingMountAttack;
            if (attacking && !attackRequested && animator.HasState(0, Attack))
            {
                motion = Attack;
                attackEndsAt = Time.time + AttackSeconds;
                animator.Play(Attack, 0, 0f);
            }
            attackRequested = attacking;
            if (Time.time < attackEndsAt)
                return;

            float speed = Time.deltaTime > 0 && stride < 2f ? stride / Time.deltaTime : 0f;
            int next = speed < .15f ? Idle : speed < (walkSpeed + runSpeed) * .5f ? Walk : Run;
            if (next != motion && animator.HasState(0, next))
            {
                motion = next;
                animator.CrossFadeInFixedTime(next, .12f, 0);
            }
        }

        private void LateUpdate()
        {
            if (localDriver != null)
            {
                transform.SetPositionAndRotation(driver.position, driver.rotation);
                localDriver.AlignToMount();
            }
        }
    }
}
